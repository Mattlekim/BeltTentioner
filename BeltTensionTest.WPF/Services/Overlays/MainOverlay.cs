using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BeltTensionTest.WPF.Services.Data;
using Microsoft.Xna.Framework.Graphics;
using GameTime = Microsoft.Xna.Framework.GameTime;
using MonoXR.Client;
using XnaColor = Microsoft.Xna.Framework.Color;
using XnaRectangle = Microsoft.Xna.Framework.Rectangle;
using XnaVector2 = Microsoft.Xna.Framework.Vector2;

namespace BeltTensionTest.WPF.Services.Overlays
{
    /// <summary>
    /// In-VR standings panel ("Main"). Shows a window of cars around the
    /// player — position, driver name, last lap and gap to you — filtered to
    /// the player's car class. Its behaviour follows the current iRacing
    /// session type: in a race it orders by race position, colors cars a lap
    /// ahead red and a lap behind blue, and gaps come from CarIdxF2Time; in
    /// practice/qualifying it orders by best lap and gaps are best-lap deltas.
    /// </summary>
    public sealed class MainOverlay : OverlayRenderTarget
    {
        /// <summary>How many cars the panel shows (the player included).</summary>
        public const int DefaultCarsToDisplay = 7;

        private const int TitleBarHeight = 48;
        private const int RowHeight = 44;
        private const int RowSpacing = 3;
        private const int PanelWidth = 700;
        private const int Pad = 8;

        // Column layout (x offsets inside the panel).
        private const int ColPos = 18;
        private const int ColName = 66;
        private const int ColLastLapRight = PanelWidth - 150; // right edge of "last lap"
        private const int ColGapRight = PanelWidth - 18;      // right edge of "gap"

        // App palette (Resources/Styles.xaml), matching BeltSettingsOverlay.
        private static readonly XnaColor PanelBg = new XnaColor(0x12, 0x12, 0x1E, 235);
        private static readonly XnaColor TitleBg = new XnaColor(0x1C, 0x1C, 0x2E, 245);
        private static readonly XnaColor TitleText = new XnaColor(0xD0, 0xD0, 0xF0);
        private static readonly XnaColor Accent = new XnaColor(0x64, 0x96, 0xFF);
        private static readonly XnaColor Border = new XnaColor(0x46, 0x46, 0x6A);
        private static readonly XnaColor RowText = new XnaColor(0xE6, 0xE6, 0xF5);
        private static readonly XnaColor RowTextDim = new XnaColor(0xA0, 0xA0, 0xBE);

        // Row backgrounds. The player gets a clearly different color; cars a
        // lap ahead/behind get red/blue; everyone else cycles subtle shade
        // variants (keyed by position, so a driver keeps their shade as the
        // window scrolls) to make adjacent rows easy to tell apart.
        private static readonly XnaColor PlayerBg = new XnaColor(0x6E, 0x54, 0x10, 235);   // gold
        private static readonly XnaColor LapAheadBg = new XnaColor(0x6E, 0x1A, 0x1A, 235); // red
        private static readonly XnaColor LapBehindBg = new XnaColor(0x1A, 0x30, 0x6E, 235);// blue
        private static readonly XnaColor[] RowShades =
        {
            new XnaColor(0x1A, 0x1A, 0x28, 235),
            new XnaColor(0x22, 0x22, 0x32, 235),
            new XnaColor(0x1C, 0x24, 0x32, 235),
            new XnaColor(0x24, 0x1C, 0x32, 235),
        };

        private struct StandingsRow
        {
            public int Pos;
            public string Name;
            public string LastLap;
            public string Gap;
            public int LapDelta;   // whole laps this car is ahead (+) / behind (-) the player; race only
            public bool IsPlayer;
        }

        private readonly SpriteBatch _sb;
        private readonly Texture2D _white;
        private readonly SpriteFont _font;     // title
        private readonly SpriteFont _fontBody; // rows
        private readonly int _collapsedWidth;
        private readonly int _carsToDisplay;

        private readonly List<StandingsRow> _rows = new();
        private string _sessionLabel = "No Session";
        private string _lastSnapshot = string.Empty;

        public override int CollapsedWidth => _collapsedWidth;
        public override int CollapsedHeight => TitleBarHeight;

        private static int HeightFor(int cars) =>
            TitleBarHeight + Pad + cars * (RowHeight + RowSpacing) + Pad;

