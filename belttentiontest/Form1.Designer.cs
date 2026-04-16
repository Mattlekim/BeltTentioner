using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using belttentiontest.Controls;

namespace belttentiontest
{
    partial class Form1
    {
        private IContainer components = null;
        private Button buttonConnect;
        private ModernCheckBox checkBoxTest;
        private Label labelMaxPower;
        private Label labelGForceToBelt;
        private Label lblSettingsSaved;
        private Label lblChangesNotSaved;
        private Panel _scrollPanel;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
                components.Dispose();
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        private void InitializeComponent()
        {
            ComponentResourceManager resources = new ComponentResourceManager(typeof(Form1));
            buttonConnect = new Button();
            labelMaxPower = new Label();
            labelGForceToBelt = new Label();
            lb_ABS_Status = new Label();
            gb_Car_Settings = new CollapsibleGroupBox();
            _cb_tilt = new CollapsibleGroupBox();
            _ttb_masterTilt = new ThinTrackBar();
            label14 = new Label();
            _cb_tilt_invertRoll = new ModernCheckBox();
            _ttb_roll = new ThinTrackBar();
            label12 = new Label();
            _cb_tilt_invertPitch = new ModernCheckBox();
            _ttb_pitch = new ThinTrackBar();
            label11 = new Label();
            _ttb_restingPoint = new ThinTrackBar();
            _ttb_maxOutput = new ThinTrackBar();
            _gb_vertical = new CollapsibleGroupBox();
            cb_invertHeave = new ModernCheckBox();
            _ttb_verStr = new ThinTrackBar();
            label5 = new Label();
            _gb_cornering = new CollapsibleGroupBox();
            _ttb_corneringStr = new ThinTrackBar();
            _ttb_corneringCurve = new ThinTrackBar();
            label8 = new Label();
            cb_invert_sway = new ModernCheckBox();
            label9 = new Label();
            _gb_Braking = new CollapsibleGroupBox();
            cb_invertSurge = new ModernCheckBox();
            _ttb_brakingStr = new ThinTrackBar();
            _ttb_brakingCurve = new ThinTrackBar();
            labelCurveAmount = new Label();
            lb_carName = new Label();
            label7 = new Label();
            _ttb_restingPoint2 = new ThinTrackBar();
            label2 = new Label();
            groupBox1 = new CollapsibleGroupBox();
            ck_Inverted = new ModernCheckBox();
            cb_duelMotors = new ModernCheckBox();
            lb_SelectedMotor = new ListBox();
            bnt_Apply = new Button();
            _ttb_motorEnd = new ThinTrackBar();
            _ttb_motorStart = new ThinTrackBar();
            label3 = new Label();
            lblSettingsSaved = new Label();
            lblChangesNotSaved = new Label();
            groupBox2 = new CollapsibleGroupBox();
            label10 = new Label();
            label4 = new Label();
            _ttb_negativeSway = new ThinTrackBar();
            cb_ABS_Enabled = new ModernCheckBox();
            _ttb_ABS = new ThinTrackBar();
            label6 = new Label();
            cb_AutoConnect = new ModernCheckBox();
            _of_Control = new OnOffStatusControl();
            _of_simHub = new OnOffStatusControl();
            lb_simhub = new Label();
            _of_seatbeltDevice = new OnOffStatusControl();
            _gb_simhub = new CollapsibleGroupBox();
            _lb_menu = new Label();
            _onSupportCorn = new OnOffStatusControl();
            _on_supoortVer = new OnOffStatusControl();
            _on_supportBrake = new OnOffStatusControl();
            _scrollPanel = new Panel();
            _ttb_maxOutput2 = new ThinTrackBar();
            gb_Car_Settings.SuspendLayout();
            _cb_tilt.SuspendLayout();
            _gb_vertical.SuspendLayout();
            _gb_cornering.SuspendLayout();
            _gb_Braking.SuspendLayout();
            groupBox1.SuspendLayout();
            groupBox2.SuspendLayout();
            _gb_simhub.SuspendLayout();
            _scrollPanel.SuspendLayout();
            SuspendLayout();
            // 
            // buttonConnect
            // 
            buttonConnect.BackColor = Color.FromArgb(100, 160, 255);
            buttonConnect.FlatAppearance.BorderColor = Color.FromArgb(70, 110, 200);
            buttonConnect.FlatStyle = FlatStyle.Flat;
            buttonConnect.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            buttonConnect.ForeColor = Color.White;
            buttonConnect.Location = new Point(225, 89);
            buttonConnect.Name = "buttonConnect";
            buttonConnect.Size = new Size(75, 23);
            buttonConnect.TabIndex = 3;
            buttonConnect.Tag = "de";
            buttonConnect.Text = "Connect";
            buttonConnect.UseVisualStyleBackColor = false;
            buttonConnect.Click += buttonConnect_Click;
            // 
            // labelMaxPower
            // 
            labelMaxPower.AutoSize = true;
            labelMaxPower.Location = new Point(13, 45);
            labelMaxPower.Name = "labelMaxPower";
            labelMaxPower.Size = new Size(70, 15);
            labelMaxPower.TabIndex = 15;
            labelMaxPower.Text = "Max Output";
            // 
            // labelGForceToBelt
            // 
            labelGForceToBelt.AutoSize = true;
            labelGForceToBelt.Location = new Point(5, 56);
            labelGForceToBelt.Name = "labelGForceToBelt";
            labelGForceToBelt.Size = new Size(52, 15);
            labelGForceToBelt.TabIndex = 18;
            labelGForceToBelt.Text = "Strength";
            // 
            // lb_ABS_Status
            // 
            lb_ABS_Status.Location = new Point(0, 0);
            lb_ABS_Status.Name = "lb_ABS_Status";
            lb_ABS_Status.Size = new Size(100, 23);
            lb_ABS_Status.TabIndex = 0;
            // 
            // gb_Car_Settings
            // 
            gb_Car_Settings.Collapsed = false;
            gb_Car_Settings.Collapsible = true;
            gb_Car_Settings.Controls.Add(_cb_tilt);
            gb_Car_Settings.Controls.Add(_ttb_restingPoint);
            gb_Car_Settings.Controls.Add(_ttb_maxOutput);
            gb_Car_Settings.Controls.Add(_gb_vertical);
            gb_Car_Settings.Controls.Add(_gb_cornering);
            gb_Car_Settings.Controls.Add(_gb_Braking);
            gb_Car_Settings.Controls.Add(lb_carName);
            gb_Car_Settings.Controls.Add(label7);
            gb_Car_Settings.Controls.Add(labelMaxPower);
            gb_Car_Settings.ForeColor = Color.White;
            gb_Car_Settings.Location = new Point(12, 137);
            gb_Car_Settings.Name = "gb_Car_Settings";
            gb_Car_Settings.Size = new Size(290, 512);
            gb_Car_Settings.TabIndex = 23;
            gb_Car_Settings.TabStop = false;
            gb_Car_Settings.Text = "Car Settings";
            // 
            // _cb_tilt
            // 
            _cb_tilt.Collapsed = false;
            _cb_tilt.Collapsible = false;
            _cb_tilt.Controls.Add(_ttb_masterTilt);
            _cb_tilt.Controls.Add(label14);
            _cb_tilt.Controls.Add(_cb_tilt_invertRoll);
            _cb_tilt.Controls.Add(_ttb_roll);
            _cb_tilt.Controls.Add(label12);
            _cb_tilt.Controls.Add(_cb_tilt_invertPitch);
            _cb_tilt.Controls.Add(_ttb_pitch);
            _cb_tilt.Controls.Add(label11);
            _cb_tilt.Location = new Point(7, 390);
            _cb_tilt.Name = "_cb_tilt";
            _cb_tilt.Size = new Size(276, 111);
            _cb_tilt.TabIndex = 117;
            _cb_tilt.TabStop = false;
            _cb_tilt.Text = "Gravity (Tilt)";
            // 
            // _ttb_masterTilt
            // 
            _ttb_masterTilt.DecimalPlaces = 0;
            _ttb_masterTilt.FillColor = Color.FromArgb(255, 128, 0);
            _ttb_masterTilt.Location = new Point(95, 76);
            _ttb_masterTilt.Maximum = 200F;
            _ttb_masterTilt.Minimum = 1F;
            _ttb_masterTilt.Name = "_ttb_masterTilt";
            _ttb_masterTilt.Size = new Size(175, 20);
            _ttb_masterTilt.TabIndex = 127;
            _ttb_masterTilt.ThumbColor = Color.White;
            _ttb_masterTilt.TrackColor = Color.FromArgb(55, 55, 80);
            _ttb_masterTilt.Value = 1F;
            _ttb_masterTilt.ValueChanged += nud_masterTilt_ValueChanged;
            // 
            // label14
            // 
            label14.AutoSize = true;
            label14.Location = new Point(4, 81);
            label14.Name = "label14";
            label14.Size = new Size(91, 15);
            label14.TabIndex = 125;
            label14.Text = "Master Strength";
            // 
            // _cb_tilt_invertRoll
            // 
            _cb_tilt_invertRoll.BackColor = Color.Transparent;
            _cb_tilt_invertRoll.Font = new Font("Segoe UI", 9F);
            _cb_tilt_invertRoll.ForeColor = Color.FromArgb(160, 160, 190);
            _cb_tilt_invertRoll.Location = new Point(197, 45);
            _cb_tilt_invertRoll.Name = "_cb_tilt_invertRoll";
            _cb_tilt_invertRoll.Size = new Size(78, 20);
            _cb_tilt_invertRoll.TabIndex = 123;
            _cb_tilt_invertRoll.Text = "Invert";
            _cb_tilt_invertRoll.CheckedChanged += _cb_tilt_invertRoll_CheckedChanged;
            // 
            // _ttb_roll
            // 
            _ttb_roll.DecimalPlaces = 0;
            _ttb_roll.FillColor = Color.FromArgb(0, 192, 0);
            _ttb_roll.Location = new Point(40, 47);
            _ttb_roll.Maximum = 200F;
            _ttb_roll.Minimum = 1F;
            _ttb_roll.Name = "_ttb_roll";
            _ttb_roll.Size = new Size(161, 20);
            _ttb_roll.TabIndex = 119;
            _ttb_roll.ThumbColor = Color.White;
            _ttb_roll.TrackColor = Color.FromArgb(55, 55, 80);
            _ttb_roll.Value = 1F;
            _ttb_roll.ValueChanged += nud_roll_ValueChanged;
            // 
            // label12
            // 
            label12.AutoSize = true;
            label12.Location = new Point(6, 49);
            label12.Name = "label12";
            label12.Size = new Size(27, 15);
            label12.TabIndex = 117;
            label12.Text = "Roll";
            // 
            // _cb_tilt_invertPitch
            // 
            _cb_tilt_invertPitch.BackColor = Color.Transparent;
            _cb_tilt_invertPitch.Font = new Font("Segoe UI", 9F);
            _cb_tilt_invertPitch.ForeColor = Color.FromArgb(160, 160, 190);
            _cb_tilt_invertPitch.Location = new Point(197, 22);
            _cb_tilt_invertPitch.Name = "_cb_tilt_invertPitch";
            _cb_tilt_invertPitch.Size = new Size(78, 20);
            _cb_tilt_invertPitch.TabIndex = 116;
            _cb_tilt_invertPitch.Text = "Invert";
            _cb_tilt_invertPitch.CheckedChanged += _cb_tilt_invertPitch_CheckedChanged;
            // 
            // _ttb_pitch
            // 
            _ttb_pitch.DecimalPlaces = 0;
            _ttb_pitch.FillColor = Color.Blue;
            _ttb_pitch.Location = new Point(40, 22);
            _ttb_pitch.Maximum = 200F;
            _ttb_pitch.Minimum = 1F;
            _ttb_pitch.Name = "_ttb_pitch";
            _ttb_pitch.Size = new Size(161, 20);
            _ttb_pitch.TabIndex = 115;
            _ttb_pitch.ThumbColor = Color.White;
            _ttb_pitch.TrackColor = Color.FromArgb(55, 55, 80);
            _ttb_pitch.Value = 1F;
            _ttb_pitch.ValueChanged += nud_pitch_ValueChanged;
            // 
            // label11
            // 
            label11.AutoSize = true;
            label11.Location = new Point(6, 24);
            label11.Name = "label11";
            label11.Size = new Size(34, 15);
            label11.TabIndex = 53;
            label11.Text = "Pitch";
            // 
            // _ttb_restingPoint
            // 
            _ttb_restingPoint.DecimalPlaces = 0;
            _ttb_restingPoint.FillColor = Color.Cyan;
            _ttb_restingPoint.Location = new Point(94, 72);
            _ttb_restingPoint.Maximum = 30F;
            _ttb_restingPoint.Minimum = 0F;
            _ttb_restingPoint.Name = "_ttb_restingPoint";
            _ttb_restingPoint.Size = new Size(186, 20);
            _ttb_restingPoint.TabIndex = 110;
            _ttb_restingPoint.ThumbColor = Color.White;
            _ttb_restingPoint.TrackColor = Color.FromArgb(55, 55, 80);
            _ttb_restingPoint.Value = 0F;
            _ttb_restingPoint.ValueChanged += percentageUpDownRestingPoint_ValueChanged;
            // 
            // _ttb_maxOutput
            // 
            _ttb_maxOutput.DecimalPlaces = 0;
            _ttb_maxOutput.FillColor = Color.Red;
            _ttb_maxOutput.Location = new Point(94, 44);
            _ttb_maxOutput.Maximum = 100F;
            _ttb_maxOutput.Minimum = 1F;
            _ttb_maxOutput.Name = "_ttb_maxOutput";
            _ttb_maxOutput.Size = new Size(186, 20);
            _ttb_maxOutput.TabIndex = 109;
            _ttb_maxOutput.ThumbColor = Color.White;
            _ttb_maxOutput.TrackColor = Color.FromArgb(55, 55, 80);
            _ttb_maxOutput.Value = 100F;
            _ttb_maxOutput.ValueChanged += numericUpDownMaxPower_ValueChanged;
            // 
            // _gb_vertical
            // 
            _gb_vertical.Collapsed = false;
            _gb_vertical.Collapsible = false;
            _gb_vertical.Controls.Add(cb_invertHeave);
            _gb_vertical.Controls.Add(_ttb_verStr);
            _gb_vertical.Controls.Add(label5);
            _gb_vertical.ForeColor = Color.FromArgb(255, 128, 0);
            _gb_vertical.Location = new Point(6, 316);
            _gb_vertical.Name = "_gb_vertical";
            _gb_vertical.Size = new Size(276, 70);
            _gb_vertical.TabIndex = 108;
            _gb_vertical.TabStop = false;
            _gb_vertical.Text = "Vertical (Heave)";
            // 
            // cb_invertHeave
            // 
            cb_invertHeave.BackColor = Color.Transparent;
            cb_invertHeave.Font = new Font("Segoe UI", 9F);
            cb_invertHeave.ForeColor = Color.FromArgb(255, 128, 0);
            cb_invertHeave.Location = new Point(7, 47);
            cb_invertHeave.Name = "cb_invertHeave";
            cb_invertHeave.Size = new Size(78, 20);
            cb_invertHeave.TabIndex = 116;
            cb_invertHeave.Text = "Invert";
            cb_invertHeave.CheckedChanged += cb_invertHeave_CheckedChanged;
            // 
            // _ttb_verStr
            // 
            _ttb_verStr.DecimalPlaces = 0;
            _ttb_verStr.FillColor = Color.FromArgb(255, 128, 0);
            _ttb_verStr.Location = new Point(70, 23);
            _ttb_verStr.Maximum = 200F;
            _ttb_verStr.Minimum = 1F;
            _ttb_verStr.Name = "_ttb_verStr";
            _ttb_verStr.Size = new Size(200, 20);
            _ttb_verStr.TabIndex = 115;
            _ttb_verStr.ThumbColor = Color.White;
            _ttb_verStr.TrackColor = Color.FromArgb(55, 55, 80);
            _ttb_verStr.Value = 1F;
            _ttb_verStr.ValueChanged += nudVertical_ValueChanged;
            // 
            // label5
            // 
            label5.AutoSize = true;
            label5.Location = new Point(6, 24);
            label5.Name = "label5";
            label5.Size = new Size(52, 15);
            label5.TabIndex = 53;
            label5.Text = "Strength";
            // 
            // _gb_cornering
            // 
            _gb_cornering.Collapsed = false;
            _gb_cornering.Collapsible = false;
            _gb_cornering.Controls.Add(_ttb_corneringStr);
            _gb_cornering.Controls.Add(_ttb_corneringCurve);
            _gb_cornering.Controls.Add(label8);
            _gb_cornering.Controls.Add(cb_invert_sway);
            _gb_cornering.Controls.Add(label9);
            _gb_cornering.ForeColor = Color.FromArgb(0, 192, 0);
            _gb_cornering.Location = new Point(6, 203);
            _gb_cornering.Name = "_gb_cornering";
            _gb_cornering.Size = new Size(276, 107);
            _gb_cornering.TabIndex = 107;
            _gb_cornering.TabStop = false;
            _gb_cornering.Text = "Cornering (Sway)";
            // 
            // _ttb_corneringStr
            // 
            _ttb_corneringStr.DecimalPlaces = 0;
            _ttb_corneringStr.FillColor = Color.Lime;
            _ttb_corneringStr.Location = new Point(70, 53);
            _ttb_corneringStr.Maximum = 200F;
            _ttb_corneringStr.Minimum = 1F;
            _ttb_corneringStr.Name = "_ttb_corneringStr";
            _ttb_corneringStr.Size = new Size(200, 20);
            _ttb_corneringStr.TabIndex = 114;
            _ttb_corneringStr.ThumbColor = Color.White;
            _ttb_corneringStr.TrackColor = Color.FromArgb(55, 55, 80);
            _ttb_corneringStr.Value = 1F;
            _ttb_corneringStr.ValueChanged += nud_coneringStrengh_ValueChanged;
            _ttb_corneringStr.Click += _ttb_corneringStr_Click;
            // 
            // _ttb_corneringCurve
            // 
            _ttb_corneringCurve.DecimalPlaces = 2;
            _ttb_corneringCurve.FillColor = Color.Lime;
            _ttb_corneringCurve.Location = new Point(70, 25);
            _ttb_corneringCurve.Maximum = 10F;
            _ttb_corneringCurve.Minimum = 0.1F;
            _ttb_corneringCurve.Name = "_ttb_corneringCurve";
            _ttb_corneringCurve.Size = new Size(200, 20);
            _ttb_corneringCurve.TabIndex = 113;
            _ttb_corneringCurve.ThumbColor = Color.White;
            _ttb_corneringCurve.TrackColor = Color.FromArgb(55, 55, 80);
            _ttb_corneringCurve.Value = 1F;
            _ttb_corneringCurve.ValueChanged += nud_ConeringCurveAmount_ValueChanged;
            // 
            // label8
            // 
            label8.AutoSize = true;
            label8.Location = new Point(5, 25);
            label8.Name = "label8";
            label8.Size = new Size(38, 15);
            label8.TabIndex = 12;
            label8.Text = "Curve";
            // 
            // cb_invert_sway
            // 
            cb_invert_sway.BackColor = Color.Transparent;
            cb_invert_sway.Font = new Font("Segoe UI", 9F);
            cb_invert_sway.ForeColor = Color.Lime;
            cb_invert_sway.Location = new Point(6, 78);
            cb_invert_sway.Name = "cb_invert_sway";
            cb_invert_sway.Size = new Size(78, 20);
            cb_invert_sway.TabIndex = 55;
            cb_invert_sway.Text = "Invert";
            cb_invert_sway.CheckedChanged += cb_invert_sway_CheckedChanged;
            // 
            // label9
            // 
            label9.AutoSize = true;
            label9.Location = new Point(5, 53);
            label9.Name = "label9";
            label9.Size = new Size(52, 15);
            label9.TabIndex = 18;
            label9.Text = "Strength";
            // 
            // _gb_Braking
            // 
            _gb_Braking.Collapsed = false;
            _gb_Braking.Collapsible = false;
            _gb_Braking.Controls.Add(cb_invertSurge);
            _gb_Braking.Controls.Add(_ttb_brakingStr);
            _gb_Braking.Controls.Add(_ttb_brakingCurve);
            _gb_Braking.Controls.Add(labelCurveAmount);
            _gb_Braking.Controls.Add(labelGForceToBelt);
            _gb_Braking.ForeColor = SystemColors.MenuHighlight;
            _gb_Braking.Location = new Point(7, 90);
            _gb_Braking.Name = "_gb_Braking";
            _gb_Braking.Size = new Size(276, 107);
            _gb_Braking.TabIndex = 106;
            _gb_Braking.TabStop = false;
            _gb_Braking.Text = "Braking (Surge)";
            // 
            // cb_invertSurge
            // 
            cb_invertSurge.BackColor = Color.Transparent;
            cb_invertSurge.Font = new Font("Segoe UI", 9F);
            cb_invertSurge.ForeColor = SystemColors.Highlight;
            cb_invertSurge.Location = new Point(6, 81);
            cb_invertSurge.Name = "cb_invertSurge";
            cb_invertSurge.Size = new Size(78, 20);
            cb_invertSurge.TabIndex = 113;
            cb_invertSurge.Text = "Invert";
            cb_invertSurge.CheckedChanged += cb_invertSurge_CheckedChanged;
            // 
            // _ttb_brakingStr
            // 
            _ttb_brakingStr.DecimalPlaces = 0;
            _ttb_brakingStr.FillColor = Color.FromArgb(100, 160, 255);
            _ttb_brakingStr.Location = new Point(70, 56);
            _ttb_brakingStr.Maximum = 200F;
            _ttb_brakingStr.Minimum = 1F;
            _ttb_brakingStr.Name = "_ttb_brakingStr";
            _ttb_brakingStr.Size = new Size(200, 20);
            _ttb_brakingStr.TabIndex = 112;
            _ttb_brakingStr.ThumbColor = Color.White;
            _ttb_brakingStr.TrackColor = Color.FromArgb(55, 55, 80);
            _ttb_brakingStr.Value = 1F;
            _ttb_brakingStr.ValueChanged += numericUpDownGForceToBelt_ValueChanged_1;
            // 
            // _ttb_brakingCurve
            // 
            _ttb_brakingCurve.DecimalPlaces = 2;
            _ttb_brakingCurve.FillColor = Color.FromArgb(100, 160, 255);
            _ttb_brakingCurve.Location = new Point(70, 27);
            _ttb_brakingCurve.Maximum = 5F;
            _ttb_brakingCurve.Minimum = 0.1F;
            _ttb_brakingCurve.Name = "_ttb_brakingCurve";
            _ttb_brakingCurve.Size = new Size(200, 20);
            _ttb_brakingCurve.TabIndex = 111;
            _ttb_brakingCurve.ThumbColor = Color.White;
            _ttb_brakingCurve.TrackColor = Color.FromArgb(55, 55, 80);
            _ttb_brakingCurve.Value = 1F;
            _ttb_brakingCurve.ValueChanged += numericUpDownCurveAmount_ValueChanged;
            // 
            // labelCurveAmount
            // 
            labelCurveAmount.AutoSize = true;
            labelCurveAmount.Location = new Point(5, 28);
            labelCurveAmount.Name = "labelCurveAmount";
            labelCurveAmount.Size = new Size(38, 15);
            labelCurveAmount.TabIndex = 12;
            labelCurveAmount.Text = "Curve";
            labelCurveAmount.Click += labelCurveAmount_Click;
            // 
            // lb_carName
            // 
            lb_carName.AutoSize = true;
            lb_carName.Location = new Point(13, 20);
            lb_carName.Name = "lb_carName";
            lb_carName.Size = new Size(60, 15);
            lb_carName.TabIndex = 23;
            lb_carName.Text = "Car Name";
            // 
            // label7
            // 
            label7.AutoSize = true;
            label7.Location = new Point(13, 73);
            label7.Name = "label7";
            label7.Size = new Size(77, 15);
            label7.TabIndex = 104;
            label7.Text = "Resting Point";
            // 
            // _ttb_restingPoint2
            // 
            _ttb_restingPoint2.DecimalPlaces = 0;
            _ttb_restingPoint2.FillColor = Color.DodgerBlue;
            _ttb_restingPoint2.Location = new Point(0, 0);
            _ttb_restingPoint2.Maximum = 100F;
            _ttb_restingPoint2.Minimum = 1F;
            _ttb_restingPoint2.Name = "_ttb_restingPoint2";
            _ttb_restingPoint2.Size = new Size(150, 20);
            _ttb_restingPoint2.TabIndex = 0;
            _ttb_restingPoint2.ThumbColor = Color.White;
            _ttb_restingPoint2.TrackColor = Color.FromArgb(55, 55, 80);
            _ttb_restingPoint2.Value = 1F;
            _ttb_restingPoint2.Visible = false;
            // 
            // label2
            // 
            label2.AutoSize = true;
            label2.Location = new Point(4, 67);
            label2.Name = "label2";
            label2.Size = new Size(65, 15);
            label2.TabIndex = 25;
            label2.Text = "Start Angle";
            // 
            // groupBox1
            // 
            groupBox1.Collapsed = false;
            groupBox1.Collapsible = true;
            groupBox1.Controls.Add(ck_Inverted);
            groupBox1.Controls.Add(cb_duelMotors);
            groupBox1.Controls.Add(lb_SelectedMotor);
            groupBox1.Controls.Add(bnt_Apply);
            groupBox1.Controls.Add(_ttb_motorEnd);
            groupBox1.Controls.Add(_ttb_motorStart);
            groupBox1.Controls.Add(label3);
            groupBox1.Controls.Add(label2);
            groupBox1.Controls.Add(lblSettingsSaved);
            groupBox1.Controls.Add(lblChangesNotSaved);
            groupBox1.ForeColor = Color.White;
            groupBox1.Location = new Point(12, 759);
            groupBox1.Name = "groupBox1";
            groupBox1.Size = new Size(290, 173);
            groupBox1.TabIndex = 26;
            groupBox1.TabStop = false;
            groupBox1.Text = "Motor Settings";
            // 
            // ck_Inverted
            // 
            ck_Inverted.BackColor = Color.Transparent;
            ck_Inverted.Font = new Font("Segoe UI", 9F);
            ck_Inverted.ForeColor = Color.FromArgb(160, 160, 190);
            ck_Inverted.Location = new Point(7, 117);
            ck_Inverted.Name = "ck_Inverted";
            ck_Inverted.Size = new Size(91, 20);
            ck_Inverted.TabIndex = 32;
            ck_Inverted.Text = "Inverted";
            ck_Inverted.CheckedChanged += bnt_Inverted_CheckedChanged;
            // 
            // cb_duelMotors
            // 
            cb_duelMotors.BackColor = Color.Transparent;
            cb_duelMotors.Font = new Font("Segoe UI", 9F);
            cb_duelMotors.ForeColor = Color.FromArgb(160, 160, 190);
            cb_duelMotors.Location = new Point(165, 22);
            cb_duelMotors.Name = "cb_duelMotors";
            cb_duelMotors.Size = new Size(114, 20);
            cb_duelMotors.TabIndex = 31;
            cb_duelMotors.Text = "Dual Motors";
            cb_duelMotors.CheckedChanged += cb_duelMotors_CheckedChanged;
            // 
            // lb_SelectedMotor
            // 
            lb_SelectedMotor.BackColor = Color.FromArgb(30, 30, 50);
            lb_SelectedMotor.BorderStyle = BorderStyle.FixedSingle;
            lb_SelectedMotor.ForeColor = Color.FromArgb(160, 160, 190);
            lb_SelectedMotor.FormattingEnabled = true;
            lb_SelectedMotor.ItemHeight = 15;
            lb_SelectedMotor.Items.AddRange(new object[] { "Left Motor", "Right Motor" });
            lb_SelectedMotor.Location = new Point(6, 20);
            lb_SelectedMotor.Name = "lb_SelectedMotor";
            lb_SelectedMotor.Size = new Size(120, 32);
            lb_SelectedMotor.TabIndex = 30;
            lb_SelectedMotor.SelectedIndexChanged += lb_SelectedMotor_SelectedIndexChanged;
            // 
            // bnt_Apply
            // 
            bnt_Apply.BackColor = Color.FromArgb(80, 200, 120);
            bnt_Apply.FlatAppearance.BorderColor = Color.FromArgb(50, 150, 90);
            bnt_Apply.FlatStyle = FlatStyle.Flat;
            bnt_Apply.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            bnt_Apply.ForeColor = Color.White;
            bnt_Apply.Location = new Point(187, 135);
            bnt_Apply.Name = "bnt_Apply";
            bnt_Apply.Size = new Size(75, 23);
            bnt_Apply.TabIndex = 29;
            bnt_Apply.Text = "Apply";
            bnt_Apply.UseVisualStyleBackColor = false;
            bnt_Apply.Click += bnt_Apply_Click;
            // 
            // _ttb_motorEnd
            // 
            _ttb_motorEnd.DecimalPlaces = 0;
            _ttb_motorEnd.FillColor = Color.FromArgb(100, 160, 255);
            _ttb_motorEnd.Location = new Point(90, 88);
            _ttb_motorEnd.Maximum = 270F;
            _ttb_motorEnd.Minimum = 0F;
            _ttb_motorEnd.Name = "_ttb_motorEnd";
            _ttb_motorEnd.Size = new Size(172, 20);
            _ttb_motorEnd.TabIndex = 28;
            _ttb_motorEnd.ThumbColor = Color.White;
            _ttb_motorEnd.TrackColor = Color.FromArgb(55, 55, 80);
            _ttb_motorEnd.Value = 180F;
            _ttb_motorEnd.ValueChanged += nud_Motor_End_ValueChanged;
            // 
            // _ttb_motorStart
            // 
            _ttb_motorStart.DecimalPlaces = 0;
            _ttb_motorStart.FillColor = Color.FromArgb(100, 160, 255);
            _ttb_motorStart.Location = new Point(90, 59);
            _ttb_motorStart.Maximum = 270F;
            _ttb_motorStart.Minimum = 0F;
            _ttb_motorStart.Name = "_ttb_motorStart";
            _ttb_motorStart.Size = new Size(172, 20);
            _ttb_motorStart.TabIndex = 24;
            _ttb_motorStart.ThumbColor = Color.White;
            _ttb_motorStart.TrackColor = Color.FromArgb(55, 55, 80);
            _ttb_motorStart.Value = 0F;
            _ttb_motorStart.ValueChanged += nud_Motor_Start_ValueChanged;
            // 
            // label3
            // 
            label3.AutoSize = true;
            label3.Location = new Point(6, 90);
            label3.Name = "label3";
            label3.Size = new Size(61, 15);
            label3.TabIndex = 27;
            label3.Text = "End Angle";
            // 
            // lblSettingsSaved
            // 
            lblSettingsSaved.AutoSize = true;
            lblSettingsSaved.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            lblSettingsSaved.ForeColor = Color.Green;
            lblSettingsSaved.Location = new Point(20, 139);
            lblSettingsSaved.Name = "lblSettingsSaved";
            lblSettingsSaved.Size = new Size(109, 19);
            lblSettingsSaved.TabIndex = 33;
            lblSettingsSaved.Tag = "de";
            lblSettingsSaved.Text = "Settings saved.";
            lblSettingsSaved.Visible = false;
            // 
            // lblChangesNotSaved
            // 
            lblChangesNotSaved.AutoSize = true;
            lblChangesNotSaved.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            lblChangesNotSaved.ForeColor = Color.Red;
            lblChangesNotSaved.Location = new Point(9, 139);
            lblChangesNotSaved.Name = "lblChangesNotSaved";
            lblChangesNotSaved.Size = new Size(139, 19);
            lblChangesNotSaved.TabIndex = 34;
            lblChangesNotSaved.Text = "Changes Not Saved";
            lblChangesNotSaved.Visible = false;
            // 
            // groupBox2
            // 
            groupBox2.Collapsed = false;
            groupBox2.Collapsible = true;
            groupBox2.Controls.Add(label10);
            groupBox2.Controls.Add(label4);
            groupBox2.Controls.Add(_ttb_negativeSway);
            groupBox2.Controls.Add(cb_ABS_Enabled);
            groupBox2.Controls.Add(_ttb_ABS);
            groupBox2.Controls.Add(label6);
            groupBox2.Location = new Point(12, 657);
            groupBox2.Name = "groupBox2";
            groupBox2.Size = new Size(290, 96);
            groupBox2.TabIndex = 27;
            groupBox2.TabStop = false;
            groupBox2.Text = "Other";
            groupBox2.Enter += groupBox2_Enter;
            // 
            // label10
            // 
            label10.AutoSize = true;
            label10.Location = new Point(9, 69);
            label10.Name = "label10";
            label10.Size = new Size(76, 15);
            label10.TabIndex = 62;
            label10.Text = "ABS Strength";
            // 
            // label4
            // 
            label4.AutoSize = true;
            label4.Location = new Point(6, 26);
            label4.Name = "label4";
            label4.Size = new Size(84, 15);
            label4.TabIndex = 59;
            label4.Text = "Negative Sway";
            // 
            // _ttb_negativeSway
            // 
            _ttb_negativeSway.DecimalPlaces = 0;
            _ttb_negativeSway.FillColor = Color.FromArgb(80, 200, 120);
            _ttb_negativeSway.Location = new Point(95, 22);
            _ttb_negativeSway.Maximum = 100F;
            _ttb_negativeSway.Minimum = 0F;
            _ttb_negativeSway.Name = "_ttb_negativeSway";
            _ttb_negativeSway.Size = new Size(185, 20);
            _ttb_negativeSway.TabIndex = 60;
            _ttb_negativeSway.ThumbColor = Color.White;
            _ttb_negativeSway.TrackColor = Color.FromArgb(55, 55, 80);
            _ttb_negativeSway.Value = 0F;
            _ttb_negativeSway.ValueChanged += nud_negativeSway_ValueChanged;
            // 
            // cb_ABS_Enabled
            // 
            cb_ABS_Enabled.BackColor = Color.Transparent;
            cb_ABS_Enabled.Font = new Font("Segoe UI", 9F);
            cb_ABS_Enabled.ForeColor = Color.FromArgb(160, 160, 190);
            cb_ABS_Enabled.Location = new Point(95, 45);
            cb_ABS_Enabled.Name = "cb_ABS_Enabled";
            cb_ABS_Enabled.Size = new Size(90, 20);
            cb_ABS_Enabled.TabIndex = 35;
            cb_ABS_Enabled.Text = "Enabled";
            cb_ABS_Enabled.CheckedChanged += cb_ABS_Enabled_CheckedChanged;
            // 
            // _ttb_ABS
            // 
            _ttb_ABS.DecimalPlaces = 1;
            _ttb_ABS.FillColor = Color.FromArgb(220, 60, 60);
            _ttb_ABS.Location = new Point(95, 67);
            _ttb_ABS.Maximum = 30F;
            _ttb_ABS.Minimum = 3F;
            _ttb_ABS.Name = "_ttb_ABS";
            _ttb_ABS.Size = new Size(185, 20);
            _ttb_ABS.TabIndex = 55;
            _ttb_ABS.ThumbColor = Color.White;
            _ttb_ABS.TrackColor = Color.FromArgb(55, 55, 80);
            _ttb_ABS.Value = 3F;
            _ttb_ABS.ValueChanged += nud_ABS_ValueChanged;
            // 
            // label6
            // 
            label6.AutoSize = true;
            label6.Location = new Point(8, 48);
            label6.Name = "label6";
            label6.Size = new Size(61, 15);
            label6.TabIndex = 36;
            label6.Text = "ABS Effect";
            // 
            // cb_AutoConnect
            // 
            cb_AutoConnect.BackColor = Color.Transparent;
            cb_AutoConnect.Font = new Font("Segoe UI", 9F);
            cb_AutoConnect.ForeColor = Color.FromArgb(160, 160, 190);
            cb_AutoConnect.Location = new Point(15, 115);
            cb_AutoConnect.Name = "cb_AutoConnect";
            cb_AutoConnect.Size = new Size(211, 20);
            cb_AutoConnect.TabIndex = 28;
            cb_AutoConnect.Tag = "de";
            cb_AutoConnect.Text = "Connect To Device On Startup";
            cb_AutoConnect.CheckedChanged += cb_AutoConnect_CheckedChanged;
            // 
            // _of_Control
            // 
            _of_Control.BackColor = Color.FromArgb(18, 18, 30);
            _of_Control.Font = new Font("Segoe UI", 9F);
            _of_Control.ForeColor = Color.FromArgb(160, 160, 190);
            _of_Control.IsOn = false;
            _of_Control.Location = new Point(143, 2);
            _of_Control.Name = "_of_Control";
            _of_Control.OffColor = Color.Red;
            _of_Control.OnColor = Color.LimeGreen;
            _of_Control.Size = new Size(77, 24);
            _of_Control.StatusText = "Iracing:";
            _of_Control.TabIndex = 30;
            _of_Control.Text = "Iracing:";
            _of_Control.TextColor = Color.FromArgb(160, 160, 190);
            // 
            // _of_simHub
            // 
            _of_simHub.BackColor = Color.FromArgb(18, 18, 30);
            _of_simHub.Font = new Font("Segoe UI", 9F);
            _of_simHub.ForeColor = Color.FromArgb(160, 160, 190);
            _of_simHub.IsOn = false;
            _of_simHub.Location = new Point(226, 2);
            _of_simHub.Name = "_of_simHub";
            _of_simHub.OffColor = Color.Red;
            _of_simHub.OnColor = Color.LimeGreen;
            _of_simHub.Size = new Size(87, 24);
            _of_simHub.StatusText = "SIMHUB:";
            _of_simHub.TabIndex = 31;
            _of_simHub.Text = "SIMHUB:";
            _of_simHub.TextColor = Color.FromArgb(160, 160, 190);
            // 
            // lb_simhub
            // 
            lb_simhub.AutoSize = true;
            lb_simhub.Location = new Point(9, 19);
            lb_simhub.Name = "lb_simhub";
            lb_simhub.Size = new Size(41, 15);
            lb_simhub.TabIndex = 32;
            lb_simhub.Text = "Game:";
            // 
            // _of_seatbeltDevice
            // 
            _of_seatbeltDevice.BackColor = Color.FromArgb(18, 18, 30);
            _of_seatbeltDevice.Font = new Font("Segoe UI", 14F, FontStyle.Bold);
            _of_seatbeltDevice.ForeColor = Color.FromArgb(160, 160, 190);
            _of_seatbeltDevice.IsOn = false;
            _of_seatbeltDevice.Location = new Point(11, 88);
            _of_seatbeltDevice.Name = "_of_seatbeltDevice";
            _of_seatbeltDevice.OffColor = Color.Red;
            _of_seatbeltDevice.OnColor = Color.LimeGreen;
            _of_seatbeltDevice.Size = new Size(185, 24);
            _of_seatbeltDevice.StatusText = "Seatbelt Device:";
            _of_seatbeltDevice.TabIndex = 33;
            _of_seatbeltDevice.Text = "Seatbelt Device:";
            _of_seatbeltDevice.TextColor = Color.FromArgb(160, 160, 190);
            // 
            // _gb_simhub
            // 
            _gb_simhub.Collapsed = false;
            _gb_simhub.Collapsible = true;
            _gb_simhub.Controls.Add(_lb_menu);
            _gb_simhub.Controls.Add(_onSupportCorn);
            _gb_simhub.Controls.Add(_on_supoortVer);
            _gb_simhub.Controls.Add(_on_supportBrake);
            _gb_simhub.Controls.Add(lb_simhub);
            _gb_simhub.Enabled = false;
            _gb_simhub.Location = new Point(6, 23);
            _gb_simhub.Name = "_gb_simhub";
            _gb_simhub.Size = new Size(298, 59);
            _gb_simhub.TabIndex = 34;
            _gb_simhub.TabStop = false;
            _gb_simhub.Tag = "de";
            _gb_simhub.Text = "SIMHUB";
            // 
            // _lb_menu
            // 
            _lb_menu.AutoSize = true;
            _lb_menu.Location = new Point(187, 17);
            _lb_menu.Name = "_lb_menu";
            _lb_menu.Size = new Size(41, 15);
            _lb_menu.TabIndex = 36;
            _lb_menu.Text = "Game:";
            // 
            // _onSupportCorn
            // 
            _onSupportCorn.BackColor = Color.FromArgb(18, 18, 30);
            _onSupportCorn.Font = new Font("Segoe UI", 9F);
            _onSupportCorn.ForeColor = Color.FromArgb(160, 160, 190);
            _onSupportCorn.IsOn = false;
            _onSupportCorn.Location = new Point(202, 35);
            _onSupportCorn.Name = "_onSupportCorn";
            _onSupportCorn.OffColor = Color.Red;
            _onSupportCorn.OnColor = Color.LimeGreen;
            _onSupportCorn.Size = new Size(89, 24);
            _onSupportCorn.StatusText = "Cornering";
            _onSupportCorn.TabIndex = 35;
            _onSupportCorn.Text = "Cornering";
            _onSupportCorn.TextColor = Color.FromArgb(160, 160, 190);
            // 
            // _on_supoortVer
            // 
            _on_supoortVer.BackColor = Color.FromArgb(18, 18, 30);
            _on_supoortVer.Font = new Font("Segoe UI", 9F);
            _on_supoortVer.ForeColor = Color.FromArgb(160, 160, 190);
            _on_supoortVer.IsOn = false;
            _on_supoortVer.Location = new Point(5, 35);
            _on_supoortVer.Name = "_on_supoortVer";
            _on_supoortVer.OffColor = Color.Red;
            _on_supoortVer.OnColor = Color.LimeGreen;
            _on_supoortVer.Size = new Size(77, 24);
            _on_supoortVer.StatusText = "Vertical";
            _on_supoortVer.TabIndex = 34;
            _on_supoortVer.Text = "Vertical";
            _on_supoortVer.TextColor = Color.FromArgb(160, 160, 190);
            // 
            // _on_supportBrake
            // 
            _on_supportBrake.BackColor = Color.FromArgb(18, 18, 30);
            _on_supportBrake.Font = new Font("Segoe UI", 9F);
            _on_supportBrake.ForeColor = Color.FromArgb(160, 160, 190);
            _on_supportBrake.IsOn = false;
            _on_supportBrake.Location = new Point(108, 35);
            _on_supportBrake.Name = "_on_supportBrake";
            _on_supportBrake.OffColor = Color.Red;
            _on_supportBrake.OnColor = Color.LimeGreen;
            _on_supportBrake.Size = new Size(76, 24);
            _on_supportBrake.StatusText = "Braking";
            _on_supportBrake.TabIndex = 33;
            _on_supportBrake.Text = "Braking";
            _on_supportBrake.TextColor = Color.FromArgb(160, 160, 190);
            // 
            // _scrollPanel
            // 
            _scrollPanel.AutoScroll = true;
            _scrollPanel.BackColor = Color.FromArgb(18, 18, 30);
            _scrollPanel.Controls.Add(_gb_simhub);
            _scrollPanel.Controls.Add(_of_seatbeltDevice);
            _scrollPanel.Controls.Add(_of_simHub);
            _scrollPanel.Controls.Add(_of_Control);
            _scrollPanel.Controls.Add(cb_AutoConnect);
            _scrollPanel.Controls.Add(groupBox2);
            _scrollPanel.Controls.Add(groupBox1);
            _scrollPanel.Controls.Add(gb_Car_Settings);
            _scrollPanel.Controls.Add(buttonConnect);
            _scrollPanel.Dock = DockStyle.Fill;
            _scrollPanel.Location = new Point(0, 0);
            _scrollPanel.Name = "_scrollPanel";
            _scrollPanel.Size = new Size(332, 944);
            _scrollPanel.TabIndex = 0;
            _scrollPanel.MouseWheel += _scrollPanel_MouseWheel;
            // 
            // _ttb_maxOutput2
            // 
            _ttb_maxOutput2.DecimalPlaces = 0;
            _ttb_maxOutput2.FillColor = Color.DodgerBlue;
            _ttb_maxOutput2.Location = new Point(0, 0);
            _ttb_maxOutput2.Maximum = 100F;
            _ttb_maxOutput2.Minimum = 1F;
            _ttb_maxOutput2.Name = "_ttb_maxOutput2";
            _ttb_maxOutput2.Size = new Size(150, 20);
            _ttb_maxOutput2.TabIndex = 0;
            _ttb_maxOutput2.ThumbColor = Color.White;
            _ttb_maxOutput2.TrackColor = Color.FromArgb(55, 55, 80);
            _ttb_maxOutput2.Value = 1F;
            _ttb_maxOutput2.Visible = false;
            // 
            // Form1
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.FromArgb(18, 18, 30);
            ClientSize = new Size(332, 944);
            Controls.Add(_scrollPanel);
            ForeColor = Color.FromArgb(160, 160, 190);
            Icon = (Icon)resources.GetObject("$this.Icon");
            MaximumSize = new Size(348, 32000);
            MinimumSize = new Size(348, 300);
            Name = "Form1";
            Text = "Belt Tensioner";
            Load += Form1_Load;
            gb_Car_Settings.ResumeLayout(false);
            gb_Car_Settings.PerformLayout();
            _cb_tilt.ResumeLayout(false);
            _cb_tilt.PerformLayout();
            _gb_vertical.ResumeLayout(false);
            _gb_vertical.PerformLayout();
            _gb_cornering.ResumeLayout(false);
            _gb_cornering.PerformLayout();
            _gb_Braking.ResumeLayout(false);
            _gb_Braking.PerformLayout();
            groupBox1.ResumeLayout(false);
            groupBox1.PerformLayout();
            groupBox2.ResumeLayout(false);
            groupBox2.PerformLayout();
            _gb_simhub.ResumeLayout(false);
            _gb_simhub.PerformLayout();
            _scrollPanel.ResumeLayout(false);
            ResumeLayout(false);
        }

