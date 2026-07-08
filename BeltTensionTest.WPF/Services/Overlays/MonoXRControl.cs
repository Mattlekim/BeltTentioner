using Microsoft.Xna.Framework.Graphics;
using GameTime = Microsoft.Xna.Framework.GameTime;
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

        // Shared palette, matching BeltSettingsOverlay.
        protected static readonly XnaColor SelectedText = XnaColor.White;
        protected static readonly XnaColor NormalText = new XnaColor(160, 160, 190);
        protected static readonly XnaColor DisabledText = new XnaColor(90, 90, 110);
        protected static readonly XnaColor SelectionFill = new XnaColor(40, 60, 120, 200);
        protected static readonly XnaColor TrackFill = new XnaColor(30, 34, 60);
        protected static readonly XnaColor AccentSelected = XnaColor.Gold;
        protected static readonly XnaColor AccentNormal = new XnaColor(90, 110, 180);

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
