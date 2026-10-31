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
    /// In-VR card for the car in front: appears once it is within <see cref="ShowGap"/> seconds
    /// on track and shows who it is, the gap, and what the driver has been blamed for in earlier
    /// incidents ("Dive bomb x3"), the same counts the car-behind box shows. Disappears when the
    /// car drops back out of range. The car and gap come from <see cref="Data.LapCatchMonitor"/>
    /// (nearest car ahead on track); the counts from <see cref="Data.DriverBlame"/>. Suppressed in
    /// Lone Qualify sessions. Edit mode draws a placeholder so the card can be found and dragged.
    /// </summary>
    public sealed class CarAheadOverlay : OverlayRenderTarget
    {
        /// <summary>Seconds ahead on track within which the card shows.</summary>
        public const double ShowGap = 4.0;

        private const int CardWidth = 320;
        private const int Pad = 14;
        private const int NameRow = 38;
        private const int BlameRow = 28;
        private const int MaxBlameLines = 4;
        private const int HeaderHeight = 44;
        private const int DefaultHeight = Pad + NameRow + BlameRow + Pad;

        private static readonly XnaColor PanelBg = new XnaColor(0x12, 0x12, 0x1E, 235);
        private static readonly XnaColor TitleBg = new XnaColor(0x1C, 0x1C, 0x2E, 245);
        private static readonly XnaColor TitleText = new XnaColor(0xD0, 0xD0, 0xF0);
        private static readonly XnaColor BodyText = new XnaColor(0xA0, 0xA0, 0xBE);
        private static readonly XnaColor Border = new XnaColor(0x46, 0x46, 0x6A);
        private static XnaColor Accent => ColorBlind.Map(CbRole.Info, new XnaColor(0x64, 0x96, 0xFF));
        private static XnaColor Warn => ColorBlind.Map(CbRole.Warn, new XnaColor(0xFF, 0xD5, 0x2E));
        private static XnaColor Close => ColorBlind.Map(CbRole.Bad, new XnaColor(0xFF, 0x4D, 0x4D));

        private readonly SpriteBatch _sb;
        private SpriteFont _fontHeader => OverlayFonts.Title(GraphicsDevice, 26f, bold: true);
        private SpriteFont _fontBody => OverlayFonts.Body(GraphicsDevice, 26f);
        private SpriteFont _fontSmall => OverlayFonts.Body(GraphicsDevice, 20f);

        private bool _show;
        private string _name = string.Empty;
        private string _gap = string.Empty;
        private bool _closeGap;
        private string[] _blameLines = Array.Empty<string>();

        public override int CollapsedWidth => (int)_fontHeader.MeasureString(Name).X + 60;
        public override int CollapsedHeight => HeaderHeight;

        public CarAheadOverlay(GraphicsDevice device, int x, int y)
            : base(device, CardWidth, DefaultHeight, x, y)
        {
            Name = "Car Ahead";
            _sb = new SpriteBatch(device);
        }

        public override void Update(GameTime gameTime)
        {
            var svc = IracingService.Instance;
            bool loneQual = svc.SessionType.IndexOf("Lone Qual", StringComparison.OrdinalIgnoreCase) >= 0;
            var ahead = Data.LapCatchMonitor.Instance.Ahead;
            bool show = svc.IsConnected && !loneQual && ahead.State != Data.CatchState.NoCar && ahead.GapSeconds <= ShowGap;

            string name = string.Empty, gap = string.Empty;
            bool closeGap = false;
            string[] lines = Array.Empty<string>();
            if (show)
            {
                gap = $"+{ahead.GapSeconds:0.00}";
                closeGap = ahead.GapSeconds <= 1.0;
                var car = FindCar(ahead.CarIdx);
                if (car != null)
                {
                    name = car.CarNumber.Length > 0 ? $"#{car.CarNumber} {car.DriverName}" : car.DriverName;
                    lines = BlameLinesFor(car);
                }
            }

            if (show == _show && name == _name && gap == _gap && closeGap == _closeGap &&
                lines.AsSpan().SequenceEqual(_blameLines))
                return;
            _show = show;
            _name = name;
            _gap = gap;
            _closeGap = closeGap;
            _blameLines = lines;

            // As tall as its lines need, and wide enough for the longest of them.
            int width = CardWidth;
            foreach (string line in lines)
                width = Math.Max(width, (int)_fontSmall.MeasureString(line).X + Pad * 2);
            Resize(width, Pad + NameRow + Math.Max(1, lines.Length) * BlameRow + Pad);
            Invalidate();
        }

        private static Data.Car? FindCar(int carIdx)
        {
            foreach (var car in IracingService.Instance.Cars)
                if (car.CarIdx == carIdx) return car;
            return null;
        }

        private static string[] BlameLinesFor(Data.Car car)
        {
            var reasons = Data.DriverBlame.Instance.For(car);
            if (reasons.Count == 0) return Array.Empty<string>();
            var list = new List<string>();
            for (int i = 0; i < reasons.Count && i < MaxBlameLines; i++)
                list.Add($"{reasons[i].Reason} x{reasons[i].Count}");
            if (reasons.Count > MaxBlameLines) list[^1] = $"+{reasons.Count - MaxBlameLines + 1} more";
            return list.ToArray();
        }

        // Cut a name with ".." so it fits beside the gap.
        private string Fit(string text, float maxWidth)
        {
            if (_fontBody.MeasureString(text).X <= maxWidth) return text;
            for (int len = text.Length - 1; len > 0; len--)
            {
                string candidate = text.Substring(0, len) + "..";
                if (_fontBody.MeasureString(candidate).X <= maxWidth) return candidate;
            }
            return "..";
        }

        public override void Render(GameTime gameTime)
        {
            int radius = OverlayStyle.CornerRadius;
            GraphicsDevice.Clear(XnaColor.Transparent);

            if (IsCollapsed)
            {
                _sb.Begin();
                var pill = new XnaRectangle(0, 0, CollapsedWidth, CollapsedHeight);
                MonoXRDraw.RoundedRect(_sb, pill, CollapsedHeight / 2, TitleBg);
                MonoXRDraw.RoundedRectOutline(_sb, pill, CollapsedHeight / 2, 2, Border);
                int dotR = 6;
                _sb.Draw(MonoXRDraw.Circle(GraphicsDevice, dotR),
                    new XnaRectangle(20 - dotR, CollapsedHeight / 2 - dotR, dotR * 2, dotR * 2), Accent);
                _sb.DrawString(_fontHeader, Name,
                    new XnaVector2(34, (CollapsedHeight - _fontHeader.LineSpacing) / 2f), TitleText);
                _sb.End();
                return;
            }

            // Invisible while nobody is in range; edit mode draws a placeholder.
            bool placeholder = !_show && EditMode;
            if (!_show && !placeholder) return;

            _sb.Begin();
            var panel = new XnaRectangle(0, 0, Width, Height);
            MonoXRDraw.RoundedRect(_sb, panel, radius, PanelBg);

            if (placeholder)
            {
                _sb.DrawString(_fontBody, "Car Ahead", new XnaVector2(Pad, Pad), TitleText);
                _sb.DrawString(_fontSmall, $"Shows within {ShowGap:0} s", new XnaVector2(Pad, Pad + NameRow - 4), BodyText);
                MonoXRDraw.RoundedRectOutline(_sb, panel, radius, 2, Border);
                _sb.End();
                return;
            }

            // Name on the left, gap on the right (red once it is within a second).
            var gapSize = _fontBody.MeasureString(_gap);
            _sb.DrawString(_fontBody, _gap, new XnaVector2(Width - Pad - gapSize.X, Pad), _closeGap ? Close : Accent);
            _sb.DrawString(_fontBody, Fit(_name, Width - Pad * 3 - gapSize.X), new XnaVector2(Pad, Pad), TitleText);

            int y = Pad + NameRow;
            if (_blameLines.Length == 0)
                _sb.DrawString(_fontSmall, "No blame on record", new XnaVector2(Pad, y), BodyText);
            for (int i = 0; i < _blameLines.Length; i++)
                _sb.DrawString(_fontSmall, _blameLines[i], new XnaVector2(Pad, y + i * BlameRow), Warn);

            MonoXRDraw.RoundedRectOutline(_sb, panel, radius, 2, Border);
            _sb.End();
        }

        public override void Dispose()
        {
            _sb.Dispose();
            base.Dispose();
        }
    }
}
