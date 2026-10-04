using System;
using Microsoft.Xna.Framework.Graphics;
using GameTime = Microsoft.Xna.Framework.GameTime;
using MonoXR.Client;
using XnaColor = Microsoft.Xna.Framework.Color;
using XnaRectangle = Microsoft.Xna.Framework.Rectangle;
using XnaVector2 = Microsoft.Xna.Framework.Vector2;

namespace BeltTensionTest.WPF.Services.Overlays
{
    /// <summary>
    /// In-VR distance-to-car-behind readout, ported from IrachingHud's
    /// DeltaCarBehindBox + CarClass.DeltaToCarBehind. Shows the on-track time
    /// gap (negative seconds, e.g. "-0.85") to the nearest car behind while it
    /// is within the "show" window (default 2 s). The box is black washed
    /// with 50% of a lap-relation tint — blue for a lapped car behind you,
    /// red for a car a lap up closing to lap you, plain black when racing for
    /// position. The readout is white, turning red once the car is inside the
    /// "close" gap (default 0.2 s), where it can optionally flash. Suppressed
    /// in Lone Qualify sessions, like the original.
    ///
    /// Detection lives in <see cref="Data.CarStatusMonitor"/>; the show/close
    /// thresholds and the flash toggle come from the app settings (OpenXR tab
    /// of Preferences), read live so changes apply without a restart.
    /// </summary>
    public sealed class CarBehindOverlay : OverlayRenderTarget
    {
        // The original box is 100x50; scaled 1.6x so the readout stays legible
        // in VR, like the slow-car bar.
        private const int BoxWidth = 160;
        private const int BoxHeight = 80;
        private const int HeaderHeight = 44;

        // IrachingHud's defaults: TimeGapShow -2, TimeGapFlash -0.2, Flash Near off.
        public const double DefaultShowGap = 2.0;
        public const double DefaultCloseGap = 0.2;

        private static readonly XnaColor CardSlate = new XnaColor(0x3A, 0x3A, 0x5A, 245);
        private static readonly XnaColor CardSlateDark = new XnaColor(0x20, 0x20, 0x34);

        private readonly Func<Models.AppSettings?> _settings;
        private readonly SpriteBatch _sb;
        private readonly SpriteFont _fontHeader;
        private readonly SpriteFont _fontBig;
        private readonly SpriteFont _fontLabel;
        private readonly Texture2D _pixel;
        private readonly int _collapsedWidth;

        private bool _show;
        private bool _close;
        private float _gap;               // negative seconds to the car behind
        private Data.BehindLap _lap;
        private float _flasher;           // original cadence: dt*6, cycle 1, drawn above 0.4

        public override int CollapsedWidth => _collapsedWidth;
        public override int CollapsedHeight => HeaderHeight;

        public CarBehindOverlay(GraphicsDevice device, int x, int y, Func<Models.AppSettings?> settings)
            : base(device, BoxWidth, BoxHeight, x, y)
        {
            Name = "Car Behind";
            _settings = settings;
            _sb = new SpriteBatch(device);
            _fontHeader = RuntimeSpriteFont.Bake(device, "Segoe UI", 26f, System.Drawing.FontStyle.Bold);
            // The original draws its readout in MainFont (Comic Sans MS Bold
            // 20pt) and its edit label in ContentFont (13pt); both scaled 1.6x.
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

            _flasher += dt * 6f;
            if (_flasher > 1f) _flasher = 0f;

            bool wasShowing = _show;
            float lastGap = _gap;

            var s = _settings();
            double showGap = s != null && s.OverlayCarBehindShowGap > 0 ? s.OverlayCarBehindShowGap : DefaultShowGap;
            double closeGap = s != null && s.OverlayCarBehindCloseGap > 0 ? s.OverlayCarBehindCloseGap : DefaultCloseGap;
            bool flashClose = s?.OverlayCarBehindFlashClose ?? false;

            var svc = IracingService.Instance;
            bool loneQual = svc.SessionType.IndexOf("Lone Qual", StringComparison.OrdinalIgnoreCase) >= 0;
            var (active, gap, lap) = Data.CarStatusMonitor.Instance.Behind;
            gap = (float)Math.Round(gap, 2);
            _show = svc.IsConnected && !loneQual && active && gap >= -showGap;
            _gap = gap;
            _lap = lap;
            _close = gap >= -closeGap;

            // Steady unless the car is inside the close gap with flashing on.
            if (!_close || !flashClose)
                _flasher = 1f;

            // Redraw while the readout can change (gap updates, flashing);
            // when the car drops out of range, one more redraw wipes the box.
            if (_show && (lastGap != _gap || (_close && flashClose)) || _show != wasShowing)
                Invalidate();
        }

        public override void Render(GameTime gameTime)
        {
            GraphicsDevice.Clear(XnaColor.Transparent);

            if (IsCollapsed)
            {
                _sb.Begin();
                var pill = new XnaRectangle(0, 0, CollapsedWidth, CollapsedHeight);
                MonoXRDraw.RoundedRect(_sb, pill, CollapsedHeight / 2, CardSlate);
                MonoXRDraw.RoundedRectOutline(_sb, pill, CollapsedHeight / 2, 2, CardSlateDark);
                int dotR = 6;
                _sb.Draw(MonoXRDraw.Circle(GraphicsDevice, dotR),
                    new XnaRectangle(20 - dotR, CollapsedHeight / 2 - dotR, dotR * 2, dotR * 2), XnaColor.White);
                _sb.DrawString(_fontHeader, Name,
                    new XnaVector2(34, (CollapsedHeight - _fontHeader.LineSpacing) / 2f), XnaColor.White);
                _sb.End();
                return;
            }

            // Invisible while nobody is in range; edit mode draws a plain black
            // box with a small label so it can be found and dragged (the
            // original's MoveMode).
            bool placeholder = !_show && EditMode;
            if (!_show && !placeholder) return;

            _sb.Begin();

            var box = new XnaRectangle(0, 0, Width, Height);
            _sb.Draw(_pixel, box, XnaColor.Black);

            if (placeholder)
            {
                _sb.DrawString(_fontLabel, "Delta Behind", XnaVector2.Zero, XnaColor.White);
                _sb.End();
                return;
            }

            // Matches DeltaCarBehindBox: black, washed with 50% of the
            // lap-relation colour, readout centered.
            var tint = _lap switch
            {
                Data.BehindLap.LappedBy => XnaColor.Blue,
                Data.BehindLap.LapsUp => XnaColor.Red,
                _ => XnaColor.Black,
            };
            _sb.Draw(_pixel, box, tint * 0.5f);

            if (_flasher > 0.4f)
            {
                string readout = $"{_gap:0.00}";
                var size = _fontBig.MeasureString(readout);
                _sb.DrawString(_fontBig, readout,
                    new XnaVector2((Width - size.X) / 2f, (Height - size.Y) / 2f),
                    _close ? XnaColor.Red : XnaColor.White);
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
