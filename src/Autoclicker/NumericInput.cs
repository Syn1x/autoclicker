using System;
using System.Windows.Forms;

namespace Autoclicker
{
    internal sealed class NumericInput : NumericUpDown
    {
        internal NumericInput() { TextAlign = HorizontalAlignment.Center; }

        internal void CommitEdit()
        {
            // Use the control's existing parsing and range validation, including
            // restoring the last valid value for empty or invalid text.
            if (UserEdit) ValidateEditText();
            if (String.IsNullOrWhiteSpace(Text))
                Text = Value.ToString((ThousandsSeparator ? "N" : "F") + DecimalPlaces,
                    System.Globalization.CultureInfo.CurrentCulture);
        }

        protected override void OnLeave(EventArgs e)
        {
            CommitEdit();
            base.OnLeave(e);
        }
    }

    internal static class InputCommit
    {
        internal static void Attach(Form form)
        {
            WatchClicks(form, form);
            form.Deactivate += delegate { CommitOutside(form, null); };
            form.FormClosing += delegate { CommitOutside(form, null); };
        }

        private static void WatchClicks(Form form, Control control)
        {
            control.MouseDown += delegate
            {
                CommitOutside(form, control);
                // Labels and panels do not normally take focus. Treat them as
                // leaving the editor too, without focusing anything on blur.
                if (form.ActiveControl is NumericInput &&
                    control != form.ActiveControl && !form.ActiveControl.Contains(control))
                    form.ActiveControl = null;
            };
            control.ControlAdded += delegate(object sender, ControlEventArgs e) { WatchClicks(form, e.Control); };
            foreach (Control child in control.Controls) WatchClicks(form, child);
        }

        internal static void CommitOutside(Control root, Control clicked)
        {
            NumericInput input = root as NumericInput;
            if (input != null)
            {
                if (clicked != input && (clicked == null || !input.Contains(clicked))) input.CommitEdit();
                return;
            }
            foreach (Control child in root.Controls) CommitOutside(child, clicked);
        }
    }
}
