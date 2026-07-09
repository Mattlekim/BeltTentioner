using System;
using Microsoft.Xna.Framework.Graphics;
using XnaColor = Microsoft.Xna.Framework.Color;
using XnaRectangle = Microsoft.Xna.Framework.Rectangle;
using XnaVector2 = Microsoft.Xna.Framework.Vector2;

namespace BeltTensionTest.WPF.Services.Overlays
{
    /// <summary>
    /// In-VR slider: name on the left, value bar with numeric readout on the
    /// right. Adjust with <see cref="Increase"/>/<see cref="Decrease"/> (wire
    /// these to <see cref="OverlayNavigation"/> actions in the hosting overlay).
    /// </summary>
    public sealed class MonoXRSliderControl : MonoXRControl
    {
        private float _value;

        public MonoXRSliderControl(string name, float min, float max, float value,
                                   float step = 1f, string format = "0")
        {
            if (max <= min) throw new ArgumentException("max must be greater than min", nameof(max));
            Name = name;
            Minimum = min;
            Maximum = max;
            Step = step;
            Format = format;
            _value = Math.Clamp(value, min, max);
        }

        public string Name { get; set; }
        public float Minimum { get; }
        public float Maximum { get; }
        public float Step { get; set; }

        /// <summary>Numeric format for the value readout (e.g. "0", "0.0").</summary>
        public string Format { get; set; }

        /// <summary>
        /// Fill color of the value bar, mirroring the WPF slider's per-axis
        /// FillBrush (e.g. blue for Surge, green for Sway). Null falls back to
        /// the shared accent color.
        /// </summary>
        public XnaColor? FillColor { get; set; }

        // WPF slider template colors (Resources/Styles.xaml).
        private static readonly XnaColor TrackEmpty = new XnaColor(0x37, 0x37, 0x4E);
        private static readonly XnaColor ThumbFill = XnaColor.White;
        private static readonly XnaColor ThumbStroke = new XnaColor(0x28, 0x28, 0x40);

        /// <summary>Raised whenever <see cref="Value"/> actually changes.</summary>
        public event Action<float>? ValueChanged;

        public float Value
        {
            get => _value;
            set
            {
                float clamped = Math.Clamp(value, Minimum, Maximum);
                if (clamped == _value) return;
                _value = clamped;
                ValueChanged?.Invoke(_value);
            }
        }

        public override void Increase() { if (IsEnabled) Value += Step; }
        public override void Decrease() { if (IsEnabled) Value -= Step; }

        public override void Draw(SpriteBatch spriteBatch, SpriteFont font, Texture2D white)
        {
            DrawSelectionBackground(spriteBatch, white);

            int pad = 12;
            int textY = Bounds.Y + (Bounds.Height - font.LineSpacing) / 2;
            spriteBatch.DrawString(font, Name, new XnaVector2(Bounds.X + pad, textY), TextColor);

            // Value bar occupies the right half, leaving room for the readout.
            // Same anatomy as the WPF slider template: thin track, colored
            // fill up to the value, white round-ish thumb with a dark stroke.
            string readout = _value.ToString(Format);
            int readoutW = (int)Math.Ceiling(font.MeasureString(readout).X);
            int barX = Bounds.X + Bounds.Width / 2;
            int barW = Bounds.X + Bounds.Width - barX - readoutW - pad * 2;
            int barH = 6;
            int barY = Bounds.Y + (Bounds.Height - barH) / 2;
            if (barW > 0)
            {
                float t = Math.Clamp((_value - Minimum) / (Maximum - Minimum), 0f, 1f);
                var fill = !IsEnabled ? DisabledText : (FillColor ?? AccentColor);
                spriteBatch.Draw(white, new XnaRectangle(barX, barY, barW, barH), TrackEmpty);
                spriteBatch.Draw(white, new XnaRectangle(barX, barY, (int)(barW * t), barH), fill);

                // Thumb centered on the value position, clamped to the track.
                int thumbSize = 18;
                int thumbX = Math.Clamp(barX + (int)(barW * t) - thumbSize / 2, barX, barX + barW - thumbSize);
                int thumbY = Bounds.Y + (Bounds.Height - thumbSize) / 2;
                spriteBatch.Draw(white, new XnaRectangle(thumbX, thumbY, thumbSize, thumbSize), ThumbStroke);
                spriteBatch.Draw(white, new XnaRectangle(thumbX + 2, thumbY + 2, thumbSize - 4, thumbSize - 4),
                                 IsEnabled ? ThumbFill : NormalText);
            }

            spriteBatch.DrawString(font, readout,
                                   new XnaVector2(Bounds.X + Bounds.Width - readoutW - pad, textY),
                                   TextColor);
        }
    }
}
