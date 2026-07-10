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
    /// practice/qualifying it works as a relative box — ordered by physical
    /// track location with the player pinned to the middle row and the
    /// nearest cars ahead/behind around them — and gaps are best-lap deltas.
    /// </summary>
    public sealed class MainOverlay : OverlayRenderTarget
    {
        /// <summary>How many cars the panel shows (the player included).</summary>
        public const int DefaultCarsToDisplay = 7;

        private const int TitleBarHeight = 48;
        private const int HeaderRowHeight = 34; // column labels above the rows
        private const int RowHeight = 44;
        private const int RowSpacing = 3;
        private const int PanelWidth = 1010;
        private const int Pad = 8;

        // Player sector strip at the bottom (ported from IrachingHud's
        // RelativeBox): per-sector live/last times + delta vs the best lap,
        // and a projected lap column on the right.
        private const int SectorStripHeight = 110;
        private const int SectorColWidth = 110;

        // Columns. Fixed widths except Driver, which absorbs the leftover
        // panel width. The user can reorder them by dragging a column header
        // in edit mode; the order round-trips through ColumnOrder.
        private enum Col { Pos, Driver, Sectors, LastLap, Rel, Gap, Status }

        private static readonly Col[] DefaultColumnOrder =
            { Col.Pos, Col.Driver, Col.Status, Col.LastLap, Col.Rel, Col.Gap, Col.Sectors };

        private Col[] _colOrder = (Col[])DefaultColumnOrder.Clone();
        private int _dragCol = -1; // index into _colOrder being dragged in edit mode

        private const int ColGapX = 8;          // horizontal gap between cells
        private const int ColContentLeft = Pad + 8;
        private const int ColContentRight = PanelWidth - Pad - 10;

        private static int FixedWidth(Col c) => c switch
        {
            Col.Pos => 58,
            Col.Status => 100,
            Col.Sectors => 100,
            Col.LastLap => 135,
            Col.Rel => 105,
            Col.Gap => 125,
            _ => 0, // Driver: flexible
        };

        private static string HeaderOf(Col c) => c switch
        {
            Col.Pos => "POS",
            Col.Driver => "DRIVER",
            Col.Status => "STATUS",
            Col.Sectors => "SECTORS",
            Col.LastLap => "LAST LAP",
            Col.Rel => "REL",
            Col.Gap => "GAP",
            _ => string.Empty,
        };

        private static bool RightAligned(Col c) => c != Col.Pos && c != Col.Driver && c != Col.Status;

        /// <summary>Current cell layout: one (column, left, width) per column, in display order.</summary>
        private (Col Col, int Left, int Width)[] ColumnCells()
        {
            int flex = ColContentRight - ColContentLeft - ColGapX * (_colOrder.Length - 1);
            foreach (var c in _colOrder) flex -= FixedWidth(c);

            var cells = new (Col, int, int)[_colOrder.Length];
            int x = ColContentLeft;
            for (int i = 0; i < _colOrder.Length; i++)
            {
                int w = _colOrder[i] == Col.Driver ? Math.Max(80, flex) : FixedWidth(_colOrder[i]);
                cells[i] = (_colOrder[i], x, w);
                x += w + ColGapX;
            }
            return cells;
        }

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
            public byte[] Sectors; // one SecCode per track sector for this car
            public int LastCmp;    // last lap vs player's: -1 faster (red), +1 slower (green), 0 neutral
            public string Status;  // "PIT" / "PIT IN" / "PIT OUT" / ""
            public bool Hazard;    // triggering the yellow-flag or slow-car warning → flashing box
        }

        // Per-car sector bar color codes (StandingsRow.Sectors values).
        private const byte SecNone = 0;    // no completed pass yet
        private const byte SecSession = 1; // fastest this session (any car)  → purple
        private const byte SecPersonal = 2;// this car's session best         → green
        private const byte SecSlower = 3;  // completed, no improvement       → yellow
        private const byte SecInvalid = 4; // last pass invalid               → red

        private readonly SpriteBatch _sb;
        private readonly Texture2D _white;
        private readonly SpriteFont _font;     // title
        private readonly SpriteFont _fontBody; // rows
        private readonly SpriteFont _fontHead; // column headers
        private readonly int _collapsedWidth;
        private readonly int _carsToDisplay;

        private readonly List<StandingsRow> _rows = new();
        private string _sessionLabel = "No Session";
        private string _lastSnapshot = string.Empty;

        public override int CollapsedWidth => _collapsedWidth;
        public override int CollapsedHeight => TitleBarHeight;

        private static int HeightFor(int cars) =>
            TitleBarHeight + Pad + HeaderRowHeight + cars * (RowHeight + RowSpacing) + Pad + SectorStripHeight;

        // Sector timing state. Written on the SDK telemetry thread
        // (PlayerCarUpdated), read on the UI thread (Update/Render) — every
        // access goes through _sectorLock.
        private readonly object _sectorLock = new();
        private readonly SectorTimer _sectorTimer = new();
        private double[]? _bestLapSectorTimes;   // sector Last values of the best valid lap
        private double _bestLapFromSectors = double.PositiveInfinity;

        // Per-car sector timing for the row bars. Written on the SDK thread
        // (CarsUpdated), read on the UI thread — guarded by _carSectorLock.
        // _sessionBestSectors is the fastest valid time per sector across all
        // cars this session; everything resets when the session changes,
        // restarts (session clock jumps backwards) or iRacing disconnects.
        private readonly object _carSectorLock = new();
        private readonly Dictionary<int, SectorTimer> _carTimers = new();
        private double[] _sessionBestSectors = Array.Empty<double>();
        private double _lastSessionTime;

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
            _fontHead = RuntimeSpriteFont.Bake(device, "Segoe UI", 18f, System.Drawing.FontStyle.Bold);
            _collapsedWidth = (int)_font.MeasureString(Name).X + 60; // name + accent dot + padding

            // Sector timing runs at full telemetry rate (60 Hz) so boundary
            // crossings land inside the validity windows; the overlay's own
            // 30 fps Update would miss them at speed.
            _sectorTimer.LapCompleted = OnSectorLapCompleted;
            IracingService.Instance.PlayerCarUpdated += OnPlayerCarUpdated;
            IracingService.Instance.CarsUpdated += OnCarsUpdated;
            IracingService.Instance.SessionTypeChanged += OnSessionTypeChanged;
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

        // SDK telemetry thread: advance every car's sector timer and fold
        // completed times into the session-best-per-sector table.
        private void OnCarsUpdated(IReadOnlyList<Car> cars)
        {
            var svc = IracingService.Instance;
            var starts = svc.SectorStartPcts;
            if (starts == null || starts.Count == 0) return;
            double time = svc.SessionTime;

            lock (_carSectorLock)
            {
                // Session restart: the session clock jumping backwards means
                // the old times belong to a session that no longer exists.
                if (time < _lastSessionTime - 1) ResetCarSectorsLocked();
                _lastSessionTime = time;

                if (_sessionBestSectors.Length != starts.Count)
                {
                    _sessionBestSectors = new double[starts.Count];
                    Array.Fill(_sessionBestSectors, double.PositiveInfinity);
                }

                foreach (var car in cars)
                {
                    if (car.CarIdx < 0) continue;
                    if (!_carTimers.TryGetValue(car.CarIdx, out var timer))
                        _carTimers[car.CarIdx] = timer = new SectorTimer();
                    timer.Update(starts, time, Math.Clamp(car.LapDistPct, 0f, 1f));

                    for (int i = 0; i < timer.Sectors.Count && i < _sessionBestSectors.Length; i++)
                    {
                        var s = timer.Sectors[i];
                        if (!s.Active && s.Valid && s.Last > 0 && s.Last < _sessionBestSectors[i])
                            _sessionBestSectors[i] = s.Last;
                    }
                }
            }
        }

        private void OnSessionTypeChanged(string _) => ResetAllSectors();

        private void ResetAllSectors()
        {
            lock (_sectorLock)
            {
                _sectorTimer.Reset();
                _bestLapSectorTimes = null;
                _bestLapFromSectors = double.PositiveInfinity;
            }
            lock (_carSectorLock)
                ResetCarSectorsLocked();
        }

        // Callers hold _carSectorLock.
        private void ResetCarSectorsLocked()
        {
            _carTimers.Clear();
            Array.Fill(_sessionBestSectors, double.PositiveInfinity);
        }

        private void OnIracingDisconnected() => ResetAllSectors();

        // ----- Edit-mode column reordering -----------------------------------

        /// <summary>Raised when the user finishes dragging a column into a new order.</summary>
        public event Action? ColumnOrderChanged;

        /// <summary>
        /// Comma-separated column order for persistence, e.g.
        /// "Pos,Driver,LastLap,Rel,Gap,Sectors". Setting anything that is not
        /// a full permutation of the known columns is ignored.
        /// </summary>
        public string ColumnOrder
        {
            get => string.Join(",", _colOrder);
            set
            {
                if (string.IsNullOrWhiteSpace(value)) return;
                var parsed = new List<Col>();
                foreach (var part in value.Split(','))
                    if (Enum.TryParse(part.Trim(), true, out Col c) && !parsed.Contains(c))
                        parsed.Add(c);
                if (parsed.Count != DefaultColumnOrder.Length) return;
                _colOrder = parsed.ToArray();
                Invalidate();
            }
        }

        // A press on the header row grabs that column instead of dragging the
        // whole panel; anywhere else the panel moves as before.
        public override bool OnEditPress(int x, int y)
        {
            if (IsCollapsed) return false;
            int top = TitleBarHeight + Pad;
            if (y < top || y >= top + HeaderRowHeight) return false;
            var cells = ColumnCells();
            for (int i = 0; i < cells.Length; i++)
            {
                if (x >= cells[i].Left && x < cells[i].Left + cells[i].Width)
                {
                    _dragCol = i;
                    Invalidate();
                    return true;
                }
            }
            return false;
        }

        // Swap with a neighbor once the pointer crosses that neighbor's
        // center; loop so one fast drag event can jump several columns.
        public override void OnEditDrag(int x, int y)
        {
            if (_dragCol < 0) return;
            while (true)
            {
                var cells = ColumnCells();
                if (_dragCol > 0 &&
                    x < cells[_dragCol - 1].Left + cells[_dragCol - 1].Width / 2)
                {
                    (_colOrder[_dragCol - 1], _colOrder[_dragCol]) = (_colOrder[_dragCol], _colOrder[_dragCol - 1]);
                    _dragCol--;
                    Invalidate();
                    continue;
                }
                if (_dragCol < cells.Length - 1 &&
                    x > cells[_dragCol + 1].Left + cells[_dragCol + 1].Width / 2)
                {
                    (_colOrder[_dragCol + 1], _colOrder[_dragCol]) = (_colOrder[_dragCol], _colOrder[_dragCol + 1]);
                    _dragCol++;
                    Invalidate();
                    continue;
                }
                break;
            }
        }

        public override void OnEditRelease(int x, int y)
        {
            if (_dragCol < 0) return;
            _dragCol = -1;
            Invalidate();
            ColumnOrderChanged?.Invoke();
        }

        /// <summary>
        /// Color codes for one car's sector bars: the last completed pass per
        /// sector vs the session best (purple), the car's own best (green),
        /// slower (yellow), invalid (red) or never attempted (dim).
        /// </summary>
        private byte[] SectorCodesFor(int carIdx)
        {
            lock (_carSectorLock)
            {
                if (!_carTimers.TryGetValue(carIdx, out var timer) || timer.Sectors.Count == 0)
                    return Array.Empty<byte>();

                var codes = new byte[timer.Sectors.Count];
                for (int i = 0; i < codes.Length; i++)
                {
                    var s = timer.Sectors[i];
                    if (s.Passes == 0) codes[i] = SecNone;
                    else if (!s.Valid || s.Last <= 0) codes[i] = SecInvalid;
                    else if (i < _sessionBestSectors.Length && s.Last <= _sessionBestSectors[i] + 0.005)
                        codes[i] = SecSession;
                    else if (s.Last <= s.Best + 0.005) codes[i] = SecPersonal;
                    else codes[i] = SecSlower;
                }
                return codes;
            }
        }

        // Flash phase for the status column's hazard boxes (original cadence).
        private float _flasher;

        public override void Update(GameTime gameTime)
        {
            _flasher += (float)gameTime.ElapsedGameTime.TotalSeconds * 15f;
            if (_flasher > 2f) _flasher -= 2f;

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
            bool anyHazard = false;
            foreach (var r in _rows)
            {
                sb.Append('|').Append(r.Pos).Append(r.Name).Append(r.LastLap)
                  .Append(r.Rel).Append(r.Gap).Append(r.LapDelta).Append(r.IsPlayer ? '*' : ' ')
                  .Append(r.LastCmp).Append(r.Status).Append(r.Hazard ? 'H' : ' ');
                foreach (byte code in r.Sectors) sb.Append((char)('0' + code));
                anyHazard |= r.Hazard;
            }
            // A visible hazard box flashes, so fold the flash phase into the
            // snapshot to keep the panel repainting while one is on screen.
            if (anyHazard) sb.Append(_flasher > 1f ? 'F' : 'f');

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

            // Practice/qualy: relative ordering by track location, player
            // centered — a car's timed standing means nothing for who is
            // physically around you.
            if (!isRace)
            {
                BuildRelativeRows(classCars, player);
                return;
            }

            // Race: order by race position (unclassified cars last).
            List<Car> ordered = classCars
                .OrderBy(c => c.Position > 0 ? c.Position : int.MaxValue)
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

                var (status, hazard) = StatusFor(car.CarIdx);
                _rows.Add(new StandingsRow
                {
                    Pos = isRace && car.ClassPosition > 0 ? car.ClassPosition : i + 1,
                    Name = car.DriverName,
                    LastLap = FormatLapTime(car.LastLapTime),
                    Rel = isPlayer ? string.Empty : FormatRelative(car, player),
                    Gap = isPlayer ? string.Empty : FormatGap(car, player, isRace, lapDelta),
                    LapDelta = lapDelta,
                    IsPlayer = isPlayer,
                    Sectors = SectorCodesFor(car.CarIdx),
                    LastCmp = CompareLastLap(car, player, isPlayer),
                    Status = status,
                    Hazard = hazard,
                });
            }
        }

        /// <summary>
        /// Practice/qualifying rows: ordered by physical track location like
        /// iRacing's relative box. The player sits on the middle row with the
        /// nearest cars ahead of them above (farthest at the top) and the
        /// nearest behind below. Cars in the garage / not in world are hidden;
        /// the position badge still shows the timed (best lap) standing.
        /// </summary>
        private void BuildRelativeRows(List<Car> classCars, Car player)
        {
            var rankByIdx = classCars
                .OrderBy(c => c.BestLapTime > 0 ? c.BestLapTime : float.MaxValue)
                .ThenBy(c => c.CarIdx)
                .Select((c, i) => (c.CarIdx, Rank: i + 1))
                .ToDictionary(t => t.CarIdx, t => t.Rank);

            // Signed lap-fraction distance to the player, folded into
            // (-0.5, 0.5]: positive = physically ahead of you on track.
            float playerPct = Math.Clamp(player.LapDistPct, 0f, 1f);
            static float RelTo(float pct, float playerPct)
            {
                float d = pct - playerPct;
                if (d > 0.5f) d -= 1f;
                else if (d < -0.5f) d += 1f;
                return d;
            }

            var others = classCars
                .Where(c => c.CarIdx != player.CarIdx && !c.IsInGarage && c.LapDistPct >= 0)
                .Select(c => (Car: c, Rel: RelTo(Math.Clamp(c.LapDistPct, 0f, 1f), playerPct)))
                .ToList();

            int half = _carsToDisplay / 2;
            var ahead = others.Where(o => o.Rel > 0)
                .OrderBy(o => o.Rel).Take(half).ToList();
            var behind = others.Where(o => o.Rel <= 0)
                .OrderByDescending(o => o.Rel).Take(half).ToList();

            for (int i = ahead.Count - 1; i >= 0; i--)
                AddRelativeRow(ahead[i].Car, player, rankByIdx);
            AddRelativeRow(player, player, rankByIdx);
            foreach (var o in behind)
                AddRelativeRow(o.Car, player, rankByIdx);
        }

        private void AddRelativeRow(Car car, Car player, Dictionary<int, int> rankByIdx)
        {
            bool isPlayer = car.CarIdx == player.CarIdx;
            var (status, hazard) = StatusFor(car.CarIdx);
            _rows.Add(new StandingsRow
            {
                Pos = car.ClassPosition > 0 ? car.ClassPosition
                    : rankByIdx.TryGetValue(car.CarIdx, out int rank) ? rank : 0,
                Name = car.DriverName,
                LastLap = FormatLapTime(car.LastLapTime),
                Rel = isPlayer ? string.Empty : FormatRelative(car, player),
                Gap = isPlayer ? string.Empty : FormatGap(car, player, isRace: false, lapDelta: 0),
                LapDelta = 0,
                IsPlayer = isPlayer,
                Sectors = SectorCodesFor(car.CarIdx),
                LastCmp = CompareLastLap(car, player, isPlayer),
                Status = status,
                Hazard = hazard,
            });
        }

        /// <summary>Status cell content: estimated pit phase + whether this car is triggering a warning card.</summary>
        private static (string Text, bool Hazard) StatusFor(int carIdx)
        {
            var st = CarStatusMonitor.Instance.StatusOf(carIdx);
            string text = st.Pit switch
            {
                PitState.Pit => "PIT",
                PitState.PitIn => "PIT IN",
                PitState.PitOut => "PIT OUT",
                PitState.OutLap => "OUT LAP",
                _ => string.Empty,
            };
            return (text, st.YellowHazard || st.SlowHazard);
        }

        /// <summary>Last lap vs the player's: -1 = faster than you, +1 = slower, 0 = player row / no times.</summary>
        private static int CompareLastLap(Car car, Car player, bool isPlayer)
        {
            if (isPlayer || car.LastLapTime <= 0 || player.LastLapTime <= 0) return 0;
            return car.LastLapTime < player.LastLapTime ? -1
                 : car.LastLapTime > player.LastLapTime ? 1 : 0;
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

            // Column headers in the user's order (right-aligned labels line up
            // with the right-aligned values beneath them), with a thin rule
            // under the header row. In edit mode each header gets a faint box
            // (drag handle hint) and a dragged column is highlighted.
            var cells = ColumnCells();
            int headTop = TitleBarHeight + Pad;
            int headY = headTop + (HeaderRowHeight - _fontHead.LineSpacing) / 2;
            int rowsBottom = headTop + HeaderRowHeight + _rows.Count * (RowHeight + RowSpacing);

            for (int i = 0; i < cells.Length; i++)
            {
                var (col, cl, cw) = cells[i];
                bool dragging = EditMode && i == _dragCol;

                if (EditMode)
                {
                    var handle = new XnaRectangle(cl - 3, headTop, cw + 6, HeaderRowHeight - 4);
                    MonoXRDraw.RoundedRect(_sb, handle, 6, dragging ? Accent * 0.35f : XnaColor.White * 0.06f);
                    if (dragging) // shade the whole column while it is being moved
                        _sb.Draw(_white, new XnaRectangle(cl - 3, headTop + HeaderRowHeight,
                            cw + 6, Math.Max(0, rowsBottom - headTop - HeaderRowHeight)), Accent * 0.12f);
                }

                string label = HeaderOf(col);
                float lx = RightAligned(col) ? cl + cw - _fontHead.MeasureString(label).X : cl;
                _sb.DrawString(_fontHead, label, new XnaVector2(lx, headY), dragging ? Accent : RowTextDim);
            }
            MonoXRDraw.HorizontalFade(_sb,
                new XnaRectangle(Pad + 4, headTop + HeaderRowHeight - 4, Width - (Pad + 4) * 2, 2), Border);

            if (_rows.Count == 0)
            {
                _sb.DrawString(_fontBody, "Waiting for iRacing...",
                    new XnaVector2(ColContentLeft + 10, headTop + HeaderRowHeight + 10), RowTextDim);
            }

            int y = headTop + HeaderRowHeight;
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

                foreach (var (col, cl, cw) in cells)
                {
                    switch (col)
                    {
                        case Col.Pos:
                        {
                            // Position number in a rounded badge.
                            var badgeRect = new XnaRectangle(cl, y + 5, Math.Min(42, cw), RowHeight - 10);
                            MonoXRDraw.RoundedRect(_sb, badgeRect, 6, badge);
                            string pos = row.Pos.ToString();
                            var posSize = _fontBody.MeasureString(pos);
                            _sb.DrawString(_fontBody, pos,
                                new XnaVector2(badgeRect.X + (badgeRect.Width - posSize.X) / 2f, textY), RowText);
                            break;
                        }
                        case Col.Driver:
                            _sb.DrawString(_fontBody, TruncateText(row.Name, cw),
                                new XnaVector2(cl, textY), RowText);
                            break;
                        case Col.Status:
                        {
                            // A car triggering the yellow-flag / slow-car card
                            // gets a flashing yellow box; pit phases are text.
                            bool boxOn = row.Hazard && _flasher > 1f;
                            if (boxOn)
                                MonoXRDraw.RoundedRect(_sb,
                                    new XnaRectangle(cl - 2, y + 5, cw + 4, RowHeight - 10), 6, SecYellow);
                            if (!string.IsNullOrEmpty(row.Status))
                            {
                                XnaColor c = boxOn ? new XnaColor(0x1A, 0x14, 0x00)
                                    : row.Status == "PIT OUT" ? SecGreen
                                    : row.Status == "PIT IN" ? SecYellow
                                    : row.Status == "OUT LAP" ? Accent
                                    : RowTextDim;
                                float sy = y + (RowHeight - _fontHead.LineSpacing) / 2f;
                                _sb.DrawString(_fontHead, row.Status, new XnaVector2(cl + 4, sy), c);
                            }
                            break;
                        }
                        case Col.Sectors:
                        {
                            // One thin vertical bar per sector, right-aligned
                            // in the cell, colored by that car's last pass
                            // (see SectorCodesFor).
                            if (row.Sectors == null || row.Sectors.Length == 0) break;
                            const int BarW = 10, BarGap = 5;
                            int barH = RowHeight - 14;
                            int barX = cl + cw - row.Sectors.Length * (BarW + BarGap) + BarGap;
                            foreach (byte code in row.Sectors)
                            {
                                XnaColor c = code switch
                                {
                                    SecSession => SecPurple,
                                    SecPersonal => SecGreen,
                                    SecSlower => SecYellow,
                                    SecInvalid => SecRed,
                                    _ => new XnaColor(0x3A, 0x3A, 0x52),
                                };
                                MonoXRDraw.RoundedRect(_sb, new XnaRectangle(barX, y + 7, BarW, barH), 3, c);
                                barX += BarW + BarGap;
                            }
                            break;
                        }
                        case Col.LastLap:
                        {
                            // Faster last lap than yours = red (threat),
                            // slower = green, no comparison = dim.
                            XnaColor c = row.LastCmp < 0 ? SecRed
                                       : row.LastCmp > 0 ? SecGreen : RowTextDim;
                            DrawRight(row.LastLap, cl + cw, textY, c);
                            break;
                        }
                        case Col.Rel:
                            if (!string.IsNullOrEmpty(row.Rel))
                                DrawRight(row.Rel, cl + cw, textY, RowText);
                            break;
                        case Col.Gap:
                            if (!string.IsNullOrEmpty(row.Gap))
                                DrawRight(row.Gap, cl + cw, textY, RowText);
                            break;
                    }
                }

                y += RowHeight + RowSpacing;
            }

            DrawSectorStrip();

            void DrawRight(string text, int rightEdge, float textY, XnaColor color)
            {
                _sb.DrawString(_fontBody, text,
                    new XnaVector2(rightEdge - _fontBody.MeasureString(text).X, textY), color);
            }

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

        /// <summary>Trim text (with an ellipsis) so it never overflows its column cell.</summary>
        private string TruncateText(string name, float maxWidth)
        {
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
            IracingService.Instance.CarsUpdated -= OnCarsUpdated;
            IracingService.Instance.SessionTypeChanged -= OnSessionTypeChanged;
            IracingService.Instance.Disconnected -= OnIracingDisconnected;
            _fontHead.Texture.Dispose();
            _fontBody.Texture.Dispose();
            _font.Texture.Dispose();
            _white.Dispose();
            _sb.Dispose();
            base.Dispose();
        }
    }
}
