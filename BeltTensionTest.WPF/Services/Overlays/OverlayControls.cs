using System;
using Microsoft.Xna.Framework.Graphics;
using MonoXR.Client;
using XnaColor = Microsoft.Xna.Framework.Color;
using XnaRectangle = Microsoft.Xna.Framework.Rectangle;
using XnaVector2 = Microsoft.Xna.Framework.Vector2;

namespace BeltTensionTest.WPF.Services.Overlays
{
    /// <summary>
    /// A row of color swatches: a label on the left, the palette on the
    /// right, with a ring around the one in use. Clicking a swatch picks it;
    /// navigation Increase/Decrease steps through the palette, so it works
    /// from a wheel button as well as the pointer.
    ///
    /// A fixed palette rather than R/G/B sliders on purpose — picking a color
    /// in VR should be one click, and nine well-separated colors is all a
    /// "which row is my rival" marker needs.
    /// </summary>
    public sealed class OverlayColorRow : MonoXRControl
    {
        private const int Gap = 8;
        private const int EdgePad = 12;

        private readonly int[] _palette;
        private int _value;

        public OverlayColorRow(string label, int[] palette, int value)
        {
            Label = label;
            _palette = palette;
            _value = value;
            PreferredHeight = 56;
        }

        public string Label { get; set; }

        /// <summary>Selected color, 0xRRGGBB. Setting it does not raise <see cref="ValueChanged"/>.</summary>
        public int Value
        {
            get => _value;
            set => _value = value;
        }

        /// <summary>Raised when the user picks a different color.</summary>
        public event Action<int>? ValueChanged;

        public override void Increase() => Step(+1);
        public override void Decrease() => Step(-1);

        private void Step(int direction)
        {
            if (!IsEnabled || _palette.Length == 0) return;
            int i = Array.IndexOf(_palette, _value);
            if (i < 0) i = 0;
            Pick(_palette[(i + direction + _palette.Length) % _palette.Length]);
        }

        private void Pick(int color)
        {
            if (color == _value) return;
            _value = color;
            ValueChanged?.Invoke(color);
        }

        public override bool OnPointerPress(int x, int y)
        {
            if (!IsEnabled || !Bounds.Contains(x, y)) return false;
            for (int i = 0; i < _palette.Length; i++)
            {
                if (!SwatchRect(i).Contains(x, y)) continue;
                Pick(_palette[i]);
                return true;
            }
            // A press on the label half still claims the row, so it doesn't
            // fall through and start dragging the panel out from under it.
            return true;
        }

        private int SwatchSize => Math.Min(32, Bounds.Height - 16);

        private XnaRectangle SwatchRect(int i)
        {
            int size = SwatchSize;
            int total = _palette.Length * size + (_palette.Length - 1) * Gap;
            int x = Bounds.Right - EdgePad - total + i * (size + Gap);
            return new XnaRectangle(x, Bounds.Y + (Bounds.Height - size) / 2, size, size);
        }

