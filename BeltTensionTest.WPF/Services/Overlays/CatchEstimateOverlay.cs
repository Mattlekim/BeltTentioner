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
    /// In-VR catch estimate: how many laps until you catch the car in front, and until the car
    /// behind catches you. Shows "Not catching" when the gap is steady or growing. The numbers
    /// come from <see cref="Data.LapCatchMonitor"/>; the panel only presents them and redraws when
    /// the text changes.
    /// </summary>
    public sealed class CatchEstimateOverlay : OverlayRenderTarget
    {
        private const int PanelWidth = 440;
        private const int TitleBarHeight = 56;
        public override int TitleBarSourceHeight => TitleBarHeight;
        private const int RowHeight = 76;
        private const int PanelHeight = TitleBarHeight + 14 + RowHeight * 2 + 6;

        // Behind is red when it will catch you within this many laps.
        private const float DangerLaps = 5f;

        private static readonly XnaColor PanelBg = new XnaColor(0x12, 0x12, 0x1E, 235);
        private static readonly XnaColor TitleBg = new XnaColor(0x1C, 0x1C, 0x2E, 245);
        private static readonly XnaColor TitleText = new XnaColor(0xD0, 0xD0, 0xF0);
        private static readonly XnaColor BodyText = new XnaColor(0xA0, 0xA0, 0xBE);
        private static XnaColor Accent => ColorBlind.Map(CbRole.Info, new XnaColor(0x64, 0x96, 0xFF));
        private static readonly XnaColor Border = new XnaColor(0x46, 0x46, 0x6A);
        private static XnaColor Good => ColorBlind.Map(CbRole.Good, new XnaColor(0x4C, 0xC3, 0x6A));
        private static XnaColor Hot => ColorBlind.Map(CbRole.Bad, new XnaColor(0xFF, 0x4D, 0x4D));
        private static XnaColor Warn => ColorBlind.Map(CbRole.Amber, new XnaColor(0xFF, 0xB0, 0x2E));

        private readonly SpriteBatch _sb;
        private readonly Texture2D _white;
        private readonly SpriteFont _fontTitle;
        private readonly SpriteFont _fontBody;
        private readonly SpriteFont _fontSmall;
        private readonly int _collapsedWidth;

        // What each row shows, rebuilt every Update; the panel redraws when any of it changes.
        private struct RowText : IEquatable<RowText>
        {
            public string Readout, Detail;
            public XnaColor Color;
            public bool Equals(RowText o) => Readout == o.Readout && Detail == o.Detail && Color == o.Color;
        }
        private RowText _ahead, _behind;

        public override int CollapsedWidth => _collapsedWidth;
        public override int CollapsedHeight => TitleBarHeight;

        public CatchEstimateOverlay(GraphicsDevice device, int x, int y)
            : base(device, PanelWidth, PanelHeight, x, y)
        {
            Name = "Catch";
            _sb = new SpriteBatch(device);
            _white = new Texture2D(device, 1, 1);
            _white.SetData(new[] { XnaColor.White });
            _fontTitle = RuntimeSpriteFont.Bake(device, "Segoe UI", 32f);
            _fontBody = RuntimeSpriteFont.Bake(device, "Segoe UI", 26f);
            _fontSmall = RuntimeSpriteFont.Bake(device, "Segoe UI", 20f);
            _collapsedWidth = (int)_fontTitle.MeasureString(Name).X + 60;
            _ahead = Describe(Data.CatchEstimate.None, ahead: true);
            _behind = Describe(Data.CatchEstimate.None, ahead: false);
        }

        public override void Update(GameTime gameTime)
        {
            var monitor = Data.LapCatchMonitor.Instance;
            var ahead = Describe(monitor.Ahead, ahead: true);
            var behind = Describe(monitor.Behind, ahead: false);
            if (ahead.Equals(_ahead) && behind.Equals(_behind)) return;
            _ahead = ahead;
            _behind = behind;
            Invalidate();
        }

        private static RowText Describe(Data.CatchEstimate e, bool ahead)
        {
            switch (e.State)
            {
                case Data.CatchState.Catching:
                {
                    string laps = FormatLaps(e.LapsToCatch);
                    string detail = $"{CarLabel(e.CarIdx)}Gap {e.GapSeconds:0.0} s, {e.ClosingPerLap:0.00} s/lap";
                    // Catching the car ahead is good news; the car behind catching you is not.
                    XnaColor color = ahead ? Good : e.LapsToCatch <= DangerLaps ? Hot : Warn;
                    return new RowText { Readout = laps, Detail = detail, Color = color };
                }
                case Data.CatchState.NotCatching:
                    return new RowText
                    {
                        Readout = "Not catching",
                        Detail = $"{CarLabel(e.CarIdx)}Gap {e.GapSeconds:0.0} s",
                        Color = BodyText,
                    };
                case Data.CatchState.Unknown:
                    return new RowText
                    {
                        Readout = "Calculating",
                        Detail = $"{CarLabel(e.CarIdx)}Needs a few clean laps",
                        Color = BodyText,
                    };
                default:
                    return new RowText { Readout = "-", Detail = ahead ? "No car ahead" : "No car behind", Color = BodyText };
            }
        }

        private static string FormatLaps(float laps)
        {
            if (laps < 1f) return "Under 1 lap";
            string text = laps.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
            return text == "1.0" ? "1 lap" : text + " laps";
        }

        // "#12 " for the car being measured, or nothing if it is not in the list.
        private static string CarLabel(int carIdx)
        {
            foreach (var car in IracingService.Instance.Cars)
                if (car.CarIdx == carIdx && car.CarNumber.Length > 0)
                    return $"#{car.CarNumber}  ";
            return string.Empty;
        }

        public override void Render(GameTime gameTime)
        {
            int Radius = OverlayStyle.CornerRadius;
            GraphicsDevice.Clear(XnaColor.Transparent);
            _sb.Begin();

            if (IsCollapsed)
            {
                var pill = new XnaRectangle(0, 0, CollapsedWidth, CollapsedHeight);
                MonoXRDraw.RoundedRect(_sb, pill, CollapsedHeight / 2, TitleBg);
                MonoXRDraw.RoundedRectOutline(_sb, pill, CollapsedHeight / 2, 2, Border);
                int dotR = 6;
                _sb.Draw(MonoXRDraw.Circle(GraphicsDevice, dotR),
                    new XnaRectangle(20 - dotR, CollapsedHeight / 2 - dotR, dotR * 2, dotR * 2), Accent);
                _sb.DrawString(_fontTitle, Name, new XnaVector2(34, (CollapsedHeight - _fontTitle.LineSpacing) / 2f), TitleText);
                _sb.End();
                return;
            }

            var panel = new XnaRectangle(0, 0, Width, Height);
            MonoXRDraw.RoundedRect(_sb, panel, Radius, PanelBg);

            MonoXRDraw.RoundedRect(_sb, new XnaRectangle(0, 0, Width, TitleBarHeight), Radius, TitleBg, roundBottom: false);
            MonoXRDraw.VerticalFade(_sb, new XnaRectangle(0, 0, Width, TitleBarHeight / 2), XnaColor.White * 0.05f);
            _sb.DrawString(_fontTitle, "Catch", new XnaVector2(20, OverlayStyle.TitleY(_fontTitle, TitleBarHeight) + 2), XnaColor.Black * 0.45f);
            _sb.DrawString(_fontTitle, "Catch", new XnaVector2(20, OverlayStyle.TitleY(_fontTitle, TitleBarHeight)), TitleText);
            _sb.Draw(_white, new XnaRectangle(0, TitleBarHeight - 3, Width, 3), Accent);
            MonoXRDraw.VerticalFade(_sb, new XnaRectangle(0, TitleBarHeight, Width, 14), Accent * 0.25f);

            int y = TitleBarHeight + 14;
            DrawRow(y, "Car ahead", _ahead);
            DrawRow(y + RowHeight, "Car behind", _behind);

            MonoXRDraw.RoundedRectOutline(_sb, panel, Radius, 2, Border);
            _sb.End();
        }

        /// <summary>Label on the left, readout on the right, gap detail underneath.</summary>
        private void DrawRow(int y, string label, RowText row)
        {
            const int pad = 20;
            _sb.DrawString(_fontBody, label, new XnaVector2(pad, y), BodyText);

            var size = _fontBody.MeasureString(row.Readout);
            _sb.DrawString(_fontBody, row.Readout, new XnaVector2(Width - pad - size.X, y), row.Color);

            _sb.DrawString(_fontSmall, row.Detail, new XnaVector2(pad, y + _fontBody.LineSpacing + 2), BodyText);
        }

        public override void Dispose()
        {
            _fontSmall.Texture.Dispose();
            _fontBody.Texture.Dispose();
            _fontTitle.Texture.Dispose();
            _white.Dispose();
            _sb.Dispose();
            base.Dispose();
        }
    }
}
