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
                Check(!reopened.Enabled && reopened.Options.Style == CrosshairStyle.OpenCross && reopened.Options.Size == 20 &&
                    reopened.Options.Color.ToArgb() == Color.Lime.ToArgb(), "Appearance must persist while visibility starts off.");
            }
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
                using (var dialog = new CrosshairSettingsForm(controller))
                {
                    dialog.Opacity = 0;
                    dialog.Show(form);
                    Application.DoEvents();
                    using (var image = new Bitmap(dialog.Width, dialog.Height))
                    {
                        dialog.DrawToBitmap(image, new Rectangle(Point.Empty, dialog.Size));
                        image.Save(Path.Combine(root, "crosshair-settings.png"), ImageFormat.Png);
                    }
                    TrackBar hue = null;
                    ComboBox style = null;
                    foreach (Control control in dialog.Controls)
                    {
                        if (control is TrackBar) hue = (TrackBar)control;
                        if (control.AccessibleName == "Crosshair style") style = (ComboBox)control;
                    }
                    hue.Value = 240;
                    style.SelectedIndex = 4;
                    Check(controller.Options.Color.ToArgb() == Color.Blue.ToArgb() && controller.Options.Style == CrosshairStyle.CircleDot &&
                        controller.Options.Size == 16, "Color and style controls must update the live overlay.");
                    controller.SetEnabled(false);
                    Check(!toggle.Checked, "Main toggle must stay in sync with settings.");
                    dialog.Close();
                }
                toggle.Checked = true;
                form.Close();
                Check(!IsWindow(overlay) && form.IsDisposed, "Closing Autoclicker must destroy the crosshair and main form.");
            }
            File.WriteAllText(Path.Combine(root, "crosshair.txt"), "PASS: five transparent styles, hue slider, persisted appearance, screen centering, native click-through, no focus theft, independent toggle, minimize and close cleanup.");
        }
    }
}
