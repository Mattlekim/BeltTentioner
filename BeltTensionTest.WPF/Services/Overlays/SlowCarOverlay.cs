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
    /// is ahead and within 10 seconds, a dark olive bar appears with a flashing
    /// readout of the time gap to it. Suppressed in Lone Qualify sessions,
    /// like the original.
    ///
    /// Detection lives in <see cref="Data.CarStatusMonitor"/> (shared with
    /// the standings' status column); this overlay only presents it.
    /// </summary>
    public sealed class SlowCarOverlay : OverlayRenderTarget
    {
        // The original box is 200x50; scaled 1.6x so the readout stays legible
        // in VR, keeping the same 4:1 bar proportions.
        private const int BoxWidth = 320;
        private const int BoxHeight = 80;
        private const int HeaderHeight = 44;

        // Collapsed pill stays amber so it isn't mistaken for the
        // yellow-flag pill at a glance.
        private static readonly XnaColor CardAmber = new XnaColor(0xFF, 0x9E, 0x2E, 245);
        private static readonly XnaColor CardAmberDark = new XnaColor(0xB5, 0x66, 0x00);
        private static readonly XnaColor TextDark = new XnaColor(0x1F, 0x10, 0x00);

        private readonly SpriteBatch _sb;
        private readonly SpriteFont _fontHeader;
        private readonly SpriteFont _fontBig;
        private readonly SpriteFont _fontLabel;
        private readonly Texture2D _pixel;
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
            // The original draws its readout in its MainFont (Comic Sans MS
            // Bold 20pt) and its edit label at half scale; both scaled 1.6x.
            _fontBig = RuntimeSpriteFont.Bake(device, "Comic Sans MS", 32f, System.Drawing.FontStyle.Bold);
            _fontLabel = RuntimeSpriteFont.Bake(device, "Comic Sans MS", 16f, System.Drawing.FontStyle.Bold);
            _pixel = new Texture2D(device, 1, 1);
            _pixel.SetData(new[] { XnaColor.White });
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

            // Invisible while idle; edit mode draws the box with a steady
            // readout and a small label so it can be found and dragged
            // (the original's MoveMode).
            bool placeholder = !_show && EditMode;
            if (!_show && !placeholder) return;

            _sb.Begin();

            // Matches SlowCarBox: a black bar washed with 40% yellow (a dark
            // olive, so it reads apart from the solid yellow-flag bar), with
            // only the gap readout centered in black, flashing while live.
            var box = new XnaRectangle(0, 0, Width, Height);
            _sb.Draw(_pixel, box, XnaColor.Black);
            _sb.Draw(_pixel, box, XnaColor.Yellow * 0.4f);

            if (placeholder)
                _sb.DrawString(_fontLabel, "Slow Car Warning", XnaVector2.Zero, XnaColor.Black);

            if (placeholder || _flasher > 1f)
            {
                string readout = $"{_warnTime:00.00}";
                var size = _fontBig.MeasureString(readout);
                _sb.DrawString(_fontBig, readout,
                    new XnaVector2((Width - size.X) / 2f, (Height - size.Y) / 2f), XnaColor.Black);
            }

            _sb.End();
        }

        public override void Dispose()
        {
            _pixel.Dispose();
            _fontLabel.Texture.Dispose();
            _fontBig.Texture.Dispose();
            _fontHeader.Texture.Dispose();
            _sb.Dispose();
            base.Dispose();
        }
    }
}
