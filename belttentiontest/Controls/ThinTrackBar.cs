using System;
using System.Drawing;
using System.Windows.Forms;

namespace belttentiontest.Controls
{
    public class ThinTrackBar : Control
    {
        public float Minimum { get; set; } = 1;
        public float Maximum { get; set; } = 100;

        private float _value = 1;
        public float Value
        {
            get => _value;
            set
            {
                float clamped = Math.Max(Minimum, Math.Min(Maximum, value));
                if (clamped == _value) return;

                _value = clamped;
                Invalidate();
                ValueChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public event EventHandler ValueChanged;

        public Color TrackColor { get; set; } = Color.Gray;
        public Color FillColor { get; set; } = Color.DodgerBlue;
        public Color ThumbColor { get; set; } = Color.White;

        private bool dragging = false;

        protected override Size DefaultSize
        {
            get { return new Size(100, 20); }
        }


        public ThinTrackBar()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw |
                     ControlStyles.UserPaint, true);

            Height = 20;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            int trackY = Height / 2 - 2;
            Rectangle trackRect = new Rectangle(0, trackY, Width, 4);

            // Background track
            using (var b = new SolidBrush(TrackColor))
                g.FillRectangle(b, trackRect);

            // Filled portion
            float percent = (float)(Value - Minimum) / (Maximum - Minimum);
            int fillWidth = (int)(percent * Width);

            using (var b = new SolidBrush(FillColor))
                g.FillRectangle(b, new Rectangle(0, trackY, fillWidth, 4));

            // Thumb

            int thumbpos = (int)(percent * (Width - 13  ));
            int thumbX = fillWidth - 6;
            Rectangle thumbRect = new Rectangle(thumbpos, trackY - 4, 12, 12);

            using (var b = new SolidBrush(ThumbColor))
                g.FillEllipse(b, thumbRect);

            using (var p = new Pen(Color.Black, 1))
                g.DrawEllipse(p, thumbRect);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            dragging = true;
            SetValueFromMouse(e.X);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (dragging)
                SetValueFromMouse(e.X);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            dragging = false;
        }

        private void SetValueFromMouse(int mouseX)
        {
            if (Width <= 0) return;

            float percent = Math.Max(0f, Math.Min(1f, (float)mouseX / Width));
            Value = Minimum + percent * (Maximum - Minimum);
        }

        // 🔁 Binding helper (restored)
        public static void Bind(ThinTrackBar trackBar, NumericUpDown numeric)
        {
            // Match ranges
            numeric.Minimum = (int)trackBar.Minimum;
            numeric.Maximum = (int)trackBar.Maximum;

            // Initial sync
            trackBar.Value = (int)numeric.Value;

            bool updating = false;

            trackBar.ValueChanged += (s, e) =>
            {
                if (updating) return;
                updating = true;
                numeric.Value = Math.Max(numeric.Minimum,
                                  Math.Min(numeric.Maximum, (decimal)trackBar.Value));
                updating = false;
            };

            numeric.ValueChanged += (s, e) =>
            {
                if (updating) return;
                updating = true;
                trackBar.Value = (int)numeric.Value;
                updating = false;
            };
        }
    }
}