        public MainOverlay(GraphicsDevice device, int x, int y,
                           int carsToDisplay = DefaultCarsToDisplay)
            : base(device, PanelWidth, HeightFor(Math.Max(1, carsToDisplay)), x, y)
        {
            Name = "Main";
            _carsToDisplay = Math.Max(1, carsToDisplay);
            _sb = new SpriteBatch(device);
            _white = new Texture2D(device, 1, 1);
            _white.SetData(new[] { XnaColor.White });
            _font = RuntimeSpriteFont.Bake(device, "Segoe UI", 30f);
            _fontBody = RuntimeSpriteFont.Bake(device, "Segoe UI", 24f);
            _collapsedWidth = (int)_font.MeasureString(Name).X + 32;
        }

        public override void Update(GameTime gameTime)
        {
            var svc = IracingService.Instance;
            string sessionType = svc.SessionType;
            bool isRace = sessionType.IndexOf("Race", StringComparison.OrdinalIgnoreCase) >= 0;
            _sessionLabel = string.IsNullOrEmpty(sessionType)
                ? (svc.IsConnected ? "Session" : "No Session")
                : sessionType;

            _rows.Clear();
            var player = svc.PlayerCar;
            var all = svc.Cars; // list object is replaced, never mutated — safe to enumerate

            if (player.CarIdx >= 0 && all.Count > 0)
                BuildRows(all, player, isRace);

            // Only redraw/republish when something visible actually changed.
            var sb = new StringBuilder(_sessionLabel);
            foreach (var r in _rows)
                sb.Append('|').Append(r.Pos).Append(r.Name).Append(r.LastLap)
                  .Append(r.Gap).Append(r.LapDelta).Append(r.IsPlayer ? '*' : ' ');
            string snapshot = sb.ToString();
            if (snapshot != _lastSnapshot)
            {
                _lastSnapshot = snapshot;
                Invalidate();
            }
        }

        private void BuildRows(IReadOnlyList<Car> all, Car player, bool isRace)
        {
            // Only cars in the player's class (multiclass sessions show a
            // single-class board, like iRacing's own relative/standings).
            var classCars = all
                .Where(c => player.CarClassId < 0 || c.CarClassId == player.CarClassId)
                .ToList();

            // Race: order by race position (unclassified cars last).
            // Practice/qualy: order by best lap (no time yet -> last).
            List<Car> ordered = isRace
                ? classCars.OrderBy(c => c.Position > 0 ? c.Position : int.MaxValue)
                           .ThenBy(c => c.CarIdx).ToList()
                : classCars.OrderBy(c => c.BestLapTime > 0 ? c.BestLapTime : float.MaxValue)
                           .ThenBy(c => c.CarIdx).ToList();

            int playerIdx = ordered.FindIndex(c => c.CarIdx == player.CarIdx);
            if (playerIdx < 0) playerIdx = 0;

            // Window of _carsToDisplay rows centered on the player, clamped to
            // the ends of the standings.
            int count = Math.Min(_carsToDisplay, ordered.Count);
            int first = Math.Clamp(playerIdx - count / 2, 0, Math.Max(0, ordered.Count - count));

            float playerDist = player.Lap + Math.Clamp(player.LapDistPct, 0f, 1f);

            for (int i = first; i < first + count; i++)
            {
                var car = ordered[i];
                bool isPlayer = car.CarIdx == player.CarIdx;

                int lapDelta = 0;
                if (isRace && !isPlayer && car.Lap >= 0 && player.Lap >= 0)
                {
                    float dist = car.Lap + Math.Clamp(car.LapDistPct, 0f, 1f);
                    lapDelta = (int)Math.Truncate(dist - playerDist);
                }

                _rows.Add(new StandingsRow
                {
                    Pos = isRace && car.ClassPosition > 0 ? car.ClassPosition : i + 1,
                    Name = car.DriverName,
                    LastLap = FormatLapTime(car.LastLapTime),
                    Gap = isPlayer ? "YOU" : FormatGap(car, player, isRace, lapDelta),
                    LapDelta = lapDelta,
                    IsPlayer = isPlayer,
                });
            }
        }

        /// <summary>Signed gap to the player: negative = ahead of you, positive = behind you.</summary>
        private static string FormatGap(Car car, Car player, bool isRace, int lapDelta)
        {
            if (isRace)
            {
                if (lapDelta >= 1) return $"+{lapDelta}L";
                if (lapDelta <= -1) return $"{lapDelta}L";
                if (car.F2Time <= 0 && player.F2Time <= 0) return "--";
                float diff = car.F2Time - player.F2Time; // more time behind leader = behind you
                return diff.ToString("+0.0;-0.0;0.0");
            }

            if (car.BestLapTime <= 0 || player.BestLapTime <= 0) return "--";
            float delta = car.BestLapTime - player.BestLapTime;
            return delta.ToString("+0.000;-0.000;0.000");
        }

