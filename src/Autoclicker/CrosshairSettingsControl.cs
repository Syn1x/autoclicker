using System;
using System.Drawing;
using System.Windows.Forms;

namespace Autoclicker
{
    internal sealed class CrosshairSettingsControl : UserControl
    {
        private readonly CrosshairController controller;
        internal LargeCheckBox Toggle { get; private set; }
        private readonly CrosshairPreview preview;
        private readonly Label colorValue;
        internal event Action<string> Error;
        private readonly ToolTip tips = new ToolTip();

        internal CrosshairSettingsControl(CrosshairController value)
        {
            controller = value;
            SuspendLayout();
            AutoScaleMode = AutoScaleMode.Inherit;
            Size = new Size(420, 320);
            BackColor = AppColors.Background;
            ForeColor = AppColors.Text;
            Font = new Font("Segoe UI", 9F);
            DoubleBuffered = true;
            Toggle = Check("Show crosshair", 0, 0, 270, controller.Enabled);
            Toggle.AccessibleName = "Show crosshair overlay";
            Toggle.CheckedChanged += delegate
            {
                if (Toggle.Checked == controller.Enabled) return;
                try { controller.SetEnabled(Toggle.Checked); }
                catch (System.ComponentModel.Win32Exception)
                { Toggle.Checked = false; ShowError("Couldn't display the overlay. Try again."); }
            };
            controller.Changed += SyncEnabled;
            preview = new CrosshairPreview(controller.Options);
            preview.SetBounds(302, 0, 118, 88);
            Controls.Add(preview);
            Label("Style", 0, 53, 100, 24);
            var style = new DarkComboBox { AccessibleName = "Crosshair style" };
            style.Items.AddRange(new object[] { "Dot", "Cross", "Open cross", "Circle", "Circle + dot" });
            style.SetBounds(110, 48, 174, 28);
            style.SelectedIndex = (int)controller.Options.Style;
            Controls.Add(style);

            Label("Size", 0, 109, 100, 24);
            var sizeSlider = Slider(110, 103, 214, 2, 32, controller.Options.Size, "Crosshair size slider");
            var size = Number(336, 105, 2, 32, controller.Options.Size, "Crosshair size in pixels");
            Label("px", 398, 110, 22, 22, 8F);
            Label thicknessLabel = Label("Thickness", 0, 153, 100, 24);
            var thicknessSlider = Slider(110, 147, 214, 5, 80, (int)(controller.Options.Thickness * 10), "Crosshair thickness slider");
            thicknessSlider.LargeChange = 5;
            var thickness = Number(336, 149, 0.5M, 8, controller.Options.Thickness, "Crosshair thickness in pixels", 1);
            thickness.Increment = 0.5M;
            Label("px", 398, 154, 22, 22, 8F);
            Action refreshThickness = delegate
            {
                thickness.Enabled = thicknessSlider.Enabled = controller.Options.Style != CrosshairStyle.Dot;
                thicknessLabel.ForeColor = thickness.Enabled ? AppColors.SecondaryText : AppColors.DisabledText;
                tips.SetToolTip(thicknessLabel, thickness.Enabled ? "Line and ring width" : "For a solid dot, use Size.");
            };
            refreshThickness();
            style.SelectedIndexChanged += delegate
            {
                controller.Options.Style = (CrosshairStyle)style.SelectedIndex;
                if (style.SelectedIndex != 0 && size.Value < 8) size.Value = 16;
                refreshThickness();
                ApplyAppearance();
            };
            size.ValueChanged += delegate
            { controller.Options.Size = (int)size.Value; sizeSlider.Value = controller.Options.Size; ApplyAppearance(); };
            sizeSlider.ValueChanged += delegate { size.Value = sizeSlider.Value; };
            thickness.ValueChanged += delegate
            { controller.Options.Thickness = thickness.Value; thicknessSlider.Value = (int)(thickness.Value * 10); ApplyAppearance(); };
            thicknessSlider.ValueChanged += delegate { thickness.Value = thicknessSlider.Value / 10M; };

            Label("Color", 0, 197, 100, 24);
            var hue = Slider(110, 191, 214, 0, 359, controller.Options.Hue, "Crosshair color hue");
            hue.HueTrack = true;
            hue.LargeChange = 15;
            colorValue = Label("", 330, 197, 90, 22, 8F);
            colorValue.TextAlign = ContentAlignment.MiddleRight;
            var white = Check("White", 0, 232, 190, controller.Options.White);
            var outline = Check("Dark outline", 210, 232, 210, controller.Options.Outline);
            white.CheckedChanged += delegate { controller.Options.White = white.Checked; ApplyAppearance(); };
            outline.CheckedChanged += delegate { controller.Options.Outline = outline.Checked; ApplyAppearance(); };
            hue.ValueChanged += delegate
            {
                controller.Options.Hue = hue.Value;
                white.Checked = false;
                controller.Options.White = false;
                ApplyAppearance();
            };
            Label("Display", 0, 281, 100, 24);
            var displays = new DarkComboBox { AccessibleName = "Crosshair display" };
            displays.SetBounds(110, 276, 310, 28);
            displays.Items.Add("Primary display");
            Screen[] screens = Screen.AllScreens;
            displays.SelectedIndex = 0;
            for (int i = 0; i < screens.Length; i++)
            {
                Screen screen = screens[i];
                displays.Items.Add("Display " + (i + 1) + " · " + screen.Bounds.Width + " × " + screen.Bounds.Height);
                if (screen.DeviceName == controller.Options.Display) displays.SelectedIndex = i + 1;
            }
            displays.SelectedIndexChanged += delegate
            {
                controller.Options.Display = displays.SelectedIndex == 0 ? "" : screens[displays.SelectedIndex - 1].DeviceName;
                ApplyAppearance();
            };
            Controls.Add(displays);
            tips.SetToolTip(Toggle, "For windowed / borderless games. Stays visible while minimized; closes with the app.");
            ApplyAppearance();
            ResumeLayout(false);
        }

