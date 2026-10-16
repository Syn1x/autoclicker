using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace Autoclicker
{
    // Each ring stays at its click location while newer rings can expand
    // independently. A bounded pool and shared frames avoid per-tick drawing.
    internal sealed class CursorPulseFeedback : IDisposable
    {
        internal const int DurationMs = 180;
        internal const int FrameCount = 13;
        internal const int MaxRings = 12;
        internal const int HueSteps = 72;
        internal const int ColorCycleMs = 6000;
        private readonly List<CursorPulseOverlay> rings = new List<CursorPulseOverlay>();
        // Five-degree hue steps, populated lazily: at most ~15 MiB of pixels.
        // Once cached, animation and repeated color cycles only upload frames.
        private readonly Bitmap[,] frames = new Bitmap[HueSteps, FrameCount];
        internal IList<CursorPulseOverlay> Rings { get { return rings.AsReadOnly(); } }

        internal static int HueIndexAt(long now)
        {
            long phase = ((now % ColorCycleMs) + ColorCycleMs) % ColorCycleMs;
            return (int)(phase * HueSteps / ColorCycleMs);
        }

        internal static Color SpectrumColor(int hueIndex)
        {
            Color hue = CrosshairOptions.HueColor(hueIndex * 360 / HueSteps);
            // Lift the darkest channels slightly so blue remains visible too.
            return Color.FromArgb(60 + hue.R * 195 / 255, 60 + hue.G * 195 / 255, 60 + hue.B * 195 / 255);
        }

        internal Bitmap GetFrame(int hueIndex, int frame)
        {
            if (frames[hueIndex, frame] == null)
                frames[hueIndex, frame] = CreateFrame(frame / (float)(FrameCount - 1), hueIndex);
            return frames[hueIndex, frame];
        }

        internal void Update(long now, Point cursor, bool emit)
        {
            foreach (CursorPulseOverlay ring in rings) Animate(ring, now);
            if (!emit) return;
            CursorPulseOverlay available = null;
            foreach (CursorPulseOverlay ring in rings)
                if (!ring.Active) { available = ring; break; }
            if (available == null)
            {
                if (rings.Count < MaxRings)
                {
                    available = new CursorPulseOverlay();
                    rings.Add(available);
                }
                else
                {
                    available = rings[0];
                    foreach (CursorPulseOverlay ring in rings)
                        if (ring.StartedAt < available.StartedAt) available = ring;
                }
            }
            available.Begin(cursor, now, HueIndexAt(now));
            Animate(available, now);
        }

        private void Animate(CursorPulseOverlay ring, long now)
        {
            if (!ring.Active) return;
            long elapsed = Math.Max(0, now - ring.StartedAt);
            if (elapsed >= DurationMs) { ring.Stop(); return; }
            int frame = (int)(elapsed * (FrameCount - 1) / DurationMs);
            ring.ShowFrame(GetFrame(ring.HueIndex, frame), frame);
        }

        internal void Hide()
        {
            foreach (CursorPulseOverlay ring in rings) ring.Stop();
        }

        internal static Bitmap CreateFrame(float progress, int hueIndex = 0)
        {
            progress = Math.Max(0, Math.Min(1, progress));
            var image = new Bitmap(CursorPulseOverlay.PulseSize, CursorPulseOverlay.PulseSize, PixelFormat.Format32bppPArgb);
            if (progress >= 1) return image;
            using (Graphics graphics = Graphics.FromImage(image))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                float radius = 2 + 22 * progress;
                float center = CursorPulseOverlay.PulseSize / 2F;
                var bounds = new RectangleF(center - radius, center - radius, radius * 2, radius * 2);
                float fade = (float)Math.Pow(1 - progress, 1.5);
                Color color = SpectrumColor(hueIndex);
                foreach (int width in new[] { 10, 6, 2 })
                {
                    int alpha = (int)((width == 2 ? 240 : width == 6 ? 55 : 20) * fade);
                    using (var pen = new Pen(Color.FromArgb(alpha, color), width))
                        graphics.DrawEllipse(pen, bounds);
                }
            }
            return image;
        }

        public void Dispose()
        {
            foreach (CursorPulseOverlay ring in rings) ring.Dispose();
            rings.Clear();
            foreach (Bitmap frame in frames) if (frame != null) frame.Dispose();
        }
    }

    // Independent, disabled, click-through windows remain visible when the
    // main form is minimized without stealing focus or moving the real cursor.
    internal sealed class CursorPulseOverlay : Form
    {
        internal const int PulseSize = 64;
        internal bool Active { get; private set; }
        internal long StartedAt { get; private set; }
        internal Point Origin { get; private set; }
        internal int HueIndex { get; private set; }
        internal int FrameIndex { get; private set; } = -1;

        internal CursorPulseOverlay()
        {
            Text = "Autoclicker click indicator";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(PulseSize, PulseSize);
            TopMost = true;
            Enabled = false;
        }

        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams parameters = base.CreateParams;
                parameters.ExStyle |= 0x80000 | 0x20 | 0x08000000 | 0x80;
                return parameters;
            }
        }
        protected override void OnPaintBackground(PaintEventArgs e) { }
        protected override void OnPaint(PaintEventArgs e) { }
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x84) { message.Result = new IntPtr(-1); return; }
            if (message.Msg == 0x21) { message.Result = new IntPtr(3); return; }
            base.WndProc(ref message);
        }
        protected override void OnHandleDestroyed(EventArgs e)
        {
            FrameIndex = -1;
            base.OnHandleDestroyed(e);
        }

        internal void Begin(Point cursor, long now, int hueIndex)
        {
            Origin = cursor;
            StartedAt = now;
            HueIndex = hueIndex;
            FrameIndex = -1;
            Active = true;
            Bounds = new Rectangle(CenteredLocation(cursor), new Size(PulseSize, PulseSize));
        }

        internal void ShowFrame(Bitmap image, int index)
        {
            if (FrameIndex != index)
            {
                LayeredWindow.SetBitmap(Handle, image, CenteredLocation(Origin));
                FrameIndex = index;
            }
            if (!Visible) Show();
        }

        internal void Stop() { Active = false; Hide(); }

        internal static Point CenteredLocation(Point cursor)
        {
            // Preserve the exact click point at edges and negative coordinates.
            return new Point(cursor.X - PulseSize / 2, cursor.Y - PulseSize / 2);
        }
    }
}
