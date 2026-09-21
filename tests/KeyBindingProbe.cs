using System;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace Autoclicker
{
    internal static class KeyBindingProbe
    {
        private static readonly BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance;
        private static T Field<T>(ClickerForm form, string name)
        {
            return (T)typeof(ClickerForm).GetField(name, Flags).GetValue(form);
        }
        private static void Check(bool value, string description)
        {
            if (!value) throw new Exception(description);
        }

        [STAThread]
        private static int Main(string[] args)
        {
            string report = args[0];
            string settings = Path.Combine(Path.GetDirectoryName(report), "ui-key-" + Guid.NewGuid().ToString("N") + ".settings");
            try
            {
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
                using (ClickerForm form = new ClickerForm(true, settings))
                {
                    form.Opacity = 0;
                    form.ShowInTaskbar = false;
                    ClickEngine fake = new ClickEngine(delegate { throw new Exception("The probe must not click."); });
                    typeof(ClickerForm).GetField("engine", Flags).SetValue(form, fake);
                    Timer timer = Field<Timer>(form, "timer");
                    timer.Tick -= (EventHandler)Delegate.CreateDelegate(typeof(EventHandler), form,
                        typeof(ClickerForm).GetMethod("OnTick", Flags));
                    Field<ToggleSounds>(form, "sounds").Dispose();
                    typeof(ClickerForm).GetField("sounds", Flags).SetValue(form, null);
                    form.Show();
                    Application.DoEvents();
                    GlobalHotkeys hook = Field<GlobalHotkeys>(form, "hotkeys");
                    Button change = Field<Button>(form, "changeKey");
                    Button reset = Field<Button>(form, "resetKey");
                    Label startHint = Field<Label>(form, "startHint");
                    Button stop = Field<Button>(form, "stop");
                    Label value = Field<Label>(form, "bindingValue");
                    Label footer = Field<Label>(form, "shortcutFooter");
                    Check(!startHint.TabStop && !startHint.CanSelect && startHint.AccessibleRole == AccessibleRole.StaticText,
                        "The start area must be a non-focusable label.");
                    typeof(Control).GetMethod("OnClick", Flags).Invoke(startHint, new object[] { EventArgs.Empty });
                    Check(!fake.Active && !timer.Enabled, "Clicking the start label must not start.");
                    change.PerformClick();
                    Check(hook.Bindings.Capturing && !timer.Enabled, "Capture must stop clicking.");
                    Check(hook.DispatchKey((int)Keys.F9, true), "Capture must consume the new key's press.");
                    Check(hook.DispatchKey((int)Keys.F9, true), "Capture must consume repeats.");
                    Check(!fake.Active, "A captured press must not start.");
                    hook.DispatchKey((int)Keys.F9, false);
                    Application.DoEvents();
                    Check(!fake.Active && !timer.Enabled, "Finishing capture must leave clicking stopped.");
                    Check(value.Text == "F9" && footer.Text.Contains("F9") && startHint.Text == "Press F9 to start", "All shortcut labels must update.");
                    Check(KeySettings.Load(settings) == (int)Keys.F9, "The chosen key must persist.");
                    Check(!hook.DispatchKey((int)Keys.F11, true), "Old F11 must pass through.");
                    hook.DispatchKey((int)Keys.F11, false);
                    hook.DispatchKey((int)Keys.F9, true);
                    Application.DoEvents();
                    Check(fake.Active && timer.Enabled, "The new key must start.");
                    Check(startHint.Text == "Press F9 to stop", "The label must reflect the running state.");
                    hook.DispatchKey((int)Keys.F9, true);
                    Application.DoEvents();
                    Check(fake.Active, "Auto-repeat must not stop.");
                    hook.DispatchKey((int)Keys.F9, false);
                    hook.DispatchKey((int)Keys.F9, true);
                    Application.DoEvents();
                    Check(!fake.Active && !timer.Enabled, "The new key must stop on its next press.");
                    hook.DispatchKey((int)Keys.F9, false);
                    hook.DispatchKey((int)Keys.F9, true);
                    Application.DoEvents();
                    Check(fake.Active, "The key must restart before testing Stop.");
                    stop.PerformClick();
                    Check(!fake.Active && !timer.Enabled, "The Stop button must remain clickable.");
                    hook.DispatchKey((int)Keys.F9, false);
                    change.PerformClick();
                    hook.DispatchKey((int)Keys.Z, true);
                    change.PerformClick();
                    hook.DispatchKey((int)Keys.Z, false);
                    Application.DoEvents();
                    Check(value.Text == "F9" && !fake.Active && !hook.Bindings.Capturing, "Cancel must retain the binding without toggling.");
                    change.PerformClick();
                    typeof(Form).GetMethod("OnDeactivate", Flags).Invoke(form, new object[] { EventArgs.Empty });
                    Check(!hook.Bindings.Capturing && value.Text == "F9", "Switching away must cancel capture.");
                    using (ClickerForm reopened = new ClickerForm(false, settings))
                        Check(Field<Label>(reopened, "bindingValue").Text == "F9", "A new app window must restore the saved key.");
                    reset.PerformClick();
                    Check(value.Text == "F11" && KeySettings.Load(settings) == (int)Keys.F11,
                        "Reset must restore and save F11.");
                    CheckBox autoStop = Field<CheckBox>(form, "autoStop");
                    NumericUpDown minutes = Field<NumericUpDown>(form, "stopMinutes");
                    NumericUpDown seconds = Field<NumericUpDown>(form, "stopSeconds");
                    Label timerHint = Field<Label>(form, "autoStopHint");
                    Label detail = Field<Label>(form, "detail");
                    Check(autoStop.Checked && minutes.Enabled && seconds.Enabled && minutes.Value == 1 && seconds.Value == 0,
                        "The timer must default to one minute and be enabled.");
                    minutes.Value = 0;
                    seconds.Value = 0;
                    hook.DispatchKey((int)Keys.F11, true);
                    hook.DispatchKey((int)Keys.F11, false);
                    Application.DoEvents();
                    Check(!fake.Active && !timer.Enabled && detail.Text.Contains("at least 1 second"),
                        "A zero duration must not silently start unlimited clicking.");
                    minutes.Value = 2;
                    seconds.Value = 30;
                    Check(timerHint.Text == "READY / 02:30 per run", "Entered duration must show correctly.");
                    hook.DispatchKey((int)Keys.F11, true);
                    hook.DispatchKey((int)Keys.F11, false);
                    Application.DoEvents();
                    Check(fake.Active && fake.StopsAt - fake.StartsAt == 150000 &&
                        !autoStop.Enabled && !minutes.Enabled && !seconds.Enabled,
                        "Starting must arm the chosen duration and lock its settings.");
                    Check(timerHint.Text == "TIME LEFT  /  02:30", "The countdown must start immediately.");
                    typeof(ClickerForm).GetMethod("UpdateAutoStopHint", Flags).Invoke(form, new object[] { fake.StopsAt - 1 });
                    Check(timerHint.Text == "TIME LEFT  /  00:01", "Countdown must round remaining fractions up.");
                    fake.Tick(fake.StopsAt, false);
                    typeof(ClickerForm).GetMethod("OnTick", Flags).Invoke(form, new object[] { null, EventArgs.Empty });
                    Check(!fake.Active && !timer.Enabled && autoStop.Enabled && minutes.Enabled &&
                        detail.Text.StartsWith("Time is up.") && startHint.Text == "Press F11 to start",
                        "Automatic expiry must stop the UI timer and restore stopped controls.");
                    hook.DispatchKey((int)Keys.F11, true);
                    hook.DispatchKey((int)Keys.F11, false);
                    Application.DoEvents();
                    Check(fake.Active && !fake.TimeLimitReached && fake.StopsAt - fake.StartsAt == 150000,
                        "Restarting must get the full configured duration.");
                    stop.PerformClick();
                    Check(!fake.Active && !timer.Enabled && minutes.Enabled, "Stop must cancel a timed run early.");
                    change.PerformClick();
                    Check(!autoStop.Enabled && !minutes.Enabled, "Binding capture must lock timer settings.");
                    change.PerformClick();
                    Check(autoStop.Enabled && minutes.Enabled, "Cancel must restore timer settings.");
                    minutes.Value = 999;
                    seconds.Value = 59;
                    hook.DispatchKey((int)Keys.F11, true);
                    hook.DispatchKey((int)Keys.F11, false);
                    Application.DoEvents();
                    Check(fake.StopsAt - fake.StartsAt == 59999000, "The maximum duration must not overflow.");
                    stop.PerformClick();
                    autoStop.Checked = false;
                    hook.DispatchKey((int)Keys.F11, true);
                    hook.DispatchKey((int)Keys.F11, false);
                    Application.DoEvents();
                    Check(fake.Active && fake.StopsAt == 0, "Unchecked timer must restore continuous clicking.");
                    stop.PerformClick();
                    form.Close();
                    Check(form.IsDisposed, "The updated UI must close fully.");
                }
                File.WriteAllText(report, "PASS: optional timer, zero-duration validation, minutes/seconds conversion, countdown, automatic stopped UI, restart, early Stop, settings locks, maximum duration, continuous mode, noninteractive start label, clickable Stop, Change key UI, capture without starting, held-key suppression, dynamic labels, saved binding, old key pass-through, new key on/off, Cancel, focus-loss cancellation, reopening, reset to F11, full close. No real keyboard or mouse input was sent.");
                return 0;
            }
            catch (Exception error)
            {
                File.WriteAllText(report, "FAIL: " + error);
                return 1;
            }
            finally { if (File.Exists(settings)) File.Delete(settings); }
        }
    }
}
