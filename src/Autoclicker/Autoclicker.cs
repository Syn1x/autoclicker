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

            if (args.Length == 2 && args[0] == "--apply-update")
                return PortableUpdater.RunHelper(args[1]);

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
                using (PortableUpdateBackend backend = new PortableUpdateBackend())
                using (UpdateCoordinator updates = new UpdateCoordinator(backend))
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
                            IntPtr window = Native.FindMainWindow(candidate.Id);
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
        private readonly Action press;
        private readonly Action release;
        private readonly Func<int, int, int> randomRange;
        private long nextDue;
        private long releaseDue, nextPressAllowed;
        private int interval;
        private int holdMs, gapMs;
        private bool varyTiming;
        public bool Active { get; private set; }
        public bool ButtonDown { get; private set; }
        public long Count { get; private set; }
        public long StartsAt { get; private set; }
        public long StopsAt { get; private set; }
        public bool TimeLimitReached { get; private set; }

        public ClickEngine(Action pressAction, Action releaseAction)
            : this(pressAction, releaseAction, new Random().Next) { }

        internal ClickEngine(Action pressAction, Action releaseAction, Func<int, int, int> randomSource)
        {
            press = pressAction;
            release = releaseAction;
            randomRange = randomSource;
        }

        public void Start(long now, int intervalMs, int delayMs, bool variationEnabled = false, int durationMs = 0)
        {
            if (intervalMs < 20 || intervalMs > 2000)
                throw new ArgumentOutOfRangeException("intervalMs");
            if (delayMs < 0) throw new ArgumentOutOfRangeException("delayMs");
            if (durationMs < 0) throw new ArgumentOutOfRangeException("durationMs");
            Stop();
            interval = intervalMs;
            holdMs = Math.Min(20, intervalMs / 2);
            gapMs = Math.Min(10, intervalMs / 4);
            varyTiming = variationEnabled;
            StartsAt = now + delayMs;
            StopsAt = durationMs == 0 ? 0 : StartsAt + durationMs;
            TimeLimitReached = false;
            nextDue = StartsAt;
            nextPressAllowed = StartsAt;
            Count = 0;
            Active = true;
        }

        public void Stop()
        {
            Active = false;
            ReleaseButton();
        }

        private void ReleaseButton()
        {
            if (!ButtonDown) return;
            release();
            // Keep the pending release if Windows rejects it, so stopping or
            // closing can retry instead of forgetting a held mouse button.
            ButtonDown = false;
        }

        public void Tick(long now, bool pointerIsOnThisApp)
        {
            if (!Active) return;
            // Release on every stop path, even when a press is still in flight.
            if (StopsAt != 0 && now >= StopsAt)
            {
                TimeLimitReached = true;
                Stop();
                return;
            }
            if (pointerIsOnThisApp)
            {
                Stop();
                return;
            }
            try
            {
                if (ButtonDown)
                {
                    if (now < releaseDue) return;
                    ReleaseButton();
                    nextPressAllowed = now + gapMs;
                    return;
                }
                if (now < nextDue || now < nextPressAllowed) return;
                press();
                ButtonDown = true;
                releaseDue = now + holdMs;
                Count++;
            }
            catch
            {
                Active = false;
                try { ReleaseButton(); } catch { }
                throw;
            }
            int nextInterval = interval;
            if (varyTiming)
            {
                int spread = interval / 10;
                nextInterval += randomRange(-spread, spread + 1);
            }
            // Keep the average cadence, but never flood delayed input in a burst.
            // Press/release are separate timer phases: no sleeps on the UI
            // thread, and no zero-duration presses between game input polls.
            nextDue += nextInterval;
            if (nextDue <= now) nextDue = now + nextInterval;
        }
    }

    internal sealed partial class ClickerForm : Form
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
        private readonly ClickEngine engine = new ClickEngine(Native.LeftDown, Native.LeftUp);
        private ClickPulse clickPulse;
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
        private CheckBox autoStop;
        private NumericUpDown stopMinutes;
        private NumericUpDown stopSeconds;
        private Label autoStopHint;
        private Label updateStatus;
        private Button checkUpdates;
        private UpdateCoordinator updates;
        private readonly CrosshairController crosshair;
        private CheckBox crosshairToggle;
        private CrosshairSettingsControl crosshairSettings;

        private string KeyName { get { return KeyBindings.DisplayName(toggleKey); } }
        private string StoppedMessage { get { return "Stopped. " + KeyName + " starts again."; } }

        public ClickerForm(bool hooks) : this(hooks, hooks ? AppInfo.SettingsPath : null) { }

        public ClickerForm(bool hooks, string configurationPath)
        {
            SuspendLayout();
            enableHotkeys = hooks;
            settingsPath = configurationPath;
            toggleKey = KeySettings.Load(settingsPath);
            crosshair = new CrosshairController(settingsPath);
            Text = "Autoclicker";
            using (Stream iconStream = typeof(ClickerForm).Assembly.GetManifestResourceStream("Autoclicker.AppIcon.ico"))
            {
                applicationIcon = new Icon(iconStream);
                Icon = applicationIcon;
            }
            BuildInterface();

            timer.Interval = 15;
            timer.Tick += OnTick;
            FormClosing += delegate { StopClicking("Stopped."); crosshair.SetEnabled(false); };
            Deactivate += delegate { if (choosingKey) CancelKeyCapture(); };
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ResumeLayout(false);
            if (enableHotkeys) sounds = new ToggleSounds();
            InputCommit.Attach(this);
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
            checkUpdates.Text = updates.ActionText;
            checkUpdates.Enabled = updates.CanCheck || updates.CanConfirm || updates.CanRestart;
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
            status.Text = "Choose a key";
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
            changeKey.Text = "Change";
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
            NumericUpDown input = new NumericInput();
            input.SetBounds(x, 13, 78, 31);
            input.Maximum = maximum;
            input.Value = value;
            input.Font = new Font("Segoe UI", 10F);
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
                text = "Remaining " + FormatDuration(engine.StopsAt - now);
            else if (!autoStop.Checked)
                text = "Runs until stopped";
            else if (DurationMilliseconds == 0)
                text = "Enter at least 1 second";
            else
                text = "Stops after " + FormatDuration(DurationMilliseconds);
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
                    ? (baseInterval - spread) + "-" + (baseInterval + spread) + " ms"
                    : "Steady " + baseInterval + " ms";
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
                status.Text = "Key unavailable";
                status.ForeColor = AppColors.Warning;
                detail.Text = "Windows could not enable the shortcut. Reopen Autoclicker to retry.";
            }
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            try { engine.Stop(); } catch { }
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
            try { engine.Start(now, (int)interval.Value, 0, variation.Checked, autoStop.Checked ? DurationMilliseconds : 0); }
            catch { StopClicking("Windows could not release the previous click. Press and release your left mouse button."); return; }
            UpdateAutoStopControls();
            UpdateAutoStopHint(now);
            interval.Enabled = false;
            variation.Enabled = false;
            startHint.Text = "Press " + KeyName + " to stop";
            status.ForeColor = accent;
            status.BackColor = AppColors.Selection;
            status.Text = "Clicking";
            detail.Text = "Left clicks at your cursor.";
            counter.Text = "0";
            timer.Start();
            if (sounds != null) sounds.Play(true);
        }

        private void StopClicking(string reason)
        {
            bool wasClicking = timer.Enabled;
            try { engine.Stop(); }
            catch { reason = "Windows could not release the click. Press and release your left mouse button."; }
            timer.Stop();
            clickPulse.Lit = false;
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
            status.Text = "Stopped";
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
            RefreshClickActivity();
            if (!engine.Active)
            {
                StopClicking(engine.TimeLimitReached
                    ? "Time is up. Press " + KeyName + " to start another run."
                    : "Move to the game and press " + KeyName + " to start again.");
                return;
            }
            UpdateAutoStopHint(now);
            status.Text = "Clicking";
            detail.Text = "Left clicks at your cursor.";
            Text = "Autoclicker - Clicking (" + KeyName + " toggles off)";
        }

        private void RefreshClickActivity()
        {
            clickPulse.Lit = engine.Active && engine.ButtonDown;
            counter.Text = engine.Count.ToString("N0");
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                try { engine.Stop(); } catch { }
                timer.Dispose();
                if (crosshairSettings != null) { crosshairSettings.Dispose(); crosshairSettings = null; }
                crosshair.Dispose();
            }
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
        private delegate bool EnumWindowCallback(IntPtr window, IntPtr parameter);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowCallback callback, IntPtr parameter);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr window, int index);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr window, System.Text.StringBuilder title, int maximum);

        internal static IntPtr FindMainWindow(int processId)
        {
            IntPtr found = IntPtr.Zero;
            EnumWindows(delegate(IntPtr window, IntPtr parameter)
            {
                uint pid;
                GetWindowThreadProcessId(window, out pid);
                if (pid != (uint)processId || !IsWindowVisible(window) || (GetWindowLong(window, -20) & 0x08000000) != 0)
                    return true;
                var title = new System.Text.StringBuilder(256);
                GetWindowText(window, title, title.Capacity);
                if (title.ToString() != "Autoclicker" && !title.ToString().StartsWith("Autoclicker - Clicking (", StringComparison.Ordinal))
                    return true;
                found = window;
                return false;
            }, IntPtr.Zero);
            return found;
        }

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

        internal static Input CreateMouseButton(uint flags)
        {
            Input input = new Input();
            input.Data.Mouse.Flags = flags;
            return input;
        }

        internal static void LeftDown() { SendMouseButton(0x0002); }
        internal static void LeftUp() { SendMouseButton(0x0004); }

        private static void SendMouseButton(uint flags)
        {
            Input input = CreateMouseButton(flags);
            if (SendInput(1, new Input[] { input }, Marshal.SizeOf(typeof(Input))) != 1)
                throw new InvalidOperationException("Windows did not accept the mouse button event.");
        }
    }

    internal static partial class Verification
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
                double averageGap = VerifyClickTiming();
                Check(Marshal.SizeOf(typeof(Native.Input)) == (IntPtr.Size == 8 ? 40 : 28), "Windows INPUT structure size.");
                Native.Input[] pair = new[] { Native.CreateMouseButton(2), Native.CreateMouseButton(4) };
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
