using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Autoclicker
{
    internal static class CrosshairProbe
    {
        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr window, int index);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(Point point);
        [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr window);
        private static readonly BindingFlags Fields = BindingFlags.NonPublic | BindingFlags.Instance;
        private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }

        internal static void Run(string root)
        {
            Check(CrosshairDrawing.CenteredBounds(new Rectangle(-1920, -120, 1920, 1080)).Location == new Point(-992, 388),
                "Crosshair must center on displays with negative coordinates using full display bounds.");
            string settings = Path.Combine(root, "crosshair-test.settings");
            using (var controller = new CrosshairController(settings))
            {
                Check(!controller.Enabled && controller.Options.Style == CrosshairStyle.Dot && controller.Options.White,
                    "First launch must start with an optional white dot, switched off.");
                IntPtr foreground = GetForegroundWindow();
                controller.SetEnabled(true);
                Application.DoEvents();
                CrosshairOverlay overlay = controller.Overlay;
                IntPtr handle = overlay.Handle;
                int styles = GetWindowLong(handle, -20);
                Check((styles & 0x080800A8) == 0x080800A8, "Overlay must be topmost, layered, transparent, nonactivating, and a tool window.");
                Check(GetForegroundWindow() == foreground, "Showing the crosshair must not steal focus.");
                Check(overlay.Owner == null && !overlay.Enabled && !overlay.ShowInTaskbar, "Overlay must be independent, disabled and absent from the taskbar.");
                Point center = new Point(overlay.Left + 32, overlay.Top + 32);
                Check(WindowFromPoint(center) != handle, "The autoclicker's cursor guard must see through the overlay.");
                Check(Native.SendMessage(handle, 0x84, IntPtr.Zero, IntPtr.Zero).ToInt32() == -1, "Overlay hit-testing must pass through.");
                controller.Options.Style = CrosshairStyle.OpenCross;
                controller.Options.Size = 20;
                controller.Options.Thickness = 3.5M;
                controller.Options.White = false;
                controller.Options.Hue = 120;
                controller.Refresh();
                Check(GetForegroundWindow() == foreground, "Changing the overlay must not steal focus.");
                controller.SetEnabled(false);
                Check(!overlay.Visible, "Toggling off must immediately hide the crosshair.");
                for (int i = 0; i < 8; i++) { controller.SetEnabled(true); controller.SetEnabled(false); }
                Check(controller.Overlay.Handle == handle, "Toggling should reuse the overlay window.");
                Check(controller.Save(), "Crosshair preferences must save.");
                controller.Dispose();
                Check(!IsWindow(handle), "Disposing must destroy the native overlay window.");
            }
            using (var reopened = new CrosshairController(settings))
            {
                Check(!reopened.Enabled && reopened.Options.Style == CrosshairStyle.OpenCross && reopened.Options.Size == 20 && reopened.Options.Thickness == 3.5M &&
                    reopened.Options.Color.ToArgb() == Color.Lime.ToArgb(), "Appearance must persist while visibility starts off.");
            }
            File.WriteAllText(settings + ".crosshair.json", "{\"Style\":3,\"Size\":24,\"Hue\":240,\"White\":false,\"Outline\":false,\"Display\":\"saved monitor\"}");
            var legacy = CrosshairOptions.Load(settings + ".crosshair.json");
            Check(legacy.Thickness == 1.5M && legacy.Style == CrosshairStyle.Circle && legacy.Size == 24 && legacy.Hue == 240 &&
                !legacy.White && !legacy.Outline && legacy.Display == "saved monitor", "Adding thickness must preserve all old appearance settings.");
            legacy.Thickness = 99;
            legacy.Save(settings + ".crosshair.json");
            var repaired = CrosshairOptions.Load(settings + ".crosshair.json");
            Check(repaired.Thickness == 1.5M && repaired.Style == CrosshairStyle.Circle && repaired.Size == 24,
                "Invalid thickness must recover without resetting other settings.");
            File.WriteAllText(settings + ".crosshair.json", "{bad json");
            Check(CrosshairOptions.Load(settings + ".crosshair.json").Size == 4, "Broken settings must recover to defaults.");
            File.WriteAllText(settings + ".crosshair.json", "{\"Size\":999,\"Hue\":900,\"Style\":50}");
            Check(CrosshairOptions.Load(settings + ".crosshair.json").Size == 4, "Invalid settings must recover to defaults.");

            using (var sheet = new Bitmap(640, 230))
            using (Graphics graphics = Graphics.FromImage(sheet))
            {
                graphics.Clear(AppColors.Background);
                foreach (CrosshairStyle style in Enum.GetValues(typeof(CrosshairStyle)))
                {
                    var options = new CrosshairOptions { Style = style, Size = style == CrosshairStyle.Dot ? 4 : 20 };
                    using (var bitmap = new Bitmap(64, 64, PixelFormat.Format32bppPArgb))
                    {
                        using (Graphics paint = Graphics.FromImage(bitmap)) CrosshairDrawing.Draw(paint, new PointF(32, 32), options);
                        Check(bitmap.GetPixel(0, 0).A == 0, "Overlay corners must be completely transparent.");
                        if (style == CrosshairStyle.OpenCross || style == CrosshairStyle.Circle)
                            Check(bitmap.GetPixel(32, 32).A == 0, "Open styles must leave the aim point clear.");
                        else Check(bitmap.GetPixel(32, 32).A > 0, "Dot and solid cross styles must mark the center.");
                        graphics.DrawImageUnscaled(bitmap, (int)style * 128 + 32, 35);
                    }
                    options.White = false;
                    options.Hue = (int)style * 70;
                    CrosshairDrawing.Draw(graphics, new PointF((int)style * 128 + 64, 154), options);
                    using (var font = new Font("Segoe UI", 9))
                    using (var brush = new SolidBrush(AppColors.Text))
                        graphics.DrawString(style.ToString(), font, brush, (int)style * 128 + 20, 194);
                }
                sheet.Save(Path.Combine(root, "crosshair-styles.png"), ImageFormat.Png);
            }

            using (var form = new ClickerForm(false))
            {
                form.Opacity = 0;
                form.ShowInTaskbar = false;
                form.Show();
                var controller = (CrosshairController)typeof(ClickerForm).GetField("crosshair", Fields).GetValue(form);
                var toggle = (CheckBox)typeof(ClickerForm).GetField("crosshairToggle", Fields).GetValue(form);
                var engine = (ClickEngine)typeof(ClickerForm).GetField("engine", Fields).GetValue(form);
                toggle.Checked = true;
                Check(controller.Enabled && !engine.Active, "Crosshair toggle must not start clicking.");
                Check(Native.FindMainWindow(System.Diagnostics.Process.GetCurrentProcess().Id) == form.Handle,
                    "Opening a second copy must find the main app rather than the topmost overlay.");
                IntPtr overlay = controller.Overlay.Handle;
                form.WindowState = FormWindowState.Minimized;
                Application.DoEvents();
                Check(controller.Enabled, "Minimizing the main window must preserve the overlay.");
                form.WindowState = FormWindowState.Normal;
                {
                    var dialog = (CrosshairSettingsControl)typeof(ClickerForm).GetField("crosshairSettings", Fields).GetValue(form);
                    ((Button)typeof(ClickerForm).GetField("crosshairTab", Fields).GetValue(form)).PerformClick();
                    Application.DoEvents();
                    using (var image = new Bitmap(form.Width, form.Height))
                    {
                        form.DrawToBitmap(image, new Rectangle(Point.Empty, form.Size));
                        image.Save(Path.Combine(root, "crosshair-settings.png"), ImageFormat.Png);
                    }
                    SlimSlider hue = null;
                    SlimSlider sizeSlider = null, thicknessSlider = null;
                    NumericUpDown size = null, thickness = null;
                    ComboBox style = null;
                    foreach (Control control in dialog.Controls)
                    {
                        if (control.AccessibleName == "Crosshair color hue") hue = (SlimSlider)control;
                        if (control.AccessibleName == "Crosshair size slider") sizeSlider = (SlimSlider)control;
                        if (control.AccessibleName == "Crosshair thickness slider") thicknessSlider = (SlimSlider)control;
                        if (control.AccessibleName == "Crosshair size in pixels") size = (NumericUpDown)control;
                        if (control.AccessibleName == "Crosshair thickness in pixels") thickness = (NumericUpDown)control;
                        if (control.AccessibleName == "Crosshair style") style = (ComboBox)control;
                    }
                    Check(!thickness.Enabled && !thicknessSlider.Enabled, "A solid dot uses size rather than line thickness.");
                    hue.Value = 240;
                    style.SelectedIndex = 4;
                    Check(controller.Options.Color.ToArgb() == Color.Blue.ToArgb() && controller.Options.Style == CrosshairStyle.CircleDot &&
                        controller.Options.Size == 16, "Color and style controls must update the live overlay.");
                    Check(sizeSlider.Value == 16 && thickness.Enabled && thicknessSlider.Enabled, "Changing style must synchronize size and enable stroke controls.");
                    sizeSlider.Value = 28;
                    Check(controller.Options.Size == 28 && size.Value == 28, "Size slider must update both the number and the live overlay.");
                    size.Value = 10;
                    Check(sizeSlider.Value == 10 && controller.Options.Size == 10, "Size input must update the slider.");
                    thicknessSlider.Value = 47;
                    Check(controller.Options.Thickness == 4.7M && thickness.Value == 4.7M, "Thickness slider must update the number and overlay.");
                    thickness.Value = 2.5M;
                    Check(thicknessSlider.Value == 25 && controller.Options.Thickness == 2.5M, "Thickness input must update the slider.");
                    style.SelectedIndex = 0;
                    Check(!thickness.Enabled && controller.Options.Thickness == 2.5M, "Dot selection must retain the chosen line thickness.");
                    style.SelectedIndex = 1;
                    Check(thickness.Enabled && thickness.Value == 2.5M, "Line styles must restore the chosen thickness.");
                    sizeSlider.Value = 32;
                    thicknessSlider.Value = 80;
                    using (var image = new Bitmap(form.Width, form.Height))
                    {
                        form.DrawToBitmap(image, new Rectangle(Point.Empty, form.Size));
                        image.Save(Path.Combine(root, "crosshair-thickness.png"), ImageFormat.Png);
                    }
                    controller.SetEnabled(false);
                    Check(!toggle.Checked, "Main toggle must stay in sync with settings.");
                    ((Button)typeof(ClickerForm).GetField("clickerTab", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form)).PerformClick();
                }
                toggle.Checked = true;
                form.Close();
                Check(!IsWindow(overlay) && form.IsDisposed, "Closing Autoclicker must destroy the crosshair and main form.");
            }
            foreach (CrosshairStyle style in Enum.GetValues(typeof(CrosshairStyle)))
            {
                long thin = Coverage(style, 0.5M), thick = Coverage(style, 8M);
                Check(style == CrosshairStyle.Dot ? thin == thick : thick > thin * 2,
                    "Thickness must visibly change lines and rings while leaving solid dot size unchanged: " + style);
                Check(Coverage(style, 0.5M, 2) > 0, "The size slider's minimum must still draw every style: " + style);
            }
            File.WriteAllText(Path.Combine(root, "crosshair.txt"), "PASS: five transparent styles, hue slider, persisted appearance, screen centering, native click-through, no focus theft, independent toggle, minimize and close cleanup.");
        }

        private static long Coverage(CrosshairStyle style, decimal thickness, int size = 32)
        {
            var options = new CrosshairOptions { Style = style, Size = size, Thickness = thickness, Outline = false };
            using (var image = new Bitmap(64, 64, PixelFormat.Format32bppPArgb))
            {
                using (Graphics paint = Graphics.FromImage(image)) CrosshairDrawing.Draw(paint, new PointF(32, 32), options);
                long coverage = 0;
                for (int y = 0; y < 64; y++)
                    for (int x = 0; x < 64; x++)
                    {
                        int alpha = image.GetPixel(x, y).A;
                        if (x == 0 || y == 0 || x == 63 || y == 63)
                            Check(alpha == 0, "The largest/thickest crosshair must fit without clipping.");
                        coverage += alpha;
                    }
                return coverage;
            }
        }
    }
}