        #endregion

        private CollapsibleGroupBox gb_Car_Settings;
        private Label lb_carName;
        private Label lb_ABS_Status;
        private Label labelCurveAmount;
        private Label label2;
        private CollapsibleGroupBox groupBox1;
        private Button bnt_Apply;
        private ThinTrackBar _ttb_motorEnd;
        private ThinTrackBar _ttb_motorStart;
        private Label label3;
        private ListBox lb_SelectedMotor;
        private ModernCheckBox cb_duelMotors;
        private ModernCheckBox ck_Inverted;
        private Label label5;
        private CollapsibleGroupBox groupBox2;
        private ThinTrackBar _ttb_ABS;
        private Label label6;
        private ModernCheckBox cb_ABS_Enabled;
        private ModernCheckBox cb_invert_sway;
        private ModernCheckBox cb_AutoConnect;
        private ThinTrackBar _ttb_restingPoint2;
        private Label label7;
        private CollapsibleGroupBox _gb_Braking;
        private CollapsibleGroupBox _gb_cornering;
        private Label label8;
        private Label label9;
        private CollapsibleGroupBox _gb_vertical;
        private ThinTrackBar _ttb_maxOutput;
        private ThinTrackBar _ttb_restingPoint;
        private ThinTrackBar _ttb_verStr;
        private ThinTrackBar _ttb_corneringStr;
        private ThinTrackBar _ttb_corneringCurve;
        private ThinTrackBar _ttb_brakingStr;
        private ThinTrackBar _ttb_brakingCurve;
        private OnOffStatusControl _of_Control;
        private OnOffStatusControl _of_simHub;
        private Label lb_simhub;
        private OnOffStatusControl _of_seatbeltDevice;
        private CollapsibleGroupBox _gb_simhub;
        private OnOffStatusControl _onSupportCorn;
        private OnOffStatusControl _on_supoortVer;
        private OnOffStatusControl _on_supportBrake;
        private Label _lb_menu;
        private ModernCheckBox cb_invertHeave;
        private ModernCheckBox cb_invertSurge;
        private Label label4;
        private ThinTrackBar _ttb_negativeSway;
        private CollapsibleGroupBox _cb_tilt;
        private ModernCheckBox _cb_tilt_invertPitch;
        private ThinTrackBar _ttb_pitch;
        private Label label11;
        private Label label10;
        private ThinTrackBar _ttb_masterTilt;
        private Label label14;
        private ModernCheckBox _cb_tilt_invertRoll;
        private ThinTrackBar _ttb_roll;
        private Label label12;
        private ThinTrackBar _ttb_maxOutput2;
    }
}
