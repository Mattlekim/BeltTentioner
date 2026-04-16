using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace belttentiontest.Controls
{
    public class OnOffStatusControl : Control
    {
        private bool _isOn;
        private string _statusText = "Status";

        public event EventHandler? StateChanged;

        public OnOffStatusControl()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            DoubleBuffered = true;
            BackColor = Color.FromArgb(18, 18, 30);
            ForeColor = Color.FromArgb(160, 160, 190);
            Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            Size = new Size(160, 24);
        }

        [Category("Appearance")]
        [Description("Text shown in the control")]
        public string StatusText
        {
            get => _statusText;
            set
            {
                if (value == _statusText) return;
                _statusText = value ?? string.Empty;
                Invalidate();
            }
        }

        [Category("Behavior")]
        [Description("Indicates whether control is On (true) or Off (false)")]
        public bool IsOn
        {
            get => _isOn;
            set
            {
                if (_isOn == value) return;
                _isOn = value;
                Invalidate();
                StateChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        [Category("Appearance")]
        [Description("Color of the ON indicator")]
        public Color OnColor { get; set; } = Color.LimeGreen;

        [Category("Appearance")]
        [Description("Color of the OFF indicator")]
        public Color OffColor { get; set; } = Color.Red;

        [Category("Appearance")]
        [Description("Color of the status text")]
        public Color TextColor { get; set; } = Color.FromArgb(160, 160, 190);

        protected override void OnPaint(PaintEventArgs pe)
        {
            base.OnPaint(pe);
            var g = pe.Graphics;
            g.Clear(BackColor);

            // compute circle size and positions
            int padding = 4;
            int circleDiameter = Math.Max(8, Height - padding * 2);
            int circleX = Width - padding - circleDiameter;
            int circleY = (Height - circleDiameter) / 2;

            // draw text clipped to available area
            var textAreaWidth = Math.Max(0, circleX - padding - 2);
            var textRect = new Rectangle(padding, 0, textAreaWidth, Height);

            Color drawTextColor = Enabled ? TextColor : SystemColors.GrayText;
            using (var textBrush = new SolidBrush(drawTextColor))
            using (var sf = new StringFormat { LineAlignment = StringAlignment.Center, Alignment = StringAlignment.Near })
            {
                g.DrawString(StatusText, Font, textBrush, textRect, sf);
            }

            // choose indicator color based on enabled state
            Color indicatorColor = IsOn ? OnColor : OffColor;
            if (!Enabled)
            {
                // desaturate / grey out when disabled
                indicatorColor = SystemColors.GrayText;
            }

            // draw indicator circle with border
            using (var indicatorBrush = new SolidBrush(indicatorColor))
            using (var pen = new Pen(Color.FromArgb(120, 0, 0, 0)))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.FillEllipse(indicatorBrush, circleX, circleY, circleDiameter, circleDiameter);
                g.DrawEllipse(pen, circleX, circleY, circleDiameter, circleDiameter);
            }
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            // only toggle when enabled
            if (Enabled)
            {
                IsOn = !IsOn;
            }
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            Invalidate();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Invalidate();
        }

        public override string Text
        {
            get => StatusText;
            set => StatusText = value;
        }
    }
}
