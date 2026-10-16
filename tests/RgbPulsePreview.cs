using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace Autoclicker
{
    // Offline preview: shares production color/frame rendering, never creates
    // overlay windows or installs hooks, and sends no mouse or keyboard input.
    internal static class RgbPulsePreview
    {
        internal static void Render(string directory)
        {
            Directory.CreateDirectory(directory);
            using (var feedback = new CursorPulseFeedback())
            using (var heading = new Font("Segoe UI", 17, FontStyle.Regular))
            using (var caption = new Font("Segoe UI", 9))
            using (var text = new SolidBrush(AppColors.Text))
            using (var muted = new SolidBrush(AppColors.MutedText))
            using (var divider = new Pen(AppColors.Border))
            {
                for (int now = 0; now < CursorPulseFeedback.ColorCycleMs; now += 20)
                using (var image = new Bitmap(620, 300))
                using (Graphics graphics = Graphics.FromImage(image))
                {
                    graphics.Clear(AppColors.Background);
                    graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    graphics.DrawString("RGB click pulse", heading, text, 28, 20);
                    graphics.DrawString("20 clicks / sec   ·   One rainbow cycle every 6 seconds", caption, muted, 30, 58);
                    graphics.DrawLine(divider, 310, 104, 310, 250);
                    DrawSample(graphics, feedback, now, new Point(160, 173), 1);
                    DrawSample(graphics, feedback, now, new Point(458, 173), 3);
                    graphics.DrawString("Actual size", caption, muted, 128, 253);
                    graphics.DrawString("3× detail", caption, muted, 435, 253);
                    image.Save(Path.Combine(directory, "frame-" + (now / 20).ToString("D4") + ".png"), ImageFormat.Png);
                }
            }
        }

        private static void DrawSample(Graphics graphics, CursorPulseFeedback feedback, int now, Point origin, float scale)
        {
            GraphicsState state = graphics.Save();
            try
            {
                graphics.TranslateTransform(origin.X, origin.Y);
                graphics.ScaleTransform(scale, scale);
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                // Include the preceding cycle's tail so the GIF loops seamlessly.
                for (int started = (now / 50) * 50 - 150; started <= now; started += 50)
                {
                    int age = now - started;
                    if (age >= CursorPulseFeedback.DurationMs) continue;
                    int frame = age * (CursorPulseFeedback.FrameCount - 1) / CursorPulseFeedback.DurationMs;
                    Bitmap ring = feedback.GetFrame(CursorPulseFeedback.HueIndexAt(started), frame);
                    graphics.DrawImage(ring, -32, -32, 64, 64);
                }
                // A neutral cursor sketch marks the click hotspot at (0, 0).
                PointF[] pointer = { new PointF(0, 0), new PointF(1, 16), new PointF(5, 12),
                    new PointF(8, 19), new PointF(11, 18), new PointF(8, 11), new PointF(14, 11) };
                using (var outline = new Pen(Color.FromArgb(20, 20, 20), 1.5F))
                using (var fill = new SolidBrush(Color.FromArgb(235, 235, 235)))
                {
                    graphics.FillPolygon(fill, pointer);
                    graphics.DrawPolygon(outline, pointer);
                }
            }
            finally { graphics.Restore(state); }
        }
    }
}
