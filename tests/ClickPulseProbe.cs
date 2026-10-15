using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace Autoclicker
{
    internal static class ClickPulseProbe
    {
        private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
        private static T Field<T>(ClickerForm form, string name)
        { return (T)typeof(ClickerForm).GetField(name, Fields).GetValue(form); }
        private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
        private static void Refresh(ClickerForm form)
        { typeof(ClickerForm).GetMethod("RefreshClickActivity", Fields).Invoke(form, null); }
        private static Color Pixel(Control pulse)
        {
            using (var image = new Bitmap(pulse.Width, pulse.Height))
            {
                pulse.DrawToBitmap(image, pulse.ClientRectangle);
                return image.GetPixel(image.Width / 2, image.Height / 2);
            }
        }

        internal static void Run(string root)
        {
            foreach (int dpi in new[] { 96, 144, 192 })
            using (var form = new ClickerForm(false))
            {
                form.Opacity = 0;
                form.ShowInTaskbar = false;
                bool down = false;
                var engine = new ClickEngine(delegate { down = true; }, delegate { down = false; });
                typeof(ClickerForm).GetField("engine", Fields).SetValue(form, engine);
                form.Show();
                float factor = dpi / form.CurrentAutoScaleDimensions.Width;
                form.Scale(new SizeF(factor, factor));
                form.PerformLayout();
                ClickPulse pulse = Field<ClickPulse>(form, "clickPulse");
                Check(!pulse.Lit && !pulse.TabStop && !pulse.CanSelect, "The indicator must start idle and not steal focus.");
                Check(pulse.AccessibleDescription.Contains("does not confirm"), "The indicator must describe Windows acceptance, not game registration.");
                foreach (Control other in pulse.Parent.Controls)
                    if (other != pulse) Check(!pulse.Bounds.IntersectsWith(other.Bounds), "The pulse must not overlap header controls at " + dpi + " DPI.");
                Color idle = Pixel(pulse);
                engine.Start(0, 50, 0);
                Refresh(form);
                Check(!pulse.Lit, "Starting alone must not flash before input is accepted.");
                for (int t = 0; t < 150; t += 50)
                {
                    engine.Tick(t, false);
                    Refresh(form);
                    Check(down && pulse.Lit && Field<Label>(form, "counter").Text == engine.Count.ToString(), "Each accepted press must light the pulse and update its count.");
                    Check(Pixel(pulse).ToArgb() != idle.ToArgb(), "A pulse must produce a visible color change.");
                    engine.Tick(t + 20, false);
                    Refresh(form);
                    Check(!down && !pulse.Lit && Pixel(pulse).ToArgb() == idle.ToArgb(), "Each release must visibly return to idle.");
                }
                engine.Tick(150, false);
                Refresh(form);
                Field<Button>(form, "stop").PerformClick();
                Check(!pulse.Lit && !down && !engine.Active, "Stop must clear the pulse and release held input.");
                engine.Start(200, 50, 0, false, 10);
                engine.Tick(200, false);
                engine.Tick(210, false);
                Refresh(form);
                Check(!pulse.Lit && !down, "Automatic stop must clear the pulse and release input.");
                var failure = new ClickEngine(delegate { throw new InvalidOperationException(); }, delegate { });
                typeof(ClickerForm).GetField("engine", Fields).SetValue(form, failure);
                failure.Start(0, 50, 0);
                try { failure.Tick(0, false); } catch (InvalidOperationException) { }
                Refresh(form);
                Check(!pulse.Lit && Field<Label>(form, "counter").Text == "0", "Rejected input must not produce a success pulse or count.");
                typeof(ClickerForm).GetField("engine", Fields).SetValue(form, engine);
                engine.Start(300, 50, 0);
                engine.Tick(300, false);
                Refresh(form);
                Field<Label>(form, "status").Text = "Clicking";
                using (var image = new Bitmap(form.Width, form.Height))
                {
                    form.DrawToBitmap(image, new Rectangle(Point.Empty, form.Size));
                    image.Save(Path.Combine(root, "click-pulse-" + dpi + ".png"), ImageFormat.Png);
                }
                form.Close();
                Check(!engine.Active && !down && form.IsDisposed, "Closing while a click is held must release input and dispose the form.");
            }
            File.WriteAllText(Path.Combine(root, "click-pulse.txt"), "PASS: pulse for accepted presses only, visible idle/active change, no focus stealing or overlap at 100/150/200% scaling, release between clicks, stop/expiry/close cleanup, no pulse for input failure. No real input sent.");
        }
    }
}