        private static string FormatLapTime(float seconds)
        {
            if (seconds <= 0) return "--:--.---";
            int minutes = (int)(seconds / 60f);
            float rest = seconds - minutes * 60f;
            return $"{minutes}:{rest:00.000}";
        }

        public override void Render(GameTime gameTime)
        {
            if (IsCollapsed)
            {
                GraphicsDevice.Clear(XnaColor.Transparent);
                _sb.Begin();
                _sb.Draw(_white, new XnaRectangle(0, 0, CollapsedWidth, CollapsedHeight), TitleBg);
                _sb.DrawString(_font, Name, new XnaVector2(16, 8), TitleText);
                _sb.Draw(_white, new XnaRectangle(0, CollapsedHeight - 3, CollapsedWidth, 3), Accent);
                _sb.Draw(_white, new XnaRectangle(0, 0, CollapsedWidth, 2), Border);
                _sb.Draw(_white, new XnaRectangle(0, 0, 2, CollapsedHeight), Border);
                _sb.Draw(_white, new XnaRectangle(CollapsedWidth - 2, 0, 2, CollapsedHeight), Border);
                _sb.End();
                return;
            }

            GraphicsDevice.Clear(PanelBg);
            _sb.Begin();

            // Title bar shows the current session type.
            _sb.Draw(_white, new XnaRectangle(0, 0, Width, TitleBarHeight), TitleBg);
            _sb.DrawString(_font, $"{Name} - {_sessionLabel}", new XnaVector2(16, 8), TitleText);
            _sb.Draw(_white, new XnaRectangle(0, TitleBarHeight - 3, Width, 3), Accent);

            if (_rows.Count == 0)
            {
                _sb.DrawString(_fontBody, "Waiting for iRacing...",
                    new XnaVector2(ColPos, TitleBarHeight + Pad + 10), RowTextDim);
            }

            int y = TitleBarHeight + Pad;
            foreach (var row in _rows)
            {
                XnaColor bg;
                if (row.IsPlayer) bg = PlayerBg;
                else if (row.LapDelta >= 1) bg = LapAheadBg;
                else if (row.LapDelta <= -1) bg = LapBehindBg;
                else bg = RowShades[Math.Abs(row.Pos) % RowShades.Length];

                _sb.Draw(_white, new XnaRectangle(Pad, y, Width - Pad * 2, RowHeight), bg);

                float textY = y + (RowHeight - _fontBody.LineSpacing) / 2f;
                _sb.DrawString(_fontBody, row.Pos.ToString(), new XnaVector2(ColPos, textY), RowText);
                _sb.DrawString(_fontBody, TruncateName(row.Name), new XnaVector2(ColName, textY), RowText);

                var lastLapSize = _fontBody.MeasureString(row.LastLap);
                _sb.DrawString(_fontBody, row.LastLap,
                    new XnaVector2(ColLastLapRight - lastLapSize.X, textY), RowTextDim);

                var gapSize = _fontBody.MeasureString(row.Gap);
                _sb.DrawString(_fontBody, row.Gap,
                    new XnaVector2(ColGapRight - gapSize.X, textY), RowText);

                y += RowHeight + RowSpacing;
            }

            // Thin panel outline.
            _sb.Draw(_white, new XnaRectangle(0, 0, Width, 2), Border);
            _sb.Draw(_white, new XnaRectangle(0, Height - 2, Width, 2), Border);
            _sb.Draw(_white, new XnaRectangle(0, 0, 2, Height), Border);
            _sb.Draw(_white, new XnaRectangle(Width - 2, 0, 2, Height), Border);

            _sb.End();
        }

        /// <summary>Trim the driver name (with an ellipsis) so it never runs into the lap-time column.</summary>
        private string TruncateName(string name)
        {
            float maxWidth = ColLastLapRight - 120 - ColName;
            if (string.IsNullOrEmpty(name) || _fontBody.MeasureString(name).X <= maxWidth)
                return name;
            for (int len = name.Length - 1; len > 0; len--)
            {
                string candidate = name.Substring(0, len) + "..";
                if (_fontBody.MeasureString(candidate).X <= maxWidth)
                    return candidate;
            }
            return "..";
        }

        public override void Dispose()
        {
            _fontBody.Texture.Dispose();
            _font.Texture.Dispose();
            _white.Dispose();
            _sb.Dispose();
            base.Dispose();
        }
    }
}
