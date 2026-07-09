using Microsoft.Xna.Framework.Graphics;
using GameTime = Microsoft.Xna.Framework.GameTime;
using MonoXR.Client;
using XnaColor = Microsoft.Xna.Framework.Color;
using XnaRectangle = Microsoft.Xna.Framework.Rectangle;

namespace BeltTensionTest.WPF.Services.Overlays
{
    /// <summary>
    /// A lightweight in-VR widget drawn inside an <see cref="OverlayRenderTarget"/>.
    /// Controls do not own a SpriteBatch/font/pixel texture — the hosting overlay
    /// passes its own to <see cref="Draw"/>, the same resources it uses for the
    /// rest of the panel. Coordinates are local to the hosting render target.
    /// </summary>
    public abstract class MonoXRControl
    {
        /// <summary>Position and size within the hosting overlay, in pixels.</summary>
        public XnaRectangle Bounds { get; set; }

        /// <summary>Whether navigation currently targets this control (draws highlighted).</summary>
        public bool IsSelected { get; set; }

        /// <summary>Disabled controls draw dimmed and ignore input helpers.</summary>
        public bool IsEnabled { get; set; } = true;

        /// <summary>
        /// Row height this control wants when stacked in a <see cref="MonoXRMenuControl"/>;
        /// 0 means use the menu's ItemHeight. Lets slim rows (labels) mix with full rows.
        /// </summary>
        public int PreferredHeight { get; set; }

        // Shared palette, mirroring the WPF app brushes in Resources/Styles.xaml.
        protected static readonly XnaColor SelectedText = new XnaColor(0xD0, 0xD0, 0xF0);   // TextBrightBrush
        protected static readonly XnaColor NormalText = new XnaColor(0xA0, 0xA0, 0xBE);     // TextPrimaryBrush
        protected static readonly XnaColor DisabledText = new XnaColor(0x66, 0x66, 0x80);   // disabled foreground
        protected static readonly XnaColor SelectionFill = new XnaColor(0x37, 0x37, 0x5A, 200); // button hover
        protected static readonly XnaColor TrackFill = new XnaColor(0x26, 0x26, 0x3A);      // BgMidBrush
        protected static readonly XnaColor AccentSelected = new XnaColor(0x64, 0x96, 0xFF); // AccentBlueBrush
        protected static readonly XnaColor AccentNormal = new XnaColor(0x4A, 0x6E, 0xBB);   // dimmed accent blue
        protected static readonly XnaColor Divider = new XnaColor(0x46, 0x46, 0x6A);        // BorderBrush

        protected XnaColor TextColor =>
            !IsEnabled ? DisabledText : IsSelected ? SelectedText : NormalText;

        protected XnaColor AccentColor =>
            !IsEnabled ? DisabledText : IsSelected ? AccentSelected : AccentNormal;

        /// <summary>Per-frame logic (animation, held-button repeat, ...).</summary>
        public virtual void Update(GameTime gameTime)
        {
        }

        /// <summary>
        /// Navigation "increase" landed on this control. Sliders step up,
        /// checkboxes toggle; default is a no-op.
        /// </summary>
        public virtual void Increase()
        {
        }

        /// <summary>Navigation "decrease" landed on this control. See <see cref="Increase"/>.</summary>
        public virtual void Decrease()
        {
        }

        /// <summary>
        /// Draw into the hosting overlay. <paramref name="spriteBatch"/> must already
        /// be inside Begin/End; <paramref name="white"/> is a 1x1 white pixel texture.
        /// </summary>
        public abstract void Draw(SpriteBatch spriteBatch, SpriteFont font, Texture2D white);

        /// <summary>Draws the selection background used by all controls.</summary>
        protected void DrawSelectionBackground(SpriteBatch spriteBatch, Texture2D white)
        {
            if (IsSelected)
                spriteBatch.Draw(white, Bounds, SelectionFill);
        }
    }
}
