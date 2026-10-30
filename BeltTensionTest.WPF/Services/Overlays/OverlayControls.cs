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
    /// A row that shows the current option in a box; clicking it opens a list of
    /// every option to pick from. The list is drawn by <see cref="DrawPopup"/>
    /// after the rest of the panel, so it sits on top of the rows around it, and
    /// <see cref="HandlePopupPress"/> must be offered each press before the
    /// menu sees it. Opens below the row, or above it when there is no room.
    /// Navigation Increase/Decrease still step through the options without
    /// opening the list, so it works from a wheel button as well.
    /// </summary>
    public sealed class OverlayDropdownRow : MonoXRControl
    {
        private const int EdgePad = 12;
        private const int ArrowWidth = 34;
        private const int PopupPad = 6;

        private readonly string[] _options;
        private readonly bool[] _optionEnabled;
        private int _index;
        private int _hover = -1;
        private int _itemHeight = 40;
        private XnaRectangle _box;
        private XnaRectangle _popup;

        public OverlayDropdownRow(string label, string[] options, int index)
        {
            Label = label;
            _options = options.Length > 0 ? options : new[] { string.Empty };
            _optionEnabled = new bool[_options.Length];
            Array.Fill(_optionEnabled, true);
            _index = Math.Clamp(index, 0, _options.Length - 1);
            PreferredHeight = 48;
        }

        public string Label { get; set; }

        public bool IsOpen { get; private set; }

        /// <summary>An unavailable option is drawn greyed out and struck through, and cannot be picked.</summary>
        public void SetOptionEnabled(int index, bool enabled)
        {
            if (index >= 0 && index < _options.Length) _optionEnabled[index] = enabled;
        }

        /// <summary>Selected option. Setting it does not raise <see cref="SelectedIndexChanged"/>.</summary>
        public int SelectedIndex
        {
            get => _index;
            set => _index = Math.Clamp(value, 0, _options.Length - 1);
        }

        public event Action<int>? SelectedIndexChanged;

        public void Close()
        {
            IsOpen = false;
            _hover = -1;
        }

        public override void Increase() => Step(+1);
        public override void Decrease() => Step(-1);

        private void Step(int direction)
        {
            Close();
            if (!IsEnabled || _options.Length < 2) return;
            // Skip the options that are not available.
            int next = _index;
            for (int i = 0; i < _options.Length; i++)
            {
                next = (next + direction + _options.Length) % _options.Length;
                if (_optionEnabled[next]) break;
            }
            Pick(next);
        }

        private void Pick(int index)
        {
            Close();
            if (index == _index || !_optionEnabled[index]) return;
            _index = index;
            SelectedIndexChanged?.Invoke(_index);
        }

        public override bool OnPointerPress(int x, int y)
        {
            if (!IsEnabled || !Bounds.Contains(x, y)) return false;
            // The value box opens the list; the label side just claims the press.
            if (_options.Length > 1 && _box.Contains(x, y))
            {
                IsOpen = !IsOpen;
                _hover = -1;
            }
            return true;
        }

        /// <summary>
        /// While the list is open, a press picks the option under it or closes the list;
        /// either way the press is used up. Returns false when the list is closed.
        /// </summary>
        public bool HandlePopupPress(int x, int y)
        {
            if (!IsOpen) return false;
            int item = ItemAt(x, y);
            if (item >= 0 && !_optionEnabled[item]) return true; // an unavailable option: the list stays open
            if (item >= 0) Pick(item);
            else Close();
            return true;
        }

        private int ItemAt(int x, int y)
        {
            if (!_popup.Contains(x, y)) return -1;
            int item = (y - _popup.Y - PopupPad) / _itemHeight;
            return y >= _popup.Y + PopupPad && item >= 0 && item < _options.Length ? item : -1;
        }

        /// <summary>The pointer is resting at <paramref name="x"/>,<paramref name="y"/>: highlight the option under it. Returns true when the highlight changed (the panel needs redrawing).</summary>
        public bool HandleHover(int x, int y)
        {
            int item = IsOpen ? ItemAt(x, y) : -1;
            if (item == _hover) return false;
            _hover = item;
            return IsOpen;
        }

        /// <summary>The pointer left the panel. Returns true when a highlight was dropped.</summary>
        public bool HandleHoverExit()
        {
            if (_hover < 0) return false;
            _hover = -1;
            return IsOpen;
        }
        public override void Draw(SpriteBatch spriteBatch, SpriteFont font, Texture2D white)
        {
            DrawSelectionBackground(spriteBatch, white);

            float widest = 0f;
            foreach (string option in _options)
                widest = Math.Max(widest, font.MeasureString(option).X);
            int width = Math.Min((int)Math.Ceiling(widest) + ArrowWidth + 32, Bounds.Width / 2);

            int textY = Bounds.Y + (Bounds.Height - font.LineSpacing) / 2;
            spriteBatch.DrawString(font, Label, new XnaVector2(Bounds.X + EdgePad + 8, textY), TextColor);

            _box = new XnaRectangle(Bounds.Right - EdgePad - width, Bounds.Y + 5, width, Bounds.Height - 10);
            MonoXRDraw.RoundedRect(spriteBatch, _box, 8, TrackFill);
            if (IsOpen) MonoXRDraw.RoundedRect(spriteBatch, _box, 8, SelectionFill);

            var arrow = _options.Length > 1 ? AccentColor : DisabledText;
            var down = font.MeasureString("v");
            spriteBatch.DrawString(font, "v",
                new XnaVector2(_box.Right - ArrowWidth + (ArrowWidth - down.X) / 2f, textY), arrow);
            spriteBatch.DrawString(font, _options[_index], new XnaVector2(_box.X + 14, textY), TextColor);
        }

        /// <summary>Draw the open list on top of everything else in the panel; does nothing while closed.</summary>
        /// <param name="panelHeight">Height of the hosting panel, to keep the list inside it.</param>
        public void DrawPopup(SpriteBatch spriteBatch, SpriteFont font, Texture2D white, int panelHeight)
        {
            if (!IsOpen) return;

            _itemHeight = font.LineSpacing + 10;
            int height = _options.Length * _itemHeight + PopupPad * 2;
            int y = _box.Bottom + 4;
            if (y + height > panelHeight - 4) y = Math.Max(4, _box.Y - 4 - height);
            _popup = new XnaRectangle(_box.X, y, _box.Width, height);

            MonoXRDraw.RoundedRect(spriteBatch, _popup, 8, new XnaColor(0x1E, 0x1E, 0x30));
            for (int i = 0; i < _options.Length; i++)
            {
                var item = new XnaRectangle(_popup.X + 4, _popup.Y + PopupPad + i * _itemHeight, _popup.Width - 8, _itemHeight);
                bool available = _optionEnabled[i];
                // The picked option has a fill and an accent bar; the one under the pointer is
                // lit up (red when it cannot be picked).
                if (i == _index) MonoXRDraw.RoundedRect(spriteBatch, item, 6, SelectionFill);
                if (i == _hover)
                {
                    MonoXRDraw.RoundedRect(spriteBatch, item, 6, available ? new XnaColor(0x46, 0x6E, 0xC8, 150) : new XnaColor(0xB4, 0x3C, 0x3C, 90));
                    MonoXRDraw.RoundedRectOutline(spriteBatch, item, 6, 2, available ? AccentSelected : new XnaColor(0xDC, 0x3C, 0x3C));
                }
                if (i == _index)
                    MonoXRDraw.RoundedRect(spriteBatch, new XnaRectangle(item.X + 3, item.Y + 6, 5, item.Height - 12), 2, AccentSelected);

                var textPos = new XnaVector2(item.X + 16, item.Y + (item.Height - font.LineSpacing) / 2f);
                spriteBatch.DrawString(font, _options[i], textPos,
                    !available ? DisabledText : i == _index || i == _hover ? SelectedText : NormalText);
                if (!available)
                {
                    float w = font.MeasureString(_options[i]).X;
                    spriteBatch.Draw(white, new XnaRectangle((int)textPos.X, item.Y + item.Height / 2, (int)w, 2), DisabledText);
                }
            }
            MonoXRDraw.RoundedRectOutline(spriteBatch, _popup, 8, 2, AccentSelected);
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
