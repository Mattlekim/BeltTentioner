using System;
using Microsoft.Xna.Framework.Graphics;
using GameTime = Microsoft.Xna.Framework.GameTime;
using XnaColor = Microsoft.Xna.Framework.Color;
using XnaRectangle = Microsoft.Xna.Framework.Rectangle;
using XnaVector2 = Microsoft.Xna.Framework.Vector2;

namespace BeltTensionTest.WPF.Services.Overlays
{
    /// <summary>
    /// In-VR group box modeled on the WPF GroupBox: header text sitting over a
    /// gap in the top border line, with a single <see cref="Content"/> control
    /// (typically a <see cref="MonoXRMenuControl"/>) laid out inside the border
    /// with <see cref="Padding"/>. Purely visual — it draws no selection state
    /// of its own and forwards Update/Increase/Decrease to its content.
    /// </summary>
    public sealed class MonoXRGroupBox : MonoXRControl
    {
        private const int BorderThickness = 2;

        // Measured from the font on first Draw; Update may run before that,
        // so start with a typical line height.
        private int _headerHeight = 32;

        public MonoXRGroupBox(string header, MonoXRControl? content = null)
        {
            Header = header;
            Content = content;
        }

        /// <summary>Text drawn over the top border, like the WPF GroupBox header.</summary>
        public string Header { get; set; }

        /// <summary>The control shown inside the box.</summary>
        public MonoXRControl? Content { get; set; }

        /// <summary>Space between the border and the content, in pixels.</summary>
        public int Padding { get; set; } = 12;

        /// <summary>How far the header is inset from the left border edge.</summary>
        public int HeaderIndent { get; set; } = 16;

        public XnaColor BorderColor { get; set; } = new XnaColor(70, 70, 106, 255);

        // Forward adjust actions so a group box is transparent to menu navigation.
        public override void Increase() => Content?.Increase();
        public override void Decrease() => Content?.Decrease();

        public override void Update(GameTime gameTime)
        {
            if (Content == null) return;
            Content.Bounds = GetContentBounds();
            Content.Update(gameTime);
        }

        public override void Draw(SpriteBatch spriteBatch, SpriteFont font, Texture2D white)
        {
            // The border's top edge runs through the vertical middle of the
            // header text, like WPF; the box itself starts that far down.
            _headerHeight = font.LineSpacing;
            int top = Bounds.Y + _headerHeight / 2;
            int boxH = Bounds.Height - _headerHeight / 2;

            // Top border, split around the header text.
            var headerSize = font.MeasureString(Header);
            int gapX = Bounds.X + HeaderIndent;
            int gapW = string.IsNullOrEmpty(Header) ? 0 : (int)Math.Ceiling(headerSize.X) + Padding;
            spriteBatch.Draw(white, new XnaRectangle(Bounds.X, top, Math.Max(0, gapX - Bounds.X - Padding / 2), BorderThickness), BorderColor);
            int rightStart = gapX + gapW;
            spriteBatch.Draw(white, new XnaRectangle(rightStart, top, Math.Max(0, Bounds.Right - rightStart), BorderThickness), BorderColor);

            // Bottom and side borders.
            spriteBatch.Draw(white, new XnaRectangle(Bounds.X, top + boxH - BorderThickness, Bounds.Width, BorderThickness), BorderColor);
            spriteBatch.Draw(white, new XnaRectangle(Bounds.X, top, BorderThickness, boxH), BorderColor);
            spriteBatch.Draw(white, new XnaRectangle(Bounds.Right - BorderThickness, top, BorderThickness, boxH), BorderColor);

            spriteBatch.DrawString(font, Header, new XnaVector2(gapX + Padding / 2, Bounds.Y),
                                   IsEnabled ? SelectedText : DisabledText);

            if (Content != null)
            {
                Content.Bounds = GetContentBounds();
                Content.Draw(spriteBatch, font, white);
            }
        }

        private XnaRectangle GetContentBounds()
        {
            int top = Bounds.Y + _headerHeight + Padding;
            return new XnaRectangle(Bounds.X + BorderThickness + Padding,
                                    top,
                                    Bounds.Width - (BorderThickness + Padding) * 2,
                                    Bounds.Bottom - top - BorderThickness - Padding);
        }
    }
}
