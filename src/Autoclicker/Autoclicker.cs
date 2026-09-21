using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("Autoclicker")]
[assembly: System.Reflection.AssemblyProduct("Autoclicker")]

namespace Autoclicker
{
    // Neutral dark palette shared across the form and custom-painted controls.
    internal static class AppColors
    {
        internal static readonly Color Background = Color.FromArgb(24, 24, 24);
        internal static readonly Color Header = Color.FromArgb(19, 19, 19);
        internal static readonly Color Inset = Color.FromArgb(28, 28, 28);
        internal static readonly Color Surface = Color.FromArgb(33, 33, 33);
        internal static readonly Color Raised = Color.FromArgb(40, 40, 40);
        internal static readonly Color Button = Color.FromArgb(48, 48, 48);
        internal static readonly Color Border = Color.FromArgb(51, 51, 51);
        internal static readonly Color ControlBorder = Color.FromArgb(65, 65, 65);
        internal static readonly Color Text = Color.FromArgb(223, 223, 223);
        internal static readonly Color SecondaryText = Color.FromArgb(185, 185, 185);
        internal static readonly Color MutedText = Color.FromArgb(159, 159, 159);
        internal static readonly Color DisabledText = Color.FromArgb(118, 118, 118);
        internal static readonly Color Selection = Color.FromArgb(237, 237, 237);
        internal static readonly Color InverseText = Color.FromArgb(24, 24, 24);
        internal static readonly Color Warning = Color.FromArgb(255, 184, 108);
        internal static readonly Color Danger = Color.FromArgb(255, 103, 100);
        internal static readonly Color DangerBackground = Color.FromArgb(53, 33, 32);
        internal static readonly Color DangerBorder = Color.FromArgb(102, 59, 58);
    }

    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length == 2 && args[0] == "--self-test")
                return Verification.Run(args[1]);

            Velopack.VelopackApp.Build().SetAutoApplyOnStartup(false).Run();

            Native.SetProcessDPIAware();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (args.Length == 2 && args[0] == "--preview")
            {
                using (ClickerForm form = new ClickerForm(false))
                {
                    form.ShowInTaskbar = false;
                    form.Opacity = 0;
                    form.Show();
                    form.PerformLayout();
                    using (Bitmap bitmap = new Bitmap(form.Width, form.Height))
                    {
                        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
                        bitmap.Save(args[1], System.Drawing.Imaging.ImageFormat.Png);
                    }
                }
                return 0;
            }

            bool created;
            using (Mutex singleInstance = new Mutex(true, "Local\\Autoclicker_1E4A3BBE", out created))
            {
                if (!created)
                {
                    // A modal "already open" dialog leaves a second process
                    // alive even after the real window has been closed.
                    FocusExistingWindow();
                    return 0;
                }
                using (UpdateCoordinator updates = new UpdateCoordinator(new VelopackBackend()))
                {
                    using (ClickerForm form = new ClickerForm(true))
                    {
                        form.AttachUpdates(updates);
                        Application.Run(form);
                    }
                    updates.Dispose();
                    updates.ApplyOnExit();
                }
            }
            return 0;
        }

        private static void FocusExistingWindow()
        {
            using (Process current = Process.GetCurrentProcess())
            {
                Process[] candidates = Process.GetProcessesByName(current.ProcessName);
                try
                {
                    foreach (Process candidate in candidates)
                    {
                        if (candidate.Id == current.Id) continue;
                        try
                        {
                            IntPtr window = candidate.MainWindowHandle;
                            if (window == IntPtr.Zero) continue;
                            Native.ShowWindowAsync(window, 9); // Restore if minimized.
                            Native.SetForegroundWindow(window);
                            return;
                        }
                        catch (InvalidOperationException) { } // Existing copy exited.
                        catch (System.ComponentModel.Win32Exception) { }
                    }
                }
                finally
                {
                    foreach (Process candidate in candidates) candidate.Dispose();
                }
            }
        }
    }

    // Scheduling is separated from mouse input so tests never send real clicks.
    internal sealed class ClickEngine
    {
        private readonly Action click;
        private readonly Func<int, int, int> randomRange;
        private long nextDue;
        private int interval;
        private bool varyTiming;
        public bool Active { get; private set; }
        public long Count { get; private set; }
        public long StartsAt { get; private set; }
        public long StopsAt { get; private set; }
        public bool TimeLimitReached { get; private set; }

        public ClickEngine(Action clickAction) : this(clickAction, new Random().Next) { }

        internal ClickEngine(Action clickAction, Func<int, int, int> randomSource)
        {
            click = clickAction;
            randomRange = randomSource;
        }

        public void Start(long now, int intervalMs, int delayMs, bool variationEnabled = false, int durationMs = 0)
        {
            if (intervalMs < 20 || intervalMs > 2000)
                throw new ArgumentOutOfRangeException("intervalMs");
            if (delayMs < 0) throw new ArgumentOutOfRangeException("delayMs");
            if (durationMs < 0) throw new ArgumentOutOfRangeException("durationMs");
            interval = intervalMs;
            varyTiming = variationEnabled;
            StartsAt = now + delayMs;
            StopsAt = durationMs == 0 ? 0 : StartsAt + durationMs;
            TimeLimitReached = false;
            nextDue = StartsAt;
            Count = 0;
            Active = true;
        }

        public void Stop() { Active = false; }

        public void Tick(long now, bool pointerIsOnThisApp)
        {
            if (!Active) return;
            // Check the deadline before scheduling or sending any more input.
            if (StopsAt != 0 && now >= StopsAt)
            {
                TimeLimitReached = true;
                Stop();
                return;
            }
            if (now < nextDue) return;
            if (pointerIsOnThisApp)
            {
                Stop();
                return;
            }
            try { click(); }
            catch { Stop(); throw; }
            Count++;
            int nextInterval = interval;
            if (varyTiming)
            {
                int spread = interval / 10;
                nextInterval += randomRange(-spread, spread + 1);
            }
            // Keep the average cadence, but never flood delayed input in a burst.
            // Sampling one bounded interval adds no sleep or busy-wait work.
            nextDue += nextInterval;
            if (nextDue <= now) nextDue = now + nextInterval;
        }
    }

    internal sealed class ClickerForm : Form
    {
        private const int ToggleId = 1;
        private const int BindingId = 2;
        private readonly bool enableHotkeys;
        private readonly string settingsPath;
        private int toggleKey;
        private bool choosingKey;
        private GlobalHotkeys hotkeys;
        private ToggleSounds sounds;
        private Icon applicationIcon;
        private readonly int processId = Process.GetCurrentProcess().Id;
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        private readonly ClickEngine engine = new ClickEngine(Native.LeftClick);
        private readonly Color accent = AppColors.InverseText;
        private readonly Color muted = AppColors.MutedText;
        private readonly Color soft = AppColors.SecondaryText;
        private Label status;
        private Label detail;
        private Label counter;
        private Label rate;
        private NumericUpDown interval;
        private CheckBox variation;
        private Label timingHint;
        private Label startHint;
        private Button stop;
        private Button changeKey;
        private Button resetKey;
        private Label bindingValue;
        private Label shortcutFooter;
        private CheckBox autoStop;
        private NumericUpDown stopMinutes;
        private NumericUpDown stopSeconds;
        private Label autoStopHint;
        private Label updateStatus;
        private Button checkUpdates;
        private UpdateCoordinator updates;

        private string KeyName { get { return KeyBindings.DisplayName(toggleKey); } }
        private string StoppedMessage { get { return "Stopped. " + KeyName + " starts again."; } }

        public ClickerForm(bool hooks) : this(hooks, hooks ? AppInfo.SettingsPath : null) { }

        public ClickerForm(bool hooks, string configurationPath)
        {
            SuspendLayout();
            enableHotkeys = hooks;
            settingsPath = configurationPath;
            toggleKey = KeySettings.Load(settingsPath);
            Text = "Autoclicker";
            using (Stream iconStream = typeof(ClickerForm).Assembly.GetManifestResourceStream("Autoclicker.AppIcon.ico"))
            {
                applicationIcon = new Icon(iconStream);
                Icon = applicationIcon;
            }
            ClientSize = new Size(620, 798);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.None;
            MaximizeBox = false;
            BackColor = AppColors.Background;
            ForeColor = AppColors.Text;
            Font = new Font("Segoe UI", 9F);
            DoubleBuffered = true;

            Panel topbar = new Panel();
            topbar.SetBounds(1, 3, 618, 71);
            topbar.BackColor = AppColors.Header;
            Controls.Add(topbar);
            Label title = AddLabel(topbar, "Autoclicker", 18, 21, 485, 34, 18F, ForeColor, false);
            AttachWindowDrag(topbar);
            AttachWindowDrag(title);
            Button minimize = MakeButton("-", 532, 22, 28, topbar.BackColor, soft);
            minimize.Parent = topbar;
            minimize.Height = 28;
            minimize.AccessibleName = "Minimize";
            minimize.Click += delegate { WindowState = FormWindowState.Minimized; };
            Button close = MakeButton("X", 572, 22, 28, topbar.BackColor, soft);
            close.Parent = topbar;
            close.Height = 28;
            close.AccessibleName = "Close Autoclicker";
            close.Click += delegate { Close(); };

            StyledPanel toolbar = MakePanel(this, 1, 74, 618, 39, AppColors.Header);
            AddLabel(toolbar, "MOUSE CONTROL", 18, 12, 220, 18, 8F, soft, true);
            status = AddLabel(toolbar, "STOPPED", 456, 8, 140, 23, 8F, muted, true);
            status.TextAlign = ContentAlignment.MiddleCenter;
            status.BackColor = AppColors.Button;
            AddLabel(this, "CURRENT SESSION", 20, 133, 300, 18, 7.5F, muted, true);
            Label module = AddLabel(this, "INPUT / 01", 460, 132, 140, 20, 8F, muted, false);
            module.Font = new Font("Consolas", 8F);
            module.TextAlign = ContentAlignment.MiddleRight;

            StyledPanel telemetry = MakePanel(this, 20, 157, 580, 92, AppColors.Inset);
            AddDivider(telemetry, 193, 0, 1, 92);
            AddDivider(telemetry, 386, 0, 1, 92);
            AddLabel(telemetry, "TARGET RATE", 14, 13, 163, 18, 7.5F, muted, true);
            rate = AddLabel(telemetry, "", 12, 40, 165, 38, 22F, ForeColor, false);
            rate.Font = new Font("Consolas", 22F);
            AddLabel(telemetry, "SESSION CLICKS", 207, 13, 163, 18, 7.5F, muted, true);
            counter = AddLabel(telemetry, "0", 205, 40, 165, 38, 22F, ForeColor, false);
            counter.Font = new Font("Consolas", 22F);
            AddLabel(telemetry, "CLICK MODE", 400, 13, 163, 18, 7.5F, muted, true);
            AddLabel(telemetry, "Left button", 398, 42, 162, 25, 13F, soft, false);
            AddLabel(telemetry, "AT CURSOR", 400, 69, 163, 15, 7F, muted, false);

            AddLabel(this, "CLICK SETTINGS", 20, 271, 300, 18, 7.5F, muted, true);
            StyledPanel speedPanel = MakePanel(this, 20, 294, 282, 129, AppColors.Surface);
            StyledPanel variationPanel = MakePanel(this, 314, 294, 286, 129, AppColors.Surface);
            AddLabel(speedPanel, "Click interval", 14, 13, 220, 22, 9F, soft, false);
            interval = new NumericUpDown();
            interval.SetBounds(16, 47, 137, 35);
            interval.Minimum = 20;
            interval.Maximum = 2000;
            interval.Increment = 10;
            interval.Value = 50;
            interval.Font = new Font("Consolas", 19F);
            interval.BackColor = AppColors.Background;
            interval.ForeColor = ForeColor;
            interval.BorderStyle = BorderStyle.FixedSingle;
            interval.AccessibleName = "Click interval in milliseconds";
            interval.ValueChanged += delegate { UpdateRate(); };
            speedPanel.Controls.Add(interval);
            AddLabel(speedPanel, "ms", 166, 58, 55, 25, 10F, muted, false);
            AddLabel(speedPanel, "Lower interval = faster clicks", 14, 99, 260, 18, 8F, muted, false);

            variation = new LargeCheckBox();
            variation.SetBounds(14, 8, 257, 30);
            variation.Text = "Timing variation";
            variation.Checked = true;
            variation.ForeColor = soft;
            variation.CheckedChanged += delegate { UpdateRate(); };
            variationPanel.Controls.Add(variation);
            AddLabel(variationPanel, "+/- 10%", 12, 47, 250, 37, 20F, soft, false).Font = new Font("Consolas", 20F);
            timingHint = AddLabel(variationPanel, "", 14, 99, 260, 18, 8F, muted, false);
            UpdateRate();

            StyledPanel keyPanel = MakePanel(this, 20, 436, 580, 61, AppColors.Surface);
            AddLabel(keyPanel, "START / STOP KEY", 14, 9, 300, 17, 7.5F, muted, true);
            bindingValue = AddLabel(keyPanel, KeyName, 12, 29, 305, 26, 12F, soft, true);
            changeKey = MakeButton("Change key", 352, 13, 114, AppColors.Button, soft);
            changeKey.Parent = keyPanel;
            changeKey.Height = 35;
            changeKey.AccessibleName = "Change start and stop key";
            changeKey.Click += delegate { if (choosingKey) CancelKeyCapture(); else BeginKeyCapture(); };
            resetKey = MakeButton("Reset F11", 476, 13, 90, AppColors.Surface, muted);
            resetKey.Parent = keyPanel;
            resetKey.Height = 35;
            resetKey.Click += delegate
            {
                if (choosingKey) CancelKeyCapture();
                StopClicking(StoppedMessage);
                ApplyBinding((int)Keys.F11);
            };

            StyledPanel autoStopPanel = MakePanel(this, 20, 510, 580, 82, AppColors.Surface);
            autoStop = new LargeCheckBox();
            autoStop.SetBounds(14, 10, 185, 30);
            autoStop.Text = "Stop after";
            autoStop.Checked = true;
            autoStop.AccessibleName = "Enable automatic stop";
            autoStop.ForeColor = soft;
            autoStopPanel.Controls.Add(autoStop);
            stopMinutes = MakeDurationInput(autoStopPanel, 220, 999, 1, "Automatic stop minutes");
            AddLabel(autoStopPanel, "min", 305, 20, 52, 24, 9F, muted, false);
            stopSeconds = MakeDurationInput(autoStopPanel, 370, 59, 0, "Automatic stop seconds");
            AddLabel(autoStopPanel, "sec", 455, 20, 52, 24, 9F, muted, false);
            autoStopHint = AddLabel(autoStopPanel, "", 14, 54, 552, 19, 8F, muted, false);
            autoStop.CheckedChanged += delegate { UpdateAutoStopControls(); UpdateAutoStopHint(clock.ElapsedMilliseconds); };
            stopMinutes.ValueChanged += delegate { UpdateAutoStopHint(clock.ElapsedMilliseconds); };
            stopSeconds.ValueChanged += delegate { UpdateAutoStopHint(clock.ElapsedMilliseconds); };
            UpdateAutoStopControls();
            UpdateAutoStopHint(clock.ElapsedMilliseconds);

            StyledPanel messagePanel = MakePanel(this, 20, 605, 580, 43, AppColors.Raised);
            messagePanel.LineColor = AppColors.Border;
            detail = AddLabel(messagePanel, "Hover over the team button, then press " + KeyName + " to start.",
                12, 10, 554, 29, 9F, AppColors.SecondaryText, false);
            startHint = AddLabel(this, "Press " + KeyName + " to start", 20, 660, 366, 43,
                9F, AppColors.SecondaryText, true);
            startHint.BackColor = AppColors.Raised;
            startHint.TextAlign = ContentAlignment.MiddleCenter;
            startHint.AccessibleRole = AccessibleRole.StaticText;
            startHint.TabStop = false;
            stop = MakeButton("STOP", 400, 660, 200, AppColors.DangerBackground, AppColors.Danger);
            stop.FlatAppearance.BorderColor = AppColors.DangerBorder;
            stop.Click += delegate { if (choosingKey) CancelKeyCapture(); else StopClicking(StoppedMessage); };
            AddDivider(this, 1, 718, 618, 1);
            shortcutFooter = AddLabel(this, KeyName + "   Toggle on / off", 20, 732, 430, 18, 8F, muted, false);
            Label version = AddLabel(this, "v" + AppInfo.Version, 470, 732, 130, 18, 8F, muted, false);
            version.TextAlign = ContentAlignment.MiddleRight;
            updateStatus = AddLabel(this, "Updates check automatically on launch", 20, 768, 435, 18, 8F, muted, false);
            checkUpdates = MakeButton("Check for updates", 466, 758, 134, AppColors.Button, soft);
            checkUpdates.Height = 29;
            checkUpdates.Enabled = false;
            checkUpdates.Click += async delegate { if (updates != null) await updates.CheckAsync(); };

            timer.Interval = 15;
            timer.Tick += OnTick;
            FormClosing += delegate { StopClicking("Stopped."); };
            Deactivate += delegate { if (choosingKey) CancelKeyCapture(); };
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ResumeLayout(false);
            if (enableHotkeys) sounds = new ToggleSounds();
        }

        internal void AttachUpdates(UpdateCoordinator coordinator)
        {
            updates = coordinator;
            updates.Changed += RefreshUpdates;
            RefreshUpdates();
            Shown += async delegate { await coordinator.CheckAsync(); };
            FormClosed += delegate { coordinator.Dispose(); };
        }

        private void RefreshUpdates()
        {
            if (IsDisposed || Disposing) return;
            updateStatus.Text = updates.Status;
            checkUpdates.Enabled = updates.CanCheck;
        }

        private void BeginKeyCapture()
        {
            if (hotkeys == null || !hotkeys.Active) return;
            StopClicking(StoppedMessage);
            choosingKey = true;
            UpdateAutoStopControls();
            hotkeys.Bindings.BeginCapture();
            startHint.Text = "Choose a start / stop key";
            interval.Enabled = false;
            variation.Enabled = false;
            resetKey.Enabled = false;
            changeKey.Text = "Cancel";
            bindingValue.Text = "Press and release a key...";
            status.Text = "BINDING";
            status.ForeColor = AppColors.Warning;
            detail.Text = "Press and release your new key. Click Cancel to keep " + KeyName + ".";
        }

        private void CancelKeyCapture()
        {
            if (!choosingKey) return;
            if (hotkeys != null)
            {
                hotkeys.Bindings.CancelCapture();
                hotkeys.Bindings.SetToggleKey(toggleKey);
            }
            EndKeyCapture();
            StopClicking("Key unchanged. " + KeyName + " toggles on / off.");
        }

        private void EndKeyCapture()
        {
            choosingKey = false;
            UpdateAutoStopControls();
            changeKey.Text = "Change key";
            resetKey.Enabled = true;
            interval.Enabled = true;
            variation.Enabled = true;
            bindingValue.Text = KeyName;
        }

        private void ApplyBinding(int key)
        {
            if (!KeyBindings.IsBindable(key)) return;
            toggleKey = key;
            if (hotkeys != null) hotkeys.Bindings.SetToggleKey(key);
            EndKeyCapture();
            shortcutFooter.Text = KeyName + "   Toggle on / off";
            bool saved = KeySettings.TrySave(settingsPath, key);
            StopClicking(saved ? KeyName + " is saved. Press it to start / stop."
                : KeyName + " is set for this session. Settings could not be saved.");
        }

        private StyledPanel MakePanel(Control parent, int x, int y, int w, int h, Color background)
        {
            StyledPanel panel = new StyledPanel();
            panel.SetBounds(x, y, w, h);
            panel.BackColor = background;
            parent.Controls.Add(panel);
            return panel;
        }

        private NumericUpDown MakeDurationInput(Control parent, int x, int maximum, int value, string name)
        {
            NumericUpDown input = new NumericUpDown();
            input.SetBounds(x, 13, 78, 31);
            input.Maximum = maximum;
            input.Value = value;
            input.Font = new Font("Consolas", 14F);
            input.BackColor = AppColors.Background;
            input.ForeColor = ForeColor;
            input.BorderStyle = BorderStyle.FixedSingle;
            input.AccessibleName = name;
            parent.Controls.Add(input);
            return input;
        }

        private int DurationMilliseconds
        {
            get { return (int)((stopMinutes.Value * 60M + stopSeconds.Value) * 1000M); }
        }

        private void UpdateAutoStopControls()
        {
            autoStop.Enabled = !engine.Active && !choosingKey;
            stopMinutes.Enabled = autoStop.Enabled && autoStop.Checked;
            stopSeconds.Enabled = autoStop.Enabled && autoStop.Checked;
        }

        private static string FormatDuration(long milliseconds)
        {
            long seconds = (Math.Max(0L, milliseconds) + 999L) / 1000L;
            return (seconds / 60).ToString("00") + ":" + (seconds % 60).ToString("00");
        }

        private void UpdateAutoStopHint(long now)
        {
            string text;
            if (engine.Active && engine.StopsAt != 0)
                text = "TIME LEFT  /  " + FormatDuration(engine.StopsAt - now);
            else if (!autoStop.Checked)
                text = "OFF / runs until stopped";
            else if (DurationMilliseconds == 0)
                text = "Enter at least 1 second";
            else
                text = "READY / " + FormatDuration(DurationMilliseconds) + " per run";
            if (autoStopHint.Text != text) autoStopHint.Text = text;
        }

        private void AddDivider(Control parent, int x, int y, int w, int h)
        {
            Panel line = new Panel();
            line.SetBounds(x, y, w, h);
            line.BackColor = AppColors.Border;
            parent.Controls.Add(line);
        }

        private void AttachWindowDrag(Control control)
        {
            control.MouseDown += delegate(object sender, MouseEventArgs e)
            {
                if (e.Button != MouseButtons.Left) return;
                Native.ReleaseCapture();
                Native.SendMessage(Handle, 0x00A1, new IntPtr(2), IntPtr.Zero);
            };
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (Pen border = new Pen(AppColors.ControlBorder))
                e.Graphics.DrawRectangle(border, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
            using (Pen topLine = new Pen(AppColors.ControlBorder, 2))
                e.Graphics.DrawLine(topLine, 1, 1, ClientSize.Width - 2, 1);
        }

        private Label AddLabel(Control parent, string text, int x, int y, int w, int h,
            float size, Color color, bool bold)
        {
            Label label = new Label();
            label.SetBounds(x, y, w, h);
            label.Text = text;
            label.ForeColor = color;
            label.Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular);
            parent.Controls.Add(label);
            return label;
        }

        private Button MakeButton(string text, int x, int y, int w, Color bg, Color fg)
        {
            Button button = new Button();
            button.SetBounds(x, y, w, 43);
            button.Text = text;
            button.BackColor = bg;
            button.ForeColor = fg;
            button.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.BorderColor = AppColors.ControlBorder;
            button.FlatAppearance.MouseOverBackColor = Color.FromArgb(
                Math.Min(255, bg.R + 8), Math.Min(255, bg.G + 8), Math.Min(255, bg.B + 8));
            button.Cursor = Cursors.Hand;
            Controls.Add(button);
            return button;
        }

        private void UpdateRate()
        {
            rate.Text = (1000M / interval.Value).ToString("0.#") + " /s";
            if (timingHint != null)
            {
                int baseInterval = (int)interval.Value;
                int spread = baseInterval / 10;
                timingHint.Text = variation.Checked
                    ? (baseInterval - spread) + "-" + (baseInterval + spread) + " ms target window"
                    : "OFF / steady " + baseInterval + " ms";
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (!enableHotkeys) return;
            hotkeys = new GlobalHotkeys(Handle, toggleKey);
            changeKey.Enabled = hotkeys.Active;
            if (!hotkeys.Active)
            {
                startHint.Text = "Shortcut unavailable";
                status.Text = "KEY ERROR";
                status.ForeColor = AppColors.Warning;
                detail.Text = "Windows could not enable the shortcut. Reopen Autoclicker to retry.";
            }
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            engine.Stop();
            timer.Stop();
            if (hotkeys != null) hotkeys.Dispose();
            hotkeys = null;
            base.OnHandleDestroyed(e);
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == Native.WmHotkey)
            {
                int id = message.WParam.ToInt32();
                if (id == BindingId && choosingKey)
                    ApplyBinding(message.LParam.ToInt32());
                else if (id == ToggleId && !choosingKey)
                {
                    if (engine.Active) StopClicking(StoppedMessage);
                    else StartClicking();
                }
            }
            base.WndProc(ref message);
        }

        private void StartClicking()
        {
            if (choosingKey || hotkeys == null || !hotkeys.Active) return;
            Validate();
            if (autoStop.Checked && DurationMilliseconds == 0)
            {
                detail.Text = "Enter at least 1 second, or uncheck Stop after.";
                return;
            }
            long now = clock.ElapsedMilliseconds;
            engine.Start(now, (int)interval.Value, 0, variation.Checked, autoStop.Checked ? DurationMilliseconds : 0);
            UpdateAutoStopControls();
            UpdateAutoStopHint(now);
            interval.Enabled = false;
            variation.Enabled = false;
            startHint.Text = "Press " + KeyName + " to stop";
            status.ForeColor = accent;
            status.BackColor = AppColors.Selection;
            status.Text = "CLICKING";
            detail.Text = "Clicking at your cursor. Press " + KeyName + " again to stop.";
            counter.Text = "0";
            timer.Start();
            if (sounds != null) sounds.Play(true);
        }

        private void StopClicking(string reason)
        {
            bool wasClicking = timer.Enabled;
            engine.Stop();
            timer.Stop();
            // The engine may already have stopped itself after an input error
            // or when the cursor moved onto this window. The timer remembers
            // that transition so these stops also receive the off cue.
            if (wasClicking && sounds != null) sounds.Play(false);
            UpdateAutoStopControls();
            UpdateAutoStopHint(clock.ElapsedMilliseconds);
            interval.Enabled = true;
            variation.Enabled = true;
            startHint.Text = "Press " + KeyName + " to start";
            if (enableHotkeys && (hotkeys == null || !hotkeys.Active)) return;
            status.Text = "STOPPED";
            status.ForeColor = muted;
            status.BackColor = AppColors.Button;
            detail.Text = reason;
            Text = "Autoclicker";
        }

        private void OnTick(object sender, EventArgs e)
        {
            long now = clock.ElapsedMilliseconds;
            try
            {
                engine.Tick(now, Native.CursorIsOverProcess(processId));
            }
            catch (Exception)
            {
                StopClicking("Windows could not send a click. Clicking has stopped.");
                return;
            }
            if (!engine.Active)
            {
                StopClicking(engine.TimeLimitReached
                    ? "Time is up. Press " + KeyName + " to start another run."
                    : "Move to the game and press " + KeyName + " to start again.");
                return;
            }
            UpdateAutoStopHint(now);
            status.Text = "CLICKING";
            detail.Text = "Clicking at your cursor. Press " + KeyName + " again to stop.";
            counter.Text = engine.Count.ToString("N0");
            Text = "Autoclicker - Clicking (" + KeyName + " toggles off)";
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { engine.Stop(); timer.Dispose(); }
            base.Dispose(disposing);
            if (disposing)
            {
                if (sounds != null) { sounds.Dispose(); sounds = null; }
                if (applicationIcon != null) { applicationIcon.Dispose(); applicationIcon = null; }
            }
        }
    }

    internal sealed class ToggleSounds : IDisposable
    {
        private MemoryStream onStream;
        private MemoryStream offStream;
        private System.Media.SoundPlayer onPlayer;
        private System.Media.SoundPlayer offPlayer;

        internal ToggleSounds()
        {
            try
            {
                onStream = new MemoryStream(CreateTone(660, 990));
                offStream = new MemoryStream(CreateTone(495, 330));
                onPlayer = new System.Media.SoundPlayer(onStream);
                offPlayer = new System.Media.SoundPlayer(offStream);
                // Preload before clicking begins; Play is asynchronous.
                onPlayer.Load();
                offPlayer.Load();
            }
            catch (Exception) { Dispose(); }
        }

        internal void Play(bool active)
        {
            try
            {
                System.Media.SoundPlayer player = active ? onPlayer : offPlayer;
                if (player != null) player.Play();
            }
            catch (Exception)
            {
                // Audio is optional: an unavailable device must never prevent
                // a start or stop action from taking effect.
            }
        }

        internal static byte[] CreateTone(double startHz, double endHz)
        {
            const int sampleRate = 22050;
            const int samples = 2646; // 120 ms, mono, 16-bit PCM.
            using (MemoryStream wave = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(wave))
            {
                writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
                writer.Write(36 + samples * 2);
                writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
                writer.Write(16);
                writer.Write((short)1);
                writer.Write((short)1);
                writer.Write(sampleRate);
                writer.Write(sampleRate * 2);
                writer.Write((short)2);
                writer.Write((short)16);
                writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
                writer.Write(samples * 2);
                double phase = 0;
                for (int i = 0; i < samples; i++)
                {
                    double progress = i / (double)(samples - 1);
                    double frequency = startHz + (endHz - startHz) * progress;
                    double fade = Math.Min(1, i / 220.0) * Math.Min(1, (samples - 1 - i) / 330.0);
                    writer.Write((short)(Math.Sin(phase) * fade * 0.16 * short.MaxValue));
                    phase += 2 * Math.PI * frequency / sampleRate;
                }
                writer.Flush();
                return wave.ToArray();
            }
        }

        public void Dispose()
        {
            // End native asynchronous playback before releasing its buffers.
            try
            {
                if (onPlayer != null) onPlayer.Stop();
                else if (offPlayer != null) offPlayer.Stop();
            }
            catch (Exception) { }
            if (onPlayer != null) { onPlayer.Dispose(); onPlayer = null; }
            if (offPlayer != null) { offPlayer.Dispose(); offPlayer = null; }
            if (onStream != null) { onStream.Dispose(); onStream = null; }
            if (offStream != null) { offStream.Dispose(); offStream = null; }
        }
    }

    internal sealed class LargeCheckBox : CheckBox
    {
        internal LargeCheckBox()
        {
            AutoSize = false;
            FlatStyle = FlatStyle.Flat;
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.UserPaint | ControlStyles.Opaque | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnCheckedChanged(EventArgs e)
        {
            base.OnCheckedChanged(e);
            Invalidate();
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            Invalidate();
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            Invalidate();
        }

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            // ButtonBase's background path does not fill this custom glyph's
            // entire buffer. Paint our own background before drawing any text.
            using (SolidBrush background = new SolidBrush(BackColor))
                e.Graphics.FillRectangle(background, ClientRectangle);
            float scale = e.Graphics.DpiX / 96F;
            int size = (int)Math.Round(20F * scale);
            int top = (Height - size) / 2;
            Rectangle box = new Rectangle(0, top, size - 1, size - 1);
            Color lineColor = !Enabled ? AppColors.ControlBorder
                : Checked ? AppColors.Selection : AppColors.MutedText;
            Color fillColor = Checked && Enabled ? AppColors.Selection : AppColors.Background;
            using (SolidBrush fill = new SolidBrush(fillColor)) e.Graphics.FillRectangle(fill, box);
            using (Pen border = new Pen(lineColor)) e.Graphics.DrawRectangle(border, box);
            if (Checked)
            {
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using (Pen check = new Pen(Enabled ? AppColors.InverseText : AppColors.DisabledText, 2F * scale))
                    e.Graphics.DrawLines(check, new PointF[] {
                        new PointF(size * 0.23F, top + size * 0.49F),
                        new PointF(size * 0.43F, top + size * 0.70F),
                        new PointF(size * 0.78F, top + size * 0.28F) });
            }
            int textLeft = size + (int)Math.Round(8F * scale);
            TextRenderer.DrawText(e.Graphics, Text, Font,
                new Rectangle(textLeft, 0, Math.Max(0, Width - textLeft), Height),
                Enabled ? ForeColor : AppColors.DisabledText,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            if (Focused && ShowFocusCues)
                ControlPaint.DrawFocusRectangle(e.Graphics, new Rectangle(0, 0, Width - 1, Height - 1));
        }
    }

    internal sealed class StyledPanel : Panel
    {
        internal Color LineColor = AppColors.Border;
        internal StyledPanel() { DoubleBuffered = true; }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (Pen line = new Pen(LineColor))
                e.Graphics.DrawRectangle(line, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
        }
    }

    internal static class KeySettings
    {
        internal static int Load(string path)
        {
            try
            {
                int key;
                if (!String.IsNullOrEmpty(path) && File.Exists(path) &&
                    Int32.TryParse(File.ReadAllText(path).Trim(), out key) && KeyBindings.IsBindable(key))
                    return key;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return (int)Keys.F11;
        }

        internal static bool TrySave(string path, int key)
        {
            if (String.IsNullOrEmpty(path) || !KeyBindings.IsBindable(key)) return false;
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                string directory = Path.GetDirectoryName(Path.GetFullPath(path));
                if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);
                File.WriteAllText(temporary, key.ToString(System.Globalization.CultureInfo.InvariantCulture));
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
                return true;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
            finally
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    internal struct KeyResult
    {
        internal bool Handled;
        internal int Action;
        internal int Key;
        internal KeyResult(bool handled, int action, int key)
        {
            Handled = handled;
            Action = action;
            Key = key;
        }
    }

    // Only transient held-key flags are kept, never keystroke history.
    // A captured key is applied on release, so assigning it cannot start clicking.
    internal sealed class KeyBindings
    {
        private readonly bool[] held = new bool[256];
        private readonly bool[] suppressed = new bool[256];
        private int candidate;
        internal int ToggleKey { get; private set; }
        internal bool Capturing { get; private set; }

        internal KeyBindings(int key = (int)Keys.F11) { SetToggleKey(key); }

        internal static bool IsBindable(int key) { return key >= 8 && key <= 254; }

        internal static string DisplayName(int key)
        {
            if (key >= (int)Keys.D0 && key <= (int)Keys.D9) return ((char)key).ToString();
            switch ((Keys)key)
            {
                case Keys.LShiftKey: return "Left Shift";
                case Keys.RShiftKey: return "Right Shift";
                case Keys.ShiftKey: return "Shift";
                case Keys.LControlKey: return "Left Ctrl";
                case Keys.RControlKey: return "Right Ctrl";
                case Keys.ControlKey: return "Ctrl";
                case Keys.LMenu: return "Left Alt";
                case Keys.RMenu: return "Right Alt";
                case Keys.Menu: return "Alt";
                case Keys.LWin: return "Left Windows";
                case Keys.RWin: return "Right Windows";
                case Keys.Return: return "Enter";
                case Keys.Back: return "Backspace";
                case Keys.Capital: return "Caps Lock";
                case Keys.Prior: return "Page Up";
                case Keys.Next: return "Page Down";
                case Keys.Snapshot: return "Print Screen";
                case Keys.OemMinus: return "-";
                case Keys.Oemplus: return "=";
                case Keys.Oemcomma: return ",";
                case Keys.OemPeriod: return ".";
                case Keys.OemQuestion: return "/";
                default: return new KeysConverter().ConvertToString((Keys)key);
            }
        }

        internal void SetToggleKey(int key)
        {
            if (!IsBindable(key)) throw new ArgumentOutOfRangeException("key");
            ToggleKey = key;
            CancelCapture();
        }

        internal void BeginCapture() { Capturing = true; candidate = 0; }
        internal void CancelCapture() { Capturing = false; candidate = 0; }

        internal KeyResult Process(int key, bool down)
        {
            if (!IsBindable(key)) return new KeyResult();
            bool wasHeld = held[key];
            held[key] = down;
            if (!down)
            {
                bool handled = suppressed[key];
                suppressed[key] = false;
                if (Capturing && candidate == key)
                {
                    SetToggleKey(key);
                    return new KeyResult(true, 2, key);
                }
                return new KeyResult(handled, 0, key);
            }
            if (suppressed[key]) return new KeyResult(true, 0, key);
            if (wasHeld) return new KeyResult();
            if (Capturing)
            {
                if (candidate == 0) candidate = key;
                suppressed[key] = true;
                return new KeyResult(true, 0, key);
            }
            if (key == ToggleKey)
            {
                suppressed[key] = true;
                return new KeyResult(true, 1, key);
            }
            return new KeyResult();
        }
    }

    internal sealed class GlobalHotkeys : IDisposable
    {
        private readonly IntPtr window;
        private readonly Native.KeyboardProc callback;
        internal readonly KeyBindings Bindings;
        private IntPtr hook;
        internal bool Active { get { return hook != IntPtr.Zero; } }

        internal GlobalHotkeys(IntPtr targetWindow, int key = (int)Keys.F11)
        {
            window = targetWindow;
            Bindings = new KeyBindings(key);
            callback = OnKeyboard;
            hook = Native.SetWindowsHookEx(13, callback, Native.GetModuleHandle(null), 0);
        }

        private IntPtr OnKeyboard(int code, IntPtr message, IntPtr data)
        {
            if (code >= 0)
            {
                int kind = message.ToInt32();
                bool down = kind == 0x0100 || kind == 0x0104;
                bool up = kind == 0x0101 || kind == 0x0105;
                int key = Marshal.ReadInt32(data);
                if ((down || up) && DispatchKey(key, down))
                    return new IntPtr(1);
            }
            return Native.CallNextHookEx(hook, code, message, data);
        }

        internal bool DispatchKey(int key, bool down)
        {
            KeyResult result = Bindings.Process(key, down);
            if (result.Action != 0)
                Native.PostMessage(window, Native.WmHotkey, new IntPtr(result.Action), new IntPtr(result.Key));
            return result.Handled;
        }

        public void Dispose()
        {
            if (hook != IntPtr.Zero) Native.UnhookWindowsHookEx(hook);
            hook = IntPtr.Zero;
            GC.KeepAlive(callback);
        }
    }

    internal static class Native
    {
        internal const int WmHotkey = 0x8001;
        [DllImport("user32.dll")] internal static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] internal static extern bool ReleaseCapture();
        [DllImport("user32.dll")]
        internal static extern IntPtr SendMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] internal static extern bool ShowWindowAsync(IntPtr window, int command);
        [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(IntPtr window);
        internal delegate IntPtr KeyboardProc(int code, IntPtr message, IntPtr data);
        [DllImport("user32.dll", SetLastError = true)]
        internal static extern IntPtr SetWindowsHookEx(int type, KeyboardProc callback, IntPtr module, uint thread);
        [DllImport("user32.dll")] internal static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")]
        internal static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        internal static extern IntPtr GetModuleHandle(string name);
        [DllImport("user32.dll")]
        internal static extern bool PostMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint count, Input[] inputs, int size);
        [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
        [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(Point point);
        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);

        [StructLayout(LayoutKind.Sequential)]
        internal struct Input { public uint Type; public InputUnion Data; }
        [StructLayout(LayoutKind.Explicit)]
        internal struct InputUnion { [FieldOffset(0)] public MouseInput Mouse; }
        [StructLayout(LayoutKind.Sequential)]
        internal struct MouseInput
        {
            public int X, Y;
            public uint MouseData, Flags, Time;
            public UIntPtr ExtraInfo;
        }

        internal static bool CursorIsOverProcess(int processId)
        {
            Point point;
            if (!GetCursorPos(out point)) return true;
            uint pid;
            GetWindowThreadProcessId(WindowFromPoint(point), out pid);
            return pid == (uint)processId;
        }

        internal static Input[] CreateClick()
        {
            Input down = new Input();
            down.Data.Mouse.Flags = 0x0002;
            Input up = new Input();
            up.Data.Mouse.Flags = 0x0004;
            return new Input[] { down, up };
        }

        internal static void LeftClick()
        {
            Input[] inputs = CreateClick();
            uint sent = SendInput(2, inputs, Marshal.SizeOf(typeof(Input)));
            if (sent != 2)
            {
                if (sent == 1) SendInput(1, new Input[] { inputs[1] }, Marshal.SizeOf(typeof(Input)));
                throw new InvalidOperationException("SendInput did not accept both mouse events.");
            }
        }
    }

    internal static class Verification
    {
        private static void Check(bool condition, string description)
        {
            if (!condition) throw new Exception(description);
        }

        internal static int Run(string reportPath)
        {
            try
            {
                KeyBindings bindings = new KeyBindings();
                Check(bindings.Process((int)Keys.F11, true).Action == 1, "F11 must toggle by default.");
                Check(bindings.Process((int)Keys.F11, true).Action == 0, "Holding the key must not toggle repeatedly.");
                Check(bindings.Process((int)Keys.F11, false).Action == 0, "Releasing the key must not toggle.");
                Check(bindings.Process((int)Keys.F11, true).Action == 1, "A second key press must toggle again.");
                Check(!bindings.Process((int)Keys.F12, true).Handled, "Unbound keys must pass through.");
                Check(!bindings.Process((int)Keys.F12, false).Handled, "Unbound key releases must pass through.");
                foreach (Keys key in new Keys[] { Keys.F9, Keys.F12, Keys.A, Keys.D7, Keys.Space,
                    Keys.Escape, Keys.Tab, Keys.LControlKey, Keys.RShiftKey, Keys.LWin, Keys.VolumeUp, Keys.OemMinus })
                {
                    KeyBindings rebind = new KeyBindings();
                    rebind.BeginCapture();
                    KeyResult press = rebind.Process((int)key, true);
                    Check(press.Handled && press.Action == 0 && rebind.Capturing,
                        "Capturing " + key + " must consume the key without clicking.");
                    Check(rebind.Process((int)key, true).Action == 0, "Holding a captured key must not toggle.");
                    KeyResult release = rebind.Process((int)key, false);
                    Check(release.Handled && release.Action == 2 && release.Key == (int)key &&
                        rebind.ToggleKey == (int)key && !rebind.Capturing, "Release must finish rebinding.");
                    Check(!rebind.Process((int)Keys.F11, true).Handled, "The old key must return to normal use.");
                    Check(rebind.Process((int)key, true).Action == 1, "The new key must toggle.");
                    Check(rebind.Process((int)key, true).Action == 0, "New-key auto-repeat must be suppressed.");
                    rebind.Process((int)key, false);
                    Check(rebind.Process((int)key, true).Action == 1, "The new key must toggle off on its next press.");
                }
                KeyBindings cancelled = new KeyBindings();
                cancelled.BeginCapture();
                cancelled.Process((int)Keys.A, true);
                cancelled.CancelCapture();
                KeyResult cancelledRelease = cancelled.Process((int)Keys.A, false);
                Check(cancelledRelease.Handled && cancelledRelease.Action == 0 && cancelled.ToggleKey == (int)Keys.F11,
                    "Cancel must preserve the old key and consume the captured key's release.");
                Check(!cancelled.Process((int)Keys.A, true).Handled, "Cancelled keys must work normally again.");
                KeyBindings alreadyHeld = new KeyBindings();
                alreadyHeld.Process((int)Keys.B, true);
                alreadyHeld.BeginCapture();
                Check(alreadyHeld.Process((int)Keys.B, true).Action == 0, "A previously held key must not become the binding.");
                Check(alreadyHeld.Process((int)Keys.B, false).Action == 0 && alreadyHeld.Capturing,
                    "Capture must wait for a fresh key press.");
                string settingsTest = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(reportPath)),
                    "key-test-" + Guid.NewGuid().ToString("N") + ".settings");
                try
                {
                    Check(KeySettings.Load(settingsTest) == (int)Keys.F11, "Missing settings must default to F11.");
                    Check(KeySettings.TrySave(settingsTest, (int)Keys.F9), "A selected key must save.");
                    Check(KeySettings.Load(settingsTest) == (int)Keys.F9, "A saved key must reload.");
                    Check(KeySettings.TrySave(settingsTest, (int)Keys.Space) && KeySettings.Load(settingsTest) == (int)Keys.Space,
                        "Changing an existing saved key must replace it.");
                    File.WriteAllText(settingsTest, "invalid-key");
                    Check(KeySettings.Load(settingsTest) == (int)Keys.F11, "Invalid settings must recover to F11.");
                    Check(!KeySettings.TrySave(settingsTest, 0), "An invalid key must not overwrite settings.");
                }
                finally { if (File.Exists(settingsTest)) File.Delete(settingsTest); }
                using (GlobalHotkeys hotkeys = new GlobalHotkeys(IntPtr.Zero))
                {
                    Check(hotkeys.Active, "Windows must accept the keyboard hook.");
                    hotkeys.Dispose();
                    Check(!hotkeys.Active, "Closing must remove the keyboard hook.");
                }
                int clicks = 0;
                ClickEngine engine = new ClickEngine(delegate { clicks++; });
                engine.Tick(0, false);
                Check(clicks == 0, "Idle must not click.");
                engine.Start(0, 50, 3000);
                engine.Tick(2999, false);
                Check(clicks == 0, "Countdown must not click early.");
                engine.Tick(3000, false);
                engine.Tick(3049, false);
                Check(clicks == 1, "Interval must be respected.");
                engine.Tick(3050, false);
                Check(clicks == 2 && engine.Count == 2, "Scheduled click and count.");
                engine.Stop();
                engine.Tick(10000, false);
                Check(clicks == 2, "Stopping must cancel future clicks.");
                engine.Start(10000, 20, 0);
                engine.Tick(10000, false);
                Check(engine.Count == 1, "Restart must reset counter and click immediately.");
                engine.Tick(20000, false);
                engine.Tick(20000, false);
                Check(engine.Count == 2, "Late ticks must never create a catch-up burst.");
                engine.Tick(20020, true);
                Check(!engine.Active && engine.Count == 2, "Own window must stop clicking.");
                engine.Start(30000, 50, 3000);
                engine.Stop();
                engine.Tick(33000, false);
                Check(engine.Count == 0, "Stop must cancel countdown.");
                ClickEngine timed = new ClickEngine(delegate { });
                timed.Start(1000, 2000, 0, false, 1000);
                timed.Tick(1000, false);
                timed.Tick(1999, false);
                Check(timed.Active && timed.Count == 1, "A timed run must click immediately and remain active before expiry.");
                timed.Tick(2000, false);
                Check(!timed.Active && timed.TimeLimitReached && timed.Count == 1,
                    "Expiry must stop even when the next click is not due yet.");
                timed.Start(3000, 50, 0, false, 1000);
                Check(!timed.TimeLimitReached && timed.StopsAt == 4000, "Restart must reset the full duration and expiry state.");
                for (int time = 3000; time <= 4000; time += 50) timed.Tick(time, false);
                Check(!timed.Active && timed.Count == 20, "No click may be sent at the deadline.");
                timed.Start(5000, 50, 0, true, 1000);
                timed.Tick(5000, false);
                timed.Tick(9000, false);
                Check(!timed.Active && timed.TimeLimitReached && timed.Count == 1,
                    "A delayed tick must stop without a late click, including with variation.");
                timed.Start(10000, 50, 0, false, 1000);
                timed.Stop();
                timed.Tick(12000, false);
                Check(!timed.TimeLimitReached && timed.Count == 0, "Manual stop must cancel the timed run.");
                timed.Start(13000, 50, 0);
                timed.Tick(100000, false);
                Check(timed.Active && timed.StopsAt == 0, "Turning the timer off must clear the previous deadline.");
                bool badDurationRejected = false;
                try { timed.Start(0, 50, 0, false, -1); }
                catch (ArgumentOutOfRangeException) { badDurationRejected = true; }
                Check(badDurationRejected, "Negative durations must be rejected.");
                int cadenceClicks = 0;
                ClickEngine cadence = new ClickEngine(delegate { cadenceClicks++; });
                cadence.Start(0, 50, 0);
                for (int time = 0; time < 10000; time += 16) cadence.Tick(time, false);
                Check(cadenceClicks == 200, "Cadence must average 20 clicks/second at a 16 ms timer granularity.");
                Random seededRandom = new Random(182);
                long simulatedTime = 0;
                long previousClick = -1;
                long minimumGap = long.MaxValue;
                long maximumGap = 0;
                ClickEngine varied = new ClickEngine(delegate
                {
                    if (previousClick >= 0)
                    {
                        long gap = simulatedTime - previousClick;
                        Check(gap >= 45 && gap <= 55, "Varied targets must stay within +/-10 percent.");
                        minimumGap = Math.Min(minimumGap, gap);
                        maximumGap = Math.Max(maximumGap, gap);
                    }
                    previousClick = simulatedTime;
                }, seededRandom.Next);
                varied.Start(0, 50, 0, true);
                for (simulatedTime = 0; simulatedTime < 60000; simulatedTime++) varied.Tick(simulatedTime, false);
                Check(minimumGap == 45 && maximumGap == 55, "Variation must produce different click intervals.");
                double averageGap = previousClick / (double)(varied.Count - 1);
                Check(averageGap > 49.5 && averageGap < 50.5, "Variation must preserve the average target speed.");
                long variedCount = varied.Count;
                varied.Stop();
                varied.Tick(100000, false);
                Check(varied.Count == variedCount, "Stop must immediately cancel a pending varied click.");
                ClickEngine variationDisabled = new ClickEngine(delegate { }, delegate(int min, int max)
                {
                    throw new Exception("Disabled variation must not request randomness.");
                });
                variationDisabled.Start(0, 50, 0, false);
                variationDisabled.Tick(0, false);
                variationDisabled.Tick(49, false);
                Check(variationDisabled.Count == 1, "Disabled variation must retain fixed timing.");
                variationDisabled.Tick(50, false);
                Check(variationDisabled.Count == 2, "Disabled variation must retain fixed cadence.");
                ClickEngine delayedVariation = new ClickEngine(delegate { }, new Random(183).Next);
                delayedVariation.Start(0, 50, 3000, true);
                delayedVariation.Tick(2999, false);
                Check(delayedVariation.Count == 0, "Variation must respect the start countdown.");
                delayedVariation.Tick(3000, false);
                delayedVariation.Tick(10000, false);
                delayedVariation.Tick(10000, false);
                Check(delayedVariation.Count == 2, "Varied clicks must not catch up in a burst after a stall.");
                ClickEngine coarseTimer = new ClickEngine(delegate { }, new Random(184).Next);
                coarseTimer.Start(0, 50, 0, true);
                for (int time = 0; time < 60000; time += 16) coarseTimer.Tick(time, false);
                Check(coarseTimer.Count >= 1188 && coarseTimer.Count <= 1212,
                    "Varied timing must stay near 20 clicks/second with realistic timer granularity.");
                bool badIntervalRejected = false;
                try { cadence.Start(0, 0, 0); }
                catch (ArgumentOutOfRangeException) { badIntervalRejected = true; }
                Check(badIntervalRejected, "Invalid intervals must be rejected.");
                ClickEngine failure = new ClickEngine(delegate { throw new InvalidOperationException(); });
                failure.Start(0, 50, 0);
                try { failure.Tick(0, false); } catch (InvalidOperationException) { }
                Check(!failure.Active && failure.Count == 0, "Input failure must stop clicking.");
                Check(Marshal.SizeOf(typeof(Native.Input)) == (IntPtr.Size == 8 ? 40 : 28), "Windows INPUT structure size.");
                Native.Input[] pair = Native.CreateClick();
                Check(pair.Length == 2 && pair[0].Type == 0 && pair[1].Type == 0 &&
                    pair[0].Data.Mouse.Flags == 2 && pair[1].Data.Mouse.Flags == 4,
                    "Each click must pair left-down with left-up, without moving the pointer.");
                File.WriteAllText(reportPath, "PASS: timed stop before pending clicks, deadline boundary, delayed expiry, timer restart/cancel/disable, duration validation, customizable key capture, release-to-assign, cancellation, saved binding reload/replacement, invalid settings recovery, held-key suppression, previous-key pass-through, keyboard categories, native hook installation/removal, bounded timing variation, average speed, stopping during variation, fixed timing option, countdown, no catch-up bursts, scheduling, stop, restart, own-window guard, interval validation, failure handling, native INPUT layout, paired mouse events.\r\nSimulated variation average: " + averageGap.ToString("0.000") + " ms.\r\nNo real mouse or keyboard input was sent.\r\n");
                return 0;
            }
            catch (Exception error)
            {
                File.WriteAllText(reportPath, "FAIL: " + error.ToString());
                return 1;
            }
        }
    }
}