        private void SyncEnabled(object sender, EventArgs e) { Toggle.Checked = controller.Enabled; }
        private SlimSlider Slider(int x, int y, int width, int minimum, int maximum, int value, string name)
        {
            var slider = new SlimSlider { Minimum = minimum, Maximum = maximum, Value = value,
                BackColor = BackColor, AccessibleName = name };
            slider.SetBounds(x, y, width, 30);
            Controls.Add(slider);
            return slider;
        }
        private NumericInput Number(int x, int y, decimal min, decimal max, decimal value, string name, int decimals = 0)
        {
            var number = new NumericInput { Minimum = min, Maximum = max, DecimalPlaces = decimals, Value = value,
                BackColor = AppColors.Inset, ForeColor = ForeColor, BorderStyle = BorderStyle.FixedSingle, AccessibleName = name };
            number.SetBounds(x, y, 58, 28);
            Controls.Add(number);
            return number;
        }
        private void ApplyAppearance()
        {
            preview.Invalidate();
            Color color = controller.Options.Color;
            colorValue.Text = "#" + color.R.ToString("X2") + color.G.ToString("X2") + color.B.ToString("X2");
            try { controller.Refresh(); }
            catch (System.ComponentModel.Win32Exception)
            {
                controller.SetEnabled(false);
                ShowError("Couldn't refresh the overlay. Toggle it to retry.");
            }
        }
        private void ShowError(string message) { if (Error != null) Error(message); }
        private Label Label(string text, int x, int y, int width, int height, float size = 9F)
        {
            var label = new Label { Text = text, ForeColor = AppColors.SecondaryText, Font = new Font("Segoe UI", size) };
            label.SetBounds(x, y, width, height); Controls.Add(label); return label;
        }
        private LargeCheckBox Check(string text, int x, int y, int width, bool value)
        {
            var check = new LargeCheckBox { Text = text, Checked = value, ForeColor = AppColors.SecondaryText };
            check.SetBounds(x, y, width, 30); Controls.Add(check); return check;
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                InputCommit.CommitOutside(this, null);
                controller.Changed -= SyncEnabled;
                controller.Save();
                tips.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
