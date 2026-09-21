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

    internal sealed class HueStrip : Control
    {
        internal HueStrip() { DoubleBuffered = true; }
        protected override void OnPaint(PaintEventArgs e)
        {
            for (int x = 0; x < Width; x++)
                using (var pen = new Pen(CrosshairOptions.HueColor(x * 359 / Math.Max(1, Width - 1))))
                    e.Graphics.DrawLine(pen, x, 0, x, Height);
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

    internal sealed class CrosshairSettingsForm : Form
    {
        private readonly CrosshairController controller;
        private readonly LargeCheckBox enabled;
        private readonly CrosshairPreview preview;
        private readonly Label colorValue;
        private readonly Label hint;
        internal CrosshairSettingsForm(CrosshairController value)
        {
            controller = value;
            SuspendLayout();
            Text = "Crosshair settings";
            ClientSize = new Size(440, 534);
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            BackColor = AppColors.Background;
            ForeColor = AppColors.Text;
            Font = new Font("Segoe UI", 9F);
            Label title = Label("Crosshair", 20, 19, 310, 34, 18F);
            title.MouseDown += delegate(object sender, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left) { Native.ReleaseCapture(); Native.SendMessage(Handle, 0xA1, new IntPtr(2), IntPtr.Zero); }
            };
            Button close = Button("X", 388, 20, 32);
            close.AccessibleName = "Close crosshair settings";
            close.Click += delegate { Close(); };
            enabled = Check("Show crosshair", 20, 64, 252, controller.Enabled);
            enabled.CheckedChanged += delegate
            {
                if (enabled.Checked == controller.Enabled) return;
                try { controller.SetEnabled(enabled.Checked); }
                catch (System.ComponentModel.Win32Exception) { enabled.Checked = false; hint.Text = "Windows could not display the overlay. Try again."; }
            };
            controller.Changed += SyncEnabled;

            Label("STYLE", 20, 109, 200, 20, 8F);
            var style = new DarkComboBox();
            style.Items.AddRange(new object[] { "Dot", "Cross", "Open cross", "Circle", "Circle + dot" });
            style.SetBounds(20, 133, 230, 28);
            style.AccessibleName = "Crosshair style";
            style.SelectedIndex = (int)controller.Options.Style;
            Controls.Add(style);
            preview = new CrosshairPreview(controller.Options);
            preview.SetBounds(272, 111, 148, 108);
            Controls.Add(preview);
            Label("SIZE", 20, 173, 60, 20, 8F);
            var size = new NumericInput { Minimum = 2, Maximum = 32, Value = controller.Options.Size,
                BackColor = AppColors.Inset, ForeColor = ForeColor, BorderStyle = BorderStyle.FixedSingle };
            size.SetBounds(80, 172, 74, 28);
            size.AccessibleName = "Crosshair size in pixels";
            Controls.Add(size);
            Label("px", 166, 174, 50, 20, 9F);
            var sizeSlider = Slider(12, 202, 245, 2, 32, controller.Options.Size, "Crosshair size slider");
            Label("THICKNESS", 20, 249, 90, 20, 8F);
            var thickness = new NumericInput { Minimum = 0.5M, Maximum = 8M, DecimalPlaces = 1, Increment = 0.5M,
                Value = controller.Options.Thickness, BackColor = AppColors.Inset, ForeColor = ForeColor,
                BorderStyle = BorderStyle.FixedSingle, AccessibleName = "Crosshair thickness in pixels" };
            thickness.SetBounds(112, 247, 74, 28);
            Controls.Add(thickness);
            Label("px", 198, 249, 25, 20, 9F);
            var thicknessSlider = Slider(220, 247, 208, 5, 80, (int)(controller.Options.Thickness * 10), "Crosshair thickness slider");
            thicknessSlider.LargeChange = 5;
            Label thicknessHint = Label("", 20, 282, 400, 20, 8F);
            thicknessHint.ForeColor = AppColors.MutedText;
            Action refreshThicknessControls = delegate
            {
                thickness.Enabled = thicknessSlider.Enabled = controller.Options.Style != CrosshairStyle.Dot;
                thicknessHint.Text = thickness.Enabled ? "Line and ring width / 0.5 - 8 px" : "Dot diameter is controlled by Size.";
            };
            refreshThicknessControls();
            style.SelectedIndexChanged += delegate
            {
                controller.Options.Style = (CrosshairStyle)style.SelectedIndex;
                if (style.SelectedIndex != 0 && size.Value < 8) size.Value = 16;
                refreshThicknessControls();
                ApplyAppearance();
            };
            size.ValueChanged += delegate
            {
                controller.Options.Size = (int)size.Value;
                sizeSlider.Value = controller.Options.Size;
                ApplyAppearance();
            };
            sizeSlider.ValueChanged += delegate { size.Value = sizeSlider.Value; };
            thickness.ValueChanged += delegate
            {
                controller.Options.Thickness = thickness.Value;
                thicknessSlider.Value = (int)(thickness.Value * 10);
                ApplyAppearance();
            };
            thicknessSlider.ValueChanged += delegate { thickness.Value = thicknessSlider.Value / 10M; };

            Label("COLOR", 20, 315, 100, 20, 8F);
            colorValue = Label("", 296, 315, 124, 20, 9F);
            colorValue.TextAlign = ContentAlignment.MiddleRight;
            var hue = new TrackBar { Minimum = 0, Maximum = 359, Value = controller.Options.Hue,
                TickStyle = TickStyle.None, SmallChange = 1, LargeChange = 15, BackColor = BackColor };
            hue.SetBounds(12, 347, 417, 30);
            hue.AccessibleName = "Crosshair color hue";
            Controls.Add(hue);
            var strip = new HueStrip(); strip.SetBounds(24, 343, 392, 6); Controls.Add(strip);
            var white = Check("White", 20, 382, 145, controller.Options.White);
            var outline = Check("Dark outline", 204, 382, 210, controller.Options.Outline);
            white.CheckedChanged += delegate { controller.Options.White = white.Checked; ApplyAppearance(); };
            outline.CheckedChanged += delegate { controller.Options.Outline = outline.Checked; ApplyAppearance(); };
            hue.ValueChanged += delegate
            {
                controller.Options.Hue = hue.Value;
                white.Checked = false;
                controller.Options.White = false;
                ApplyAppearance();
            };
            Label("DISPLAY", 20, 429, 75, 20, 8F);
            var displays = new DarkComboBox { AccessibleName = "Crosshair display" };
            displays.SetBounds(104, 425, 316, 28);
            displays.Items.Add("Primary display");
            Screen[] screens = Screen.AllScreens;
            displays.SelectedIndex = 0;
            for (int i = 0; i < screens.Length; i++)
            {
                Screen screen = screens[i];
                displays.Items.Add("Display " + (i + 1) + "  /  " + screen.Bounds.Width + " x " + screen.Bounds.Height);
                if (screen.DeviceName == controller.Options.Display) displays.SelectedIndex = i + 1;
            }
            displays.SelectedIndexChanged += delegate
            {
                controller.Options.Display = displays.SelectedIndex == 0 ? "" : screens[displays.SelectedIndex - 1].DeviceName;
                ApplyAppearance();
            };
            Controls.Add(displays);
            hint = Label("Click-through overlay for windowed / borderless games.\nStays on when minimized; turns off when Autoclicker closes.", 20, 474, 400, 45, 8F);
            hint.ForeColor = AppColors.MutedText;
            ApplyAppearance();
            AutoScaleDimensions = new SizeF(96, 96);
            AutoScaleMode = AutoScaleMode.Dpi;
            ResumeLayout(false);
            InputCommit.Attach(this);
        }

        private void SyncEnabled(object sender, EventArgs e) { enabled.Checked = controller.Enabled; }
        private TrackBar Slider(int x, int y, int width, int minimum, int maximum, int value, string name)
        {
            var slider = new TrackBar { Minimum = minimum, Maximum = maximum, Value = value,
                TickStyle = TickStyle.None, SmallChange = 1, LargeChange = 2, BackColor = BackColor, AccessibleName = name };
            slider.SetBounds(x, y, width, 30);
            Controls.Add(slider);
            return slider;
        }
        private void ApplyAppearance()
        {
            preview.Invalidate();
            Color color = controller.Options.Color;
            colorValue.Text = "#" + color.R.ToString("X2") + color.G.ToString("X2") + color.B.ToString("X2");
            colorValue.ForeColor = color;
            try { controller.Refresh(); }
            catch (System.ComponentModel.Win32Exception)
            {
                controller.SetEnabled(false);
                if (hint != null) hint.Text = "Windows could not refresh the overlay. Toggle it to retry.";
            }
        }
        private Label Label(string text, int x, int y, int width, int height, float size)
        {
            var label = new Label { Text = text, ForeColor = AppColors.SecondaryText, Font = new Font("Segoe UI", size) };
            label.SetBounds(x, y, width, height); Controls.Add(label); return label;
        }
        private Button Button(string text, int x, int y, int width)
        {
            var button = new Button { Text = text, FlatStyle = FlatStyle.Flat, BackColor = AppColors.Button, ForeColor = ForeColor };
            button.FlatAppearance.BorderColor = AppColors.ControlBorder;
            button.SetBounds(x, y, width, 30); Controls.Add(button); return button;
        }
        private LargeCheckBox Check(string text, int x, int y, int width, bool value)
        {
            var check = new LargeCheckBox { Text = text, Checked = value, ForeColor = AppColors.SecondaryText };
            check.SetBounds(x, y, width, 30); Controls.Add(check); return check;
        }
        protected override void OnPaint(PaintEventArgs e)
        { base.OnPaint(e); using (var pen = new Pen(AppColors.ControlBorder)) e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1); }
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                InputCommit.CommitOutside(this, null);
                controller.Changed -= SyncEnabled;
                controller.Save();
            }
            base.Dispose(disposing);
        }
    }
}
