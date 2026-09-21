using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace Autoclicker
{
    internal static class UpdateUiProbe
    {
        private static readonly BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
        private static T Field<T>(ClickerForm form, string name)
        { return (T)typeof(ClickerForm).GetField(name, Fields).GetValue(form); }
        private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
        private static void Snapshot(Form form, string path)
        {
            using (var bitmap = new Bitmap(form.Width, form.Height))
            {
                form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
                bitmap.Save(path, ImageFormat.Png);
            }
        }

        internal static void Run(string root)
        {
            var backend = new IntegrationMain.FakeBackend { Version = "1.3.1" };
            using (var updates = new UpdateCoordinator(backend))
            using (var form = new ClickerForm(false))
            {
                form.Opacity = 0;
                form.ShowInTaskbar = false;
                form.AttachUpdates(updates);
                form.Show();
                Application.DoEvents();
                Button action = Field<Button>(form, "checkUpdates");
                Check(action.Enabled && action.Text == "Confirm update" && backend.Downloads == 0,
                    "The launch check must display a confirmation without downloading or closing.");
                Snapshot(form, Path.Combine(root, "update-confirm.png"));
                action.PerformClick();
                Application.DoEvents();
                Check(action.Enabled && action.Text == "Restart to update" && backend.Downloads == 1 && backend.Applies == 0 && !form.IsDisposed,
                    "Confirming must download and offer a separate restart action.");
                Snapshot(form, Path.Combine(root, "update-restart.png"));

                // A fake click engine validates stop-on-restart without sending input.
                var engine = new ClickEngine(delegate { throw new Exception("Update UI probe must never click."); });
                typeof(ClickerForm).GetField("engine", Fields).SetValue(form, engine);
                engine.Start(0, 50, 0);
                backend.FailApply = true;
                action.PerformClick();
                Check(!form.IsDisposed && !engine.Active && action.Enabled,
                    "A failed helper launch must stop clicking but keep the app open for retry.");
                backend.FailApply = false;
                action.PerformClick();
                updates.ApplyOnExit();
                Check(form.IsDisposed && backend.Restart && backend.Applies == 1,
                    "Restart must close the main form and queue exactly one relaunch.");
            }
            File.WriteAllText(Path.Combine(root, "update-ui.txt"),
                "PASS: confirmation before download, ready/restart action, no automatic restart, stop on restart, helper failure retry, single close/relaunch request.");
        }
    }
}
