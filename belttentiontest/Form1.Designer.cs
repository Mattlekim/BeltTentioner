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
        private NumericUpDown numericUpDownTarget;
        private Button buttonConnect;
        private TextBox textBoxIracingStatus;
        private Label labelGForce;
        private CheckBox checkBoxTest;
        private NumericUpDown numericUpDownBeltStrength;
        private Label labelBeltStrength;
        private NumericUpDown numericUpDownCurveAmount;
        private Label labelCurveAmount;
        private PictureBox pictureBoxCurveGraph;
        private NumericUpDown numericUpDownMaxPower;
        private Label labelMaxPower;

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
            numericUpDownTarget = new NumericUpDown();
            buttonConnect = new Button();
            textBoxIracingStatus = new TextBox();
            labelGForce = new Label();
            checkBoxTest = new CheckBox();
            numericUpDownBeltStrength = new NumericUpDown();
            labelBeltStrength = new Label();
            numericUpDownCurveAmount = new NumericUpDown();
            labelCurveAmount = new Label();
            pictureBoxCurveGraph = new PictureBox();
            numericUpDownMaxPower = new NumericUpDown();
            labelMaxPower = new Label();
            ((ISupportInitialize)numericUpDownTarget).BeginInit();
            ((ISupportInitialize)numericUpDownBeltStrength).BeginInit();
            ((ISupportInitialize)numericUpDownCurveAmount).BeginInit();
            ((ISupportInitialize)pictureBoxCurveGraph).BeginInit();
            ((ISupportInitialize)numericUpDownMaxPower).BeginInit();
            SuspendLayout();
            // 
            // labelStatus
            // 
            labelStatus.AutoSize = true;
            labelStatus.ForeColor = Color.Red;
            labelStatus.Location = new Point(12, 242);
            labelStatus.Name = "labelStatus";
            labelStatus.Size = new Size(118, 15);
            labelStatus.TabIndex = 1;
            labelStatus.Text = "Seatbel Not Detected";
            labelStatus.Click += labelStatus_Click;
            // 
            // numericUpDownTarget
            // 
            numericUpDownTarget.Location = new Point(12, 14);
            numericUpDownTarget.Maximum = new decimal(new int[] { 1023, 0, 0, 0 });
            numericUpDownTarget.Name = "numericUpDownTarget";
            numericUpDownTarget.Size = new Size(120, 23);
            numericUpDownTarget.TabIndex = 2;
            numericUpDownTarget.Value = new decimal(new int[] { 512, 0, 0, 0 });
            numericUpDownTarget.ValueChanged += numericUpDownTarget_ValueChanged;
            // 
            // buttonConnect
            // 
            buttonConnect.Location = new Point(197, 10);
            buttonConnect.Name = "buttonConnect";
            buttonConnect.Size = new Size(75, 23);
            buttonConnect.TabIndex = 3;
            buttonConnect.Text = "Connect";
            buttonConnect.UseVisualStyleBackColor = true;
            buttonConnect.Click += buttonConnect_Click;
            // 
            // textBoxIracingStatus
            // 
            textBoxIracingStatus.Location = new Point(12, 212);
            textBoxIracingStatus.Name = "textBoxIracingStatus";
            textBoxIracingStatus.ReadOnly = true;
            textBoxIracingStatus.Size = new Size(260, 23);
            textBoxIracingStatus.TabIndex = 6;
            textBoxIracingStatus.Text = "not connect";
            // 
            // labelGForce
            // 
            labelGForce.AutoSize = true;
            labelGForce.Location = new Point(12, 272);
            labelGForce.Name = "labelGForce";
            labelGForce.Size = new Size(76, 15);
            labelGForce.TabIndex = 7;
            labelGForce.Text = "G-Force: 0.00";
            // 
            // checkBoxTest
            // 
            checkBoxTest.AutoSize = true;
            checkBoxTest.Location = new Point(140, 14);
            checkBoxTest.Name = "checkBoxTest";
            checkBoxTest.Size = new Size(47, 19);
            checkBoxTest.TabIndex = 8;
            checkBoxTest.Text = "Test";
            checkBoxTest.UseVisualStyleBackColor = true;
            checkBoxTest.CheckedChanged += checkBoxTest_CheckedChanged;
            // 
            // numericUpDownBeltStrength
            // 
            numericUpDownBeltStrength.Location = new Point(100, 300);
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
            labelBeltStrength.Location = new Point(12, 302);
            labelBeltStrength.Name = "labelBeltStrength";
            labelBeltStrength.Size = new Size(75, 15);
            labelBeltStrength.TabIndex = 9;
            labelBeltStrength.Text = "Belt Strength";
            // 
            // numericUpDownCurveAmount
            // 
            numericUpDownCurveAmount.DecimalPlaces = 2;
            numericUpDownCurveAmount.Increment = new decimal(new int[] { 5, 0, 0, 131072 });
            numericUpDownCurveAmount.Location = new Point(180, 39);
            numericUpDownCurveAmount.Maximum = new decimal(new int[] { 50, 0, 0, 65536 });
            numericUpDownCurveAmount.Minimum = new decimal(new int[] { 1, 0, 0, 65536 });
            numericUpDownCurveAmount.Name = "numericUpDownCurveAmount";
            numericUpDownCurveAmount.Size = new Size(60, 23);
            numericUpDownCurveAmount.TabIndex = 11;
            numericUpDownCurveAmount.Value = new decimal(new int[] { 100, 0, 0, 131072 });
            numericUpDownCurveAmount.ValueChanged += numericUpDownCurveAmount_ValueChanged;
            // 
            // labelCurveAmount
            // 
            labelCurveAmount.AutoSize = true;
            labelCurveAmount.Location = new Point(12, 41);
            labelCurveAmount.Name = "labelCurveAmount";
            labelCurveAmount.Size = new Size(85, 15);
            labelCurveAmount.TabIndex = 12;
            labelCurveAmount.Text = "Curve Amount";
            // 
            // pictureBoxCurveGraph
            // 
            pictureBoxCurveGraph.Location = new Point(12, 97);
            pictureBoxCurveGraph.Name = "pictureBoxCurveGraph";
            pictureBoxCurveGraph.Size = new Size(228, 109);
            pictureBoxCurveGraph.TabIndex = 13;
            pictureBoxCurveGraph.TabStop = false;
            // 
            // numericUpDownMaxPower
            // 
            numericUpDownMaxPower.Location = new Point(180, 68);
            numericUpDownMaxPower.Maximum = new decimal(new int[] { 1023, 0, 0, 0 });
            numericUpDownMaxPower.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            numericUpDownMaxPower.Name = "numericUpDownMaxPower";
            numericUpDownMaxPower.Size = new Size(60, 23);
            numericUpDownMaxPower.TabIndex = 14;
            numericUpDownMaxPower.Value = new decimal(new int[] { 1023, 0, 0, 0 });
            numericUpDownMaxPower.ValueChanged += numericUpDownMaxPower_ValueChanged;
            // 
            // labelMaxPower
            // 
            labelMaxPower.AutoSize = true;
            labelMaxPower.Location = new Point(12, 70);
            labelMaxPower.Name = "labelMaxPower";
            labelMaxPower.Size = new Size(65, 15);
            labelMaxPower.TabIndex = 15;
            labelMaxPower.Text = "Max Power";
            // 
            // Form1
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(284, 341);
            Controls.Add(numericUpDownMaxPower);
            Controls.Add(labelMaxPower);
            Controls.Add(pictureBoxCurveGraph);
            Controls.Add(labelCurveAmount);
            Controls.Add(numericUpDownCurveAmount);
            Controls.Add(labelBeltStrength);
            Controls.Add(numericUpDownBeltStrength);
            Controls.Add(checkBoxTest);
            Controls.Add(labelGForce);
            Controls.Add(textBoxIracingStatus);
            Controls.Add(buttonConnect);
            Controls.Add(numericUpDownTarget);
            Controls.Add(labelStatus);
            Name = "Form1";
            Text = "Belt Tention Test";
            ((ISupportInitialize)numericUpDownTarget).EndInit();
            ((ISupportInitialize)numericUpDownBeltStrength).EndInit();
            ((ISupportInitialize)numericUpDownCurveAmount).EndInit();
            ((ISupportInitialize)pictureBoxCurveGraph).EndInit();
            ((ISupportInitialize)numericUpDownMaxPower).EndInit();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion
    }
}