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

        // Controls referenced by code
        private Label labelStatus;
        private GForceUpDown numericUpDownTarget;
        private Button buttonConnect;
        private TextBox textBoxIracingStatus;
        private Label labelGForce;
        private CheckBox checkBoxTest;
        private NumericUpDown numericUpDownCurveAmount;
        private PictureBox pictureBoxCurveGraph;
        private PercentageUpDown numericUpDownMaxPower;
        private Label labelMaxPower;
        private NumericUpDown numericUpDownGForceToBelt;
        private Label labelGForceToBelt;
        private Label labelAnalogValue;
        private Label labelTargetValue;
        private Label labelDistanceValue;
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
            labelStatus = new Label();
            numericUpDownTarget = new GForceUpDown();
            buttonConnect = new Button();
            textBoxIracingStatus = new TextBox();
            labelGForce = new Label();
            checkBoxTest = new CheckBox();
            numericUpDownCurveAmount = new NumericUpDown();
            pictureBoxCurveGraph = new PictureBox();
            numericUpDownMaxPower = new PercentageUpDown();
            labelMaxPower = new Label();
            label1 = new Label();
            numericUpDownGForceToBelt = new NumericUpDown();
            labelGForceToBelt = new Label();
            labelAnalogValue = new Label();
            labelTargetValue = new Label();
            labelDistanceValue = new Label();
            labelMaxGForce = new Label();
            gb_Car_Settings = new GroupBox();
            cb_livePrieview = new CheckBox();
            cb_invert_conering = new CheckBox();
            nudVertical = new NumericUpDown();
            label5 = new Label();
            nud_coneringStrengh = new NumericUpDown();
            label4 = new Label();
            lb_carName = new Label();
            labelCurveAmount = new Label();
            lblConeringCurveAmount = new Label();
            nud_ConeringCurveAmount = new NumericUpDown();
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
            ((ISupportInitialize)numericUpDownTarget).BeginInit();
            ((ISupportInitialize)numericUpDownCurveAmount).BeginInit();
            ((ISupportInitialize)pictureBoxCurveGraph).BeginInit();
            ((ISupportInitialize)numericUpDownMaxPower).BeginInit();
            ((ISupportInitialize)numericUpDownGForceToBelt).BeginInit();
            gb_Car_Settings.SuspendLayout();
            ((ISupportInitialize)nudVertical).BeginInit();
            ((ISupportInitialize)nud_coneringStrengh).BeginInit();
            ((ISupportInitialize)nud_ConeringCurveAmount).BeginInit();
            groupBox1.SuspendLayout();
            ((ISupportInitialize)nud_Motor_End).BeginInit();
            ((ISupportInitialize)nud_Motor_Start).BeginInit();
            groupBox2.SuspendLayout();
            ((ISupportInitialize)nud_ABS).BeginInit();
            SuspendLayout();
            // 
            // labelStatus
            // 
            labelStatus.AutoSize = true;
            labelStatus.Font = new Font("Segoe UI", 14F);
            labelStatus.ForeColor = Color.Red;
            labelStatus.Location = new Point(42, 4);
            labelStatus.Name = "labelStatus";
            labelStatus.Size = new Size(201, 25);
            labelStatus.TabIndex = 1;
            labelStatus.Text = "Seatbelt Not Conected";
            labelStatus.Click += labelStatus_Click;
            // 
            // numericUpDownTarget
            // 
            numericUpDownTarget.DecimalPlaces = 2;
            numericUpDownTarget.Increment = new decimal(new int[] { 1, 0, 0, 65536 });
            numericUpDownTarget.Location = new Point(7, 98);
            numericUpDownTarget.Maximum = new decimal(new int[] { 7, 0, 0, 0 });
            numericUpDownTarget.Name = "numericUpDownTarget";
            numericUpDownTarget.Size = new Size(120, 23);
            numericUpDownTarget.TabIndex = 2;
            numericUpDownTarget.Value = new decimal(new int[] { 2, 0, 0, 0 });
            numericUpDownTarget.ValueChanged += numericUpDownTarget_ValueChanged;
            // 
            // buttonConnect
            // 
            buttonConnect.Location = new Point(197, 33);
            buttonConnect.Name = "buttonConnect";
            buttonConnect.Size = new Size(75, 23);
            buttonConnect.TabIndex = 3;
            buttonConnect.Text = "Connect";
            buttonConnect.UseVisualStyleBackColor = true;
            buttonConnect.Click += buttonConnect_Click;
            // 
            // textBoxIracingStatus
            // 
            textBoxIracingStatus.Location = new Point(15, 54);
            textBoxIracingStatus.Name = "textBoxIracingStatus";
            textBoxIracingStatus.ReadOnly = true;
            textBoxIracingStatus.Size = new Size(125, 23);
            textBoxIracingStatus.TabIndex = 6;
            textBoxIracingStatus.Text = "Iracing Not Connect";
            // 
            // labelGForce
            // 
            labelGForce.AutoSize = true;
            labelGForce.Location = new Point(16, 266);
            labelGForce.Name = "labelGForce";
            labelGForce.Size = new Size(76, 15);
            labelGForce.TabIndex = 7;
            labelGForce.Text = "G-Force: 0.00";
            // 
            // checkBoxTest
            // 
            checkBoxTest.AutoSize = true;
            checkBoxTest.Location = new Point(159, 102);
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
            numericUpDownCurveAmount.Location = new Point(213, 40);
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
            pictureBoxCurveGraph.Location = new Point(21, 140);
            pictureBoxCurveGraph.Name = "pictureBoxCurveGraph";
            pictureBoxCurveGraph.Size = new Size(260, 117);
            pictureBoxCurveGraph.TabIndex = 13;
            pictureBoxCurveGraph.TabStop = false;
            // 
            // numericUpDownMaxPower
            // 
            numericUpDownMaxPower.Location = new Point(213, 67);
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
            labelMaxPower.Location = new Point(16, 69);
            labelMaxPower.Name = "labelMaxPower";
            labelMaxPower.Size = new Size(70, 15);
            labelMaxPower.TabIndex = 15;
            labelMaxPower.Text = "Max Output";
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Location = new Point(115, 257);
            label1.Name = "label1";
            label1.Size = new Size(49, 15);
            label1.TabIndex = 17;
            label1.Text = "GForces";
            // 
            // numericUpDownGForceToBelt
            // 
            numericUpDownGForceToBelt.DecimalPlaces = 2;
            numericUpDownGForceToBelt.Location = new Point(213, 95);
            numericUpDownGForceToBelt.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            numericUpDownGForceToBelt.Name = "numericUpDownGForceToBelt";
            numericUpDownGForceToBelt.Size = new Size(60, 23);
            numericUpDownGForceToBelt.TabIndex = 17;
            numericUpDownGForceToBelt.Value = new decimal(new int[] { 1, 0, 0, 0 });
            numericUpDownGForceToBelt.ValueChanged += numericUpDownGForceToBelt_ValueChanged_1;
            // 
            // labelGForceToBelt
            // 
            labelGForceToBelt.AutoSize = true;
            labelGForceToBelt.Location = new Point(16, 96);
            labelGForceToBelt.Name = "labelGForceToBelt";
            labelGForceToBelt.Size = new Size(79, 15);
            labelGForceToBelt.TabIndex = 18;
            labelGForceToBelt.Text = "Power To Belt";
            // 
            // labelAnalogValue
            // 
            labelAnalogValue.AutoSize = true;
            labelAnalogValue.Location = new Point(16, 122);
            labelAnalogValue.Name = "labelAnalogValue";
            labelAnalogValue.Size = new Size(66, 15);
            labelAnalogValue.TabIndex = 19;
            labelAnalogValue.Text = "Analog: ---";
            // 
            // labelTargetValue
            // 
            labelTargetValue.AutoSize = true;
            labelTargetValue.Location = new Point(104, 122);
            labelTargetValue.Name = "labelTargetValue";
            labelTargetValue.Size = new Size(61, 15);
            labelTargetValue.TabIndex = 20;
            labelTargetValue.Text = "Target: ---";
            // 
            // labelDistanceValue
            // 
            labelDistanceValue.AutoSize = true;
            labelDistanceValue.Location = new Point(204, 122);
            labelDistanceValue.Name = "labelDistanceValue";
            labelDistanceValue.Size = new Size(73, 15);
            labelDistanceValue.TabIndex = 21;
            labelDistanceValue.Text = "Distance: ---";
            // 
            // labelMaxGForce
            // 
            labelMaxGForce.AutoSize = true;
            labelMaxGForce.Location = new Point(168, 266);
            labelMaxGForce.Name = "labelMaxGForce";
            labelMaxGForce.Size = new Size(101, 15);
            labelMaxGForce.TabIndex = 22;
            labelMaxGForce.Text = "Max G-Force: 0.00";
            // 
            // gb_Car_Settings
            // 
            gb_Car_Settings.Controls.Add(cb_livePrieview);
            gb_Car_Settings.Controls.Add(cb_invert_conering);
            gb_Car_Settings.Controls.Add(nudVertical);
            gb_Car_Settings.Controls.Add(label5);
            gb_Car_Settings.Controls.Add(nud_coneringStrengh);
            gb_Car_Settings.Controls.Add(label4);
            gb_Car_Settings.Controls.Add(lb_carName);
            gb_Car_Settings.Controls.Add(labelCurveAmount);
            gb_Car_Settings.Controls.Add(labelMaxGForce);
            gb_Car_Settings.Controls.Add(labelDistanceValue);
            gb_Car_Settings.Controls.Add(numericUpDownCurveAmount);
            gb_Car_Settings.Controls.Add(labelTargetValue);
            gb_Car_Settings.Controls.Add(labelGForce);
            gb_Car_Settings.Controls.Add(pictureBoxCurveGraph);
            gb_Car_Settings.Controls.Add(labelAnalogValue);
            gb_Car_Settings.Controls.Add(labelMaxPower);
            gb_Car_Settings.Controls.Add(labelGForceToBelt);
            gb_Car_Settings.Controls.Add(numericUpDownMaxPower);
            gb_Car_Settings.Controls.Add(numericUpDownGForceToBelt);
            gb_Car_Settings.Controls.Add(label1);
            gb_Car_Settings.Controls.Add(lblConeringCurveAmount);
            gb_Car_Settings.Controls.Add(nud_ConeringCurveAmount);
            gb_Car_Settings.Location = new Point(12, 76);
            gb_Car_Settings.Name = "gb_Car_Settings";
            gb_Car_Settings.Size = new Size(289, 417);
            gb_Car_Settings.TabIndex = 23;
            gb_Car_Settings.TabStop = false;
            gb_Car_Settings.Text = "Car Settings";
            // 
            // cb_livePrieview
            // 
            cb_livePrieview.AutoSize = true;
            cb_livePrieview.Location = new Point(24, 284);
            cb_livePrieview.Name = "cb_livePrieview";
            cb_livePrieview.Size = new Size(91, 19);
            cb_livePrieview.TabIndex = 102;
            cb_livePrieview.Text = "Live Preview";
            cb_livePrieview.UseVisualStyleBackColor = true;
            // 
            // cb_invert_conering
            // 
            cb_invert_conering.AutoSize = true;
            cb_invert_conering.Location = new Point(24, 367);
            cb_invert_conering.Name = "cb_invert_conering";
            cb_invert_conering.Size = new Size(149, 19);
            cb_invert_conering.TabIndex = 55;
            cb_invert_conering.Text = "Invert Cornering Forces";
            cb_invert_conering.UseVisualStyleBackColor = true;
            cb_invert_conering.CheckedChanged += cb_invert_conering_CheckedChanged;
            // 
            // nudVertical
            // 
            nudVertical.DecimalPlaces = 2;
            nudVertical.Increment = new decimal(new int[] { 1, 0, 0, 65536 });
            nudVertical.Location = new Point(214, 386);
            nudVertical.Minimum = new decimal(new int[] { 5, 0, 0, 65536 });
            nudVertical.Name = "nudVertical";
            nudVertical.Size = new Size(60, 23);
            nudVertical.TabIndex = 54;
            nudVertical.Value = new decimal(new int[] { 1, 0, 0, 0 });
            nudVertical.ValueChanged += nudVertical_ValueChanged;
            // 
            // label5
            // 
            label5.AutoSize = true;
            label5.Location = new Point(21, 391);
            label5.Name = "label5";
            label5.Size = new Size(93, 15);
            label5.TabIndex = 53;
            label5.Text = "Vertical Strength";
            // 
            // nud_coneringStrengh
            // 
            nud_coneringStrengh.DecimalPlaces = 2;
            nud_coneringStrengh.Increment = new decimal(new int[] { 1, 0, 0, 65536 });
            nud_coneringStrengh.Location = new Point(213, 317);
            nud_coneringStrengh.Minimum = new decimal(new int[] { 5, 0, 0, 65536 });
            nud_coneringStrengh.Name = "nud_coneringStrengh";
            nud_coneringStrengh.Size = new Size(60, 23);
            nud_coneringStrengh.TabIndex = 36;
            nud_coneringStrengh.Value = new decimal(new int[] { 1, 0, 0, 0 });
            nud_coneringStrengh.ValueChanged += nud_coneringStrengh_ValueChanged_1;
            // 
            // label4
            // 
            label4.AutoSize = true;
            label4.Location = new Point(20, 320);
            label4.Name = "label4";
            label4.Size = new Size(108, 15);
            label4.TabIndex = 35;
            label4.Text = "Cornering Strength";
            // 
            // lb_carName
            // 
            lb_carName.AutoSize = true;
            lb_carName.Location = new Point(16, 20);
            lb_carName.Name = "lb_carName";
            lb_carName.Size = new Size(60, 15);
            lb_carName.TabIndex = 23;
            lb_carName.Text = "Car Name";
            // 
            // labelCurveAmount
            // 
            labelCurveAmount.AutoSize = true;
            labelCurveAmount.Location = new Point(16, 42);
            labelCurveAmount.Name = "labelCurveAmount";
            labelCurveAmount.Size = new Size(85, 15);
            labelCurveAmount.TabIndex = 12;
            labelCurveAmount.Text = "Curve Amount";
            // 
            // lblConeringCurveAmount
            // 
            lblConeringCurveAmount.AutoSize = true;
            lblConeringCurveAmount.Location = new Point(20, 343);
            lblConeringCurveAmount.Name = "lblConeringCurveAmount";
            lblConeringCurveAmount.Size = new Size(144, 15);
            lblConeringCurveAmount.TabIndex = 100;
            lblConeringCurveAmount.Text = "Cornering Curve Amount:";
            // 
            // nud_ConeringCurveAmount
            // 
            nud_ConeringCurveAmount.DecimalPlaces = 2;
            nud_ConeringCurveAmount.Increment = new decimal(new int[] { 5, 0, 0, 131072 });
            nud_ConeringCurveAmount.Location = new Point(213, 344);
            nud_ConeringCurveAmount.Maximum = new decimal(new int[] { 5, 0, 0, 0 });
            nud_ConeringCurveAmount.Minimum = new decimal(new int[] { 1, 0, 0, 65536 });
            nud_ConeringCurveAmount.Name = "nud_ConeringCurveAmount";
            nud_ConeringCurveAmount.Size = new Size(60, 23);
            nud_ConeringCurveAmount.TabIndex = 101;
            nud_ConeringCurveAmount.Value = new decimal(new int[] { 100, 0, 0, 131072 });
            nud_ConeringCurveAmount.ValueChanged += nud_ConeringCurveAmount_ValueChanged;
            // 
            // label2
            // 
            label2.AutoSize = true;
            label2.Location = new Point(6, 24);
            label2.Name = "label2";
            label2.Size = new Size(77, 15);
            label2.TabIndex = 25;
            label2.Text = "Start Position";
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
            groupBox1.Location = new Point(15, 562);
            groupBox1.Name = "groupBox1";
            groupBox1.Size = new Size(287, 196);
            groupBox1.TabIndex = 26;
            groupBox1.TabStop = false;
            groupBox1.Text = "Motor Settings";
            // 
            // ck_Inverted
            // 
            ck_Inverted.AutoSize = true;
            ck_Inverted.Location = new Point(7, 73);
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
            cb_duelMotors.Location = new Point(159, 74);
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
            lb_SelectedMotor.Location = new Point(7, 156);
            lb_SelectedMotor.Name = "lb_SelectedMotor";
            lb_SelectedMotor.Size = new Size(120, 34);
            lb_SelectedMotor.TabIndex = 30;
            lb_SelectedMotor.SelectedIndexChanged += lb_SelectedMotor_SelectedIndexChanged;
            // 
            // bnt_Apply
            // 
            bnt_Apply.Location = new Point(171, 167);
            bnt_Apply.Name = "bnt_Apply";
            bnt_Apply.Size = new Size(75, 23);
            bnt_Apply.TabIndex = 29;
            bnt_Apply.Text = "Apply";
            bnt_Apply.UseVisualStyleBackColor = true;
            bnt_Apply.Click += bnt_Apply_Click;
            // 
            // nud_Motor_End
            // 
            nud_Motor_End.DecimalPlaces = 2;
            nud_Motor_End.Location = new Point(190, 45);
            nud_Motor_End.Maximum = new decimal(new int[] { 270, 0, 0, 0 });
            nud_Motor_End.Name = "nud_Motor_End";
            nud_Motor_End.Size = new Size(60, 23);
            nud_Motor_End.TabIndex = 28;
            nud_Motor_End.Value = new decimal(new int[] { 100, 0, 0, 131072 });
            nud_Motor_End.ValueChanged += nud_Motor_End_ValueChanged;
            // 
            // nud_Motor_Start
            // 
            nud_Motor_Start.DecimalPlaces = 2;
            nud_Motor_Start.Location = new Point(190, 16);
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
            label3.Location = new Point(5, 47);
            label3.Name = "label3";
            label3.Size = new Size(73, 15);
            label3.TabIndex = 27;
            label3.Text = "End Position";
            // 
            // lblSettingsSaved
            // 
            lblSettingsSaved.AutoSize = true;
            lblSettingsSaved.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            lblSettingsSaved.ForeColor = Color.Green;
            lblSettingsSaved.Location = new Point(150, 145);
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
            lblChangesNotSaved.Location = new Point(58, 126);
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
            groupBox2.Location = new Point(15, 499);
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
            cb_AutoConnect.Location = new Point(164, 57);
            cb_AutoConnect.Name = "cb_AutoConnect";
            cb_AutoConnect.Size = new Size(131, 19);
            cb_AutoConnect.TabIndex = 28;
            cb_AutoConnect.Text = "Connect On Startup";
            cb_AutoConnect.UseVisualStyleBackColor = true;
            cb_AutoConnect.CheckedChanged += cb_AutoConnect_CheckedChanged;
            // 
            // Form1
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(314, 759);
            Controls.Add(cb_AutoConnect);
            Controls.Add(groupBox2);
            Controls.Add(groupBox1);
            Controls.Add(gb_Car_Settings);
            Controls.Add(textBoxIracingStatus);
            Controls.Add(buttonConnect);
            Controls.Add(labelStatus);
            Name = "Form1";
            Text = "Belt Tention Test";
            ((ISupportInitialize)numericUpDownTarget).EndInit();
            ((ISupportInitialize)numericUpDownCurveAmount).EndInit();
            ((ISupportInitialize)pictureBoxCurveGraph).EndInit();
            ((ISupportInitialize)numericUpDownMaxPower).EndInit();
            ((ISupportInitialize)numericUpDownGForceToBelt).EndInit();
            gb_Car_Settings.ResumeLayout(false);
            gb_Car_Settings.PerformLayout();
            ((ISupportInitialize)nudVertical).EndInit();
            ((ISupportInitialize)nud_coneringStrengh).EndInit();
            ((ISupportInitialize)nud_ConeringCurveAmount).EndInit();
            groupBox1.ResumeLayout(false);
            groupBox1.PerformLayout();
            ((ISupportInitialize)nud_Motor_End).EndInit();
            ((ISupportInitialize)nud_Motor_Start).EndInit();
            groupBox2.ResumeLayout(false);
            groupBox2.PerformLayout();
            ((ISupportInitialize)nud_ABS).EndInit();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Label label1;
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
        private NumericUpDown nud_coneringStrengh;
        private Label label4;
        private NumericUpDown nudVertical;
        private Label label5;
        private GroupBox groupBox2;
        private NumericUpDown nud_ABS;
        private Label label6;
        private CheckBox cb_ABS_Enabled;
        private Button bnt_testABS;
        private CheckBox cb_invert_conering;
        private Label lb_ABS_Status;
        private CheckBox cb_AutoConnect;
        private Label lblConeringCurveAmount;
        private NumericUpDown nud_ConeringCurveAmount;
        private CheckBox cb_livePrieview;
    }
}