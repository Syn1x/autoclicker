using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Autoclicker
{
    internal sealed class SlimSlider : Control
    {
        private int minimum, maximum = 100, value;
        internal int Minimum { get { return minimum; } set { minimum = value; Value = this.value; Invalidate(); } }
        internal int Maximum { get { return maximum; } set { maximum = value; Value = this.value; Invalidate(); } }
        internal int LargeChange { get; set; } = 5;
        internal bool HueTrack { get; set; }
        internal int Value
        {
            get { return value; }
            set
            {
                int next = Math.Max(minimum, Math.Min(maximum, value));
                if (this.value == next) return;
                this.value = next;
                Invalidate();
                if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
                AccessibilityNotifyClients(AccessibleEvents.ValueChange, -1);
            }
        }
        internal event EventHandler ValueChanged;
        internal SlimSlider()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            TabStop = true;
            AccessibleRole = AccessibleRole.Slider;
            Cursor = Cursors.Hand;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            float scale = e.Graphics.DpiX / 96F, padding = 8 * scale, y = Height / 2F;
            float width = Math.Max(1, Width - padding * 2);
            float x = padding + width * (Value - Minimum) / Math.Max(1F, Maximum - Minimum);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var track = new Pen(AppColors.ControlBorder, 3 * scale))
            {
                track.StartCap = track.EndCap = LineCap.Round;
                e.Graphics.DrawLine(track, padding, y, padding + width, y);
            }
            if (HueTrack && Enabled)
            {
                using (var gradient = new LinearGradientBrush(new RectangleF(padding, y - 2 * scale, width, 4 * scale), Color.Red, Color.Red, 0F))
                {
                    gradient.InterpolationColors = new ColorBlend { Colors = new[] { Color.Red, Color.Yellow, Color.Lime, Color.Cyan, Color.Blue, Color.Magenta, Color.Red },
                        Positions = new[] { 0F, 1F / 6, 2F / 6, 3F / 6, 4F / 6, 5F / 6, 1F } };
                    using (var pen = new Pen(gradient, 4 * scale)) e.Graphics.DrawLine(pen, padding, y, padding + width, y);
                }
            }
            else if (Enabled)
                using (var progress = new Pen(AppColors.MutedText, 3 * scale)) e.Graphics.DrawLine(progress, padding, y, x, y);
            float radius = 5 * scale;
            using (var fill = new SolidBrush(Enabled ? AppColors.Selection : AppColors.DisabledText))
                e.Graphics.FillEllipse(fill, x - radius, y - radius, radius * 2, radius * 2);
            if (Focused && ShowFocusCues)
                ControlPaint.DrawFocusRectangle(e.Graphics, new Rectangle(1, 1, Width - 3, Height - 3), ForeColor, BackColor);
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left || !Enabled) return;
            Focus(); Capture = true; MoveThumb(e.X);
        }
        protected override void OnMouseMove(MouseEventArgs e)
        { base.OnMouseMove(e); if (Capture && Enabled) MoveThumb(e.X); }
        protected override void OnMouseUp(MouseEventArgs e)
        { base.OnMouseUp(e); if (e.Button == MouseButtons.Left) Capture = false; }
        private void MoveThumb(int x)
        {
            float padding = 8F * DeviceDpi / 96F;
            Value = Minimum + (int)Math.Round((Maximum - Minimum) * (x - padding) / Math.Max(1F, Width - 2 * padding));
        }
        protected override bool IsInputKey(Keys keyData)
        {
            Keys key = keyData & Keys.KeyCode;
            return key == Keys.Left || key == Keys.Right || key == Keys.Up || key == Keys.Down || key == Keys.Home || key == Keys.End ||
                key == Keys.PageUp || key == Keys.PageDown || base.IsInputKey(keyData);
        }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            switch (e.KeyCode)
            {
                case Keys.Left: case Keys.Down: Value--; break;
                case Keys.Right: case Keys.Up: Value++; break;
                case Keys.PageDown: Value -= LargeChange; break;
                case Keys.PageUp: Value += LargeChange; break;
                case Keys.Home: Value = Minimum; break;
                case Keys.End: Value = Maximum; break;
                default: base.OnKeyDown(e); return;
            }
            e.Handled = e.SuppressKeyPress = true;
            base.OnKeyDown(e);
        }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
        protected override AccessibleObject CreateAccessibilityInstance() { return new SliderAccessibility(this); }
        private sealed class SliderAccessibility : ControlAccessibleObject
        {
            private readonly SlimSlider slider;
            internal SliderAccessibility(SlimSlider value) : base(value) { slider = value; }
            public override string Value
            {
                get { return slider.Value.ToString(); }
                set { int next; if (Int32.TryParse(value, out next)) slider.Value = next; }
            }
        }
    }
}
