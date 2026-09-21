using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Windows.Forms;

namespace Autoclicker
{
    internal enum CrosshairStyle { Dot, Cross, OpenCross, Circle, CircleDot }

    [DataContract]
    internal sealed class CrosshairOptions
    {
        [DataMember] internal CrosshairStyle Style = CrosshairStyle.Dot;
        [DataMember] internal int Size = 4;
        [DataMember] internal decimal Thickness = 1.5M;
        [DataMember] internal int Hue = 120;
        [DataMember] internal bool White = true;
        [DataMember] internal bool Outline = true;
        [DataMember] internal string Display = "";
        internal Color Color { get { return White ? Color.White : HueColor(Hue); } }

        [OnDeserializing]
        private void SetMissingDefaults(StreamingContext context)
        {
            // Older settings have no thickness member. Keep their original
            // 1.5 px stroke without resetting style, size, color or display.
            Thickness = 1.5M;
        }

        internal static Color HueColor(int hue)
        {
            double h = hue / 60.0;
            int x = (int)Math.Round(255 * (1 - Math.Abs(h % 2 - 1)));
            if (h < 1) return Color.FromArgb(255, x, 0);
            if (h < 2) return Color.FromArgb(x, 255, 0);
            if (h < 3) return Color.FromArgb(0, 255, x);
            if (h < 4) return Color.FromArgb(0, x, 255);
            if (h < 5) return Color.FromArgb(x, 0, 255);
            return Color.FromArgb(255, 0, x);
        }

        internal static CrosshairOptions Load(string path)
        {
            try
            {
                if (path == null || !File.Exists(path) || new FileInfo(path).Length > 4096) return new CrosshairOptions();
                using (Stream stream = File.OpenRead(path))
                {
                    var value = (CrosshairOptions)new DataContractJsonSerializer(typeof(CrosshairOptions)).ReadObject(stream);
                    if (value == null || !Enum.IsDefined(typeof(CrosshairStyle), value.Style) ||
                        value.Size < 2 || value.Size > 32 || value.Hue < 0 || value.Hue > 359)
                        return new CrosshairOptions();
                    if (value.Thickness < 0.5M || value.Thickness > 8M) value.Thickness = 1.5M;
                    value.Thickness = Decimal.Round(value.Thickness, 1);
                    return value;
                }
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is SerializationException)
            { return new CrosshairOptions(); }
        }

        internal bool Save(string path)
        {
            if (path == null) return true;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
                using (var stream = new MemoryStream())
                {
                    new DataContractJsonSerializer(typeof(CrosshairOptions)).WriteObject(stream, this);
                    File.WriteAllBytes(path, stream.ToArray());
                }
                return true;
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            { return false; }
        }
    }

    internal static class CrosshairDrawing
    {
        internal static Rectangle CenteredBounds(Rectangle screen)
        { return new Rectangle(screen.Left + screen.Width / 2 - 32, screen.Top + screen.Height / 2 - 32, 64, 64); }

        internal static void Draw(Graphics graphics, PointF center, CrosshairOptions options)
        {
            GraphicsState state = graphics.Save();
            try
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                float r = options.Size / 2F;
                using (var path = new GraphicsPath())
                {
                    if (options.Style == CrosshairStyle.Dot)
                        path.AddEllipse(center.X - r, center.Y - r, r * 2, r * 2);
                    else if (options.Style == CrosshairStyle.Circle || options.Style == CrosshairStyle.CircleDot)
                        path.AddEllipse(center.X - r, center.Y - r, r * 2, r * 2);
                    else
                    {
                        // Keep short arms visible at the size slider's minimum.
                        float gap = options.Style == CrosshairStyle.OpenCross ? Math.Min(r / 2F, Math.Max(1F, r / 3)) : 0;
                        AddLine(path, center.X - r, center.Y, center.X - gap, center.Y);
                        AddLine(path, center.X + gap, center.Y, center.X + r, center.Y);
                        AddLine(path, center.X, center.Y - r, center.X, center.Y - gap);
                        AddLine(path, center.X, center.Y + gap, center.X, center.Y + r);
                    }
                    using (var outline = new Pen(Color.FromArgb(220, 0, 0, 0), options.Style == CrosshairStyle.Dot ? 2F : (float)options.Thickness + 2F))
                    using (var color = new Pen(options.Color, (float)options.Thickness))
                    using (var fill = new SolidBrush(options.Color))
                    {
                        if (options.Outline) graphics.DrawPath(outline, path);
                        if (options.Style == CrosshairStyle.Dot) graphics.FillPath(fill, path);
                        else graphics.DrawPath(color, path);
                        if (options.Style == CrosshairStyle.CircleDot)
                        {
                            if (options.Outline)
                                using (var dotOutline = new Pen(Color.FromArgb(220, 0, 0, 0), 3.5F))
                                    graphics.DrawEllipse(dotOutline, center.X - 1, center.Y - 1, 2, 2);
                            graphics.FillEllipse(fill, center.X - 1, center.Y - 1, 2, 2);
                        }
                    }
                }
            }
            finally { graphics.Restore(state); }
        }

