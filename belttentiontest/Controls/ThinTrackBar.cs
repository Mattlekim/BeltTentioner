using System;
using System.Drawing;
using System.Windows.Forms;

namespace belttentiontest.Controls
{
    /// <summary>
    /// A slim horizontal slider with an integrated dark-themed value text box on its right.
    /// Dragging the slider or editing the box both update the shared Value and fire ValueChanged.
    /// DecimalPlaces controls how many decimal digits the box shows (0 = integer).
    /// </summary>
    public class ThinTrackBar : Control
    {
        // ?? colours ???????????????????????????????????????????????????????
        private static readonly Color BoxBack    = Color.FromArgb(28, 28, 45);
        private static readonly Color BoxFore    = Color.FromArgb(160, 160, 190);
        private static readonly Color DisabledFg = Color.FromArgb(70, 70, 90);

        // ?? layout ????????????????????????????????????????????????????????
        private const int BoxWidth = 46;
        private const int BoxGap   = 4;

        // ?? child text box ????????????????????????????????????????????????
        private readonly TextBox _box;
        private bool _updatingBox;

        // ?? range / value ?????????????????????????????????????????????????
        public float Minimum { get; set; } = 1f;
        public float Maximum { get; set; } = 100f;

        private int _decimalPlaces = 0;
        public int DecimalPlaces
        {
            get => _decimalPlaces;
            set { _decimalPlaces = Math.Max(0, value); RefreshBox(); }
        }

        private float _value = 1f;
        public float Value
        {
            get => _value;
            set
            {
                float clamped = Math.Max(Minimum, Math.Min(Maximum, value));
                if (clamped == _value) return;
                _value = clamped;
                Invalidate();
                RefreshBox();
                ValueChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public event EventHandler ValueChanged;

        // ?? appearance ????????????????????????????????????????????????????
        public Color TrackColor { get; set; } = Color.FromArgb(55, 55, 80);
        public Color FillColor  { get; set; } = Color.DodgerBlue;
        public Color ThumbColor { get; set; } = Color.White;

        private bool _dragging;

        protected override Size DefaultSize => new Size(150, 20);

        // ?? constructor ???????????????????????????????????????????????????
        public ThinTrackBar()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw |
                     ControlStyles.UserPaint, true);
            Height = 20;

            _box = new TextBox
            {
                BackColor   = BoxBack,
                ForeColor   = BoxFore,
                BorderStyle = BorderStyle.FixedSingle,
                Font        = new Font("Segoe UI", 8.5f),
                TextAlign   = HorizontalAlignment.Center,
                TabStop     = false,
            };
            _box.KeyDown   += Box_KeyDown;
            _box.LostFocus += Box_LostFocus;
            Controls.Add(_box);
            PositionBox();
            RefreshBox();
        }

        // ?? layout ????????????????????????????????????????????????????????
        private int TrackWidth => Math.Max(0, Width - BoxWidth - BoxGap);

        private void PositionBox()
        {
            int bh = Math.Min(Height, 20);
            _box.SetBounds(TrackWidth + BoxGap, (Height - bh) / 2, BoxWidth, bh);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            PositionBox();
        }

        // ?? painting ??????????????????????????????????????????????????????
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            int tw   = TrackWidth;
            int midY = Height / 2;

            // Track background
            using (var b = new SolidBrush(TrackColor))
                g.FillRectangle(b, 0, midY - 2, tw, 4);

            // Filled portion
            float range   = Maximum - Minimum;
            float percent = range == 0f ? 0f : (_value - Minimum) / range;
            int   fill    = (int)(percent * tw);

            using (var b = new SolidBrush(Enabled ? FillColor : Color.FromArgb(50, 50, 70)))
                g.FillRectangle(b, 0, midY - 2, fill, 4);

            // Thumb
            int thumbX    = (int)(percent * Math.Max(0, tw - 12));
            var thumbRect = new Rectangle(thumbX, midY - 6, 12, 12);
            using (var b = new SolidBrush(Enabled ? ThumbColor : Color.FromArgb(80, 80, 100)))
                g.FillEllipse(b, thumbRect);
            using (var p = new Pen(Color.FromArgb(40, 40, 60), 1))
                g.DrawEllipse(p, thumbRect);
        }

        // ?? mouse interaction ?????????????????????????????????????????????
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (!Enabled) return;
            _dragging = true;
            SetValueFromMouse(e.X);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_dragging) SetValueFromMouse(e.X);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _dragging = false;
        }

        private void SetValueFromMouse(int mouseX)
        {
            int tw = TrackWidth;
            if (tw <= 0) return;
            float pct = Math.Max(0f, Math.Min(1f, (float)mouseX / tw));
            Value = Minimum + pct * (Maximum - Minimum);
        }

        // ?? text box sync ?????????????????????????????????????????????????
        private void RefreshBox()
        {
            if (_updatingBox) return;
            _updatingBox = true;
            _box.Text = _value.ToString(_decimalPlaces > 0 ? "F" + _decimalPlaces : "F0");
            _updatingBox = false;
        }

        private void CommitBox()
        {
            if (float.TryParse(_box.Text,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.CurrentCulture,
                    out float parsed))
                Value = parsed;
            else
                RefreshBox();   // revert invalid input
        }

        private void Box_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)  { CommitBox();  e.SuppressKeyPress = true; }
            if (e.KeyCode == Keys.Escape) { RefreshBox(); e.SuppressKeyPress = true; }
        }

        private void Box_LostFocus(object sender, EventArgs e) => CommitBox();

        // ?? enabled state ?????????????????????????????????????????????????
        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            _box.BackColor = BoxBack;
            _box.ForeColor = Enabled ? BoxFore : DisabledFg;
            Invalidate();
        }
    }
}
