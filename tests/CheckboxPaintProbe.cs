using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace Autoclicker
{
    internal static class CheckboxPaintProbe
    {
        private static readonly BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;

        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                using (ClickerForm form = new ClickerForm(false))
                {
                    foreach (string field in new string[] { "variation", "autoStop" })
                    {
                        CheckBox box = (CheckBox)typeof(ClickerForm).GetField(field, Flags).GetValue(form);
                        foreach (int dpi in new int[] { 96, 144, 192 })
                        {
                            box.Size = new Size(260 * dpi / 96, 30 * dpi / 96);
                            foreach (bool enabled in new bool[] { true, false })
                            foreach (bool check in new bool[] { true, false, true })
                            {
                                box.Enabled = enabled;
                                box.Checked = check;
                                using (Bitmap bitmap = new Bitmap(box.Width, box.Height))
                                {
                                    bitmap.SetResolution(dpi, dpi);
                                    using (Graphics graphics = Graphics.FromImage(bitmap))
                                    {
                                        // A reused paint buffer can contain another button's pixels.
                                        graphics.Clear(Color.Magenta);
                                        typeof(LargeCheckBox).GetMethod("OnPaint", Flags).Invoke(box,
                                            new object[] { new PaintEventArgs(graphics, box.ClientRectangle) });
                                    }
                                    Color unusedArea = bitmap.GetPixel(bitmap.Width - 4, bitmap.Height / 2);
                                    if (unusedArea.ToArgb() != box.BackColor.ToArgb())
                                        throw new Exception(field + " left stale background pixels at " + dpi + " DPI.");
                                    if (enabled && dpi == 144)
                                        bitmap.Save(Path.Combine(Path.GetDirectoryName(args[0]),
                                            field + (check ? "-checked.png" : "-unchecked.png")));
                                }
                            }
                        }
                    }
                }
                File.WriteAllText(args[0], "PASS: both checkboxes clear stale pixels when checked, unchecked, and disabled at 100%, 150%, and 200% scaling. No input sent.");
                return 0;
            }
            catch (Exception error)
            {
                File.WriteAllText(args[0], "FAIL: " + error);
                return 1;
            }
        }
    }
}
