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
    /// In-VR yellow-flag warning, ported from IrachingHud's YellowFlagBox.
    /// A car "throws yellow" when it is on track (not on pit road) and its
    /// speed stays below 50 km/h for more than a second; when such a car is
    /// ahead of the player and within 10 seconds on track, a yellow card
    /// appears with a flashing readout of the time gap to the hazard.
    /// Suppressed in Lone Qualify sessions, like the original.
    ///
    /// Our <see cref="Data.Car"/> doesn't carry per-car speed, so it is
    /// derived from lap-distance deltas × track length between updates.
    /// </summary>
    public sealed class WarningOverlay : OverlayRenderTarget
    {
        private const int BoxWidth = 360;
        private const int BoxHeight = 120;
        private const int HeaderHeight = 44;

        private const float SlowSpeedKmh = 50f;   // below this a car is "slow"
        private const float SlowHoldSeconds = 1f; // ...for at least this long
        private const float WarnWindowSeconds = 10f; // hazard within this many seconds ahead

        private static readonly XnaColor CardYellow = new XnaColor(0xFF, 0xD5, 0x2E, 245);
        private static readonly XnaColor CardYellowDark = new XnaColor(0xC8, 0x9E, 0x00);
        private static readonly XnaColor TextDark = new XnaColor(0x1A, 0x14, 0x00);

        /// <summary>Tracks one car's slow-detection state between updates.</summary>
        private sealed class CarWatch
        {
            public float LastDist = float.NaN; // laps + pct, monotonic per lap
            public float SpeedKmh;
            public float BelowTime;
            public bool Warning;
        }

        private readonly SpriteBatch _sb;
        private readonly SpriteFont _fontHeader;
        private readonly SpriteFont _fontBig;
        private readonly Dictionary<int, CarWatch> _watch = new();
        private readonly int _collapsedWidth;

        private bool _show;
        private float _warnTime;   // smallest on-track gap to a hazard, seconds
        private float _flasher;    // same cadence as the original: dt*15, cycle 2

        public override int CollapsedWidth => _collapsedWidth;
        public override int CollapsedHeight => HeaderHeight;

        public WarningOverlay(GraphicsDevice device, int x, int y)
            : base(device, BoxWidth, BoxHeight, x, y)
        {
            Name = "Yellow Warning";
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
            _show = false;
            _warnTime = float.PositiveInfinity;

            var svc = IracingService.Instance;
            bool loneQual = svc.SessionType.IndexOf("Lone Qual", StringComparison.OrdinalIgnoreCase) >= 0;
            float trackLen = svc.TrackLengthMeters;
            var player = svc.PlayerCar;

            if (svc.IsConnected && !loneQual && trackLen > 0 && player.CarIdx >= 0)
            {
                foreach (var car in svc.Cars)
                {
                    if (car.CarIdx < 0) continue;
                    if (!_watch.TryGetValue(car.CarIdx, out var w))
                        _watch[car.CarIdx] = w = new CarWatch();

                    // Speed from lap-distance progress; Lap+Pct is continuous
                    // across the line so no wrap handling is needed.
                    float dist = car.Lap + Math.Clamp(car.LapDistPct, 0f, 1f);
                    if (!float.IsNaN(w.LastDist))
                    {
                        float kmh = Math.Max(0f, (dist - w.LastDist) * trackLen / dt * 3.6f);
                        if (kmh < 400f) // ignore teleports (tow, session resets)
                            w.SpeedKmh = w.SpeedKmh * 0.7f + kmh * 0.3f;
                    }
                    w.LastDist = dist;

                    // Slow-car detection, like CarClass.YellowWaring: sustained
                    // low speed on track; pits/garage reset the state.
                    if (!car.IsOnTrack || car.OnPitRoad)
                    {
                        w.Warning = false;
                        w.BelowTime = 0;
                    }
                    else if (w.SpeedKmh < SlowSpeedKmh)
                    {
                        w.BelowTime += dt;
                        if (w.BelowTime > SlowHoldSeconds) w.Warning = true;
                    }
                    else
                    {
                        w.Warning = false;
                        w.BelowTime = 0;
                    }

                    if (!w.Warning || car.CarIdx == player.CarIdx) continue;

                    // On-track gap to the player (positive = ahead), wrap-
                    // corrected at the start/finish line the same way as
                    // MainOverlay's relative column.
                    if (car.EstTime <= 0 || player.EstTime <= 0) continue;
                    float gap = car.EstTime - player.EstTime;
                    float lapTime = player.ClassEstLapTime > 0 ? player.ClassEstLapTime
                                  : car.ClassEstLapTime > 0 ? car.ClassEstLapTime : 0f;
                    if (lapTime > 0)
                    {
                        if (gap > lapTime * 0.5f) gap -= lapTime;
                        else if (gap < -lapTime * 0.5f) gap += lapTime;
                    }

                    if (gap > 0 && gap < WarnWindowSeconds)
                    {
                        _show = true;
                        if (gap < _warnTime) _warnTime = gap;
                    }
                }
            }

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
                MonoXRDraw.RoundedRect(_sb, pill, CollapsedHeight / 2, CardYellow);
                MonoXRDraw.RoundedRectOutline(_sb, pill, CollapsedHeight / 2, 2, CardYellowDark);
                int dotR = 6;
                _sb.Draw(MonoXRDraw.Circle(GraphicsDevice, dotR),
                    new XnaRectangle(20 - dotR, CollapsedHeight / 2 - dotR, dotR * 2, dotR * 2), TextDark);
                _sb.DrawString(_fontHeader, Name,
                    new XnaVector2(34, (CollapsedHeight - _fontHeader.LineSpacing) / 2f), TextDark);
                _sb.End();
                return;
            }

            // Invisible while idle; edit mode draws a dimmed placeholder so
            // the card can be found and dragged (like the original's MoveMode).
            bool placeholder = !_show && EditMode;
            if (!_show && !placeholder) return;

            float alpha = placeholder ? 0.45f : 1f;
            _sb.Begin();

            var card = new XnaRectangle(0, 0, Width, Height);
            MonoXRDraw.RoundedRect(_sb, card, 16, CardYellow * alpha);
            MonoXRDraw.VerticalFade(_sb, new XnaRectangle(16, 0, Width - 32, Height / 3), XnaColor.White * (0.18f * alpha));
            MonoXRDraw.RoundedRectOutline(_sb, card, 16, 3, CardYellowDark * alpha);

            _sb.DrawString(_fontHeader, "YELLOW FLAG", new XnaVector2(18, 8), TextDark * alpha);

            // Time-to-hazard readout, centered in the lower part. Flashes
            // while live; steady sample value in the edit-mode placeholder.
            if (placeholder || _flasher > 1f)
            {
                string readout = placeholder ? "00.00" : $"{_warnTime:00.00}";
                var size = _fontBig.MeasureString(readout);
                _sb.DrawString(_fontBig, readout,
                    new XnaVector2((Width - size.X) / 2f, HeaderHeight + (Height - HeaderHeight - size.Y) / 2f),
                    TextDark * alpha);
            }

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
