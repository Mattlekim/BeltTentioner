namespace belttentiontest
{
    partial class TestingForm
    {
        private System.ComponentModel.IContainer components = null;

        private System.Windows.Forms.TableLayoutPanel tableLayoutPanel1;
        private System.Windows.Forms.Button _btnSurge;
        private System.Windows.Forms.Button _btnSway;
        private System.Windows.Forms.Button _btnHeave;
        private System.Windows.Forms.Button _btnStop;
        private System.Windows.Forms.Label _lblStatus;
        private System.Windows.Forms.PictureBox _pictureBoxGraph;
        private System.Windows.Forms.PictureBox _pictureBoxMotorGraph;
        private System.Windows.Forms.Panel _panelRotation;
        private System.Windows.Forms.Label _lblPitch;
        private System.Windows.Forms.Label _lblRoll;
        private System.Windows.Forms.CheckBox _cbShowBraking;
        private System.Windows.Forms.CheckBox _cbShowCorn;
        private System.Windows.Forms.CheckBox _cbShowVer;
        private System.Windows.Forms.CheckBox _cbLivePreview;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
                components.Dispose();
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            tableLayoutPanel1 = new TableLayoutPanel();
            _btnSurge = new Button();
            _btnSway = new Button();
            _btnHeave = new Button();
            _btnStop = new Button();
            _cbShowBraking = new CheckBox();
            _cbShowCorn = new CheckBox();
            _cbShowVer = new CheckBox();
            _cbLivePreview = new CheckBox();
            _lblStatus = new Label();
            _pictureBoxGraph = new PictureBox();
            _pictureBoxMotorGraph = new PictureBox();
            _panelRotation = new Panel();
            _lblRoll = new Label();
            _lblPitch = new Label();
            tableLayoutPanel1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)_pictureBoxGraph).BeginInit();
            ((System.ComponentModel.ISupportInitialize)_pictureBoxMotorGraph).BeginInit();
            _panelRotation.SuspendLayout();
            SuspendLayout();
            // 
            // tableLayoutPanel1
            // 
            tableLayoutPanel1.ColumnCount = 4;
            tableLayoutPanel1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
            tableLayoutPanel1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
            tableLayoutPanel1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
            tableLayoutPanel1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
            tableLayoutPanel1.Controls.Add(_btnSurge, 0, 0);
            tableLayoutPanel1.Controls.Add(_btnSway, 1, 0);
            tableLayoutPanel1.Controls.Add(_btnHeave, 2, 0);
            tableLayoutPanel1.Controls.Add(_btnStop, 3, 0);
            tableLayoutPanel1.Controls.Add(_cbShowBraking, 0, 1);
            tableLayoutPanel1.Controls.Add(_cbShowCorn, 1, 1);
            tableLayoutPanel1.Controls.Add(_cbShowVer, 2, 1);
            tableLayoutPanel1.Controls.Add(_cbLivePreview, 3, 1);
            tableLayoutPanel1.Controls.Add(_lblStatus, 0, 2);
            tableLayoutPanel1.Dock = DockStyle.Top;
            tableLayoutPanel1.Location = new Point(0, 0);
            tableLayoutPanel1.Name = "tableLayoutPanel1";
            tableLayoutPanel1.Padding = new Padding(6);
            tableLayoutPanel1.RowCount = 3;
            tableLayoutPanel1.RowStyles.Add(new RowStyle(SizeType.Percent, 60F));
            tableLayoutPanel1.RowStyles.Add(new RowStyle(SizeType.Percent, 25F));
            tableLayoutPanel1.RowStyles.Add(new RowStyle(SizeType.Percent, 15F));
            tableLayoutPanel1.Size = new Size(700, 120);
            tableLayoutPanel1.TabIndex = 1;
            // 
            // _btnSurge
            // 
            _btnSurge.BackColor = Color.SteelBlue;
            _btnSurge.Dock = DockStyle.Fill;
            _btnSurge.FlatStyle = FlatStyle.Flat;
            _btnSurge.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            _btnSurge.ForeColor = Color.White;
            _btnSurge.Location = new Point(10, 10);
            _btnSurge.Margin = new Padding(4);
            _btnSurge.Name = "_btnSurge";
            _btnSurge.Size = new Size(164, 56);
            _btnSurge.TabIndex = 0;
            _btnSurge.Text = "Surge";
            _btnSurge.UseVisualStyleBackColor = false;
            _btnSurge.Click += BtnSurge_Click;
            // 
            // _btnSway
            // 
            _btnSway.BackColor = Color.SeaGreen;
            _btnSway.Dock = DockStyle.Fill;
            _btnSway.FlatStyle = FlatStyle.Flat;
            _btnSway.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            _btnSway.ForeColor = Color.White;
            _btnSway.Location = new Point(182, 10);
            _btnSway.Margin = new Padding(4);
            _btnSway.Name = "_btnSway";
            _btnSway.Size = new Size(164, 56);
            _btnSway.TabIndex = 1;
            _btnSway.Text = "Sway";
            _btnSway.UseVisualStyleBackColor = false;
            _btnSway.Click += BtnSway_Click;
            // 
            // _btnHeave
            // 
            _btnHeave.BackColor = Color.DarkOrange;
            _btnHeave.Dock = DockStyle.Fill;
            _btnHeave.FlatStyle = FlatStyle.Flat;
            _btnHeave.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            _btnHeave.ForeColor = Color.White;
            _btnHeave.Location = new Point(354, 10);
            _btnHeave.Margin = new Padding(4);
            _btnHeave.Name = "_btnHeave";
            _btnHeave.Size = new Size(164, 56);
            _btnHeave.TabIndex = 2;
            _btnHeave.Text = "Heave";
            _btnHeave.UseVisualStyleBackColor = false;
            _btnHeave.Click += BtnHeave_Click;
            // 
            // _btnStop
            // 
            _btnStop.BackColor = Color.Crimson;
            _btnStop.Dock = DockStyle.Fill;
            _btnStop.FlatStyle = FlatStyle.Flat;
            _btnStop.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            _btnStop.ForeColor = Color.White;
            _btnStop.Location = new Point(526, 10);
            _btnStop.Margin = new Padding(4);
            _btnStop.Name = "_btnStop";
            _btnStop.Size = new Size(164, 56);
            _btnStop.TabIndex = 3;
            _btnStop.Text = "Stop";
            _btnStop.UseVisualStyleBackColor = false;
            _btnStop.Click += BtnStop_Click;
            // 
            // _cbShowBraking
            // 
            _cbShowBraking.Checked = true;
            _cbShowBraking.CheckState = CheckState.Checked;
            _cbShowBraking.Dock = DockStyle.Fill;
            _cbShowBraking.Font = new Font("Segoe UI", 8F, FontStyle.Bold);
            _cbShowBraking.ForeColor = Color.Blue;
            _cbShowBraking.Location = new Point(10, 72);
            _cbShowBraking.Margin = new Padding(4, 2, 4, 2);
            _cbShowBraking.Name = "_cbShowBraking";
            _cbShowBraking.Size = new Size(164, 23);
            _cbShowBraking.TabIndex = 4;
            _cbShowBraking.Text = "Surge";
            _cbShowBraking.CheckedChanged += _cbShowBraking_CheckedChanged;
            // 
            // _cbShowCorn
            // 
            _cbShowCorn.Checked = true;
            _cbShowCorn.CheckState = CheckState.Checked;
            _cbShowCorn.Dock = DockStyle.Fill;
            _cbShowCorn.Font = new Font("Segoe UI", 8F, FontStyle.Bold);
            _cbShowCorn.ForeColor = Color.Green;
            _cbShowCorn.Location = new Point(182, 72);
            _cbShowCorn.Margin = new Padding(4, 2, 4, 2);
            _cbShowCorn.Name = "_cbShowCorn";
            _cbShowCorn.Size = new Size(164, 23);
            _cbShowCorn.TabIndex = 5;
            _cbShowCorn.Text = "Sway";
            _cbShowCorn.CheckedChanged += _cbShowCorn_CheckedChanged;
            // 
            // _cbShowVer
            // 
            _cbShowVer.Checked = true;
            _cbShowVer.CheckState = CheckState.Checked;
            _cbShowVer.Dock = DockStyle.Fill;
            _cbShowVer.Font = new Font("Segoe UI", 8F, FontStyle.Bold);
            _cbShowVer.ForeColor = Color.DarkOrange;
            _cbShowVer.Location = new Point(354, 72);
            _cbShowVer.Margin = new Padding(4, 2, 4, 2);
            _cbShowVer.Name = "_cbShowVer";
            _cbShowVer.Size = new Size(164, 23);
            _cbShowVer.TabIndex = 6;
            _cbShowVer.Text = "Heave";
            _cbShowVer.CheckedChanged += _cbShowVer_CheckedChanged;
            // 
            // _cbLivePreview
            // 
            _cbLivePreview.Checked = true;
            _cbLivePreview.CheckState = CheckState.Checked;
            _cbLivePreview.Dock = DockStyle.Fill;
            _cbLivePreview.Font = new Font("Segoe UI", 8F);
            _cbLivePreview.Location = new Point(526, 72);
            _cbLivePreview.Margin = new Padding(4, 2, 4, 2);
            _cbLivePreview.Name = "_cbLivePreview";
            _cbLivePreview.Size = new Size(164, 23);
            _cbLivePreview.TabIndex = 7;
            _cbLivePreview.Text = "Live";
            // 
            // _lblStatus
            // 
            tableLayoutPanel1.SetColumnSpan(_lblStatus, 4);
            _lblStatus.Dock = DockStyle.Fill;
            _lblStatus.Font = new Font("Segoe UI", 9F);
            _lblStatus.Location = new Point(9, 97);
            _lblStatus.Name = "_lblStatus";
            _lblStatus.Size = new Size(682, 17);
            _lblStatus.TabIndex = 8;
            _lblStatus.Text = "Idle";
            _lblStatus.TextAlign = ContentAlignment.MiddleCenter;
            // 
            // _pictureBoxGraph
            // 
            _pictureBoxGraph.BorderStyle = BorderStyle.FixedSingle;
            _pictureBoxGraph.Dock = DockStyle.Fill;
            _pictureBoxGraph.Location = new Point(0, 120);
            _pictureBoxGraph.Name = "_pictureBoxGraph";
            _pictureBoxGraph.Size = new Size(700, 413);
            _pictureBoxGraph.TabIndex = 0;
            _pictureBoxGraph.TabStop = false;
            // 
            // _pictureBoxMotorGraph
            // 
            _pictureBoxMotorGraph.BorderStyle = BorderStyle.FixedSingle;
            _pictureBoxMotorGraph.Dock = DockStyle.Bottom;
            _pictureBoxMotorGraph.Location = new Point(0, 533);
            _pictureBoxMotorGraph.Name = "_pictureBoxMotorGraph";
            _pictureBoxMotorGraph.Size = new Size(700, 180);
            _pictureBoxMotorGraph.TabIndex = 9;
            _pictureBoxMotorGraph.TabStop = false;
            _pictureBoxMotorGraph.SizeChanged += PictureBoxMotorGraph_SizeChanged;
            // 
            // _panelRotation
            // 
            _panelRotation.BackColor = Color.FromArgb(18, 18, 30);
            _panelRotation.Controls.Add(_lblRoll);
            _panelRotation.Controls.Add(_lblPitch);
            _panelRotation.Dock = DockStyle.Bottom;
            _panelRotation.Location = new Point(0, 713);
            _panelRotation.Name = "_panelRotation";
            _panelRotation.Size = new Size(700, 28);
            _panelRotation.TabIndex = 10;
            // 
            // _lblRoll
            // 
            _lblRoll.Dock = DockStyle.Left;
            _lblRoll.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            _lblRoll.ForeColor = Color.FromArgb(255, 180, 80);
            _lblRoll.Location = new Point(180, 0);
            _lblRoll.Name = "_lblRoll";
            _lblRoll.Size = new Size(180, 28);
            _lblRoll.TabIndex = 1;
            _lblRoll.Text = "Roll: 0.00";
            _lblRoll.TextAlign = ContentAlignment.MiddleCenter;
            // 
            // _lblPitch
            // 
            _lblPitch.Dock = DockStyle.Left;
            _lblPitch.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            _lblPitch.ForeColor = Color.FromArgb(180, 140, 255);
            _lblPitch.Location = new Point(0, 0);
            _lblPitch.Name = "_lblPitch";
            _lblPitch.Size = new Size(180, 28);
            _lblPitch.TabIndex = 2;
            _lblPitch.Text = "Pitch: 0.00";
            _lblPitch.TextAlign = ContentAlignment.MiddleCenter;
            // 
            // TestingForm
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(700, 741);
            Controls.Add(_pictureBoxGraph);
            Controls.Add(_pictureBoxMotorGraph);
            Controls.Add(_panelRotation);
            Controls.Add(tableLayoutPanel1);
            MinimumSize = new Size(500, 500);
            Name = "TestingForm";
            StartPosition = FormStartPosition.CenterParent;
            Text = "Belt Tensioner Testing";
            tableLayoutPanel1.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)_pictureBoxGraph).EndInit();
            ((System.ComponentModel.ISupportInitialize)_pictureBoxMotorGraph).EndInit();
            _panelRotation.ResumeLayout(false);
            ResumeLayout(false);
        }
    }
}