        private static void AddLine(GraphicsPath path, float x1, float y1, float x2, float y2)
        { path.StartFigure(); path.AddLine(x1, y1, x2, y2); }
    }

    // A tiny, disabled, nonactivating layered window. It has no input hook,
    // refresh loop, game integration, taskbar button, or separately running process.
    internal sealed class CrosshairOverlay : Form
    {
        private readonly CrosshairOptions options;
        internal CrosshairOverlay(CrosshairOptions value)
        {
            options = value;
            Text = "Autoclicker crosshair";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(64, 64);
            TopMost = true;
            Enabled = false; // WindowFromPoint must also skip this window.
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams parameters = base.CreateParams;
                parameters.ExStyle |= 0x80000 | 0x20 | 0x08000000 | 0x80; // Layered, transparent, no activate, tool window.
                return parameters;
            }
        }
        protected override void OnShown(EventArgs e) { base.OnShown(e); RefreshOverlay(); }
        protected override void OnPaintBackground(PaintEventArgs e) { }
        protected override void OnPaint(PaintEventArgs e) { }
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x84) { message.Result = new IntPtr(-1); return; } // HTTRANSPARENT
            if (message.Msg == 0x21) { message.Result = new IntPtr(3); return; } // MA_NOACTIVATE
            base.WndProc(ref message);
            if (message.Msg == 0x7E && Visible) RefreshOverlay(); // Monitor/resolution changed.
        }
        internal void RefreshOverlay()
        {
            Screen selected = Screen.PrimaryScreen;
            foreach (Screen screen in Screen.AllScreens)
                if (screen.DeviceName == options.Display) selected = screen;
            Rectangle bounds = CrosshairDrawing.CenteredBounds(selected.Bounds);
            Bounds = bounds;
            using (var bitmap = new Bitmap(64, 64, PixelFormat.Format32bppPArgb))
            {
                using (Graphics graphics = Graphics.FromImage(bitmap))
                    CrosshairDrawing.Draw(graphics, new PointF(32, 32), options);
                LayeredWindow.SetBitmap(Handle, bitmap, bounds.Location);
            }
        }
    }

    internal static class LayeredWindow
    {
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct Blend { internal byte Operation, Flags, Alpha, Format; }
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UpdateLayeredWindow(IntPtr window, IntPtr screen, ref Point location, ref Size size,
            IntPtr source, ref Point origin, int key, ref Blend blend, int flags);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr value);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr value);
        [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);

        internal static void SetBitmap(IntPtr window, Bitmap bitmap, Point location)
        {
            IntPtr dc = CreateCompatibleDC(IntPtr.Zero);
            IntPtr image = IntPtr.Zero, previous = IntPtr.Zero;
            try
            {
                if (dc == IntPtr.Zero) throw new System.ComponentModel.Win32Exception();
                image = bitmap.GetHbitmap(Color.FromArgb(0));
                previous = SelectObject(dc, image);
                Point origin = Point.Empty;
                Size size = bitmap.Size;
                var blend = new Blend { Alpha = 255, Format = 1 };
                if (!UpdateLayeredWindow(window, IntPtr.Zero, ref location, ref size, dc, ref origin, 0, ref blend, 2))
                    throw new System.ComponentModel.Win32Exception();
            }
            finally
            {
                if (previous != IntPtr.Zero) SelectObject(dc, previous);
                if (image != IntPtr.Zero) DeleteObject(image);
                if (dc != IntPtr.Zero) DeleteDC(dc);
            }
        }
    }

    internal sealed class CrosshairController : IDisposable
    {
        private readonly string path;
        internal readonly CrosshairOptions Options;
        internal CrosshairOverlay Overlay { get; private set; }
        internal bool Enabled { get { return Overlay != null && Overlay.Visible; } }
        internal event EventHandler Changed;
        internal CrosshairController(string settingsPath)
        {
            path = settingsPath == null ? null : settingsPath + ".crosshair.json";
            Options = CrosshairOptions.Load(path);
        }
        internal void SetEnabled(bool value)
        {
            if (value)
            {
                if (Overlay == null) Overlay = new CrosshairOverlay(Options);
                try { Overlay.RefreshOverlay(); Overlay.Show(); }
                catch { Overlay.Dispose(); Overlay = null; throw; }
            }
            else if (Overlay != null) Overlay.Hide();
            if (Changed != null) Changed(this, EventArgs.Empty);
        }
        internal void Refresh() { if (Enabled) Overlay.RefreshOverlay(); }
        internal bool Save() { return Options.Save(path); }
        public void Dispose()
        {
            if (Overlay != null) { Overlay.Dispose(); Overlay = null; }
            Save();
        }
    }

    internal sealed class CrosshairPreview : Control
    {
        internal readonly CrosshairOptions Options;
        internal CrosshairPreview(CrosshairOptions options)
        { Options = options; DoubleBuffered = true; BackColor = AppColors.Inset; }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var pen = new Pen(AppColors.Border))
            {
                e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
                e.Graphics.DrawLine(pen, Width / 2, 12, Width / 2, Height - 12);
                e.Graphics.DrawLine(pen, 12, Height / 2, Width - 12, Height / 2);
            }
            CrosshairDrawing.Draw(e.Graphics, new PointF(Width / 2F, Height / 2F), Options);
        }
    }

    internal sealed class DarkComboBox : ComboBox
    {
        internal DarkComboBox()
        {
            DropDownStyle = ComboBoxStyle.DropDownList;
            DrawMode = DrawMode.OwnerDrawFixed;
            FlatStyle = FlatStyle.Flat;
            BackColor = AppColors.Button;
            ForeColor = AppColors.Text;
        }
        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            using (var background = new SolidBrush((e.State & DrawItemState.Selected) != 0 ? AppColors.Raised : BackColor))
                e.Graphics.FillRectangle(background, e.Bounds);
            if (e.Index >= 0)
                TextRenderer.DrawText(e.Graphics, GetItemText(Items[e.Index]), Font, e.Bounds, ForeColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            e.DrawFocusRectangle();
        }
    }

}
