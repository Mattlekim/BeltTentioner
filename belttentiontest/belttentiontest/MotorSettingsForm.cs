using System;
using System.Drawing;
using System.Windows.Forms;
using BeltAPI;
using belttentiontest.Controls;

namespace belttentiontest
{
    public class MotorSettingsForm : Form
    {
        private ListBox lb_SelectedMotor;
        private ThinTrackBar ttb_motorStart;
        private ThinTrackBar ttb_motorEnd;
        private ModernCheckBox ck_Inverted;
        private ModernCheckBox cb_duelMotors;
        private Button bnt_Apply;
        private Label lblChangesNotSaved;

        private BeltSerialDevice? device;

        public MotorSettingsForm()
        {
            Text = "Motor Settings";
            Size = new Size(360, 240);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;

            lb_SelectedMotor = new ListBox() { Location = new Point(12, 12), Size = new Size(100, 60) };
            lb_SelectedMotor.Items.AddRange(new object[] { "Left", "Right" });
            lb_SelectedMotor.SelectedIndex = 0;
            lb_SelectedMotor.SelectedIndexChanged += Lb_SelectedMotor_SelectedIndexChanged;

            ttb_motorStart = new ThinTrackBar() { Location = new Point(130, 12), Size = new Size(200, 20), Minimum = 0F, Maximum = 180F };
            ttb_motorEnd = new ThinTrackBar() { Location = new Point(130, 44), Size = new Size(200, 20), Minimum = 0F, Maximum = 180F };

            var lblStart = new Label() { Text = "Start", Location = new Point(102, 14), AutoSize = true };
            var lblEnd = new Label() { Text = "End", Location = new Point(102, 46), AutoSize = true };

            ck_Inverted = new ModernCheckBox() { Location = new Point(12, 80), Size = new Size(100, 20), Text = "Invert" };
            cb_duelMotors = new ModernCheckBox() { Location = new Point(130, 80), Size = new Size(140, 20), Text = "Dual Motors" };

            bnt_Apply = new Button() { Text = "Apply", Location = new Point(245, 140), Size = new Size(85, 28) };
            bnt_Apply.Click += Bnt_Apply_Click;

            lblChangesNotSaved = new Label() { Text = "Changes not saved", ForeColor = Color.OrangeRed, Location = new Point(12, 140), AutoSize = true, Visible = false };

            Controls.Add(lb_SelectedMotor);
            Controls.Add(ttb_motorStart);
            Controls.Add(ttb_motorEnd);
            Controls.Add(lblStart);
            Controls.Add(lblEnd);
            Controls.Add(ck_Inverted);
            Controls.Add(cb_duelMotors);
            Controls.Add(bnt_Apply);
            Controls.Add(lblChangesNotSaved);

            ttb_motorStart.ValueChanged += (s, e) => lblChangesNotSaved.Visible = true;
            ttb_motorEnd.ValueChanged += (s, e) => lblChangesNotSaved.Visible = true;
            ck_Inverted.CheckedChanged += (s, e) => lblChangesNotSaved.Visible = true;
            cb_duelMotors.CheckedChanged += (s, e) => lblChangesNotSaved.Visible = true;
        }

        private void Lb_SelectedMotor_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (device == null) return;
            var ms = device.DeviceMotorSettings;
            if (lb_SelectedMotor.SelectedIndex == 1)
            {
                ttb_motorStart.Value = ms.RightMinimumAngle;
                ttb_motorEnd.Value = ms.RightMaximumAngle;
                ck_Inverted.Checked = ms.RightInverted;
            }
            else
            {
                ttb_motorStart.Value = ms.LeftMinimumAngle;
                ttb_motorEnd.Value = ms.LeftMaximumAngle;
                ck_Inverted.Checked = ms.LeftInverted;
            }
        }

        private void Bnt_Apply_Click(object? sender, EventArgs e)
        {
            if (device == null) return;

            int lmin = (int)ttb_motorStart.Value;
            int lmax = (int)ttb_motorEnd.Value;
            int rmin = (int)ttb_motorStart.Value;
            int rmax = (int)ttb_motorEnd.Value;

            bool linv = false, rinv = false;

            if (lb_SelectedMotor.SelectedIndex == 1)
            {
                // Right selected -> write right values
                rmin = (int)ttb_motorStart.Value;
                rmax = (int)ttb_motorEnd.Value;
                rinv = ck_Inverted.Checked;
                // left unchanged, read from device
                var ms = device.DeviceMotorSettings;
                lmin = (int)ms.LeftMinimumAngle;
                lmax = (int)ms.LeftMaximumAngle;
                linv = ms.LeftInverted;
            }
            else
            {
                // Left selected
                lmin = (int)ttb_motorStart.Value;
                lmax = (int)ttb_motorEnd.Value;
                linv = ck_Inverted.Checked;
                var ms = device.DeviceMotorSettings;
                rmin = (int)ms.RightMinimumAngle;
                rmax = (int)ms.RightMaximumAngle;
                rinv = ms.RightInverted;
            }

            bool both = cb_duelMotors.Checked;

            try
            {
                device.SendUpdatedSettings(lmin, lmax, rmin, rmax, linv, rinv, both);
                lblChangesNotSaved.Visible = false;
            }
            catch { }
        }

        public void UpdateFromDevice(BeltSerialDevice dev)
        {
            device = dev;
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => UpdateFromDevice(dev)));
                return;
            }

            var ms = device.DeviceMotorSettings;
            lb_SelectedMotor.SelectedIndex = 0;
            ttb_motorStart.Value = ms.LeftMinimumAngle;
            ttb_motorEnd.Value = ms.LeftMaximumAngle;
            ck_Inverted.Checked = ms.LeftInverted;
            cb_duelMotors.Checked = device.DuelMotors;
            lblChangesNotSaved.Visible = false;
        }
    }
}
