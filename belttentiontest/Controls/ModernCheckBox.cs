using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace belttentiontest.Controls
{
    /// <summary>
    /// A custom-drawn toggle-switch checkbox that matches the application's dark theme.
    /// The switch slides left/right and animates via a Timer when toggled.
    /// </summary>
    public class ModernCheckBox : Control
    {
        // --- layout constants ---
        private const int TrackW = 34;
        private const int TrackH = 16;
        private const int ThumbDiam = 12;
        private const int TextGap = 6;

        // --- colours ---
        private static readonly Color TrackOn = Color.FromArgb(80, 200, 120);
        private static readonly Color TrackOff = Color.FromArgb(55, 55, 80);
        private static readonly Color ThumbColor = Color.White;
        private static readonly Color DisabledTrack = Color.FromArgb(45, 45, 65);
        private static readonly Color DisabledThumb = Color.FromArgb(90, 90, 110);

        // --- animation ---
        private readonly System.Windows.Forms.Timer _animTimer;
        private float _thumbX;        // current animated x of thumb centre
        private float _thumbXTarget;  // where the thumb should end up

        private bool _checked;

        public event EventHandler? CheckedChanged;

        public ModernCheckBox()
        {
            SetStyle(
                ControlStyles.UserPaint |
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.SupportsTransparentBackColor,
                true);

            DoubleBuffered = true;
            BackColor = Color.Transparent;
            ForeColor = Color.FromArgb(160, 160, 190);
            Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            Cursor = Cursors.Hand;

            // initial thumb position = unchecked (left)
            _thumbX = ThumbXForState(false);
            _thumbXTarget = _thumbX;

            _animTimer = new System.Windows.Forms.Timer { Interval = 15 };
            _animTimer.Tick += AnimTimer_Tick;

            RecalcSize();
        }

        [Category("Behavior")]
        [Description("Whether the control is checked (ON).")]
        [DefaultValue(false)]
        public bool Checked
        {
            get => _checked;
            set
            {
                if (_checked == value) return;
                _checked = value;
                _thumbXTarget = ThumbXForState(_checked);
                _animTimer.Start();
                CheckedChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        // Keep the public API shape matching CheckBox so the designer code needs
        // minimal changes.
        [Browsable(false)]
        public bool AutoSize
        {
            get => false;
            set { /* ignore */ }
        }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            RecalcSize();
            Invalidate();
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            RecalcSize();
            Invalidate();
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            Invalidate();
        }

        private void RecalcSize()
        {
            using var g = CreateGraphics();
            SizeF textSize = string.IsNullOrEmpty(Text)
                ? SizeF.Empty
                : g.MeasureString(Text, Font);

            int w = TrackW + (string.IsNullOrEmpty(Text) ? 0 : TextGap + (int)Math.Ceiling(textSize.Width));
            int h = Math.Max(TrackH, (int)Math.Ceiling(textSize.Height));
            Size = new Size(w + 2, h + 2);
        }

        private float ThumbXForState(bool on)
        {
            int margin = (TrackH - ThumbDiam) / 2;
            float offX = margin + ThumbDiam / 2f;
            float onX = TrackW - margin - ThumbDiam / 2f;
            return on ? onX : offX;
        }

        private void AnimTimer_Tick(object? sender, EventArgs e)
        {
            float diff = _thumbXTarget - _thumbX;
            if (Math.Abs(diff) < 0.5f)
            {
                _thumbX = _thumbXTarget;
                _animTimer.Stop();
            }
            else
            {
                _thumbX += diff * 0.35f;
            }
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs pe)
        {
            var g = pe.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor == Color.Transparent ? Parent?.BackColor ?? Color.FromArgb(18, 18, 30) : BackColor);

            int trackY = (Height - TrackH) / 2;

            // --- track ---
            Color trackColor = Enabled
                ? (Checked ? TrackOn : TrackOff)
                : DisabledTrack;

            using (var trackBrush = new SolidBrush(trackColor))
            {
                FillRoundRect(g, trackBrush, 0, trackY, TrackW, TrackH, TrackH / 2);
            }

            // --- thumb ---
            int margin = (TrackH - ThumbDiam) / 2;
            float tx = _thumbX - ThumbDiam / 2f;
            float ty = trackY + margin;
            Color thumbCol = Enabled ? ThumbColor : DisabledThumb;

            using (var thumbBrush = new SolidBrush(thumbCol))
            {
                g.FillEllipse(thumbBrush, tx, ty, ThumbDiam, ThumbDiam);
            }

            // --- label ---
            if (!string.IsNullOrEmpty(Text))
            {
                Color textCol = Enabled ? ForeColor : SystemColors.GrayText;
                using var textBrush = new SolidBrush(textCol);
                float textY = (Height - Font.GetHeight(g)) / 2f;
                g.DrawString(Text, Font, textBrush, TrackW + TextGap, textY);
            }
        }

        private static void FillRoundRect(Graphics g, Brush brush, float x, float y, float w, float h, float radius)
        {
            using var path = new GraphicsPath();
            path.AddArc(x, y, radius * 2, radius * 2, 180, 90);
            path.AddArc(x + w - radius * 2, y, radius * 2, radius * 2, 270, 90);
            path.AddArc(x + w - radius * 2, y + h - radius * 2, radius * 2, radius * 2, 0, 90);
            path.AddArc(x, y + h - radius * 2, radius * 2, radius * 2, 90, 90);
            path.CloseFigure();
            g.FillPath(brush, path);
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            if (Enabled) Checked = !_checked;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Space && Enabled)
            {
                Checked = !_checked;
                e.Handled = true;
            }
        }

        protected override bool IsInputKey(Keys keyData) =>
            keyData == Keys.Space || base.IsInputKey(keyData);

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _animTimer.Dispose();
            base.Dispose(disposing);
        }
    }
}
