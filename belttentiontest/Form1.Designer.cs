using belttentiontest.Controls;
using System;
using System.ComponentModel;
using System.Windows.Forms;

namespace belttentiontest
{
    partial class Form1
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private IContainer components = null;
        private GForceUpDown numericUpDownTarget;
        private Button buttonConnect;
        private Label labelGForce;
        private CheckBox checkBoxTest;
        private NumericUpDown numericUpDownCurveAmount;
        private PictureBox pictureBoxCurveGraph;
        private PercentageUpDown numericUpDownMaxPower;
        private Label labelMaxPower;
        private NumericUpDown numericUpDownGForceToBelt;
        private Label labelGForceToBelt;
        private Label labelMaxGForce; // Max G-Force label
        private Label lblSettingsSaved;
        private Label lblChangesNotSaved; // Label for unsaved changes

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// Minimal InitializeComponent to provide the referenced controls.
        /// </summary>
        private void InitializeComponent()
        {
            ComponentResourceManager resources = new ComponentResourceManager(typeof(Form1));
            numericUpDownTarget = new GForceUpDown();
            buttonConnect = new Button();
            labelGForce = new Label();
            checkBoxTest = new CheckBox();
            numericUpDownCurveAmount = new NumericUpDown();
            pictureBoxCurveGraph = new PictureBox();
            numericUpDownMaxPower = new PercentageUpDown();
            labelMaxPower = new Label();
            numericUpDownGForceToBelt = new NumericUpDown();
            labelGForceToBelt = new Label();
            labelMaxGForce = new Label();
            gb_Car_Settings = new GroupBox();
            _cb_showVer = new CheckBox();
            _cb_showCorn = new CheckBox();
            _cb_showBraking = new CheckBox();
            _ttb_restingPoint = new ThinTrackBar();
            _ttb_maxOutput = new ThinTrackBar();
            _gb_vertical = new GroupBox();
            cb_invertHeave = new CheckBox();
            _ttb_verStr = new ThinTrackBar();
            nudVertical = new NumericUpDown();
            label5 = new Label();
            cb_livePrieview = new CheckBox();
            _gb_cornering = new GroupBox();
            nud_coneringStrengh = new NumericUpDown();
            _ttb_corneringStr = new ThinTrackBar();
            _ttb_corneringCurve = new ThinTrackBar();
            label8 = new Label();
            cb_invert_sway = new CheckBox();
            label9 = new Label();
            nud_ConeringCurveAmount = new NumericUpDown();
            percentageUpDownRestingPoint = new PercentageUpDown();
            _gb_Braking = new GroupBox();
            cb_invertSurge = new CheckBox();
            _ttb_brakingStr = new ThinTrackBar();
            _ttb_brakingCurve = new ThinTrackBar();
            labelCurveAmount = new Label();
            lb_carName = new Label();
            label7 = new Label();
            label1 = new Label();
            label2 = new Label();
            groupBox1 = new GroupBox();
            ck_Inverted = new CheckBox();
            cb_duelMotors = new CheckBox();
            lb_SelectedMotor = new ListBox();
            bnt_Apply = new Button();
            nud_Motor_End = new NumericUpDown();
            nud_Motor_Start = new NumericUpDown();
            label3 = new Label();
            lblSettingsSaved = new Label();
            lblChangesNotSaved = new Label();
            groupBox2 = new GroupBox();
            lb_ABS_Status = new Label();
            bnt_testABS = new Button();
            cb_ABS_Enabled = new CheckBox();
            nud_ABS = new NumericUpDown();
            label6 = new Label();
            cb_AutoConnect = new CheckBox();
            _of_Control = new OnOffStatusControl();
            _of_simHub = new OnOffStatusControl();
            lb_simhub = new Label();
            _of_seatbeltDevice = new OnOffStatusControl();
            _gb_simhub = new GroupBox();
            _lb_menu = new Label();
            _onSupportCorn = new OnOffStatusControl();
            _on_supoortVer = new OnOffStatusControl();
            _on_supportBrake = new OnOffStatusControl();
            ((ISupportInitialize)numericUpDownTarget).BeginInit();
            ((ISupportInitialize)numericUpDownCurveAmount).BeginInit();
            ((ISupportInitialize)pictureBoxCurveGraph).BeginInit();
            ((ISupportInitialize)numericUpDownMaxPower).BeginInit();
            ((ISupportInitialize)numericUpDownGForceToBelt).BeginInit();
            gb_Car_Settings.SuspendLayout();
            _gb_vertical.SuspendLayout();
            ((ISupportInitialize)nudVertical).BeginInit();
            _gb_cornering.SuspendLayout();
            ((ISupportInitialize)nud_coneringStrengh).BeginInit();
            ((ISupportInitialize)nud_ConeringCurveAmount).BeginInit();
            ((ISupportInitialize)percentageUpDownRestingPoint).BeginInit();
            _gb_Braking.SuspendLayout();
            groupBox1.SuspendLayout();
            ((ISupportInitialize)nud_Motor_End).BeginInit();
            ((ISupportInitialize)nud_Motor_Start).BeginInit();
            groupBox2.SuspendLayout();
            ((ISupportInitialize)nud_ABS).BeginInit();
            _gb_simhub.SuspendLayout();
            SuspendLayout();
            // 
            // numericUpDownTarget
            // 
            numericUpDownTarget.DecimalPlaces = 2;
            numericUpDownTarget.Increment = new decimal(new int[] { 1, 0, 0, 65536 });
            numericUpDownTarget.Location = new Point(6, 166);
            numericUpDownTarget.Maximum = new decimal(new int[] { 7, 0, 0, 0 });
            numericUpDownTarget.Minimum = new decimal(new int[] { 2, 0, 0, int.MinValue });
            numericUpDownTarget.Name = "numericUpDownTarget";
            numericUpDownTarget.Size = new Size(120, 23);
            numericUpDownTarget.TabIndex = 2;
            numericUpDownTarget.Value = new decimal(new int[] { 2, 0, 0, 0 });
            numericUpDownTarget.ValueChanged += numericUpDownTarget_ValueChanged;
            // 
            // buttonConnect
            // 
            buttonConnect.Location = new Point(225, 89);
            buttonConnect.Name = "buttonConnect";
            buttonConnect.Size = new Size(75, 23);
            buttonConnect.TabIndex = 3;
            buttonConnect.Text = "Connect";
            buttonConnect.UseVisualStyleBackColor = true;
            buttonConnect.Click += buttonConnect_Click;
            // 
            // labelGForce
            // 
            labelGForce.AutoSize = true;
            labelGForce.Location = new Point(0, 571);
            labelGForce.Name = "labelGForce";
            labelGForce.Size = new Size(76, 15);
            labelGForce.TabIndex = 7;
            labelGForce.Text = "G-Force: 0.00";
            // 
            // checkBoxTest
            // 
            checkBoxTest.AutoSize = true;
            checkBoxTest.Location = new Point(210, 167);
            checkBoxTest.Name = "checkBoxTest";
            checkBoxTest.Size = new Size(47, 19);
            checkBoxTest.TabIndex = 8;
            checkBoxTest.Text = "Test";
            checkBoxTest.UseVisualStyleBackColor = true;
            checkBoxTest.CheckedChanged += checkBoxTest_CheckedChanged;
            // 
            // numericUpDownCurveAmount
            // 
            numericUpDownCurveAmount.DecimalPlaces = 2;
            numericUpDownCurveAmount.Increment = new decimal(new int[] { 5, 0, 0, 131072 });
            numericUpDownCurveAmount.Location = new Point(212, 26);
            numericUpDownCurveAmount.Maximum = new decimal(new int[] { 50, 0, 0, 65536 });
            numericUpDownCurveAmount.Minimum = new decimal(new int[] { 1, 0, 0, 65536 });
            numericUpDownCurveAmount.Name = "numericUpDownCurveAmount";
            numericUpDownCurveAmount.Size = new Size(60, 23);
            numericUpDownCurveAmount.TabIndex = 11;
            numericUpDownCurveAmount.Value = new decimal(new int[] { 100, 0, 0, 131072 });
            numericUpDownCurveAmount.ValueChanged += numericUpDownCurveAmount_ValueChanged;
            // 
            // pictureBoxCurveGraph
            // 
            pictureBoxCurveGraph.Location = new Point(6, 433);
            pictureBoxCurveGraph.Name = "pictureBoxCurveGraph";
            pictureBoxCurveGraph.Size = new Size(276, 132);
            pictureBoxCurveGraph.TabIndex = 13;
            pictureBoxCurveGraph.TabStop = false;
            // 
            // numericUpDownMaxPower
            // 
            numericUpDownMaxPower.Location = new Point(219, 43);
            numericUpDownMaxPower.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            numericUpDownMaxPower.Name = "numericUpDownMaxPower";
            numericUpDownMaxPower.Size = new Size(60, 23);
            numericUpDownMaxPower.TabIndex = 14;
            numericUpDownMaxPower.Value = new decimal(new int[] { 100, 0, 0, 0 });
            numericUpDownMaxPower.ValueChanged += numericUpDownMaxPower_ValueChanged;
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
            // numericUpDownGForceToBelt
            // 
            numericUpDownGForceToBelt.Location = new Point(212, 55);
            numericUpDownGForceToBelt.Maximum = new decimal(new int[] { 400, 0, 0, 0 });
            numericUpDownGForceToBelt.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            numericUpDownGForceToBelt.Name = "numericUpDownGForceToBelt";
            numericUpDownGForceToBelt.Size = new Size(60, 23);
            numericUpDownGForceToBelt.TabIndex = 17;
            numericUpDownGForceToBelt.Value = new decimal(new int[] { 100, 0, 0, 0 });
            numericUpDownGForceToBelt.ValueChanged += numericUpDownGForceToBelt_ValueChanged_1;
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
            // labelMaxGForce
            // 
            labelMaxGForce.AutoSize = true;
            labelMaxGForce.Location = new Point(181, 571);
            labelMaxGForce.Name = "labelMaxGForce";
            labelMaxGForce.Size = new Size(101, 15);
            labelMaxGForce.TabIndex = 22;
            labelMaxGForce.Text = "Max G-Force: 0.00";
            // 
            // gb_Car_Settings
            // 
            gb_Car_Settings.Controls.Add(_cb_showVer);
            gb_Car_Settings.Controls.Add(_cb_showCorn);
            gb_Car_Settings.Controls.Add(_cb_showBraking);
            gb_Car_Settings.Controls.Add(_ttb_restingPoint);
            gb_Car_Settings.Controls.Add(_ttb_maxOutput);
            gb_Car_Settings.Controls.Add(_gb_vertical);
            gb_Car_Settings.Controls.Add(cb_livePrieview);
            gb_Car_Settings.Controls.Add(_gb_cornering);
            gb_Car_Settings.Controls.Add(percentageUpDownRestingPoint);
            gb_Car_Settings.Controls.Add(_gb_Braking);
            gb_Car_Settings.Controls.Add(lb_carName);
            gb_Car_Settings.Controls.Add(label7);
            gb_Car_Settings.Controls.Add(labelMaxGForce);
            gb_Car_Settings.Controls.Add(labelGForce);
            gb_Car_Settings.Controls.Add(pictureBoxCurveGraph);
            gb_Car_Settings.Controls.Add(label1);
            gb_Car_Settings.Controls.Add(labelMaxPower);
            gb_Car_Settings.Controls.Add(numericUpDownMaxPower);
            gb_Car_Settings.Location = new Point(12, 137);
            gb_Car_Settings.Name = "gb_Car_Settings";
            gb_Car_Settings.Size = new Size(289, 617);
            gb_Car_Settings.TabIndex = 23;
            gb_Car_Settings.TabStop = false;
            gb_Car_Settings.Text = "Car Settings";
            // 
            // _cb_showVer
            // 
            _cb_showVer.AutoSize = true;
            _cb_showVer.Checked = true;
            _cb_showVer.CheckState = CheckState.Checked;
            _cb_showVer.Location = new Point(212, 589);
            _cb_showVer.Name = "_cb_showVer";
            _cb_showVer.Size = new Size(64, 19);
            _cb_showVer.TabIndex = 113;
            _cb_showVer.Text = "Vertical";
            _cb_showVer.UseVisualStyleBackColor = true;
            _cb_showVer.CheckedChanged += _cb_showVer_CheckedChanged;
            // 
            // _cb_showCorn
            // 
            _cb_showCorn.AutoSize = true;
            _cb_showCorn.Checked = true;
            _cb_showCorn.CheckState = CheckState.Checked;
            _cb_showCorn.Location = new Point(102, 589);
            _cb_showCorn.Name = "_cb_showCorn";
            _cb_showCorn.Size = new Size(79, 19);
            _cb_showCorn.TabIndex = 112;
            _cb_showCorn.Text = "Cornering";
            _cb_showCorn.UseVisualStyleBackColor = true;
            _cb_showCorn.CheckedChanged += _cb_showCorn_CheckedChanged;
            // 
            // _cb_showBraking
            // 
            _cb_showBraking.AutoSize = true;
            _cb_showBraking.Checked = true;
            _cb_showBraking.CheckState = CheckState.Checked;
            _cb_showBraking.Location = new Point(5, 589);
            _cb_showBraking.Name = "_cb_showBraking";
            _cb_showBraking.Size = new Size(66, 19);
            _cb_showBraking.TabIndex = 111;
            _cb_showBraking.Text = "Braking";
            _cb_showBraking.UseVisualStyleBackColor = true;
            _cb_showBraking.CheckedChanged += _cb_showBraking_CheckedChanged;
            // 
            // _ttb_restingPoint
            // 
            _ttb_restingPoint.FillColor = Color.Cyan;
            _ttb_restingPoint.Location = new Point(94, 72);
            _ttb_restingPoint.Maximum = 30F;
            _ttb_restingPoint.Minimum = 0F;
            _ttb_restingPoint.Name = "_ttb_restingPoint";
            _ttb_restingPoint.Size = new Size(100, 20);
            _ttb_restingPoint.TabIndex = 110;
            _ttb_restingPoint.ThumbColor = Color.White;
            _ttb_restingPoint.TrackColor = Color.Gray;
            _ttb_restingPoint.Value = 1F;
            // 
            // _ttb_maxOutput
            // 
            _ttb_maxOutput.FillColor = Color.Red;
            _ttb_maxOutput.Location = new Point(94, 44);
            _ttb_maxOutput.Maximum = 100F;
            _ttb_maxOutput.Minimum = 1F;
            _ttb_maxOutput.Name = "_ttb_maxOutput";
            _ttb_maxOutput.Size = new Size(100, 20);
            _ttb_maxOutput.TabIndex = 109;
            _ttb_maxOutput.ThumbColor = Color.White;
            _ttb_maxOutput.TrackColor = Color.Gray;
            _ttb_maxOutput.Value = 1F;
            // 
            // _gb_vertical
            // 
            _gb_vertical.Controls.Add(cb_invertHeave);
            _gb_vertical.Controls.Add(_ttb_verStr);
            _gb_vertical.Controls.Add(nudVertical);
            _gb_vertical.Controls.Add(label5);
            _gb_vertical.Location = new Point(6, 328);
            _gb_vertical.Name = "_gb_vertical";
            _gb_vertical.Size = new Size(276, 74);
            _gb_vertical.TabIndex = 108;
            _gb_vertical.TabStop = false;
            _gb_vertical.Text = "Vertical (Heave)";
            // 
            // cb_invertHeave
            // 
            cb_invertHeave.AutoSize = true;
            cb_invertHeave.Location = new Point(7, 49);
            cb_invertHeave.Name = "cb_invertHeave";
            cb_invertHeave.Size = new Size(93, 19);
            cb_invertHeave.TabIndex = 116;
            cb_invertHeave.Text = "Invert Forces";
            cb_invertHeave.UseVisualStyleBackColor = true;
            cb_invertHeave.CheckedChanged += cb_invertHeave_CheckedChanged;
            // 
            // _ttb_verStr
            // 
            _ttb_verStr.FillColor = Color.FromArgb(255, 128, 0);
            _ttb_verStr.Location = new Point(88, 23);
            _ttb_verStr.Maximum = 200F;
            _ttb_verStr.Minimum = 1F;
            _ttb_verStr.Name = "_ttb_verStr";
            _ttb_verStr.Size = new Size(100, 20);
            _ttb_verStr.TabIndex = 115;
            _ttb_verStr.ThumbColor = Color.White;
            _ttb_verStr.TrackColor = Color.Gray;
            _ttb_verStr.Value = 1F;
            // 
            // nudVertical
            // 
            nudVertical.Increment = new decimal(new int[] { 1, 0, 0, 65536 });
            nudVertical.Location = new Point(210, 22);
            nudVertical.Maximum = new decimal(new int[] { 200, 0, 0, 0 });
            nudVertical.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            nudVertical.Name = "nudVertical";
            nudVertical.Size = new Size(60, 23);
            nudVertical.TabIndex = 54;
            nudVertical.Value = new decimal(new int[] { 10, 0, 0, 0 });
            nudVertical.ValueChanged += nudVertical_ValueChanged;
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
            // cb_livePrieview
            // 
            cb_livePrieview.AutoSize = true;
            cb_livePrieview.Location = new Point(102, 410);
            cb_livePrieview.Name = "cb_livePrieview";
            cb_livePrieview.Size = new Size(91, 19);
            cb_livePrieview.TabIndex = 102;
            cb_livePrieview.Text = "Live Preview";
            cb_livePrieview.UseVisualStyleBackColor = true;
            // 
            // _gb_cornering
            // 
            _gb_cornering.Controls.Add(nud_coneringStrengh);
            _gb_cornering.Controls.Add(_ttb_corneringStr);
            _gb_cornering.Controls.Add(_ttb_corneringCurve);
            _gb_cornering.Controls.Add(label8);
            _gb_cornering.Controls.Add(cb_invert_sway);
            _gb_cornering.Controls.Add(label9);
            _gb_cornering.Controls.Add(nud_ConeringCurveAmount);
            _gb_cornering.Location = new Point(6, 215);
            _gb_cornering.Name = "_gb_cornering";
            _gb_cornering.Size = new Size(276, 107);
            _gb_cornering.TabIndex = 107;
            _gb_cornering.TabStop = false;
            _gb_cornering.Text = "Cornering (Sway)";
            // 
            // nud_coneringStrengh
            // 
            nud_coneringStrengh.Location = new Point(210, 53);
            nud_coneringStrengh.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            nud_coneringStrengh.Name = "nud_coneringStrengh";
            nud_coneringStrengh.Size = new Size(60, 23);
            nud_coneringStrengh.TabIndex = 115;
            nud_coneringStrengh.Value = new decimal(new int[] { 1, 0, 0, 0 });
            nud_coneringStrengh.ValueChanged += nud_coneringStrengh_ValueChanged;
            // 
            // _ttb_corneringStr
            // 
            _ttb_corneringStr.FillColor = Color.Lime;
            _ttb_corneringStr.Location = new Point(88, 53);
            _ttb_corneringStr.Maximum = 200F;
            _ttb_corneringStr.Minimum = 1F;
            _ttb_corneringStr.Name = "_ttb_corneringStr";
            _ttb_corneringStr.Size = new Size(100, 20);
            _ttb_corneringStr.TabIndex = 114;
            _ttb_corneringStr.ThumbColor = Color.White;
            _ttb_corneringStr.TrackColor = Color.Gray;
            _ttb_corneringStr.Value = 1F;
            _ttb_corneringStr.Click += _ttb_corneringStr_Click;
            // 
            // _ttb_corneringCurve
            // 
            _ttb_corneringCurve.FillColor = Color.Lime;
            _ttb_corneringCurve.Location = new Point(88, 25);
            _ttb_corneringCurve.Maximum = 10F;
            _ttb_corneringCurve.Minimum = 0.1F;
            _ttb_corneringCurve.Name = "_ttb_corneringCurve";
            _ttb_corneringCurve.Size = new Size(100, 20);
            _ttb_corneringCurve.TabIndex = 113;
            _ttb_corneringCurve.ThumbColor = Color.White;
            _ttb_corneringCurve.TrackColor = Color.Gray;
            _ttb_corneringCurve.Value = 1F;
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
            cb_invert_sway.AutoSize = true;
            cb_invert_sway.Location = new Point(6, 78);
            cb_invert_sway.Name = "cb_invert_sway";
            cb_invert_sway.Size = new Size(93, 19);
            cb_invert_sway.TabIndex = 55;
            cb_invert_sway.Text = "Invert Forces";
            cb_invert_sway.UseVisualStyleBackColor = true;
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
            // nud_ConeringCurveAmount
            // 
            nud_ConeringCurveAmount.DecimalPlaces = 2;
            nud_ConeringCurveAmount.Increment = new decimal(new int[] { 5, 0, 0, 131072 });
            nud_ConeringCurveAmount.Location = new Point(210, 23);
            nud_ConeringCurveAmount.Maximum = new decimal(new int[] { 5, 0, 0, 0 });
            nud_ConeringCurveAmount.Minimum = new decimal(new int[] { 1, 0, 0, 65536 });
            nud_ConeringCurveAmount.Name = "nud_ConeringCurveAmount";
            nud_ConeringCurveAmount.Size = new Size(60, 23);
            nud_ConeringCurveAmount.TabIndex = 101;
            nud_ConeringCurveAmount.Value = new decimal(new int[] { 100, 0, 0, 131072 });
            nud_ConeringCurveAmount.ValueChanged += nud_ConeringCurveAmount_ValueChanged;
            // 
            // percentageUpDownRestingPoint
            // 
            percentageUpDownRestingPoint.Location = new Point(219, 70);
            percentageUpDownRestingPoint.Maximum = new decimal(new int[] { 60, 0, 0, 0 });
            percentageUpDownRestingPoint.Name = "percentageUpDownRestingPoint";
            percentageUpDownRestingPoint.Size = new Size(60, 23);
            percentageUpDownRestingPoint.TabIndex = 105;
            percentageUpDownRestingPoint.ValueChanged += percentageUpDownRestingPoint_ValueChanged;
            // 
            // _gb_Braking
            // 
            _gb_Braking.Controls.Add(cb_invertSurge);
            _gb_Braking.Controls.Add(_ttb_brakingStr);
            _gb_Braking.Controls.Add(_ttb_brakingCurve);
            _gb_Braking.Controls.Add(labelCurveAmount);
            _gb_Braking.Controls.Add(numericUpDownGForceToBelt);
            _gb_Braking.Controls.Add(labelGForceToBelt);
            _gb_Braking.Controls.Add(numericUpDownCurveAmount);
            _gb_Braking.Location = new Point(7, 97);
            _gb_Braking.Name = "_gb_Braking";
            _gb_Braking.Size = new Size(276, 112);
            _gb_Braking.TabIndex = 106;
            _gb_Braking.TabStop = false;
            _gb_Braking.Text = "Braking (Surge)";
            // 
            // cb_invertSurge
            // 
            cb_invertSurge.AutoSize = true;
            cb_invertSurge.Location = new Point(6, 81);
            cb_invertSurge.Name = "cb_invertSurge";
            cb_invertSurge.Size = new Size(93, 19);
            cb_invertSurge.TabIndex = 113;
            cb_invertSurge.Text = "Invert Forces";
            cb_invertSurge.UseVisualStyleBackColor = true;
            cb_invertSurge.CheckedChanged += cb_invertSurge_CheckedChanged;
            // 
            // _ttb_brakingStr
            // 
            _ttb_brakingStr.FillColor = Color.Blue;
            _ttb_brakingStr.Location = new Point(87, 56);
            _ttb_brakingStr.Maximum = 200F;
            _ttb_brakingStr.Minimum = 1F;
            _ttb_brakingStr.Name = "_ttb_brakingStr";
            _ttb_brakingStr.Size = new Size(100, 20);
            _ttb_brakingStr.TabIndex = 112;
            _ttb_brakingStr.ThumbColor = Color.White;
            _ttb_brakingStr.TrackColor = Color.Gray;
            _ttb_brakingStr.Value = 1F;
            // 
            // _ttb_brakingCurve
            // 
            _ttb_brakingCurve.FillColor = Color.Blue;
            _ttb_brakingCurve.Location = new Point(87, 27);
            _ttb_brakingCurve.Maximum = 10F;
            _ttb_brakingCurve.Minimum = 0.1F;
            _ttb_brakingCurve.Name = "_ttb_brakingCurve";
            _ttb_brakingCurve.Size = new Size(100, 20);
            _ttb_brakingCurve.TabIndex = 111;
            _ttb_brakingCurve.ThumbColor = Color.White;
            _ttb_brakingCurve.TrackColor = Color.Gray;
            _ttb_brakingCurve.Value = 1F;
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
            // label1
            // 
            label1.AutoSize = true;
            label1.Location = new Point(105, 567);
            label1.Name = "label1";
            label1.Size = new Size(49, 15);
            label1.TabIndex = 17;
            label1.Text = "GForces";
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
            groupBox1.Controls.Add(ck_Inverted);
            groupBox1.Controls.Add(cb_duelMotors);
            groupBox1.Controls.Add(lb_SelectedMotor);
            groupBox1.Controls.Add(bnt_Apply);
            groupBox1.Controls.Add(nud_Motor_End);
            groupBox1.Controls.Add(checkBoxTest);
            groupBox1.Controls.Add(nud_Motor_Start);
            groupBox1.Controls.Add(numericUpDownTarget);
            groupBox1.Controls.Add(label3);
            groupBox1.Controls.Add(label2);
            groupBox1.Controls.Add(lblSettingsSaved);
            groupBox1.Controls.Add(lblChangesNotSaved);
            groupBox1.Location = new Point(11, 817);
            groupBox1.Name = "groupBox1";
            groupBox1.Size = new Size(287, 196);
            groupBox1.TabIndex = 26;
            groupBox1.TabStop = false;
            groupBox1.Text = "Motor Settings";
            // 
            // ck_Inverted
            // 
            ck_Inverted.AutoSize = true;
            ck_Inverted.Location = new Point(7, 117);
            ck_Inverted.Name = "ck_Inverted";
            ck_Inverted.Size = new Size(69, 19);
            ck_Inverted.TabIndex = 32;
            ck_Inverted.Text = "Inverted";
            ck_Inverted.UseVisualStyleBackColor = true;
            ck_Inverted.CheckedChanged += bnt_Inverted_CheckedChanged;
            // 
            // cb_duelMotors
            // 
            cb_duelMotors.AutoSize = true;
            cb_duelMotors.Location = new Point(178, 22);
            cb_duelMotors.Name = "cb_duelMotors";
            cb_duelMotors.Size = new Size(91, 19);
            cb_duelMotors.TabIndex = 31;
            cb_duelMotors.Text = "Dual Motors";
            cb_duelMotors.UseVisualStyleBackColor = true;
            cb_duelMotors.CheckedChanged += cb_duelMotors_CheckedChanged;
            // 
            // lb_SelectedMotor
            // 
            lb_SelectedMotor.FormattingEnabled = true;
            lb_SelectedMotor.ItemHeight = 15;
            lb_SelectedMotor.Items.AddRange(new object[] { "Left Motor", "Right Motor" });
            lb_SelectedMotor.Location = new Point(6, 20);
            lb_SelectedMotor.Name = "lb_SelectedMotor";
            lb_SelectedMotor.Size = new Size(120, 34);
            lb_SelectedMotor.TabIndex = 30;
            lb_SelectedMotor.SelectedIndexChanged += lb_SelectedMotor_SelectedIndexChanged;
            // 
            // bnt_Apply
            // 
            bnt_Apply.Location = new Point(187, 135);
            bnt_Apply.Name = "bnt_Apply";
            bnt_Apply.Size = new Size(75, 23);
            bnt_Apply.TabIndex = 29;
            bnt_Apply.Text = "Apply";
            bnt_Apply.UseVisualStyleBackColor = true;
            bnt_Apply.Click += bnt_Apply_Click;
            // 
            // nud_Motor_End
            // 
            nud_Motor_End.Location = new Point(202, 88);
            nud_Motor_End.Maximum = new decimal(new int[] { 270, 0, 0, 0 });
            nud_Motor_End.Name = "nud_Motor_End";
            nud_Motor_End.Size = new Size(60, 23);
            nud_Motor_End.TabIndex = 28;
            nud_Motor_End.Value = new decimal(new int[] { 100, 0, 0, 131072 });
            nud_Motor_End.ValueChanged += nud_Motor_End_ValueChanged;
            // 
            // nud_Motor_Start
            // 
            nud_Motor_Start.Location = new Point(202, 59);
            nud_Motor_Start.Maximum = new decimal(new int[] { 270, 0, 0, 0 });
            nud_Motor_Start.Name = "nud_Motor_Start";
            nud_Motor_Start.Size = new Size(60, 23);
            nud_Motor_Start.TabIndex = 24;
            nud_Motor_Start.Value = new decimal(new int[] { 100, 0, 0, 131072 });
            nud_Motor_Start.ValueChanged += nud_Motor_Start_ValueChanged;
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
            groupBox2.Controls.Add(lb_ABS_Status);
            groupBox2.Controls.Add(bnt_testABS);
            groupBox2.Controls.Add(cb_ABS_Enabled);
            groupBox2.Controls.Add(nud_ABS);
            groupBox2.Controls.Add(label6);
            groupBox2.Location = new Point(11, 750);
            groupBox2.Name = "groupBox2";
            groupBox2.Size = new Size(286, 63);
            groupBox2.TabIndex = 27;
            groupBox2.TabStop = false;
            groupBox2.Text = "ABS (EXPERIMENTAL)";
            groupBox2.Enter += groupBox2_Enter;
            // 
            // lb_ABS_Status
            // 
            lb_ABS_Status.AutoSize = true;
            lb_ABS_Status.Location = new Point(13, 45);
            lb_ABS_Status.Name = "lb_ABS_Status";
            lb_ABS_Status.Size = new Size(51, 15);
            lb_ABS_Status.TabIndex = 57;
            lb_ABS_Status.Text = "ABS: Off";
            // 
            // bnt_testABS
            // 
            bnt_testABS.Location = new Point(238, 20);
            bnt_testABS.Name = "bnt_testABS";
            bnt_testABS.Size = new Size(42, 23);
            bnt_testABS.TabIndex = 56;
            bnt_testABS.Text = "Test";
            bnt_testABS.UseVisualStyleBackColor = true;
            bnt_testABS.Click += bnt_testABS_Click;
            // 
            // cb_ABS_Enabled
            // 
            cb_ABS_Enabled.AutoSize = true;
            cb_ABS_Enabled.Location = new Point(161, 24);
            cb_ABS_Enabled.Name = "cb_ABS_Enabled";
            cb_ABS_Enabled.Size = new Size(68, 19);
            cb_ABS_Enabled.TabIndex = 35;
            cb_ABS_Enabled.Text = "Enabled";
            cb_ABS_Enabled.UseVisualStyleBackColor = true;
            cb_ABS_Enabled.CheckedChanged += cb_ABS_Enabled_CheckedChanged;
            // 
            // nud_ABS
            // 
            nud_ABS.DecimalPlaces = 2;
            nud_ABS.Location = new Point(95, 22);
            nud_ABS.Maximum = new decimal(new int[] { 30, 0, 0, 0 });
            nud_ABS.Minimum = new decimal(new int[] { 3, 0, 0, 0 });
            nud_ABS.Name = "nud_ABS";
            nud_ABS.Size = new Size(60, 23);
            nud_ABS.TabIndex = 55;
            nud_ABS.Value = new decimal(new int[] { 3, 0, 0, 0 });
            nud_ABS.ValueChanged += nud_ABS_ValueChanged;
            // 
            // label6
            // 
            label6.AutoSize = true;
            label6.Location = new Point(13, 26);
            label6.Name = "label6";
            label6.Size = new Size(76, 15);
            label6.TabIndex = 36;
            label6.Text = "ABS Strength";
            // 
            // cb_AutoConnect
            // 
            cb_AutoConnect.AutoSize = true;
            cb_AutoConnect.Location = new Point(15, 115);
            cb_AutoConnect.Name = "cb_AutoConnect";
            cb_AutoConnect.Size = new Size(185, 19);
            cb_AutoConnect.TabIndex = 28;
            cb_AutoConnect.Text = "Connect To Device On Startup";
            cb_AutoConnect.UseVisualStyleBackColor = true;
            cb_AutoConnect.CheckedChanged += cb_AutoConnect_CheckedChanged;
            // 
            // _of_Control
            // 
            _of_Control.BackColor = SystemColors.Control;
            _of_Control.Font = new Font("Segoe UI", 9F);
            _of_Control.ForeColor = SystemColors.ControlText;
            _of_Control.IsOn = false;
            _of_Control.Location = new Point(143, 2);
            _of_Control.Name = "_of_Control";
            _of_Control.OffColor = Color.Red;
            _of_Control.OnColor = Color.LimeGreen;
            _of_Control.Size = new Size(77, 24);
            _of_Control.StatusText = "Iracing:";
            _of_Control.TabIndex = 30;
            _of_Control.Text = "Iracing:";
            _of_Control.TextColor = SystemColors.ControlText;
            // 
            // _of_simHub
            // 
            _of_simHub.BackColor = SystemColors.Control;
            _of_simHub.Font = new Font("Segoe UI", 9F);
            _of_simHub.ForeColor = SystemColors.ControlText;
            _of_simHub.IsOn = false;
            _of_simHub.Location = new Point(226, 2);
            _of_simHub.Name = "_of_simHub";
            _of_simHub.OffColor = Color.Red;
            _of_simHub.OnColor = Color.LimeGreen;
            _of_simHub.Size = new Size(87, 24);
            _of_simHub.StatusText = "SIMHUB:";
            _of_simHub.TabIndex = 31;
            _of_simHub.Text = "SIMHUB:";
            _of_simHub.TextColor = SystemColors.ControlText;
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
            _of_seatbeltDevice.BackColor = SystemColors.Control;
            _of_seatbeltDevice.Font = new Font("Segoe UI", 14F, FontStyle.Bold);
            _of_seatbeltDevice.ForeColor = SystemColors.ControlText;
            _of_seatbeltDevice.IsOn = false;
            _of_seatbeltDevice.Location = new Point(11, 88);
            _of_seatbeltDevice.Name = "_of_seatbeltDevice";
            _of_seatbeltDevice.OffColor = Color.Red;
            _of_seatbeltDevice.OnColor = Color.LimeGreen;
            _of_seatbeltDevice.Size = new Size(185, 24);
            _of_seatbeltDevice.StatusText = "Seatbelt Device:";
            _of_seatbeltDevice.TabIndex = 33;
            _of_seatbeltDevice.Text = "Seatbelt Device:";
            _of_seatbeltDevice.TextColor = SystemColors.ControlText;
            // 
            // _gb_simhub
            // 
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
            _onSupportCorn.BackColor = SystemColors.Control;
            _onSupportCorn.Font = new Font("Segoe UI", 9F);
            _onSupportCorn.ForeColor = SystemColors.ControlText;
            _onSupportCorn.IsOn = false;
            _onSupportCorn.Location = new Point(202, 35);
            _onSupportCorn.Name = "_onSupportCorn";
            _onSupportCorn.OffColor = Color.Red;
            _onSupportCorn.OnColor = Color.LimeGreen;
            _onSupportCorn.Size = new Size(89, 24);
            _onSupportCorn.StatusText = "Cornering";
            _onSupportCorn.TabIndex = 35;
            _onSupportCorn.Text = "Cornering";
            _onSupportCorn.TextColor = SystemColors.ControlText;
            // 
            // _on_supoortVer
            // 
            _on_supoortVer.BackColor = SystemColors.Control;
            _on_supoortVer.Font = new Font("Segoe UI", 9F);
            _on_supoortVer.ForeColor = SystemColors.ControlText;
            _on_supoortVer.IsOn = false;
            _on_supoortVer.Location = new Point(5, 35);
            _on_supoortVer.Name = "_on_supoortVer";
            _on_supoortVer.OffColor = Color.Red;
            _on_supoortVer.OnColor = Color.LimeGreen;
            _on_supoortVer.Size = new Size(77, 24);
            _on_supoortVer.StatusText = "Vertical";
            _on_supoortVer.TabIndex = 34;
            _on_supoortVer.Text = "Vertical";
            _on_supoortVer.TextColor = SystemColors.ControlText;
            // 
            // _on_supportBrake
            // 
            _on_supportBrake.BackColor = SystemColors.Control;
            _on_supportBrake.Font = new Font("Segoe UI", 9F);
            _on_supportBrake.ForeColor = SystemColors.ControlText;
            _on_supportBrake.IsOn = false;
            _on_supportBrake.Location = new Point(108, 35);
            _on_supportBrake.Name = "_on_supportBrake";
            _on_supportBrake.OffColor = Color.Red;
            _on_supportBrake.OnColor = Color.LimeGreen;
            _on_supportBrake.Size = new Size(76, 24);
            _on_supportBrake.StatusText = "Braking";
            _on_supportBrake.TabIndex = 33;
            _on_supportBrake.Text = "Braking";
            _on_supportBrake.TextColor = SystemColors.ControlText;
            // 
            // Form1
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(316, 1015);
            Controls.Add(_gb_simhub);
            Controls.Add(_of_seatbeltDevice);
            Controls.Add(_of_simHub);
            Controls.Add(_of_Control);
            Controls.Add(cb_AutoConnect);
            Controls.Add(groupBox2);
            Controls.Add(groupBox1);
            Controls.Add(gb_Car_Settings);
            Controls.Add(buttonConnect);
            Icon = (Icon)resources.GetObject("$this.Icon");
            Name = "Form1";
            Text = "Belt Tensioner";
            Load += Form1_Load;
            ((ISupportInitialize)numericUpDownTarget).EndInit();
            ((ISupportInitialize)numericUpDownCurveAmount).EndInit();
            ((ISupportInitialize)pictureBoxCurveGraph).EndInit();
            ((ISupportInitialize)numericUpDownMaxPower).EndInit();
            ((ISupportInitialize)numericUpDownGForceToBelt).EndInit();
            gb_Car_Settings.ResumeLayout(false);
            gb_Car_Settings.PerformLayout();
            _gb_vertical.ResumeLayout(false);
            _gb_vertical.PerformLayout();
            ((ISupportInitialize)nudVertical).EndInit();
            _gb_cornering.ResumeLayout(false);
            _gb_cornering.PerformLayout();
            ((ISupportInitialize)nud_coneringStrengh).EndInit();
            ((ISupportInitialize)nud_ConeringCurveAmount).EndInit();
            ((ISupportInitialize)percentageUpDownRestingPoint).EndInit();
            _gb_Braking.ResumeLayout(false);
            _gb_Braking.PerformLayout();
            groupBox1.ResumeLayout(false);
            groupBox1.PerformLayout();
            ((ISupportInitialize)nud_Motor_End).EndInit();
            ((ISupportInitialize)nud_Motor_Start).EndInit();
            groupBox2.ResumeLayout(false);
            groupBox2.PerformLayout();
            ((ISupportInitialize)nud_ABS).EndInit();
            _gb_simhub.ResumeLayout(false);
            _gb_simhub.PerformLayout();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion
        private GroupBox gb_Car_Settings;
        private Label lb_carName;
        private Label labelCurveAmount;
        private Label label2;
        private GroupBox groupBox1;
        private Button bnt_Apply;
        private NumericUpDown nud_Motor_End;
        private NumericUpDown nud_Motor_Start;
        private Label label3;
        private ListBox lb_SelectedMotor;
        private CheckBox cb_duelMotors;
        private CheckBox ck_Inverted;
        private NumericUpDown nudVertical;
        private Label label5;
        private GroupBox groupBox2;
        private NumericUpDown nud_ABS;
        private Label label6;
        private CheckBox cb_ABS_Enabled;
        private Button bnt_testABS;
        private CheckBox cb_invert_sway;
        private Label lb_ABS_Status;
        private CheckBox cb_AutoConnect;
        private NumericUpDown nud_ConeringCurveAmount;
        private CheckBox cb_livePrieview;
        private PercentageUpDown percentageUpDownRestingPoint;
        private Label label7;
        private GroupBox _gb_Braking;
        private GroupBox _gb_cornering;
        private Label label8;
        private Label label9;
        private GroupBox _gb_vertical;
        private ThinTrackBar _ttb_maxOutput;
        private ThinTrackBar _ttb_restingPoint;
        private ThinTrackBar _ttb_verStr;
        private ThinTrackBar _ttb_corneringStr;
        private ThinTrackBar _ttb_corneringCurve;
        private ThinTrackBar _ttb_brakingStr;
        private ThinTrackBar _ttb_brakingCurve;
        private CheckBox _cb_showBraking;
        private Label label1;
        private CheckBox _cb_showVer;
        private CheckBox _cb_showCorn;
        private NumericUpDown nud_coneringStrengh;
        private OnOffStatusControl _of_Control;
        private OnOffStatusControl _of_simHub;
        private Label lb_simhub;
        private OnOffStatusControl _of_seatbeltDevice;
        private GroupBox _gb_simhub;
        private OnOffStatusControl _onSupportCorn;
        private OnOffStatusControl _on_supoortVer;
        private OnOffStatusControl _on_supportBrake;
        private Label _lb_menu;
        private CheckBox cb_invertHeave;
        private CheckBox cb_invertSurge;
    }
}