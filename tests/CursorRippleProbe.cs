using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Autoclicker
{
    internal static class CursorRippleProbe
    {
        private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr window, int index);
        [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(Point point);
        [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr window);
        [StructLayout(LayoutKind.Sequential)] private struct WindowRectangle { internal int Left, Top, Right, Bottom; }
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out WindowRectangle bounds);
        private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
        private static T Field<T>(ClickerForm form, string name)
        { return (T)typeof(ClickerForm).GetField(name, Fields).GetValue(form); }
        private static void Refresh(ClickerForm form)
        { typeof(ClickerForm).GetMethod("RefreshClickActivity", Fields).Invoke(form, null); }
        private static void CheckLocation(CursorPulseOverlay ring)
        {
            WindowRectangle bounds;
            Check(GetWindowRect(ring.Handle, out bounds), "The ring must have a real overlay window.");
            Check(new Point(bounds.Left + CursorPulseOverlay.PulseSize / 2, bounds.Top + CursorPulseOverlay.PulseSize / 2) == ring.Origin,
                "The displayed ring must be centered exactly on the click point.");
        }

        internal static void Run(string root)
        {
            Check(CursorPulseFeedback.HueIndexAt(0) == CursorPulseFeedback.HueIndexAt(CursorPulseFeedback.ColorCycleMs) &&
                CursorPulseFeedback.HueIndexAt(-1) == CursorPulseFeedback.HueIndexAt(CursorPulseFeedback.ColorCycleMs - 1),
                "The spectrum must wrap cleanly at the cycle boundary.");
            Color red = CursorPulseFeedback.SpectrumColor(CursorPulseFeedback.HueIndexAt(0));
            Color green = CursorPulseFeedback.SpectrumColor(CursorPulseFeedback.HueIndexAt(2000));
            Color blue = CursorPulseFeedback.SpectrumColor(CursorPulseFeedback.HueIndexAt(4000));
            Check(red.R > red.G && red.R > red.B && green.G > green.R && green.G > green.B && blue.B > blue.R && blue.B > blue.G,
                "Each color cycle must pass through red, green, and blue.");
            foreach (Point point in new[] { new Point(0, 0), new Point(-1920, -120), new Point(3839, 2159) })
                Check(CursorPulseOverlay.CenteredLocation(point) + new Size(32, 32) == point,
                    "Screen edges and negative coordinates must retain the exact click origin.");
            using (var preview = new Bitmap(400, 92))
            using (Graphics graphics = Graphics.FromImage(preview))
            using (var font = new Font("Segoe UI", 9))
            using (var brush = new SolidBrush(AppColors.Text))
            {
                graphics.Clear(AppColors.Background);
                double previousRadius = 0;
                int previousAlpha = 256;
                for (int i = 0; i <= 4; i++)
                using (Bitmap frame = CursorPulseFeedback.CreateFrame(i / 4F))
                {
                    long alphaSum = 0;
                    double radiusSum = 0;
                    int maxAlpha = 0;
                    for (int y = 0; y < frame.Height; y++)
                    for (int x = 0; x < frame.Width; x++)
                    {
                        int alpha = frame.GetPixel(x, y).A;
                        alphaSum += alpha;
                        radiusSum += alpha * Math.Sqrt(Math.Pow(x - 32, 2) + Math.Pow(y - 32, 2));
                        maxAlpha = Math.Max(maxAlpha, alpha);
                    }
                    Check(frame.GetPixel(0, 0).A == 0, "The ring must have a transparent background.");
                    if (i < 4)
                    {
                        double radius = radiusSum / alphaSum;
                        Check(radius > previousRadius && maxAlpha < previousAlpha,
                            "The glow must expand outward while becoming more transparent.");
                        previousRadius = radius;
                        previousAlpha = maxAlpha;
                    }
                    else Check(alphaSum == 0, "The final frame must dissipate completely.");
                    graphics.DrawImageUnscaled(frame, i * 80 + 8, 0);
                    graphics.DrawString((i * 45) + " ms", font, brush, i * 80 + 17, 68);
                }
                preview.Save(Path.Combine(root, "cursor-ripple.png"), ImageFormat.Png);
            }

            using (var feedback = new CursorPulseFeedback())
            {
                Check(ReferenceEquals(feedback.GetFrame(0, 4), feedback.GetFrame(0, 4)), "The same color and animation phase must reuse its cached bitmap.");
                IntPtr foreground = GetForegroundWindow();
                Point first = new Point(Screen.PrimaryScreen.Bounds.Left + 150, Screen.PrimaryScreen.Bounds.Top + 150);
                Point second = first + new Size(70, 45);
                feedback.Update(0, first, false);
                Check(feedback.Rings.Count == 0, "No accepted click means no ring or overlay window.");
                feedback.Update(0, first, true);
                CursorPulseOverlay a = feedback.Rings[0];
                Check(a.Active && a.Visible && a.FrameIndex == 0, "The first click must show a ring immediately.");
                CheckLocation(a);
                Check((GetWindowLong(a.Handle, -20) & 0x080800A8) == 0x080800A8 && !a.Enabled && !a.ShowInTaskbar && a.Owner == null,
                    "Rings must be topmost, layered, transparent, disabled, nonactivating and independent.");
                Check(WindowFromPoint(first + new Size(3, 0)) != a.Handle, "The own-window guard must see through an opaque part of the glow.");
                Check(Native.SendMessage(a.Handle, 0x84, IntPtr.Zero, IntPtr.Zero).ToInt32() == -1 &&
                    Native.SendMessage(a.Handle, 0x21, IntPtr.Zero, IntPtr.Zero).ToInt32() == 3,
                    "Rings must pass mouse hit tests through and refuse activation.");
                feedback.Update(45, second, false);
                Check(a.FrameIndex > 0 && a.Origin == first, "A moving cursor must not drag an existing ripple away from its click origin.");
                CheckLocation(a);
                feedback.Update(50, second, true);
                Check(feedback.Rings.Count == 2 && a.Active && a.StartedAt == 0 && feedback.Rings[1].Origin == second,
                    "Rapid clicks must animate independent rings without restarting the previous one.");
                CheckLocation(feedback.Rings[1]);
                feedback.Update(180, second, false);
                Check(!a.Active && !a.Visible && feedback.Rings[1].Active, "Each ring must expire on its own deadline.");
                feedback.Update(230, second, false);
                Check(feedback.Rings.All(r => !r.Visible && !r.Active), "All rings must disappear after they dissipate.");
                IntPtr reused = a.Handle;
                feedback.Update(300, second, true);
                Check(feedback.Rings.Count == 2 && feedback.Rings[0].Handle == reused &&
                    feedback.Rings[0].HueIndex == CursorPulseFeedback.HueIndexAt(300),
                    "Reused windows must start in the current spectrum color.");
                int heldHue = feedback.Rings[0].HueIndex;
                feedback.Update(340, first, false);
                Check(feedback.Rings[0].HueIndex == heldHue, "Each expanding ring must keep its birth color without flickering between cached palettes.");
                for (int t = 341; t < 390; t++) feedback.Update(t, first, true);
                Check(feedback.Rings.Count <= CursorPulseFeedback.MaxRings, "Even extreme click bursts must use a bounded window pool.");
                Check(GetForegroundWindow() == foreground, "Animating rings must not steal focus.");
                feedback.Hide();
                Check(feedback.Rings.All(r => !r.Visible && !r.Active), "Stopping must hide every ring immediately.");
                var handles = feedback.Rings.Select(r => r.Handle).ToArray();
                feedback.Dispose();
                Check(handles.All(h => !IsWindow(h)), "Disposal must destroy all ring windows.");
            }

            using (var form = new ClickerForm(false))
            {
                form.Opacity = 0;
                form.ShowInTaskbar = false;
                bool down = false;
                var engine = new ClickEngine(delegate { down = true; }, delegate { down = false; });
                typeof(ClickerForm).GetField("engine", Fields).SetValue(form, engine);
                form.Show();
                engine.Start(0, 50, 0);
                Refresh(form);
                Check(Field<CursorPulseFeedback>(form, "cursorPulse") == null, "Starting without sending a click must not create a ripple.");
                engine.Tick(0, false);
                Refresh(form);
                var feedback = Field<CursorPulseFeedback>(form, "cursorPulse");
                Check(feedback.Rings.Count == 1 && feedback.Rings[0].Origin == Cursor.Position, "Accepted presses must emit at the actual cursor tip.");
                Refresh(form);
                Check(feedback.Rings.Count == 1, "UI refreshes must not duplicate a click ripple.");
                form.WindowState = FormWindowState.Minimized;
                Application.DoEvents();
                Check(feedback.Rings[0].Visible, "Minimizing Autoclicker must not hide the ring overlay.");
                form.WindowState = FormWindowState.Normal;
                Field<Button>(form, "stop").PerformClick();
                Check(!down && feedback.Rings.All(r => !r.Visible), "Stop must release input and hide all rings.");
                engine.Start(100, 50, 0, false, 10);
                engine.Tick(100, false);
                Refresh(form);
                engine.Tick(110, false);
                Refresh(form);
                Check(!down && feedback.Rings.All(r => !r.Visible), "Expiry must release input and hide all rings.");
                var failure = new ClickEngine(delegate { throw new InvalidOperationException(); }, delegate { });
                typeof(ClickerForm).GetField("engine", Fields).SetValue(form, failure);
                failure.Start(0, 50, 0);
                try { failure.Tick(0, false); } catch (InvalidOperationException) { }
                Refresh(form);
                Check(feedback.Rings.All(r => !r.Visible), "A rejected press must not emit a ripple.");
                typeof(ClickerForm).GetField("engine", Fields).SetValue(form, engine);
                engine.Start(200, 50, 0);
                engine.Tick(200, false);
                Refresh(form);
                var handles = feedback.Rings.Select(r => r.Handle).ToArray();
                form.Close();
                Check(!down && form.IsDisposed && handles.All(h => !IsWindow(h)), "Closing must release input and destroy every overlay.");
            }
            File.WriteAllText(Path.Combine(root, "cursor-ripple.txt"),
                "PASS: RGB spectrum cycle and wraparound, stable color per ring, cached color-frame reuse, expanding/fading transparent glow, complete dissipation, exact click origins, independent overlapping ripples, click-through/no-focus windows, bounded window reuse, minimized visibility, no duplicate or rejected-click ripples, stop/expiry/close cleanup. No real mouse or keyboard input was sent.");
        }
    }
}
