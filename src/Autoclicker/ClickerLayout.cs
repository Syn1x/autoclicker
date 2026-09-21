using System;
using System.Drawing;
using System.Windows.Forms;

namespace Autoclicker
{
    internal sealed partial class ClickerForm
    {
        private Panel clickerPage;
        private Panel settingsViewport, footerPanel;
        private Button clickerTab, crosshairTab;

        private void BuildInterface()
        {
            ClientSize = new Size(460, 610);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.None;
            MaximizeBox = false;
            BackColor = AppColors.Background;
            ForeColor = AppColors.Text;
            Font = new Font("Segoe UI", 9F);
            DoubleBuffered = true;

            var header = new Panel { BackColor = AppColors.Header };
            header.SetBounds(1, 1, 458, 59);
            Controls.Add(header);
            Label title = AddLabel(header, "Autoclicker", 19, 13, 198, 34, 17F, ForeColor, false);
            AttachWindowDrag(header);
            AttachWindowDrag(title);
            status = AddLabel(header, "Stopped", 225, 19, 122, 25, 8F, muted, false);
            status.TextAlign = ContentAlignment.MiddleCenter;
            status.BackColor = AppColors.Button;
            Button minimize = MakeButton("−", 364, 16, 28, header.BackColor, soft);
            minimize.Parent = header;
            minimize.Height = 28;
            minimize.AccessibleName = "Minimize";
            minimize.Click += delegate { WindowState = FormWindowState.Minimized; };
            Button close = MakeButton("×", 408, 16, 28, header.BackColor, soft);
            close.Parent = header;
            close.Height = 28;
            close.AccessibleName = "Close Autoclicker";
            close.Click += delegate { Close(); };

            clickerTab = MakeButton("Clicker", 20, 74, 204, AppColors.Raised, ForeColor);
            crosshairTab = MakeButton("Crosshair", 236, 74, 204, BackColor, soft);
            clickerTab.Height = crosshairTab.Height = 34;
            clickerTab.AccessibleName = "Clicker tab";
            crosshairTab.AccessibleName = "Crosshair tab";
            clickerTab.Click += delegate { SelectPage(false); };
            crosshairTab.Click += delegate { SelectPage(true); };

            settingsViewport = new Panel { BackColor = BackColor, AutoScroll = true };
            settingsViewport.SetBounds(20, 124, 438, 320);
            Controls.Add(settingsViewport);

            clickerPage = new Panel { BackColor = BackColor };
            clickerPage.SetBounds(0, 0, 420, 320);
            settingsViewport.Controls.Add(clickerPage);
            AddLabel(clickerPage, "Session clicks", 0, 8, 200, 24, 9F, muted, false);
            counter = AddLabel(clickerPage, "0", 284, 0, 136, 34, 18F, ForeColor, false);
            counter.TextAlign = ContentAlignment.MiddleRight;
            AddDivider(clickerPage, 0, 44, 420, 1);

            AddLabel(clickerPage, "Click interval", 0, 67, 176, 25, 10F, soft, false);
            interval = new NumericInput { Minimum = 20, Maximum = 2000, Increment = 10, Value = 50,
                Font = new Font("Segoe UI", 10F), BackColor = AppColors.Inset, ForeColor = ForeColor,
                BorderStyle = BorderStyle.FixedSingle, AccessibleName = "Click interval in milliseconds" };
            interval.SetBounds(194, 62, 100, 28);
            interval.ValueChanged += delegate { UpdateRate(); };
            clickerPage.Controls.Add(interval);
            AddLabel(clickerPage, "ms", 304, 67, 28, 22, 9F, muted, false);
            rate = AddLabel(clickerPage, "", 338, 67, 82, 22, 8F, muted, false);
            rate.TextAlign = ContentAlignment.MiddleRight;
            variation = new LargeCheckBox { Text = "Timing variation (±10%)", Checked = true, ForeColor = soft };
            variation.SetBounds(0, 105, 264, 30);
            variation.CheckedChanged += delegate { UpdateRate(); };
            clickerPage.Controls.Add(variation);
            timingHint = AddLabel(clickerPage, "", 272, 111, 148, 22, 8F, muted, false);
            timingHint.TextAlign = ContentAlignment.MiddleRight;
            UpdateRate();
            AddDivider(clickerPage, 0, 153, 420, 1);

            AddLabel(clickerPage, "Start / stop key", 0, 176, 142, 25, 10F, soft, false);
            bindingValue = AddLabel(clickerPage, KeyName, 145, 172, 103, 28, 10F, ForeColor, true);
            bindingValue.TextAlign = ContentAlignment.MiddleCenter;
            bindingValue.AutoEllipsis = true;
            changeKey = MakeButton("Change", 256, 170, 80, AppColors.Button, soft);
            changeKey.Parent = clickerPage;
            changeKey.Height = 32;
            changeKey.AccessibleName = "Change start and stop key";
            changeKey.Click += delegate { if (choosingKey) CancelKeyCapture(); else BeginKeyCapture(); };
            resetKey = MakeButton("Reset", 348, 170, 72, BackColor, muted);
            resetKey.Parent = clickerPage;
            resetKey.Height = 32;
            resetKey.AccessibleName = "Reset start and stop key to F11";
            resetKey.Click += delegate
            {
                if (choosingKey) CancelKeyCapture();
                StopClicking(StoppedMessage);
                ApplyBinding((int)Keys.F11);
            };

            autoStop = new LargeCheckBox { Text = "Stop after", Checked = true,
                AccessibleName = "Enable automatic stop", ForeColor = soft };
            autoStop.SetBounds(0, 226, 176, 30);
            clickerPage.Controls.Add(autoStop);
            stopMinutes = MakeDurationInput(clickerPage, 194, 999, 1, "Automatic stop minutes");
            stopMinutes.SetBounds(194, 228, 66, 28);
            AddLabel(clickerPage, "min", 268, 233, 40, 22, 9F, muted, false);
            stopSeconds = MakeDurationInput(clickerPage, 316, 59, 0, "Automatic stop seconds");
            stopSeconds.SetBounds(316, 228, 66, 28);
            AddLabel(clickerPage, "sec", 390, 233, 30, 22, 9F, muted, false);
            autoStopHint = AddLabel(clickerPage, "", 0, 274, 420, 22, 8F, muted, false);
            autoStop.CheckedChanged += delegate { UpdateAutoStopControls(); UpdateAutoStopHint(clock.ElapsedMilliseconds); };
            stopMinutes.ValueChanged += delegate { UpdateAutoStopHint(clock.ElapsedMilliseconds); };
            stopSeconds.ValueChanged += delegate { UpdateAutoStopHint(clock.ElapsedMilliseconds); };
            UpdateAutoStopControls();
            UpdateAutoStopHint(clock.ElapsedMilliseconds);

            crosshairSettings = new CrosshairSettingsControl(crosshair);
            crosshairSettings.SetBounds(0, 0, 420, 320);
            crosshairSettings.Visible = false;
            settingsViewport.Controls.Add(crosshairSettings);
            crosshairToggle = crosshairSettings.Toggle;

            footerPanel = new Panel { BackColor = BackColor };
            footerPanel.SetBounds(20, 454, 420, 150);
            Controls.Add(footerPanel);
            detail = AddLabel(footerPanel, "Left clicks at your cursor.", 0, 0, 420, 30, 8F, muted, false);
            crosshairSettings.Error += delegate(string message) { detail.Text = message; };
            startHint = AddLabel(footerPanel, "Press " + KeyName + " to start", 0, 38, 274, 36, 10F, soft, false);
            startHint.TextAlign = ContentAlignment.MiddleLeft;
            startHint.AccessibleRole = AccessibleRole.StaticText;
            startHint.TabStop = false;
            stop = MakeButton("Stop", 298, 34, 122, AppColors.DangerBackground, AppColors.Danger);
            stop.Parent = footerPanel;
            stop.Height = 38;
            stop.FlatAppearance.BorderColor = AppColors.DangerBorder;
            stop.Click += delegate { if (choosingKey) CancelKeyCapture(); else StopClicking(StoppedMessage); };

            AddDivider(footerPanel, 0, 88, 420, 1);
            updateStatus = AddLabel(footerPanel, "Checks for updates on launch", 0, 98, 266, 32, 8F, muted, false);
            AddLabel(footerPanel, "v" + AppInfo.Version, 0, 133, 200, 15, 7F, muted, false);
            checkUpdates = MakeButton("Check for updates", 274, 97, 146, AppColors.Button, soft);
            checkUpdates.Parent = footerPanel;
            checkUpdates.Height = 32;
            checkUpdates.Enabled = false;
            checkUpdates.Click += async delegate
            {
                if (updates == null) return;
                if (updates.CanRestart)
                {
                    if (choosingKey) CancelKeyCapture();
                    StopClicking("Restarting to apply the update...");
                    if (updates.RestartToApply()) Close();
                }
                else if (updates.CanConfirm) await updates.ConfirmAsync();
                else await updates.CheckAsync();
            };
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (settingsViewport == null || footerPanel == null) return;
            float scale = ClientSize.Width / 460F;
            footerPanel.Top = ClientSize.Height - footerPanel.Height - (int)Math.Round(6 * scale);
            settingsViewport.Height = Math.Max(1, footerPanel.Top - settingsViewport.Top - (int)Math.Round(10 * scale));
        }

        protected override void OnShown(EventArgs e)
        {
            // A small or highly scaled display may have less room than the
            // natural window height. Keep actions visible and scroll settings.
            Rectangle area = Screen.FromControl(this).WorkingArea;
            Height = Math.Min(Height, area.Height - 24);
            Top = Math.Max(area.Top + 12, Math.Min(Top, area.Bottom - Height - 12));
            PerformLayout();
            base.OnShown(e);
        }

        private void SelectPage(bool showCrosshair)
        {
            InputCommit.CommitOutside(this, null);
            if (choosingKey) CancelKeyCapture();
            crosshair.Save();
            settingsViewport.AutoScrollPosition = Point.Empty;
            clickerPage.Visible = !showCrosshair;
            crosshairSettings.Visible = showCrosshair;
            clickerTab.BackColor = showCrosshair ? BackColor : AppColors.Raised;
            crosshairTab.BackColor = showCrosshair ? AppColors.Raised : BackColor;
            clickerTab.ForeColor = showCrosshair ? muted : ForeColor;
            crosshairTab.ForeColor = showCrosshair ? ForeColor : muted;
            detail.Text = showCrosshair ? "Windowed / borderless games. Stays on while minimized." : "Left clicks at your cursor.";
        }
    }
}
