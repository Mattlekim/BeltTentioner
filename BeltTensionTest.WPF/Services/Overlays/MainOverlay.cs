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
        private const int PanelWidth = 820;
        private const int Pad = 8;

        // Player sector strip at the bottom (ported from IrachingHud's
        // RelativeBox): per-sector live/last times + delta vs the best lap,
        // and a projected lap column on the right.
        private const int SectorStripHeight = 110;
        private const int SectorColWidth = 110;

        // Column layout (x offsets inside the panel).
        private const int ColPos = 18;
        private const int ColName = 66;
        private const int ColLastLapRight = PanelWidth - 270; // right edge of "last lap"
        private const int ColRelRight = PanelWidth - 140;     // right edge of "rel" (on-track gap)
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
        private static readonly XnaColor PlayerBg = new XnaColor(0x1E, 0x5C, 0x38, 235);   // green
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
            public string Rel;     // on-track time gap to the player (EstTime-based)
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
            TitleBarHeight + Pad + cars * (RowHeight + RowSpacing) + Pad + SectorStripHeight;

        // Sector timing state. Written on the SDK telemetry thread
        // (PlayerCarUpdated), read on the UI thread (Update/Render) — every
        // access goes through _sectorLock.
        private readonly object _sectorLock = new();
        private readonly SectorTimer _sectorTimer = new();
        private double[]? _bestLapSectorTimes;   // sector Last values of the best valid lap
        private double _bestLapFromSectors = double.PositiveInfinity;

        // Sector-time colors, mirroring the RelativeBox rules.
        private static readonly XnaColor SecPurple = new XnaColor(0x8A, 0x2B, 0xE2); // best-lap sector
        private static readonly XnaColor SecGreen = new XnaColor(0x50, 0xC8, 0x78);  // personal sector best
        private static readonly XnaColor SecYellow = new XnaColor(0xFF, 0xD5, 0x2E); // slower
        private static readonly XnaColor SecRed = new XnaColor(0xE0, 0x50, 0x50);    // invalid / no time

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
            _collapsedWidth = (int)_font.MeasureString(Name).X + 60; // name + accent dot + padding

            // Sector timing runs at full telemetry rate (60 Hz) so boundary
            // crossings land inside the validity windows; the overlay's own
            // 30 fps Update would miss them at speed.
            _sectorTimer.LapCompleted = OnSectorLapCompleted;
            IracingService.Instance.PlayerCarUpdated += OnPlayerCarUpdated;
            IracingService.Instance.Disconnected += OnIracingDisconnected;
        }

        // SDK telemetry thread.
        private void OnPlayerCarUpdated(Car player)
        {
            var svc = IracingService.Instance;
            var starts = svc.SectorStartPcts;
            if (starts == null) return;
            lock (_sectorLock)
                _sectorTimer.Update(starts, svc.SessionTime, Math.Clamp(player.LapDistPct, 0f, 1f));
        }

        // Fires inside _sectorTimer.Update, so already under _sectorLock.
        private void OnSectorLapCompleted()
        {
            double total = 0;
            foreach (var s in _sectorTimer.Sectors)
            {
                if (!s.Valid || s.Last <= 0) return; // any bad sector → not a reference lap
                total += s.Last;
            }
            if (total < _bestLapFromSectors)
            {
                _bestLapFromSectors = total;
                _bestLapSectorTimes = _sectorTimer.Sectors.Select(s => s.Last).ToArray();
            }
        }

        private void OnIracingDisconnected()
        {
            lock (_sectorLock)
            {
                _sectorTimer.Reset();
                _bestLapSectorTimes = null;
                _bestLapFromSectors = double.PositiveInfinity;
            }
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
                  .Append(r.Rel).Append(r.Gap).Append(r.LapDelta).Append(r.IsPlayer ? '*' : ' ');

            // Sector strip state: the live sector time ticks while driving, so
            // this keeps the panel repainting during an active sector.
            lock (_sectorLock)
            {
                foreach (var s in _sectorTimer.Sectors)
                    sb.Append('|').Append(s.Active ? 'A' : 'i')
                      .Append((s.Active ? s.Current : s.Last).ToString("0.00"))
                      .Append(s.Valid ? 'v' : 'x');
                sb.Append('|').Append(_bestLapFromSectors.ToString("0.000"));
            }
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
                    Rel = isPlayer ? string.Empty : FormatRelative(car, player),
                    Gap = isPlayer ? string.Empty : FormatGap(car, player, isRace, lapDelta),
                    LapDelta = lapDelta,
                    IsPlayer = isPlayer,
                });
            }
        }

        /// <summary>
        /// On-track (relative) time gap to the player, like iRacing's relative
        /// box: positive = physically ahead of you on track, negative = behind.
        /// Based on CarIdxEstTime, wrap-corrected at the start/finish line.
        /// </summary>
        private static string FormatRelative(Car car, Car player)
        {
            if (car.EstTime <= 0 || player.EstTime <= 0) return "--";

            float delta = car.EstTime - player.EstTime;

            // When the pair straddles the start/finish line the raw delta is
            // off by a whole lap (one EstTime just reset to ~0, the other is
            // near a full lap). Fold any delta larger than half a lap back by
            // one lap. The fold length must be the class reference lap
            // (CarClassEstLapTime) — the scale EstTime itself is computed
            // against — not a driver's best lap, or the fold leaves a residual
            // error of the difference between the two.
            float lapTime = player.ClassEstLapTime > 0 ? player.ClassEstLapTime
                          : car.ClassEstLapTime > 0 ? car.ClassEstLapTime
                          : player.BestLapTime > 0 ? player.BestLapTime
                          : car.BestLapTime;
            if (lapTime > 0)
            {
                if (delta > lapTime * 0.5f) delta -= lapTime;
                else if (delta < -lapTime * 0.5f) delta += lapTime;
            }

            return delta.ToString("+0.0;-0.0;0.0");
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
            const int Radius = 16;
            const int RowRadius = 8;

            if (IsCollapsed)
            {
                GraphicsDevice.Clear(XnaColor.Transparent);
                _sb.Begin();
                var pill = new XnaRectangle(0, 0, CollapsedWidth, CollapsedHeight);
                MonoXRDraw.RoundedRect(_sb, pill, CollapsedHeight / 2, TitleBg);
                MonoXRDraw.RoundedRectOutline(_sb, pill, CollapsedHeight / 2, 2, Border);
                int dotR = 6;
                _sb.Draw(MonoXRDraw.Circle(GraphicsDevice, dotR),
                    new XnaRectangle(20 - dotR, CollapsedHeight / 2 - dotR, dotR * 2, dotR * 2), Accent);
                _sb.DrawString(_font, Name, new XnaVector2(34, (CollapsedHeight - _font.LineSpacing) / 2f), TitleText);
                _sb.End();
                return;
            }

            // Rounded panel over a transparent canvas, so the corners are
            // see-through in VR.
            GraphicsDevice.Clear(XnaColor.Transparent);
            _sb.Begin();

            var panel = new XnaRectangle(0, 0, Width, Height);
            MonoXRDraw.RoundedRect(_sb, panel, Radius, PanelBg);

            // Title bar shows the current session type, with a sheen, a
            // drop-shadowed title and a glowing accent strip underneath.
            MonoXRDraw.RoundedRect(_sb, new XnaRectangle(0, 0, Width, TitleBarHeight), Radius,
                                   TitleBg, roundBottom: false);
            MonoXRDraw.VerticalFade(_sb, new XnaRectangle(0, 0, Width, TitleBarHeight / 2), XnaColor.White * 0.05f);
            string title = $"{Name} - {_sessionLabel}";
            _sb.DrawString(_font, title, new XnaVector2(20, 10), XnaColor.Black * 0.45f);
            _sb.DrawString(_font, title, new XnaVector2(20, 8), TitleText);
            _sb.Draw(_white, new XnaRectangle(0, TitleBarHeight - 3, Width, 3), Accent);
            MonoXRDraw.VerticalFade(_sb, new XnaRectangle(0, TitleBarHeight, Width, 12), Accent * 0.25f);

            if (_rows.Count == 0)
            {
                _sb.DrawString(_fontBody, "Waiting for iRacing...",
                    new XnaVector2(ColPos, TitleBarHeight + Pad + 10), RowTextDim);
            }

            int y = TitleBarHeight + Pad;
            foreach (var row in _rows)
            {
                XnaColor bg, badge;
                if (row.IsPlayer) { bg = PlayerBg; badge = new XnaColor(0x2E, 0x8C, 0x57); }
                else if (row.LapDelta >= 1) { bg = LapAheadBg; badge = new XnaColor(0xA8, 0x30, 0x30); }
                else if (row.LapDelta <= -1) { bg = LapBehindBg; badge = new XnaColor(0x30, 0x50, 0xA8); }
                else { bg = RowShades[Math.Abs(row.Pos) % RowShades.Length]; badge = new XnaColor(0x32, 0x32, 0x48); }

                var rowRect = new XnaRectangle(Pad, y, Width - Pad * 2, RowHeight);
                MonoXRDraw.RoundedRect(_sb, rowRect, RowRadius, bg);
                // Subtle top sheen so rows read as raised cards.
                MonoXRDraw.VerticalFade(_sb, new XnaRectangle(rowRect.X + RowRadius, y, rowRect.Width - RowRadius * 2, RowHeight / 2),
                                        XnaColor.White * 0.04f);
                if (row.IsPlayer)
                    MonoXRDraw.RoundedRect(_sb, new XnaRectangle(rowRect.X, y + 4, 5, RowHeight - 8), 2,
                                           new XnaColor(0x50, 0xC8, 0x78));

                float textY = y + (RowHeight - _fontBody.LineSpacing) / 2f;

                // Position number in a rounded badge.
                var badgeRect = new XnaRectangle(rowRect.X + 8, y + 5, 42, RowHeight - 10);
                MonoXRDraw.RoundedRect(_sb, badgeRect, 6, badge);
                string pos = row.Pos.ToString();
                var posSize = _fontBody.MeasureString(pos);
                _sb.DrawString(_fontBody, pos,
                    new XnaVector2(badgeRect.X + (badgeRect.Width - posSize.X) / 2f, textY), RowText);

                _sb.DrawString(_fontBody, TruncateName(row.Name), new XnaVector2(ColName, textY), RowText);

                var lastLapSize = _fontBody.MeasureString(row.LastLap);
                _sb.DrawString(_fontBody, row.LastLap,
                    new XnaVector2(ColLastLapRight - lastLapSize.X, textY), RowTextDim);

                if (!string.IsNullOrEmpty(row.Rel))
                {
                    var relSize = _fontBody.MeasureString(row.Rel);
                    _sb.DrawString(_fontBody, row.Rel,
                        new XnaVector2(ColRelRight - relSize.X, textY), RowText);
                }

                var gapSize = _fontBody.MeasureString(row.Gap);
                _sb.DrawString(_fontBody, row.Gap,
                    new XnaVector2(ColGapRight - gapSize.X, textY), RowText);

                y += RowHeight + RowSpacing;
            }

            DrawSectorStrip();

            // Rounded panel outline.
            MonoXRDraw.RoundedRectOutline(_sb, panel, Radius, 2, Border);

            _sb.End();
        }

        /// <summary>
        /// Player sector strip at the bottom of the panel, ported from
        /// IrachingHud's RelativeBox: one column per sector (live time while
        /// in the sector; afterwards the last time colored red = invalid,
        /// purple = matches the best lap's sector, green = personal sector
        /// best, yellow = slower, with the delta to the best-lap sector
        /// underneath) and a projected-lap column on the right (completed
        /// sectors' actual times + bests for the rest).
        /// </summary>
        private void DrawSectorStrip()
        {
            int stripY = Height - SectorStripHeight;
            MonoXRDraw.HorizontalFade(_sb, new XnaRectangle(Pad + 4, stripY, Width - (Pad + 4) * 2, 2), Border);

            int headerY = stripY + 8;
            int timeY = headerY + 30;
            int deltaY = timeY + 32;

            lock (_sectorLock)
            {
                var sectors = _sectorTimer.Sectors;
                if (sectors.Count == 0)
                {
                    _sb.DrawString(_fontBody, "Waiting for sector data...",
                        new XnaVector2(Pad + 10, timeY), RowTextDim);
                    return;
                }

                double projected = 0, currentPace = 0, bestPossiblePace = 0;
                bool pastActive = false;

                for (int i = 0; i < sectors.Count; i++)
                {
                    var s = sectors[i];
                    int x = Pad + 10 + i * SectorColWidth;
                    _sb.DrawString(_fontBody, $"S{i + 1}", new XnaVector2(x + 10, headerY), RowTextDim);

                    double bls = _bestLapSectorTimes != null && i < _bestLapSectorTimes.Length
                        ? _bestLapSectorTimes[i] : double.PositiveInfinity;

                    if (s.Active)
                    {
                        // Live ticking time for the sector being driven.
                        _sb.DrawString(_fontBody, $"{s.Current:00.00}", new XnaVector2(x, timeY), RowText);
                        projected += s.Best;
                        pastActive = true;
                    }
                    else
                    {
                        projected += pastActive ? s.Best : s.Last;
                        if (!pastActive)
                        {
                            currentPace += s.Last;
                            if (!double.IsPositiveInfinity(bls)) bestPossiblePace += bls;
                        }

                        // Last time, colored by merit.
                        XnaColor timeColor = !s.Valid ? SecRed
                            : Math.Abs(s.Last - bls) < 0.005 ? SecPurple
                            : Math.Abs(s.Last - s.Best) < 0.005 ? SecGreen
                            : SecYellow;
                        _sb.DrawString(_fontBody, s.Last > 0 ? $"{s.Last:00.00}" : "--.--",
                            new XnaVector2(x, timeY), s.Last > 0 ? timeColor : RowTextDim);

                        // Delta to the best lap's sector.
                        if (s.Last <= 0)
                            _sb.DrawString(_fontBody, "-----", new XnaVector2(x, deltaY), SecRed);
                        else if (double.IsPositiveInfinity(bls))
                            _sb.DrawString(_fontBody, "-----", new XnaVector2(x, deltaY), RowTextDim);
                        else
                        {
                            double delta = s.Last - bls;
                            _sb.DrawString(_fontBody, delta.ToString("+0.00;-0.00;0.00"),
                                new XnaVector2(x, deltaY), delta <= 0 ? SecGreen : SecYellow);
                        }
                    }
                }

                // Projected lap on the right: completed sectors as driven,
                // the rest at their bests — plus delta to the best lap so far.
                float rightX = Width - Pad - 10;
                string projLabel = "Projected";
                _sb.DrawString(_fontBody, projLabel,
                    new XnaVector2(rightX - _fontBody.MeasureString(projLabel).X, headerY), RowTextDim);

                string projText = projected > 0 && projected < 2000 ? FormatLapTime((float)projected) : "--:--.---";
                _sb.DrawString(_fontBody, projText,
                    new XnaVector2(rightX - _fontBody.MeasureString(projText).X, timeY), RowText);

                if (bestPossiblePace > 0 && currentPace > 0)
                {
                    double deltaToBest = currentPace - bestPossiblePace;
                    string deltaText = deltaToBest.ToString("+0.000;-0.000;0.000");
                    _sb.DrawString(_fontBody, deltaText,
                        new XnaVector2(rightX - _fontBody.MeasureString(deltaText).X, deltaY),
                        deltaToBest <= 0 ? SecGreen : SecYellow);
                }
                else
                {
                    _sb.DrawString(_fontBody, "-----",
                        new XnaVector2(rightX - _fontBody.MeasureString("-----").X, deltaY), RowTextDim);
                }
            }
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
            IracingService.Instance.PlayerCarUpdated -= OnPlayerCarUpdated;
            IracingService.Instance.Disconnected -= OnIracingDisconnected;
            _fontBody.Texture.Dispose();
            _font.Texture.Dispose();
            _white.Dispose();
            _sb.Dispose();
            base.Dispose();
        }
    }
}
