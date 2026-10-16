using System;
using System.Drawing;
using System.Windows.Forms;

namespace Autoclicker
{
    internal sealed class ReleaseNotesForm : Form
    {
        internal ReleaseNotesForm(string version, string notes)
        {
            SuspendLayout();
            Text = "What's new - Autoclicker";
            ClientSize = new Size(500, 420);
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            BackColor = AppColors.Background;
            ForeColor = AppColors.Text;
            Font = new Font("Segoe UI", 9F);
            Padding = new Padding(1);
            DoubleBuffered = true;

            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3,
                BackColor = BackColor, Margin = Padding.Empty, Padding = Padding.Empty };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 80));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));
            Controls.Add(layout);

            var header = new Panel { Dock = DockStyle.Fill, BackColor = AppColors.Header, Margin = Padding.Empty,
                Size = new Size(498, 80) };
            var title = new Label { Text = "What's new", Font = new Font("Segoe UI", 17F), ForeColor = ForeColor,
                AutoSize = false, Location = new Point(23, 13), Size = new Size(370, 34) };
            var versionLabel = new Label { Text = "Autoclicker v" + version, ForeColor = AppColors.MutedText,
                Location = new Point(24, 50), Size = new Size(370, 20), AutoSize = false };
            var close = CreateButton("×");
            close.SetBounds(444, 20, 30, 30);
            close.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            close.AccessibleName = "Close release notes";
            close.Click += delegate { Close(); };
            header.Controls.AddRange(new Control[] { title, versionLabel, close });
            layout.Controls.Add(header, 0, 0);
            MouseEventHandler drag = delegate(object sender, MouseEventArgs e)
            {
                if (e.Button != MouseButtons.Left) return;
                Native.ReleaseCapture();
                Native.SendMessage(Handle, 0xA1, new IntPtr(2), IntPtr.Zero);
            };
            header.MouseDown += drag;
            title.MouseDown += drag;
            versionLabel.MouseDown += drag;

            var body = new TextBox { Dock = DockStyle.Fill, ReadOnly = true, Multiline = true, BorderStyle = BorderStyle.None,
                BackColor = BackColor, ForeColor = ForeColor, Font = new Font("Segoe UI", 10F),
                ScrollBars = ScrollBars.Vertical, WordWrap = true,
                Margin = new Padding(24, 20, 24, 0), Text = notes, AccessibleName = "Changes in this version" };
            layout.Controls.Add(body, 0, 1);
            var footer = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft,
                Margin = Padding.Empty, Padding = new Padding(24, 15, 24, 15), WrapContents = false };
            var done = CreateButton("Got it");
            done.Size = new Size(100, 36);
            done.Margin = Padding.Empty;
            done.DialogResult = DialogResult.OK;
            done.Click += delegate { Close(); };
            footer.Controls.Add(done);
            layout.Controls.Add(footer, 0, 2);
            AcceptButton = done;
            CancelButton = done;
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ResumeLayout(true);
        }

        private Button CreateButton(string text)
        {
            var button = new Button { Text = text, FlatStyle = FlatStyle.Flat, BackColor = AppColors.Button,
                ForeColor = AppColors.Text, Cursor = Cursors.Hand, UseVisualStyleBackColor = false };
            button.FlatAppearance.BorderColor = AppColors.ControlBorder;
            return button;
        }

        protected override void OnShown(EventArgs e)
        {
            Rectangle area = Screen.FromControl(Owner ?? this).WorkingArea;
            Width = Math.Min(Width, area.Width - 24);
            Height = Math.Min(Height, area.Height - 24);
            Location = new Point(Math.Max(area.Left + 12, Math.Min(Left, area.Right - Width - 12)),
                Math.Max(area.Top + 12, Math.Min(Top, area.Bottom - Height - 12)));
            base.OnShown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var pen = new Pen(AppColors.Border))
                e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }
    }

    internal sealed partial class ClickerForm
    {
        private ReleaseNotesForm releaseNotesWindow;
        private LinkLabel releaseNotesLink;

        internal void EnableReleaseNotesOnLaunch()
        {
            Shown += delegate
            {
                if (!ReleaseNotes.ShouldShow(settingsPath, AppInfo.Version)) return;
                BeginInvoke(new Action(delegate { if (!IsDisposed && !Disposing) ShowReleaseNotes(); }));
            };
        }

        private void ShowReleaseNotes()
        {
            if (releaseNotesWindow != null) { releaseNotesWindow.Activate(); return; }
            if (choosingKey) CancelKeyCapture();
            StopClicking(StoppedMessage);
            using (var notes = new ReleaseNotesForm(AppInfo.Version, ReleaseNotes.ReadCurrent()))
            {
                releaseNotesWindow = notes;
                try
                {
                    notes.ShowDialog(this);
                    ReleaseNotes.MarkSeen(settingsPath, AppInfo.Version);
                }
                finally { releaseNotesWindow = null; }
            }
        }
    }
}
