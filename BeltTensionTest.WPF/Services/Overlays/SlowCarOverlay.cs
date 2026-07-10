using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework.Graphics;
using GameTime = Microsoft.Xna.Framework.GameTime;
using MonoXR.Client;
using XnaColor = Microsoft.Xna.Framework.Color;
using XnaRectangle = Microsoft.Xna.Framework.Rectangle;
using XnaVector2 = Microsoft.Xna.Framework.Vector2;

namespace BeltTensionTest.WPF.Services.Overlays
{
    /// <summary>
    /// In-VR slow-car warning, ported from IrachingHud's SlowCarBox +
    /// CarClass.IsSlowCar. A car is "slow" when the on-track gap between it
    /// and the player is closing faster than <see cref="CatchRateOn"/>
    /// seconds-per-second (smoothed), i.e. you are catching it unusually
    /// quickly — with hysteresis so it clears at half that rate. Cars in the
    /// pits, out of world, or already throwing the yellow-flag warning (a
    /// near-stopped car is WarningOverlay's job) don't count. When a slow car
    /// is ahead and within 10 seconds, an amber card appears with a flashing
    /// readout of the time gap to it. Suppressed in Lone Qualify sessions,
    /// like the original.
    ///
    /// Detection lives in <see cref="Data.CarStatusMonitor"/> (shared with
    /// the standings' status column); this overlay only presents it.
    /// </summary>
    public sealed class SlowCarOverlay : OverlayRenderTarget
    {
        private const int BoxWidth = 300;
        private const int BoxHeight = 110;
        private const int HeaderHeight = 40;

        // The live card flashes between bright yellow and a dim olive (the
        // original SlowCarBox was a flashing yellow box); the collapsed pill
        // and edit placeholder stay amber so it isn't mistaken for the
        // yellow-flag card at a glance.
        private static readonly XnaColor CardAmber = new XnaColor(0xFF, 0x9E, 0x2E, 245);
        private static readonly XnaColor CardAmberDark = new XnaColor(0xB5, 0x66, 0x00);
        private static readonly XnaColor CardFlashYellow = new XnaColor(0xFF, 0xD5, 0x2E, 245);
        private static readonly XnaColor CardFlashDim = new XnaColor(0x6E, 0x58, 0x10, 235);
        private static readonly XnaColor TextDark = new XnaColor(0x1F, 0x10, 0x00);

        private readonly SpriteBatch _sb;
        private readonly SpriteFont _fontHeader;
        private readonly SpriteFont _fontBig;
        private readonly int _collapsedWidth;

        private bool _show;
        private float _warnTime;   // smallest on-track gap to a slow car, seconds
        private float _flasher;    // original cadence: dt*15, cycle 2

        public override int CollapsedWidth => _collapsedWidth;
        public override int CollapsedHeight => HeaderHeight;

        public SlowCarOverlay(GraphicsDevice device, int x, int y)
            : base(device, BoxWidth, BoxHeight, x, y)
        {
            Name = "Slow Car";
            _sb = new SpriteBatch(device);
            _fontHeader = RuntimeSpriteFont.Bake(device, "Segoe UI", 26f, System.Drawing.FontStyle.Bold);
            _fontBig = RuntimeSpriteFont.Bake(device, "Segoe UI", 44f, System.Drawing.FontStyle.Bold);
            _collapsedWidth = (int)_fontHeader.MeasureString(Name).X + 60;
        }

        public override void Update(GameTime gameTime)
        {
            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
            if (dt <= 0) return;

            _flasher += dt * 15f;
            if (_flasher > 2f) _flasher -= 2f;

            bool wasShowing = _show;

            var svc = IracingService.Instance;
            bool loneQual = svc.SessionType.IndexOf("Lone Qual", StringComparison.OrdinalIgnoreCase) >= 0;
            var (active, gap) = Data.CarStatusMonitor.Instance.Slow;
            _show = svc.IsConnected && !loneQual && active;
            _warnTime = gap;

            // While showing, the countdown and flash change every frame; when
            // the warning clears, one more redraw wipes the card.
            if (_show || wasShowing)
                Invalidate();
        }

        public override void Render(GameTime gameTime)
        {
            GraphicsDevice.Clear(XnaColor.Transparent);

            if (IsCollapsed)
            {
                _sb.Begin();
                var pill = new XnaRectangle(0, 0, CollapsedWidth, CollapsedHeight);
                MonoXRDraw.RoundedRect(_sb, pill, CollapsedHeight / 2, CardAmber);
                MonoXRDraw.RoundedRectOutline(_sb, pill, CollapsedHeight / 2, 2, CardAmberDark);
                int dotR = 6;
                _sb.Draw(MonoXRDraw.Circle(GraphicsDevice, dotR),
                    new XnaRectangle(20 - dotR, CollapsedHeight / 2 - dotR, dotR * 2, dotR * 2), TextDark);
                _sb.DrawString(_fontHeader, Name,
                    new XnaVector2(34, (CollapsedHeight - _fontHeader.LineSpacing) / 2f), TextDark);
                _sb.End();
                return;
            }

            // Invisible while idle; edit mode draws a dimmed placeholder so
            // the card can be found and dragged (the original's MoveMode).
            bool placeholder = !_show && EditMode;
            if (!_show && !placeholder) return;

            float alpha = placeholder ? 0.45f : 1f;
            _sb.Begin();

            // Live: the box itself flashes yellow (like the original); the
            // readout stays steady so it remains readable. Placeholder: amber.
            XnaColor fill = placeholder ? CardAmber * alpha
                          : _flasher > 1f ? CardFlashYellow : CardFlashDim;
            var card = new XnaRectangle(0, 0, Width, Height);
            MonoXRDraw.RoundedRect(_sb, card, 16, fill);
            MonoXRDraw.VerticalFade(_sb, new XnaRectangle(16, 0, Width - 32, Height / 3), XnaColor.White * (0.18f * alpha));
            MonoXRDraw.RoundedRectOutline(_sb, card, 16, 3, CardAmberDark * alpha);

            XnaColor text = placeholder || _flasher > 1f ? TextDark * alpha : CardFlashYellow;
            _sb.DrawString(_fontHeader, "SLOW CAR", new XnaVector2(18, 8), text);

            string readout = placeholder ? "00.00" : $"{_warnTime:00.00}";
            var size = _fontBig.MeasureString(readout);
            _sb.DrawString(_fontBig, readout,
                new XnaVector2((Width - size.X) / 2f, HeaderHeight + (Height - HeaderHeight - size.Y) / 2f),
                text);

            _sb.End();
        }

        public override void Dispose()
        {
            _fontBig.Texture.Dispose();
            _fontHeader.Texture.Dispose();
            _sb.Dispose();
            base.Dispose();
        }
    }
}
