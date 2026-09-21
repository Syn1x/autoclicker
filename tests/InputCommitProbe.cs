using System;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace Autoclicker
{
    internal static class InputCommitProbe
    {
        private static readonly BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
        private static T Field<T>(object target, string name)
        { return (T)target.GetType().GetField(name, Private).GetValue(target); }
        private static void Check(bool value, string description)
        { if (!value) throw new Exception(description); }
        private static void TypeText(NumericUpDown input, string text)
        {
            // Editing the inner textbox follows typing behavior. Assigning the
            // NumericUpDown.Text property itself would validate immediately.
            foreach (Control child in input.Controls)
                if (child is TextBox) { child.Text = text; return; }
            throw new Exception("Numeric editor was not found.");
        }
        private static void Click(Control control)
        {
            // Dispatch only to our test form; never send system mouse input.
            typeof(Control).GetMethod("OnMouseDown", Private).Invoke(control,
                new object[] { new MouseEventArgs(MouseButtons.Left, 1, 2, 2, 0) });
        }

        internal static void Run(string root)
        {
            string settings = Path.Combine(root, "input-" + Guid.NewGuid().ToString("N") + ".settings");
            using (var form = new ClickerForm(false, settings))
            {
                form.Opacity = 0;
                form.ShowInTaskbar = false;
                form.Show();
                NumericUpDown interval = Field<NumericUpDown>(form, "interval");
                NumericUpDown minutes = Field<NumericUpDown>(form, "stopMinutes");
                NumericUpDown seconds = Field<NumericUpDown>(form, "stopSeconds");
                Label detail = Field<Label>(form, "detail");
                Label rate = Field<Label>(form, "rate");
                Label duration = Field<Label>(form, "autoStopHint");
                form.ActiveControl = interval;
                TypeText(interval, "100");
                Check(rate.Text == "20 /s", "An unfinished edit should wait for commit.");
                Click(detail);
                Check(rate.Text == "10 /s", "Clicking a nonfocusable label must apply the interval without Enter.");
                TypeText(minutes, "2");
                TypeText(seconds, "15");
                Click(detail.Parent);
                Check(duration.Text == "READY / 02:15 per run", "Clicking empty panel space must apply typed duration values.");

                form.ActiveControl = interval;
                TypeText(interval, "200");
                form.ActiveControl = minutes;
                Check(rate.Text == "5 /s", "Leaving a field for another input must apply its value.");
                TypeText(interval, "5000");
                Click(form);
                Check(interval.Text == "2000" && rate.Text == "0.5 /s", "Out-of-range input must clamp and update the rate.");
                TypeText(interval, "");
                Click(detail);
                Check(interval.Text == "2000", "An empty input must restore its last valid value.");

                var controller = Field<CrosshairController>(form, "crosshair");
                using (var dialog = new CrosshairSettingsForm(controller))
                {
                    dialog.Opacity = 0;
                    dialog.Show(form);
                    NumericUpDown size = null;
                    NumericUpDown thickness = null;
                    TrackBar sizeSlider = null, thicknessSlider = null;
                    ComboBox style = null;
                    Control preview = null;
                    foreach (Control control in dialog.Controls)
                    {
                        if (control.AccessibleName == "Crosshair size in pixels") size = (NumericUpDown)control;
                        if (control.AccessibleName == "Crosshair thickness in pixels") thickness = (NumericUpDown)control;
                        if (control.AccessibleName == "Crosshair size slider") sizeSlider = (TrackBar)control;
                        if (control.AccessibleName == "Crosshair thickness slider") thicknessSlider = (TrackBar)control;
                        if (control.AccessibleName == "Crosshair style") style = (ComboBox)control;
                        if (control is CrosshairPreview) preview = control;
                    }
                    TypeText(size, "1");
                    foreach (Control child in size.Controls)
                        if (child is TextBox) Click(child);
                    Check(size.Text == "1" && controller.Options.Size == 4, "Clicking inside the input must not commit a partial number.");
                    TypeText(size, "12");
                    Click(preview);
                    Check(controller.Options.Size == 12 && sizeSlider.Value == 12, "Clicking the preview must update the size and slider without Enter.");
                    TypeText(size, "18");
                    typeof(Form).GetMethod("OnDeactivate", Private).Invoke(dialog, new object[] { EventArgs.Empty });
                    Check(controller.Options.Size == 18, "Clicking away to another window must apply the size.");
                    TypeText(size, "999");
                    Click(preview);
                    Check(size.Text == "32" && controller.Options.Size == 32, "Crosshair size must retain range validation.");
                    TypeText(size, "invalid");
                    Click(preview);
                    Check(size.Text == "32" && controller.Options.Size == 32, "Invalid text must preserve the last valid size.");
                    style.SelectedIndex = 1;
                    TypeText(thickness, (3.2M).ToString());
                    Click(preview);
                    Check(controller.Options.Thickness == 3.2M && thicknessSlider.Value == 32,
                        "Typed thickness must apply to the preview and slider on click-away.");
                    TypeText(thickness, "99");
                    Click(preview);
                    Check(controller.Options.Thickness == 8M && thicknessSlider.Value == 80, "Typed thickness must respect its maximum.");
                    TypeText(thickness, (2.5M).ToString());
                    TypeText(size, "14");
                    dialog.Close();
                    Check(controller.Options.Size == 14, "Closing the settings window must commit pending text before saving.");
                }
                using (var reopened = new CrosshairController(settings))
                    Check(reopened.Options.Size == 14 && reopened.Options.Thickness == 2.5M, "Size and thickness entered just before closing must persist.");
                Check(!Field<ClickEngine>(form, "engine").Active, "Input editing must never start clicking.");
                form.Close();
            }
            File.WriteAllText(Path.Combine(root, "input-commit.txt"),
                "PASS: interval, minutes, seconds and crosshair size apply on label/panel/preview clicks, focus changes, deactivation and close; partial edits and range validation preserved. No system input sent.");
        }
    }
}
