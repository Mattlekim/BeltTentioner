using System;
using Microsoft.Xna.Framework.Graphics;
using XnaColor = Microsoft.Xna.Framework.Color;
using XnaRectangle = Microsoft.Xna.Framework.Rectangle;
using XnaVector2 = Microsoft.Xna.Framework.Vector2;

namespace BeltTensionTest.WPF.Services.Overlays
{
    /// <summary>
    /// In-VR checkbox mirroring the basic WPF CheckBox contract: nullable
    /// <see cref="IsChecked"/>, optional three-state cycling, and
    /// Checked/Unchecked/Indeterminate/Click events. Toggle it from navigation
    /// input via <see cref="Toggle"/>.
    /// </summary>
    public sealed class MonoXRCheckbox : MonoXRControl
    {
        private bool? _isChecked;

        public MonoXRCheckbox(string content, bool? isChecked = false)
        {
            Content = content;
            _isChecked = isChecked;
        }

        /// <summary>Label drawn next to the box.</summary>
        public string Content { get; set; }

        /// <summary>When true, <see cref="Toggle"/> cycles false → true → null.</summary>
        public bool IsThreeState { get; set; }

        public event Action? Checked;
        public event Action? Unchecked;
        public event Action? Indeterminate;

        /// <summary>Raised on every <see cref="Toggle"/>, after the state events.</summary>
        public event Action? Click;

        /// <summary>true = checked, false = unchecked, null = indeterminate.</summary>
        public bool? IsChecked
        {
            get => _isChecked;
            set
            {
                if (value == _isChecked) return;
                _isChecked = value;
                switch (_isChecked)
                {
                    case true: Checked?.Invoke(); break;
                    case false: Unchecked?.Invoke(); break;
                    case null: Indeterminate?.Invoke(); break;
                }
            }
        }

        /// <summary>Advances the state exactly like a WPF CheckBox click.</summary>
        public void Toggle()
        {
            if (!IsEnabled) return;
            IsChecked = _isChecked switch
            {
                false => true,
                true => IsThreeState ? (bool?)null : false,
                null => false,
            };
            Click?.Invoke();
        }

        // Increase/decrease on a checkbox both just toggle it.
        public override void Increase() => Toggle();
        public override void Decrease() => Toggle();

        public override void Draw(SpriteBatch spriteBatch, SpriteFont font, Texture2D white)
        {
            DrawSelectionBackground(spriteBatch, white);

            int pad = 12;
            int boxSize = Math.Min(28, Bounds.Height - 8);
            int boxX = Bounds.X + pad;
            int boxY = Bounds.Y + (Bounds.Height - boxSize) / 2;

            // Box outline over a track-coloured fill.
            spriteBatch.Draw(white, new XnaRectangle(boxX, boxY, boxSize, boxSize), TrackFill);
            var edge = AccentColor;
            spriteBatch.Draw(white, new XnaRectangle(boxX, boxY, boxSize, 2), edge);
            spriteBatch.Draw(white, new XnaRectangle(boxX, boxY + boxSize - 2, boxSize, 2), edge);
            spriteBatch.Draw(white, new XnaRectangle(boxX, boxY, 2, boxSize), edge);
            spriteBatch.Draw(white, new XnaRectangle(boxX + boxSize - 2, boxY, 2, boxSize), edge);

            if (_isChecked == true)
            {
                // Filled inner square as the check mark.
                spriteBatch.Draw(white, new XnaRectangle(boxX + 6, boxY + 6, boxSize - 12, boxSize - 12),
                                 AccentColor);
            }
            else if (_isChecked == null)
            {
                // Horizontal dash for indeterminate.
                spriteBatch.Draw(white, new XnaRectangle(boxX + 6, boxY + boxSize / 2 - 2, boxSize - 12, 4),
                                 AccentColor);
            }

            int textY = Bounds.Y + (Bounds.Height - font.LineSpacing) / 2;
            spriteBatch.DrawString(font, Content,
                                   new XnaVector2(boxX + boxSize + pad, textY), TextColor);
        }
    }
}
