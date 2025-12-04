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
        private NumericUpDown numericUpDownBeltStrength;
        private Label labelBeltStrength;
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
            numericUpDownBeltStrength = new NumericUpDown();
            labelBeltStrength = new Label();
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
            ((ISupportInitialize)numericUpDownTarget).BeginInit();
            ((ISupportInitialize)numericUpDownBeltStrength).BeginInit();
            ((ISupportInitialize)numericUpDownCurveAmount).BeginInit();
            ((ISupportInitialize)pictureBoxCurveGraph).BeginInit();
            ((ISupportInitialize)numericUpDownMaxPower).BeginInit();
            ((ISupportInitialize)numericUpDownGForceToBelt).BeginInit();
            gb_Car_Settings.SuspendLayout();
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
            numericUpDownTarget.Location = new Point(12, 37);
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
            textBoxIracingStatus.Text = "not connect";
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
            checkBoxTest.Location = new Point(140, 37);
            checkBoxTest.Name = "checkBoxTest";
            checkBoxTest.Size = new Size(47, 19);
            checkBoxTest.TabIndex = 8;
            checkBoxTest.Text = "Test";
            checkBoxTest.UseVisualStyleBackColor = true;
            checkBoxTest.CheckedChanged += checkBoxTest_CheckedChanged;
            // 
            // numericUpDownBeltStrength
            // 
            numericUpDownBeltStrength.Location = new Point(112, 575);
            numericUpDownBeltStrength.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            numericUpDownBeltStrength.Name = "numericUpDownBeltStrength";
            numericUpDownBeltStrength.Size = new Size(60, 23);
            numericUpDownBeltStrength.TabIndex = 10;
            numericUpDownBeltStrength.Value = new decimal(new int[] { 10, 0, 0, 0 });
            numericUpDownBeltStrength.ValueChanged += numericUpDownBeltStrength_ValueChanged;
            // 
            // labelBeltStrength
            // 
            labelBeltStrength.AutoSize = true;
            labelBeltStrength.Location = new Point(24, 577);
            labelBeltStrength.Name = "labelBeltStrength";
            labelBeltStrength.Size = new Size(81, 15);
            labelBeltStrength.TabIndex = 9;
            labelBeltStrength.Text = "Motor Strenth";
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
            // Form1
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(314, 621);
            Controls.Add(gb_Car_Settings);
            Controls.Add(labelBeltStrength);
            Controls.Add(textBoxIracingStatus);
            Controls.Add(numericUpDownBeltStrength);
            Controls.Add(checkBoxTest);
            Controls.Add(buttonConnect);
            Controls.Add(numericUpDownTarget);
            Controls.Add(labelStatus);
            Controls.Add(labelXAxis);
            Name = "Form1";
            Text = "Belt Tention Test";
            ((ISupportInitialize)numericUpDownTarget).EndInit();
            ((ISupportInitialize)numericUpDownBeltStrength).EndInit();
            ((ISupportInitialize)numericUpDownCurveAmount).EndInit();
            ((ISupportInitialize)pictureBoxCurveGraph).EndInit();
            ((ISupportInitialize)numericUpDownMaxPower).EndInit();
            ((ISupportInitialize)numericUpDownGForceToBelt).EndInit();
            gb_Car_Settings.ResumeLayout(false);
            gb_Car_Settings.PerformLayout();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Label label1;
        private GroupBox gb_Car_Settings;
        private Label lb_carName;
        private Label labelCurveAmount;
    }
}