        public override void Draw(SpriteBatch spriteBatch, SpriteFont font, Texture2D white)
        {
            DrawSelectionBackground(spriteBatch, white);

            int textY = Bounds.Y + (Bounds.Height - font.LineSpacing) / 2;
            spriteBatch.DrawString(font, Label, new XnaVector2(Bounds.X + EdgePad + 8, textY), TextColor);

            for (int i = 0; i < _palette.Length; i++)
            {
                var r = SwatchRect(i);
                int rgb = _palette[i];
                var fill = new XnaColor((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
                if (!IsEnabled) fill *= 0.5f;

                if (rgb == _value)
                {
                    // The chosen swatch sits inside a white ring so it reads
                    // as picked even when two palette colors are close.
                    var ring = new XnaRectangle(r.X - 4, r.Y - 4, r.Width + 8, r.Height + 8);
                    MonoXRDraw.RoundedRectOutline(spriteBatch, ring, 8, 3, XnaColor.White);
                }
                MonoXRDraw.RoundedRect(spriteBatch, r, 6, fill);
            }
        }
    }

    /// <summary>
    /// A row that cycles through a fixed set of options: the label on the
    /// left, arrows around the current option on the right. Clicking an arrow
    /// (or the option text) steps it, and navigation Increase/Decrease do the
    /// same, so it works from a wheel button as well as the pointer. The
    /// in-VR stand-in for a combo box, which would need a popup nothing else
    /// in the overlay has.
    /// </summary>
    public sealed class OverlayChoiceRow : MonoXRControl
    {
        private const int EdgePad = 12;
        private const int ArrowWidth = 34;

        private readonly string[] _options;
        private int _index;

        public OverlayChoiceRow(string label, string[] options, int index)
        {
            Label = label;
            _options = options.Length > 0 ? options : new[] { string.Empty };
            _index = Math.Clamp(index, 0, _options.Length - 1);
            PreferredHeight = 48;
        }

        public string Label { get; set; }

        /// <summary>Selected option. Setting it does not raise <see cref="SelectedIndexChanged"/>.</summary>
        public int SelectedIndex
        {
            get => _index;
            set => _index = Math.Clamp(value, 0, _options.Length - 1);
        }

        public event Action<int>? SelectedIndexChanged;

        public override void Increase() => Step(+1);
        public override void Decrease() => Step(-1);

        private void Step(int direction)
        {
            if (!IsEnabled || _options.Length < 2) return;
            _index = (_index + direction + _options.Length) % _options.Length;
            SelectedIndexChanged?.Invoke(_index);
        }

        public override bool OnPointerPress(int x, int y)
        {
            if (!IsEnabled || !Bounds.Contains(x, y)) return false;
            // Left arrow steps back, anything else on the value side steps on;
            // the label side just claims the press.
            if (x >= ValueLeft) Step(x < ValueLeft + ArrowWidth ? -1 : +1);
            return true;
        }

        // The value block is right-aligned and wide enough for the longest
        // option, so stepping through them does not shuffle the arrows.
        private int ValueWidth(SpriteFont font)
        {
            float widest = 0f;
            foreach (string option in _options)
                widest = Math.Max(widest, font.MeasureString(option).X);
            return (int)Math.Ceiling(widest) + ArrowWidth * 2 + 16;
        }

        private int _valueLeft;
        private int ValueLeft => _valueLeft;

        public override void Draw(SpriteBatch spriteBatch, SpriteFont font, Texture2D white)
        {
            DrawSelectionBackground(spriteBatch, white);

            int width = Math.Min(ValueWidth(font), Bounds.Width / 2);
            _valueLeft = Bounds.Right - EdgePad - width;

            int textY = Bounds.Y + (Bounds.Height - font.LineSpacing) / 2;
            spriteBatch.DrawString(font, Label, new XnaVector2(Bounds.X + EdgePad + 8, textY), TextColor);

            var box = new XnaRectangle(_valueLeft, Bounds.Y + 5, width, Bounds.Height - 10);
            MonoXRDraw.RoundedRect(spriteBatch, box, 8, TrackFill);

            var arrow = _options.Length > 1 ? AccentColor : DisabledText;
            var less = font.MeasureString("<");
            var more = font.MeasureString(">");
            spriteBatch.DrawString(font, "<",
                new XnaVector2(box.X + (ArrowWidth - less.X) / 2f, textY), arrow);
            spriteBatch.DrawString(font, ">",
                new XnaVector2(box.Right - ArrowWidth + (ArrowWidth - more.X) / 2f, textY), arrow);

            string value = _options[_index];
            var size = font.MeasureString(value);
            spriteBatch.DrawString(font, value,
                new XnaVector2(box.X + (box.Width - size.X) / 2f, textY), TextColor);
        }
    }

    /// <summary>
    /// A row that acts as a push button: a framed, centered label that runs
    /// <see cref="Click"/> when pressed. Navigation Increase/Decrease both
    /// fire it, so it is reachable without the pointer like every other row.
    /// </summary>
    public sealed class OverlayButtonRow : MonoXRControl
    {
        public OverlayButtonRow(string text)
        {
            Text = text;
            PreferredHeight = 48;
        }

        public string Text { get; set; }

        public event Action? Click;

        public override void Increase() => Press();
        public override void Decrease() => Press();

        private void Press()
        {
            if (IsEnabled) Click?.Invoke();
        }

        public override bool OnPointerPress(int x, int y)
        {
            if (!IsEnabled || !Bounds.Contains(x, y)) return false;
            Press();
            return true;
        }

        public override void Draw(SpriteBatch spriteBatch, SpriteFont font, Texture2D white)
        {
            DrawSelectionBackground(spriteBatch, white);

            var box = new XnaRectangle(Bounds.X + 12, Bounds.Y + 5, Bounds.Width - 24, Bounds.Height - 10);
            MonoXRDraw.RoundedRect(spriteBatch, box, 8, TrackFill);
            MonoXRDraw.RoundedRectOutline(spriteBatch, box, 8, 2, AccentColor);

            var size = font.MeasureString(Text);
            spriteBatch.DrawString(font, Text,
                new XnaVector2(box.X + (box.Width - size.X) / 2f,
                               box.Y + (box.Height - font.LineSpacing) / 2f), TextColor);
        }
    }
}
