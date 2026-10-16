using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace Autoclicker
{
    internal static class ReleaseNotesProbe
    {
        private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
        private static T Field<T>(ClickerForm form, string name)
        { return (T)typeof(ClickerForm).GetField(name, Fields).GetValue(form); }
        private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }

        internal static void Run(string root)
        {
            string settings = Path.Combine(root, "notes-" + Guid.NewGuid().ToString("N"), "app.settings");
            string marker = settings + ".release-seen";
            Check(ReleaseNotes.ShouldShow(settings, "1.5.4"), "New installs and upgrades from older clients must see notes once.");
            Check(ReleaseNotes.MarkSeen(settings, "1.5.4") && !ReleaseNotes.ShouldShow(settings, "1.5.4"), "The viewed version must persist.");
            Check(ReleaseNotes.ShouldShow(settings, "1.5.5") && !ReleaseNotes.ShouldShow(settings, "1.5.3"), "Only newer versions should prompt again.");
            Check(ReleaseNotes.MarkSeen(settings, "1.5.3") && !ReleaseNotes.ShouldShow(settings, "1.5.4"), "Running an older copy must not regress the viewed-version marker.");
            File.WriteAllText(marker, "invalid");
            Check(ReleaseNotes.ShouldShow(settings, "1.5.4") && ReleaseNotes.MarkSeen(settings, "1.5.4"), "Corrupt markers must recover.");
            using (FileStream locked = File.Open(marker, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                Check(!ReleaseNotes.ShouldShow(settings, "1.5.5") && !ReleaseNotes.MarkSeen(settings, "1.5.5"),
                    "Unavailable settings must not break startup or dismissing notes.");
            Check(!ReleaseNotes.ShouldShow(null, "1.5.4") && !ReleaseNotes.MarkSeen(null, "1.5.4"), "Preview forms must not write application settings.");
            string notes = ReleaseNotes.ReadCurrent();
            Check(notes.Contains("Autoclicker " + AppInfo.Version) && notes.Contains("What's new") && notes.Contains("• "),
                "The EXE must embed the current release notes as readable text.");
            Check(ReleaseNotes.ToPlainText("# Changes\n- **Fix** `input`") == "Changes" + Environment.NewLine + "• Fix input", "Simple Markdown must be readable without raw formatting markers.");

            foreach (int dpi in new[] { 96, 144, 192 })
            using (var form = new ReleaseNotesForm(AppInfo.Version, notes))
            {
                form.Opacity = 0;
                form.Show();
                float factor = dpi / form.CurrentAutoScaleDimensions.Width;
                form.Scale(new SizeF(factor, factor));
                form.PerformLayout();
                Check(!form.ShowInTaskbar && form.AcceptButton != null && form.CancelButton != null, "Notes need a single close action and no extra taskbar entry.");
                CheckBounds(form, dpi);
                using (var image = new Bitmap(form.Width, form.Height))
                {
                    form.DrawToBitmap(image, new Rectangle(Point.Empty, form.Size));
                    image.Save(Path.Combine(root, "release-notes-" + dpi + ".png"), ImageFormat.Png);
                }
                int fullHeight = form.Height;
                form.Height = (int)(300 * dpi / 96F);
                form.PerformLayout();
                CheckBounds(form, dpi);
                form.Height = fullHeight;
                ((Button)form.AcceptButton).PerformClick();
                Check(form.IsDisposed, "Got it must fully close the notes window.");
            }

            // Exercise the real launch hook and owned modal window. A UI timer
            // dismisses only this test dialog; no real keyboard/mouse input.
            File.Delete(marker);
            int seen = OpenAndDismiss(settings, true, false);
            Check(seen == 1 && !ReleaseNotes.ShouldShow(settings, AppInfo.Version), "Launch must show notes once and mark them after dismissal. Seen: " + seen + "; pending: " + ReleaseNotes.ShouldShow(settings, AppInfo.Version));
            Check(OpenAndDismiss(settings, true, false) == 0, "A second launch of the same version must stay quiet.");
            Check(OpenAndDismiss(settings, false, true) == 1, "The version link must reopen notes even after they were viewed.");
            File.WriteAllText(marker, "1.5.3");
            Check(OpenAndDismiss(settings, true, false) == 1, "Updating from an older version must show the new notes.");
            File.WriteAllText(Path.Combine(root, "release-notes.txt"),
                "PASS: embedded installed-version notes, first launch/new-version prompt, no repeat prompts, manual reopen, persisted/corrupt/locked version marker, no downgrade regression, readable formatting, scrollable layout at 100/150/200%, close button, stop-on-open, hotkey blocked while notes are open, owned dialog cleanup. No real input sent.");
        }

        private static int OpenAndDismiss(string settings, bool automatic, bool manual)
        {
            using (var form = new ClickerForm(true, settings))
            using (var dismiss = new Timer { Interval = 10 })
            {
                form.Opacity = 0;
                form.ShowInTaskbar = false;
                bool down = false;
                var engine = new ClickEngine(delegate { down = true; }, delegate { down = false; });
                typeof(ClickerForm).GetField("engine", Fields).SetValue(form, engine);
                var clickTimer = Field<Timer>(form, "timer");
                clickTimer.Tick -= (EventHandler)Delegate.CreateDelegate(typeof(EventHandler), form,
                    typeof(ClickerForm).GetMethod("OnTick", Fields));
                Field<ToggleSounds>(form, "sounds").Dispose();
                typeof(ClickerForm).GetField("sounds", Fields).SetValue(form, null);
                if (automatic) form.EnableReleaseNotesOnLaunch();
                int seen = 0;
                Exception failure = null;
                var elapsed = Stopwatch.StartNew();
                dismiss.Tick += delegate
                {
                    var dialog = Field<ReleaseNotesForm>(form, "releaseNotesWindow");
                    if (dialog == null)
                    {
                        if (elapsed.ElapsedMilliseconds > 5000) { failure = new Exception("Release notes did not close in time."); form.Close(); }
                        return;
                    }
                    dismiss.Stop();
                    try
                    {
                        dialog.Opacity = 0;
                        seen++;
                        Check(dialog.Owner == form && !engine.Active && !down, "Opening notes must stop clicking and release a held press.");
                        GlobalHotkeys hook = Field<GlobalHotkeys>(form, "hotkeys");
                        hook.DispatchKey((int)Keys.F11, true);
                        hook.DispatchKey((int)Keys.F11, false);
                        Application.DoEvents();
                        Check(!engine.Active, "The start hotkey must be ignored while notes are open.");
                    }
                    catch (Exception error) { failure = error; }
                    finally { dismiss.Stop(); dialog.Close(); }
                };
                dismiss.Start();
                form.Show();
                if (manual)
                {
                    engine.Start(0, 50, 0);
                    engine.Tick(0, false);
                    LinkLabel link = Field<LinkLabel>(form, "releaseNotesLink");
                    typeof(LinkLabel).GetMethod("OnLinkClicked", Fields).Invoke(link,
                        new object[] { new LinkLabelLinkClickedEventArgs(link.Links[0]) });
                }
                Application.DoEvents();
                dismiss.Stop();
                form.Close();
                Check(form.IsDisposed && !Application.OpenForms.Cast<Form>().Any(f => f is ReleaseNotesForm), "Closing must leave no release notes window running.");
                if (failure != null) throw failure;
                return seen;
            }
        }

        private static void CheckBounds(Control parent, int dpi)
        {
            foreach (Control child in parent.Controls)
            {
                Check(child.Left >= 0 && child.Top >= 0 && child.Right <= parent.ClientSize.Width + 2 && child.Bottom <= parent.ClientSize.Height + 2,
                    "Notes control must fit at " + dpi + " DPI: " + child.GetType().Name + " / " + child.Bounds + " in " + parent.ClientSize);
                CheckBounds(child, dpi);
            }
        }
    }
}
