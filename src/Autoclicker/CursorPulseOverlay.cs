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
        private readonly List<CursorPulseOverlay> rings = new List<CursorPulseOverlay>();
        private readonly Bitmap[] frames = new Bitmap[FrameCount];
        internal IList<CursorPulseOverlay> Rings { get { return rings.AsReadOnly(); } }

        internal CursorPulseFeedback()
        {
            try
            {
                for (int i = 0; i < frames.Length; i++)
                    frames[i] = CreateFrame(i / (float)(FrameCount - 1));
            }
            catch { Dispose(); throw; }
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
            available.Begin(cursor, now);
            Animate(available, now);
        }

        private void Animate(CursorPulseOverlay ring, long now)
        {
            if (!ring.Active) return;
            long elapsed = Math.Max(0, now - ring.StartedAt);
            if (elapsed >= DurationMs) { ring.Stop(); return; }
            int frame = (int)(elapsed * (FrameCount - 1) / DurationMs);
            ring.ShowFrame(frames[frame], frame);
        }

        internal void Hide()
        {
            foreach (CursorPulseOverlay ring in rings) ring.Stop();
        }

        internal static Bitmap CreateFrame(float progress)
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
                foreach (int width in new[] { 10, 6, 2 })
                {
                    int alpha = (int)((width == 2 ? 240 : width == 6 ? 55 : 20) * fade);
                    using (var pen = new Pen(Color.FromArgb(alpha, 116, 245, 166), width))
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

        internal void Begin(Point cursor, long now)
        {
            Origin = cursor;
            StartedAt = now;
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
