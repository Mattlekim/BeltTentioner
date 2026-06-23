using System;
using System.Drawing;
using System.Windows.Forms;

namespace belttentiontest
{
    public static class ThemedMessageBox
    {
        public static DialogResult Show(IWin32Window owner, string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon)
        {
            return ShowInternal(owner, text, caption, buttons, icon);
        }

        public static DialogResult Show(string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon)
        {
            return ShowInternal(null, text, caption, buttons, icon);
        }

        private static DialogResult ShowInternal(IWin32Window owner, string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon)
        {
            using var dlg = new Form();
            dlg.Text = caption ?? string.Empty;
            dlg.FormBorderStyle = FormBorderStyle.FixedDialog;
            dlg.StartPosition = owner != null ? FormStartPosition.CenterParent : FormStartPosition.CenterScreen;
            dlg.ShowInTaskbar = false;
            dlg.MinimizeBox = false;
            dlg.MaximizeBox = false;
            dlg.ClientSize = new Size(430, 140);
            dlg.BackColor = Color.FromArgb(34, 34, 34);
            dlg.ForeColor = Color.White;

            var lbl = new Label
            {
                Text = text ?? string.Empty,
                AutoSize = false,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(12),
                ForeColor = Color.White,
                BackColor = Color.FromArgb(34, 34, 34)
            };

            var btnPanel = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft,
                Dock = DockStyle.Bottom,
                Height = 50,
                Padding = new Padding(8)
            };

            Button makeButton(string textBtn, DialogResult result)
            {
                var b = new Button { Text = textBtn, DialogResult = result, AutoSize = true };
                b.BackColor = Color.FromArgb(64, 64, 64);
                b.ForeColor = Color.White;
                b.FlatStyle = FlatStyle.Flat;
                b.Padding = new Padding(8, 4, 8, 4);
                return b;
            }

            if (buttons == MessageBoxButtons.OK)
            {
                var ok = makeButton("OK", DialogResult.OK);
                dlg.AcceptButton = ok;
                btnPanel.Controls.Add(ok);
            }
            else if (buttons == MessageBoxButtons.YesNo)
            {
                var yes = makeButton("Yes", DialogResult.Yes);
                var no = makeButton("No", DialogResult.No);
                dlg.AcceptButton = yes;
                dlg.CancelButton = no;
                btnPanel.Controls.Add(yes);
                btnPanel.Controls.Add(no);
            }
            else
            {
                // fallback to OK
                var ok = makeButton("OK", DialogResult.OK);
                dlg.AcceptButton = ok;
                btnPanel.Controls.Add(ok);
            }

            // Optional icon - place a small icon at left
            var iconBox = new PictureBox { Size = new Size(48, 48), Location = new Point(8, 8), SizeMode = PictureBoxSizeMode.CenterImage, BackColor = dlg.BackColor };
            try
            {
                if (icon == MessageBoxIcon.Information) iconBox.Image = SystemIcons.Information.ToBitmap();
                else if (icon == MessageBoxIcon.Question) iconBox.Image = SystemIcons.Question.ToBitmap();
                else if (icon == MessageBoxIcon.Warning) iconBox.Image = SystemIcons.Warning.ToBitmap();
                else if (icon == MessageBoxIcon.Error) iconBox.Image = SystemIcons.Error.ToBitmap();
            }
            catch { }

            // layout: icon on left, label next, buttons bottom
            var content = new Panel { Dock = DockStyle.Fill, BackColor = dlg.BackColor };
            iconBox.Location = new Point(12, 12);
            iconBox.Anchor = AnchorStyles.Left | AnchorStyles.Top;
            content.Controls.Add(iconBox);

            lbl.Location = new Point(72, 12);
            lbl.Size = new Size(dlg.ClientSize.Width - 84, dlg.ClientSize.Height - 60);
            lbl.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            content.Controls.Add(lbl);

            dlg.Controls.Add(content);
            dlg.Controls.Add(btnPanel);

            if (owner is Form fOwner)
            {
                var res = dlg.ShowDialog(fOwner);
                return res;
            }
            else
            {
                return dlg.ShowDialog();
            }
        }
    }
}
