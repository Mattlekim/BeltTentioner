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
        private Label labelXAxis; // X axis label for the graph
        private NumericUpDown numericUpDownGForceToBelt;
        private Label labelGForceToBelt;
        private Label labelAnalogValue;
        private Label labelTargetValue;
        private Label labelDistanceValue;
        private Label labelMaxGForce; // Max G-Force label
        private Label lblSettingsSaved;

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
            labelXAxis = new Label();
            label1 = new Label();
            numericUpDownGForceToBelt = new NumericUpDown();
            labelGForceToBelt = new Label();
            labelAnalogValue = new Label();
            labelTargetValue = new Label();
            labelDistanceValue = new Label();
            labelMaxGForce = new Label();
            gb_Car_Settings = new GroupBox();
            lb_carName = new Label();
            labelCurveAmount = new Label();
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
            ((ISupportInitialize)numericUpDownTarget).BeginInit();
            ((ISupportInitialize)numericUpDownCurveAmount).BeginInit();
            ((ISupportInitialize)pictureBoxCurveGraph).BeginInit();
            ((ISupportInitialize)numericUpDownMaxPower).BeginInit();
            ((ISupportInitialize)numericUpDownGForceToBelt).BeginInit();
            gb_Car_Settings.SuspendLayout();
            groupBox1.SuspendLayout();
            ((ISupportInitialize)nud_Motor_End).BeginInit();
            ((ISupportInitialize)nud_Motor_Start).BeginInit();
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
            textBoxIracingStatus.Location = new Point(24, 483);
            textBoxIracingStatus.Name = "textBoxIracingStatus";
            textBoxIracingStatus.ReadOnly = true;
            textBoxIracingStatus.Size = new Size(260, 23);
            textBoxIracingStatus.TabIndex = 6;
            textBoxIracingStatus.Text = "Iracing Not Connect";
            // 
            // labelGForce
            // 
            labelGForce.AutoSize = true;
            labelGForce.Location = new Point(16, 321);
            labelGForce.Name = "labelGForce";
            labelGForce.Size = new Size(76, 15);
            labelGForce.TabIndex = 7;
            labelGForce.Text = "G-Force: 0.00";
            // 
            // checkBoxTest
            // 
            checkBoxTest.AutoSize = true;
            checkBoxTest.Location = new Point(146, 102);
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
            numericUpDownCurveAmount.Location = new Point(213, 54);
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
            pictureBoxCurveGraph.Location = new Point(16, 163);
            pictureBoxCurveGraph.Name = "pictureBoxCurveGraph";
            pictureBoxCurveGraph.Size = new Size(260, 117);
            pictureBoxCurveGraph.TabIndex = 13;
            pictureBoxCurveGraph.TabStop = false;
            // 
            // numericUpDownMaxPower
            // 
            numericUpDownMaxPower.Location = new Point(213, 83);
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
            labelMaxPower.Location = new Point(16, 85);
            labelMaxPower.Name = "labelMaxPower";
            labelMaxPower.Size = new Size(70, 15);
            labelMaxPower.TabIndex = 15;
            labelMaxPower.Text = "Max Output";
            // 
            // labelXAxis
            // 
            labelXAxis.AutoSize = true;
            labelXAxis.Location = new Point(24, 509);
            labelXAxis.Name = "labelXAxis";
            labelXAxis.Size = new Size(80, 15);
            labelXAxis.TabIndex = 16;
            labelXAxis.Text = "Input G-Force";
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Location = new Point(115, 283);
            label1.Name = "label1";
            label1.Size = new Size(49, 15);
            label1.TabIndex = 17;
            label1.Text = "GForces";
            // 
            // numericUpDownGForceToBelt
            // 
            numericUpDownGForceToBelt.DecimalPlaces = 2;
            numericUpDownGForceToBelt.Increment = new decimal(new int[] { 5, 0, 0, 131072 });
            numericUpDownGForceToBelt.Location = new Point(213, 114);
            numericUpDownGForceToBelt.Maximum = new decimal(new int[] { 7, 0, 0, 0 });
            numericUpDownGForceToBelt.Minimum = new decimal(new int[] { 5, 0, 0, 65536 });
            numericUpDownGForceToBelt.Name = "numericUpDownGForceToBelt";
            numericUpDownGForceToBelt.Size = new Size(60, 23);
            numericUpDownGForceToBelt.TabIndex = 17;
            numericUpDownGForceToBelt.Value = new decimal(new int[] { 5, 0, 0, 65536 });
            numericUpDownGForceToBelt.ValueChanged += numericUpDownGForceToBelt_ValueChanged_1;
            // 
            // labelGForceToBelt
            // 
            labelGForceToBelt.AutoSize = true;
            labelGForceToBelt.Location = new Point(16, 114);
            labelGForceToBelt.Name = "labelGForceToBelt";
            labelGForceToBelt.Size = new Size(83, 15);
            labelGForceToBelt.TabIndex = 18;
            labelGForceToBelt.Text = "GForce To Belt";
            // 
            // labelAnalogValue
            // 
            labelAnalogValue.AutoSize = true;
            labelAnalogValue.Location = new Point(16, 142);
            labelAnalogValue.Name = "labelAnalogValue";
            labelAnalogValue.Size = new Size(66, 15);
            labelAnalogValue.TabIndex = 19;
            labelAnalogValue.Text = "Analog: ---";
            // 
            // labelTargetValue
            // 
            labelTargetValue.AutoSize = true;
            labelTargetValue.Location = new Point(104, 142);
            labelTargetValue.Name = "labelTargetValue";
            labelTargetValue.Size = new Size(61, 15);
            labelTargetValue.TabIndex = 20;
            labelTargetValue.Text = "Target: ---";
            // 
            // labelDistanceValue
            // 
            labelDistanceValue.AutoSize = true;
            labelDistanceValue.Location = new Point(204, 142);
            labelDistanceValue.Name = "labelDistanceValue";
            labelDistanceValue.Size = new Size(73, 15);
            labelDistanceValue.TabIndex = 21;
            labelDistanceValue.Text = "Distance: ---";
            // 
            // labelMaxGForce
            // 
            labelMaxGForce.AutoSize = true;
            labelMaxGForce.Location = new Point(160, 321);
            labelMaxGForce.Name = "labelMaxGForce";
            labelMaxGForce.Size = new Size(101, 15);
            labelMaxGForce.TabIndex = 22;
            labelMaxGForce.Text = "Max G-Force: 0.00";
            // 
            // gb_Car_Settings
            // 
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
            gb_Car_Settings.Location = new Point(12, 76);
            gb_Car_Settings.Name = "gb_Car_Settings";
            gb_Car_Settings.Size = new Size(289, 347);
            gb_Car_Settings.TabIndex = 23;
            gb_Car_Settings.TabStop = false;
            gb_Car_Settings.Text = "Car Settings";
            // 
            // lb_carName
            // 
            lb_carName.AutoSize = true;
            lb_carName.Location = new Point(16, 27);
            lb_carName.Name = "lb_carName";
            lb_carName.Size = new Size(60, 15);
            lb_carName.TabIndex = 23;
            lb_carName.Text = "Car Name";
            // 
            // labelCurveAmount
            // 
            labelCurveAmount.AutoSize = true;
            labelCurveAmount.Location = new Point(16, 56);
            labelCurveAmount.Name = "labelCurveAmount";
            labelCurveAmount.Size = new Size(85, 15);
            labelCurveAmount.TabIndex = 12;
            labelCurveAmount.Text = "Curve Amount";
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
            groupBox1.Location = new Point(26, 542);
            groupBox1.Name = "groupBox1";
            groupBox1.Size = new Size(263, 196);
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
            cb_duelMotors.Text = "Duel Motors";
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
            bnt_Apply.Location = new Point(175, 167);
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
            lblSettingsSaved.Location = new Point(146, 145);
            lblSettingsSaved.Name = "lblSettingsSaved";
            lblSettingsSaved.Size = new Size(109, 19);
            lblSettingsSaved.TabIndex = 33;
            lblSettingsSaved.Text = "Settings saved.";
            lblSettingsSaved.Visible = false;
            // 
            // Form1
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(314, 750);
            Controls.Add(groupBox1);
            Controls.Add(gb_Car_Settings);
            Controls.Add(textBoxIracingStatus);
            Controls.Add(buttonConnect);
            Controls.Add(labelStatus);
            Controls.Add(labelXAxis);
            Name = "Form1";
            Text = "Belt Tention Test";
            ((ISupportInitialize)numericUpDownTarget).EndInit();
            ((ISupportInitialize)numericUpDownCurveAmount).EndInit();
            ((ISupportInitialize)pictureBoxCurveGraph).EndInit();
            ((ISupportInitialize)numericUpDownMaxPower).EndInit();
            ((ISupportInitialize)numericUpDownGForceToBelt).EndInit();
            gb_Car_Settings.ResumeLayout(false);
            gb_Car_Settings.PerformLayout();
            groupBox1.ResumeLayout(false);
            groupBox1.PerformLayout();
            ((ISupportInitialize)nud_Motor_End).EndInit();
            ((ISupportInitialize)nud_Motor_Start).EndInit();
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
    }
}