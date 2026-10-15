using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Autoclicker
{
    internal sealed class ClickPulse : Control
    {
        private bool lit;
        internal bool Lit
        {
            get { return lit; }
            set { if (lit == value) return; lit = value; Invalidate(); }
        }

        internal ClickPulse()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;
            AccessibleRole = AccessibleRole.Indicator;
            AccessibleName = "Click activity";
            AccessibleDescription = "Pulses when Windows accepts a mouse press. This does not confirm that a game registered it.";
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            float size = System.Math.Min(Width, Height);
            var bounds = new RectangleF((Width - size) / 2 + size * .2F,
                (Height - size) / 2 + size * .2F, size * .6F, size * .6F);
            if (lit)
            {
                using (var halo = new SolidBrush(Color.FromArgb(38, 71, 53)))
                    e.Graphics.FillEllipse(halo, (Width - size) / 2F, (Height - size) / 2F, size, size);
            }
            using (var brush = new SolidBrush(lit ? Color.FromArgb(116, 218, 158) : AppColors.DisabledText))
                e.Graphics.FillEllipse(brush, bounds);
        }
    }
}
