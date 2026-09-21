using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace Autoclicker
{
    internal static class CompactLayoutProbe
    {
        private static readonly BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
        private static T Field<T>(ClickerForm form, string name)
        { return (T)typeof(ClickerForm).GetField(name, Fields).GetValue(form); }
        private static void Check(bool value, string message) { if (!value) throw new Exception(message); }

        internal static void Run(string root)
        {
            foreach (int dpi in new[] { 96, 144, 192 })
            using (var form = new ClickerForm(false))
            {
                form.Opacity = 0;
                form.ShowInTaskbar = false;
                form.Show();
                float factor = dpi / form.CurrentAutoScaleDimensions.Width;
                form.Scale(new SizeF(factor, factor));
                var clicker = Field<Panel>(form, "clickerPage");
                var crosshair = Field<CrosshairSettingsControl>(form, "crosshairSettings");
                var controller = Field<CrosshairController>(form, "crosshair");
                Button clickerTab = Field<Button>(form, "clickerTab"), crosshairTab = Field<Button>(form, "crosshairTab");
                int windows = Application.OpenForms.Count;
                Check(clicker.Visible && !crosshair.Visible, "The compact app must start on the Clicker tab.");
                crosshairTab.PerformClick();
                Check(!clicker.Visible && crosshair.Visible && Application.OpenForms.Count == windows,
                    "Crosshair settings must use the same window, with only one page visible.");
                controller.SetEnabled(true);
                clickerTab.PerformClick();
                Check(controller.Enabled && !crosshair.Visible, "Changing tabs must preserve the active overlay.");
                controller.SetEnabled(false);
                for (int i = 0; i < 4; i++) { crosshairTab.PerformClick(); clickerTab.PerformClick(); }
                Check(Application.OpenForms.Count == windows + 1, "Tab switches must reuse the one overlay, never create settings windows.");
                foreach (Control page in new Control[] { form, clicker, crosshair })
                    CheckBounds(page, dpi);
                foreach (bool showCrosshair in new[] { false, true })
                {
                    (showCrosshair ? crosshairTab : clickerTab).PerformClick();
                    using (var image = new Bitmap(form.Width, form.Height))
                    {
                        form.DrawToBitmap(image, new Rectangle(Point.Empty, form.Size));
                        image.Save(Path.Combine(root, (showCrosshair ? "compact-crosshair-" : "compact-clicker-") + dpi + ".png"), ImageFormat.Png);
                    }
                }
                SlimSlider slider = null;
                foreach (Control control in crosshair.Controls)
                    if (control.AccessibleName == "Crosshair size slider") slider = (SlimSlider)control;
                slider.Value = 16;
                typeof(SlimSlider).GetMethod("OnKeyDown", Fields).Invoke(slider, new object[] { new KeyEventArgs(Keys.Right) });
                Check(slider.Value == 17 && controller.Options.Size == 17, "Slider keyboard input must update the crosshair.");
                typeof(SlimSlider).GetMethod("OnKeyDown", Fields).Invoke(slider, new object[] { new KeyEventArgs(Keys.End) });
                Check(slider.Value == 32, "End must select the slider maximum.");
                typeof(SlimSlider).GetMethod("OnMouseDown", Fields).Invoke(slider,
                    new object[] { new MouseEventArgs(MouseButtons.Left, 1, 0, slider.Height / 2, 0) });
                typeof(SlimSlider).GetMethod("OnMouseUp", Fields).Invoke(slider,
                    new object[] { new MouseEventArgs(MouseButtons.Left, 1, 0, slider.Height / 2, 0) });
                Check(slider.Value == 2 && controller.Options.Size == 2, "Clicking the slider's left edge must select its minimum.");
                slider.AccessibilityObject.Value = "20";
                Check(slider.Value == 20, "The slider must expose its editable value to accessibility clients.");
                form.Close();
                Check(form.IsDisposed && controller.Overlay == null, "Closing the tabbed window must fully dispose its overlay.");
            }
            File.WriteAllText(Path.Combine(root, "compact-layout.txt"),
                "PASS: one settings window, independent overlay across tabs, no duplicated windows, controls contained at 100/150/200% scale, slider mouse/keyboard/accessibility input, full close.");
        }

        private static void CheckBounds(Control parent, int dpi)
        {
            foreach (Control control in parent.Controls)
            {
                Check(control.Left >= -1 && control.Top >= -1 && control.Right <= parent.ClientSize.Width + 2 && control.Bottom <= parent.ClientSize.Height + 2,
                    "Control exceeds its container at " + dpi + " DPI: " + control.GetType().Name + " / " + control.Text + " / " + control.Bounds + " in " + parent.ClientSize);
                if (parent is Form) continue; // Tabs deliberately share the same content area.
                foreach (Control other in parent.Controls)
                {
                    if (other == control || control.Width == 1 || control.Height == 1 || other.Width == 1 || other.Height == 1) continue;
                    Check(!control.Bounds.IntersectsWith(other.Bounds),
                        "Controls overlap at " + dpi + " DPI: " + control.Text + " / " + other.Text);
                }
            }
        }
    }
}
