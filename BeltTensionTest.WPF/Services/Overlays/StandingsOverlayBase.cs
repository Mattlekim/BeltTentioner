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
    /// Shared chrome and plumbing for the in-VR relative boxes ported from
    /// IrachingHud's RelativeBox (<see cref="RaceOverlay"/> and
    /// <see cref="QualifyingOverlay"/>): a titled panel with an optional info
    /// bar, a header row of user-reorderable columns, a window of cars around
    /// the player ordered by physical track location (farthest ahead at the
    /// top, player in the middle, nearest behind below — like the original),
    /// and an optional footer. Each panel only shows during its own session
    /// types; edit mode always draws it so it can be placed.
    ///
    /// Also owns sector timing for every car (row sector bars, the session's
    /// fastest-lap sectors) and a full-rate timer for the player's own
    /// sector strip.
    /// </summary>
    public abstract class StandingsOverlayBase : OverlayRenderTarget
    {
        /// <summary>How many cars the panel shows (the player included).</summary>
        public const int DefaultCarsToDisplay = 7;

        protected const int TitleBarHeight = 48;
        protected const int HeaderRowHeight = 34; // column labels above the rows
        protected const int RowHeight = 44;
        protected const int RowSpacing = 3;
        /// <summary>Panel width the relative boxes use; a box can ask for another.</summary>
        public const int DefaultPanelWidth = 1200;
        protected const int Pad = 8;

        // Every column either box can show. Fixed widths except Driver, which
        // absorbs the leftover panel width. The user can reorder a box's
        // columns by dragging a header in edit mode; the order round-trips
        // through ColumnOrder.
        protected enum Col { Pos, License, IRating, Driver, Status, LastLap, BestLap, CurLap, Rel, Gap, Tire, Gained, Stint, Sectors, LiveSectors }

        /// <summary>This box's columns, in their default order.</summary>
        protected abstract Col[] DefaultColumns { get; }

        private Col[] _colOrder;
        private int _dragCol = -1; // index into _colOrder being dragged in edit mode

        private const int ColGapX = 8;          // horizontal gap between cells
        private const int ColContentLeft = Pad + 8;
        private int ColContentRight => Width - Pad - 10;

        private static int FixedWidth(Col c) => c switch
        {
            Col.Pos => 58,
            Col.License => 92,
            Col.IRating => 72,
            Col.Status => 100,
            Col.LastLap => 125,
            Col.BestLap => 125,
            Col.CurLap => 135,
            Col.Rel => 90,
            Col.Gap => 100,
            Col.Tire => 30,
            Col.Gained => 52,
            Col.Stint => 60,
            Col.Sectors => 100,
            Col.LiveSectors => 300,
            _ => 0, // Driver: flexible
        };

        private static string HeaderOf(Col c) => c switch
        {
            Col.Pos => "POS",
            Col.License => "LIC",
            Col.IRating => "IR",
            Col.Driver => "DRIVER",
            Col.Status => "STATUS",
            Col.LastLap => "LAST LAP",
            Col.BestLap => "BEST LAP",
            Col.CurLap => "CURRENT",
            Col.Rel => "REL",
            Col.Gap => "GAP",
            Col.Tire => "T",
            Col.Gained => "+/-",
            Col.Stint => "STINT",
            Col.Sectors => "SECTORS",
            Col.LiveSectors => "SECTOR TIMES",
            _ => string.Empty,
        };

        private static bool RightAligned(Col c) =>
            c is Col.IRating or Col.LastLap or Col.BestLap or Col.CurLap or Col.Rel or Col.Gap
              or Col.Stint or Col.Sectors or Col.LiveSectors;

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
        protected static readonly XnaColor PanelBg = new XnaColor(0x12, 0x12, 0x1E, 235);
        protected static readonly XnaColor TitleBg = new XnaColor(0x1C, 0x1C, 0x2E, 245);
        protected static readonly XnaColor TitleText = new XnaColor(0xD0, 0xD0, 0xF0);
        protected static readonly XnaColor Accent = new XnaColor(0x64, 0x96, 0xFF);
        protected static readonly XnaColor Border = new XnaColor(0x46, 0x46, 0x6A);
        protected static readonly XnaColor RowText = new XnaColor(0xE6, 0xE6, 0xF5);
        protected static readonly XnaColor RowTextDim = new XnaColor(0xA0, 0xA0, 0xBE);
        protected static readonly XnaColor DarkText = new XnaColor(0x1A, 0x14, 0x00);

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

        // Sector / lap-time colors, mirroring the RelativeBox rules.
        protected static readonly XnaColor SecPurple = new XnaColor(0x8A, 0x2B, 0xE2); // session best
        protected static readonly XnaColor SecGreen = new XnaColor(0x50, 0xC8, 0x78);  // personal best
        protected static readonly XnaColor SecYellow = new XnaColor(0xFF, 0xD5, 0x2E); // slower
        protected static readonly XnaColor SecRed = new XnaColor(0xE0, 0x50, 0x50);    // invalid / no time
        protected static readonly XnaColor SlowAmber = new XnaColor(0xFF, 0x9E, 0x2E);

        /// <summary>One displayed car. Strings are pre-formatted; empty = blank cell.</summary>
        protected struct Row
        {
            public int CarIdx;       // the car this row is about; -1 for a divider
            public int Pos;          // 0 = no position yet (blank badge)
            public int ClassColor;   // 0xRRGGBB tint for the position badge in multiclass; -1 = none
            public string Name;
            public string License;   // "A 3.45"
            public int LicColor;     // 0xRRGGBB; -1 = none
            public bool LowSafety;   // safety rating below 2.00 (the original's red license text)
            public string IRating;   // "2345", "AI" or ""
            public string LastLap;
            public int LastCmp;      // last lap vs player's: -1 faster (red), +1 slower (green), 0 neutral
            public string BestLap;
            public bool BestIsFastest; // holds the fastest lap of the player's class
            public string CurLap;    // time into the lap being driven; empty = not on a lap
            public SectorCell[] LiveSectors; // this lap's sector times (Col.LiveSectors)
            public string Rel;       // on-track time gap to the player (EstTime-based)
            public string Gap;
            public int GapCmp;       // gap vs the reference: -1 under (green), +1 over (yellow), 0 plain
            public int LapDelta;     // whole laps ahead (+) / behind (-) the player; race only
            public bool IsPlayer;
            public int TagColor;     // friend / rival tint, 0xRRGGBB; -1 = untagged
            public bool Highlight;   // pointer is on this row (or its menu is open)
            public byte[] Sectors;   // one SecCode per track sector for this car
            public string Status;    // "PIT" / "PIT IN" / "PIT OUT" / "OUT LAP" / ""
            public bool Hazard;      // triggering the yellow-flag warning → flashing box
            public bool Slow;        // triggering the slow-car warning
            public bool Finished;    // took the checkered flag (race)
            public string Tire;      // "S" / "W" / ""
            public int? Gained;      // positions gained since the start (race)
            public string Stint;     // laps since the last pit stop (race)
            public bool Divider;     // not a car: the standings window break marker

            public void AppendTo(StringBuilder sb)
            {
                sb.Append('|').Append(CarIdx).Append(',').Append(Pos).Append(',').Append(ClassColor).Append(Name).Append(License)
                  .Append(LicColor).Append(LowSafety ? 'l' : 'L').Append(IRating).Append(LastLap)
                  .Append(LastCmp).Append(BestLap).Append(BestIsFastest ? 'F' : 'f').Append(Rel)
                  .Append(Gap).Append(GapCmp).Append(LapDelta).Append(IsPlayer ? '*' : ' ').Append(Status)
                  .Append(Hazard ? 'H' : 'h').Append(Slow ? 'S' : 's').Append(Finished ? 'C' : 'c')
                  .Append(Tire).Append(Gained?.ToString() ?? "~").Append(',').Append(Stint)
                  .Append(CurLap).Append(Divider ? 'D' : 'd')
                  .Append(TagColor).Append(Highlight ? 'H' : 'h');
                if (Sectors != null)
                    foreach (byte code in Sectors) sb.Append((char)('0' + code));
                if (LiveSectors != null)
                    foreach (var c in LiveSectors)
                        sb.Append(c.Active ? 'A' : 'i').Append(c.Code).Append(c.Time.ToString("0.00"));
            }
        }

        // Per-car sector bar color codes (Row.Sectors values).
        private const byte SecNone = 0;    // no completed pass yet
        private const byte SecSession = 1; // fastest this session (any car)  → purple
        private const byte SecPersonal = 2;// this car's session best         → green
        private const byte SecSlower = 3;  // completed, no improvement       → yellow
        private const byte SecInvalid = 4; // last pass invalid               → red

        private readonly SpriteBatch _sb;
        private readonly Texture2D _white;
        protected readonly SpriteFont Font;     // title
        protected readonly SpriteFont FontBody; // rows
        protected readonly SpriteFont FontHead; // column headers / small labels
        protected int CarsToDisplay { get; private set; }
        private readonly int _infoBarHeight;
        private readonly int _footerHeight;

        protected readonly List<Row> Rows = new();
        protected int RowOffset;       // empty rows above the first car (keeps the player centered)
        private string _sessionLabel = "No Session";

        /// <summary>
        /// Name in the title bar and the collapsed pill. Defaults to
        /// <see cref="OverlayRenderTarget.Name"/> - which is also the key the
        /// toggle bindings and show modes are saved under, so it must never
        /// change. A box whose subject depends on the session (the
        /// practice/qualifying box) overrides this instead of renaming itself.
        /// </summary>
        protected virtual string DisplayName => Name;

        /// <summary>The iRacing session type this frame ("Practice", "Qualify", "Race", ...).</summary>
        protected string CurrentSessionType { get; private set; } = string.Empty;
        private string _lastSnapshot = string.Empty;
        private bool _active;          // connected and in one of this box's session types
        protected float Flasher;       // original cadence: dt*15, cycle 2
        protected bool Multiclass;     // more than one car class in the session

        public override int CollapsedWidth => (int)Font.MeasureString(DisplayName).X + 60;
        public override int CollapsedHeight => TitleBarHeight;

        protected SpriteBatch Batch => _sb;
        protected Texture2D White => _white;

        /// <summary>Panel height that holds <paramref name="rows"/> car rows plus the chrome.</summary>
        private static int PanelHeightFor(int rows, int infoBarHeight, int footerHeight) =>
            TitleBarHeight + infoBarHeight + Pad + HeaderRowHeight
            + Math.Max(1, rows) * (RowHeight + RowSpacing) + Pad + footerHeight;

        protected StandingsOverlayBase(GraphicsDevice device, string name, int x, int y,
                                       int carsToDisplay, int infoBarHeight, int footerHeight,
                                       int panelWidth = DefaultPanelWidth)
            : base(device, panelWidth, PanelHeightFor(carsToDisplay, infoBarHeight, footerHeight), x, y)
        {
            Name = name;
            CarsToDisplay = Math.Max(1, carsToDisplay);
            _infoBarHeight = infoBarHeight;
            _footerHeight = footerHeight;
            _colOrder = (Col[])DefaultColumns.Clone();
            _sb = new SpriteBatch(device);
            _white = new Texture2D(device, 1, 1);
            _white.SetData(new[] { XnaColor.White });
            Font = RuntimeSpriteFont.Bake(device, "Segoe UI", 30f);
            FontBody = RuntimeSpriteFont.Bake(device, "Segoe UI", 24f);
            FontHead = RuntimeSpriteFont.Bake(device, "Segoe UI", 18f, System.Drawing.FontStyle.Bold);

            // Sector timing runs at full telemetry rate (60 Hz) so boundary
            // crossings land inside the validity windows; the overlay's own
            // 30 fps Update would miss them at speed.
            PlayerSectors.LapCompleted = OnPlayerLapCompleted;
            IracingService.Instance.PlayerCarUpdated += OnPlayerCarUpdated;
            IracingService.Instance.CarsUpdated += OnCarsUpdated;
            IracingService.Instance.SessionTypeChanged += OnSessionTypeChanged;
            IracingService.Instance.Disconnected += OnIracingDisconnected;
        }

        /// <summary>
        /// Grow or shrink the panel so it holds exactly <paramref name="rows"/>
        /// rows. The standings box calls this as the field size changes; the
        /// relative boxes keep the count they were built with. Safe from
        /// Update/Render, which run on the render thread.
        /// </summary>
        protected void SetRowCapacity(int rows)
        {
            rows = Math.Max(1, rows);
            if (rows == CarsToDisplay) return;
            CarsToDisplay = rows;
            Resize(Width, PanelHeightFor(rows, _infoBarHeight, _footerHeight));
        }

        /// <summary>True when this box belongs to the given iRacing session type.</summary>
        protected abstract bool IsSessionActive(string sessionType);

        /// <summary>Fill <see cref="Rows"/> for this frame (player is valid, cars non-empty).</summary>
        protected abstract void BuildRows(IReadOnlyList<Car> all, Car player);

        /// <summary>Append any extra visible state (info bar / footer) so changes trigger a repaint.</summary>
        protected virtual void AppendSnapshot(StringBuilder sb) { }

        /// <summary>Draw the info bar under the title (height given to the constructor).</summary>
        protected virtual void DrawInfoBar(XnaRectangle area) { }

        /// <summary>Draw the footer under the rows (height given to the constructor).</summary>
        protected virtual void DrawFooter(XnaRectangle area) { }

        /// <summary>Draw on top of the rows, inside the panel's SpriteBatch pass.</summary>
        protected virtual void DrawPopups() { }

        /// <summary>Message shown instead of rows when there are none.</summary>
        protected abstract string EmptyMessage { get; }

        /// <summary>Called when the session changes / restarts / iRacing disconnects.</summary>
        protected virtual void OnSessionReset() { }

        // ----- Sector timing -------------------------------------------------
        // Written on the SDK telemetry thread, read on the UI thread; every
        // access goes through SectorLock.

        protected readonly object SectorLock = new();

        /// <summary>The player's own sector timer, fed at full telemetry rate.</summary>
        protected readonly SectorTimer PlayerSectors = new();

        private readonly Dictionary<int, SectorTimer> _carTimers = new();
        private double[] _sessionBestSectors = Array.Empty<double>();
        private readonly Dictionary<int, double> _carBestLapFromSectors = new();

        /// <summary>
        /// Sector times of the session's fastest complete, all-valid lap by
        /// any car (the original's "best lap sectors"); null until one exists.
        /// </summary>
        protected double[]? FastestLapSectors;
        protected double FastestLapFromSectors = double.PositiveInfinity;
        private double _lastSessionTime;

        // SDK telemetry thread.
        private void OnPlayerCarUpdated(Car player)
        {
            var svc = IracingService.Instance;
            var starts = svc.SectorStartPcts;
            if (starts == null) return;
            lock (SectorLock)
                PlayerSectors.Update(starts, svc.SessionTime, Math.Clamp(player.LapDistPct, 0f, 1f));
        }

        // Fires inside PlayerSectors.Update, so already under SectorLock.
        // Folds the player's lap into the fastest-lap reference too, so it
        // updates the moment the player crosses the line.
        private void OnPlayerLapCompleted() => FoldLap(IracingService.Instance.PlayerCar.CarIdx, PlayerSectors);

        // Callers hold SectorLock. A lap counts only if every sector was valid.
        private void FoldLap(int carIdx, SectorTimer timer)
        {
            double total = 0;
            foreach (var s in timer.Sectors)
            {
                if (!s.Valid || s.Last <= 0) return;
                total += s.Last;
            }
            if (carIdx >= 0)
            {
                if (_carBestLapFromSectors.TryGetValue(carIdx, out double best) && best <= total) return;
                _carBestLapFromSectors[carIdx] = total;
            }
            if (total < FastestLapFromSectors)
            {
                FastestLapFromSectors = total;
                FastestLapSectors = timer.Sectors.Select(s => s.Last).ToArray();
            }
        }

        // SDK telemetry thread: advance every car's sector timer and fold
        // completed times into the session-best tables.
        private void OnCarsUpdated(IReadOnlyList<Car> cars)
        {
            var svc = IracingService.Instance;
            var starts = svc.SectorStartPcts;
            if (starts == null || starts.Count == 0) return;
            double time = svc.SessionTime;
            bool reset = false;

            lock (SectorLock)
            {
                // Session restart: the session clock jumping backwards means
                // the old times belong to a session that no longer exists.
                if (time < _lastSessionTime - 1) { ResetSectorsLocked(); reset = true; }
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
                    {
                        _carTimers[car.CarIdx] = timer = new SectorTimer();
                        int idx = car.CarIdx;
                        timer.LapCompleted = () => FoldLap(idx, timer);
                    }
                    timer.Update(starts, time, Math.Clamp(car.LapDistPct, 0f, 1f));

                    for (int i = 0; i < timer.Sectors.Count && i < _sessionBestSectors.Length; i++)
                    {
                        var s = timer.Sectors[i];
                        if (!s.Active && s.Valid && s.Last > 0 && s.Last < _sessionBestSectors[i])
                            _sessionBestSectors[i] = s.Last;
                    }
                }
            }
            if (reset) OnSessionReset();
        }

        private void OnSessionTypeChanged(string _) => ResetAll();
        private void OnIracingDisconnected() => ResetAll();

        private void ResetAll()
        {
            lock (SectorLock) ResetSectorsLocked();
            OnSessionReset();
        }

        // Callers hold SectorLock.
        private void ResetSectorsLocked()
        {
            PlayerSectors.Reset();
            _carTimers.Clear();
            _carBestLapFromSectors.Clear();
            Array.Fill(_sessionBestSectors, double.PositiveInfinity);
            FastestLapSectors = null;
            FastestLapFromSectors = double.PositiveInfinity;
        }

        /// <summary>
        /// Color codes for one car's sector bars: the last completed pass per
        /// sector vs the session best (purple), the car's own best (green),
        /// slower (yellow), invalid (red) or never attempted (dim).
        /// </summary>
        protected byte[] SectorCodesFor(int carIdx)
        {
            lock (SectorLock)
            {
                if (!_carTimers.TryGetValue(carIdx, out var timer) || timer.Sectors.Count == 0)
                    return Array.Empty<byte>();

                var codes = new byte[timer.Sectors.Count];
                for (int i = 0; i < codes.Length; i++)
                    codes[i] = MeritLocked(timer.Sectors[i], i);
                return codes;
            }
        }

        /// <summary>One sector of a car's current lap, as the rows show it.</summary>
        protected readonly struct SectorCell
        {
            /// <summary>Seconds: the live time while in the sector, the last time once past it; 0 = none.</summary>
            public readonly double Time;
            /// <summary>True while the car is inside this sector right now.</summary>
            public readonly bool Active;
            /// <summary>Merit code (the Sec* constants); SecNone = not driven yet this lap.</summary>
            public readonly byte Code;

            public SectorCell(double time, bool active, byte code)
            {
                Time = time; Active = active; Code = code;
            }
        }

        /// <summary>
        /// This lap's sector times for one car: the sectors already completed
        /// since it crossed the line (colored by merit), the one being driven
        /// (ticking), and blanks for the ones still ahead. Times from the
        /// previous lap are deliberately not shown - the lap in progress is
        /// what the standings box is reporting on.
        /// </summary>
        protected SectorCell[] SectorCellsFor(int carIdx)
        {
            lock (SectorLock)
            {
                if (!_carTimers.TryGetValue(carIdx, out var timer) || timer.Sectors.Count == 0)
                    return Array.Empty<SectorCell>();

                int active = ActiveSectorLocked(timer);
                var cells = new SectorCell[timer.Sectors.Count];
                for (int i = 0; i < cells.Length; i++)
                {
                    var s = timer.Sectors[i];
                    if (i == active) cells[i] = new SectorCell(s.Current, true, SecNone);
                    else if (active >= 0 && i > active) cells[i] = new SectorCell(0, false, SecNone);
                    else cells[i] = new SectorCell(s.Last, false, MeritLocked(s, i));
                }
                return cells;
            }
        }

        /// <summary>
        /// How far into the current lap a car is, and how that lap compares
        /// with the session's fastest one so far. The comparison only counts
        /// the sectors completed since the car crossed the line, so it steps
        /// at each sector boundary rather than drifting between them.
        /// <c>Delta</c> only means anything when <c>HasDelta</c> is set, and
        /// <c>Elapsed</c> only when <c>Running</c> is.
        /// </summary>
        protected (double Elapsed, bool Running, double Delta, bool HasDelta) LapProgressFor(int carIdx)
        {
            lock (SectorLock)
            {
                if (!_carTimers.TryGetValue(carIdx, out var timer) || timer.Sectors.Count == 0)
                    return (0, false, 0, false);

                int active = ActiveSectorLocked(timer);
                if (active < 0) return (0, false, 0, false);

                var reference = FastestLapSectors;
                double elapsed = timer.Sectors[active].Current;
                double delta = 0;
                bool running = true, hasDelta = active > 0 && reference != null;

                for (int i = 0; i < active; i++)
                {
                    double last = timer.Sectors[i].Last;
                    if (last <= 0) { running = false; hasDelta = false; break; }
                    elapsed += last;
                    if (!hasDelta) continue;
                    if (i >= reference!.Length || double.IsPositiveInfinity(reference[i])) hasDelta = false;
                    else delta += last - reference[i];
                }
                return (elapsed, running, delta, hasDelta);
            }
        }

        // Callers hold SectorLock. -1 when the car is not inside any sector
        // (not in the world, or between passes).
        private static int ActiveSectorLocked(SectorTimer timer)
        {
            for (int i = 0; i < timer.Sectors.Count; i++)
                if (timer.Sectors[i].Active) return i;
            return -1;
        }

        // Callers hold SectorLock. Merit of a completed pass through sector i.
        private byte MeritLocked(SectorSplit s, int i)
        {
            if (s.Passes == 0) return SecNone;
            if (!s.Valid || s.Last <= 0) return SecInvalid;
            if (i < _sessionBestSectors.Length && s.Last <= _sessionBestSectors[i] + 0.005) return SecSession;
            return s.Last <= s.Best + 0.005 ? SecPersonal : SecSlower;
        }

        /// <summary>Row color for a sector merit code (the Sec* constants).</summary>
        protected static XnaColor SectorColor(byte code) => code switch
        {
            SecSession => SecPurple,
            SecPersonal => SecGreen,
            SecSlower => SecYellow,
            SecInvalid => SecRed,
            _ => new XnaColor(0x3A, 0x3A, 0x52),
        };

        // ----- Edit-mode column reordering -----------------------------------

        /// <summary>Raised when the user finishes dragging a column into a new order.</summary>
        public event Action? ColumnOrderChanged;

        /// <summary>
        /// Comma-separated column order for persistence, e.g.
        /// "Pos,Driver,LastLap,Rel". Setting anything that is not a full
        /// permutation of this box's columns is ignored.
        /// </summary>
        public string ColumnOrder
        {
            get => string.Join(",", _colOrder);
            set
            {
                if (string.IsNullOrWhiteSpace(value)) return;
                var allowed = DefaultColumns;
                var parsed = new List<Col>();
                foreach (var part in value.Split(','))
                    if (Enum.TryParse(part.Trim(), true, out Col c) && allowed.Contains(c) && !parsed.Contains(c))
                        parsed.Add(c);
                if (parsed.Count != allowed.Length) return;
                _colOrder = parsed.ToArray();
                Invalidate();
            }
        }

        protected int HeaderTop => TitleBarHeight + _infoBarHeight + Pad;

        /// <summary>Top of the first car row, in panel pixels.</summary>
        protected int RowsTop => HeaderTop + HeaderRowHeight;

        /// <summary>Connected and inside one of this box's session types.</summary>
        protected bool IsActive => _active;

        /// <summary>
        /// Index into <see cref="Rows"/> at a panel-local point, or -1 when
        /// the point is off a row (the gap between rows counts as off).
        /// </summary>
        protected int RowAt(int x, int y)
        {
            if (IsCollapsed || x < Pad || x >= Width - Pad) return -1;
            int rel = y - RowsTop - RowOffset * (RowHeight + RowSpacing);
            if (rel < 0) return -1;
            int index = rel / (RowHeight + RowSpacing);
            if (index >= Rows.Count) return -1;
            return rel - index * (RowHeight + RowSpacing) < RowHeight ? index : -1;
        }

        /// <summary>
        /// Whether this box paints friend / rival colors on its rows. The
        /// standings board always does; the relative boxes follow the HUD
        /// setting, since a tinted row there competes with the lap-up/down
        /// and player colors they already use.
        /// </summary>
        protected virtual bool ShowsDriverTags => DriverTags.Instance.ShowInRelativeBoxes;

        // A press on the header row grabs that column instead of dragging the
        // whole panel; anywhere else the panel moves as before.
        public override bool OnEditPress(int x, int y)
        {
            if (IsCollapsed) return false;
            int top = HeaderTop;
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

        // ----- Update ----------------------------------------------------------

        public override void Update(GameTime gameTime)
        {
            Flasher += (float)gameTime.ElapsedGameTime.TotalSeconds * 15f;
            if (Flasher > 2f) Flasher -= 2f;

            var svc = IracingService.Instance;
            string sessionType = svc.SessionType;
            CurrentSessionType = sessionType;
            _active = svc.IsConnected && IsSessionActive(sessionType);
            _sessionLabel = string.IsNullOrEmpty(sessionType)
                ? (svc.IsConnected ? "Session" : "No Session")
                : sessionType;

            Rows.Clear();
            RowOffset = 0;
            var player = svc.PlayerCar;
            var all = svc.Cars; // list object is replaced, never mutated — safe to enumerate
            Multiclass = all.Select(c => c.CarClassId).Distinct().Count() > 1;

            if (_active && player.CarIdx >= 0 && all.Count > 0)
                BuildRows(all, player);

            // Only redraw/republish when something visible actually changed.
            var sb = new StringBuilder(_sessionLabel);
            sb.Append(DisplayName).Append(_active ? 'A' : 'a').Append(RowOffset);
            bool anyHazard = false;
            foreach (var r in Rows)
            {
                r.AppendTo(sb);
                anyHazard |= r.Hazard;
            }
            // A visible hazard box flashes, so fold the flash phase into the
            // snapshot to keep the panel repainting while one is on screen.
            if (anyHazard) sb.Append(Flasher > 1f ? 'F' : 'f');
            if (_active) AppendSnapshot(sb);

            string snapshot = sb.ToString();
            if (snapshot != _lastSnapshot)
            {
                _lastSnapshot = snapshot;
                Invalidate();
            }
        }

        // ----- Row helpers shared by both boxes --------------------------------

        /// <summary>
        /// Cars around the player by physical track location, like the
        /// original relative: up to half the window ahead (farthest first),
        /// the player, then up to half behind. Each entry carries the signed
        /// lap-fraction distance to the player folded into (-0.5, 0.5]
        /// (positive = ahead on track). Cars not in the world are left out, as
        /// is anything <paramref name="include"/> rejects.
        /// </summary>
        protected List<(Car Car, float Rel)> RelativeWindow(IReadOnlyList<Car> all, Car player,
                                                             Func<Car, float, bool>? include = null)
        {
            float playerPct = Math.Clamp(player.LapDistPct, 0f, 1f);
            static float RelTo(float pct, float playerPct)
            {
                float d = pct - playerPct;
                if (d > 0.5f) d -= 1f;
                else if (d < -0.5f) d += 1f;
                return d;
            }

            var others = all
                .Where(c => c.CarIdx != player.CarIdx && !c.IsInGarage && c.LapDistPct >= 0)
                .Select(c => (Car: c, Rel: RelTo(Math.Clamp(c.LapDistPct, 0f, 1f), playerPct)))
                .Where(o => include == null || include(o.Car, o.Rel))
                .ToList();

            int half = CarsToDisplay / 2;
            var ahead = others.Where(o => o.Rel > 0).OrderBy(o => o.Rel).Take(half).ToList();
            var behind = others.Where(o => o.Rel <= 0).OrderByDescending(o => o.Rel)
                               .Take(CarsToDisplay - 1 - half).ToList();

            // Like the original, the player stays on the middle row: with
            // fewer cars ahead than half the window, the rows start lower.
            RowOffset = half - ahead.Count;

            var window = new List<(Car, float)>();
            for (int i = ahead.Count - 1; i >= 0; i--) window.Add(ahead[i]);
            // The player's own entry: use the PlayerCar-equivalent from the list when present.
            window.Add((all.FirstOrDefault(c => c.CarIdx == player.CarIdx) ?? player, 0f));
            window.AddRange(behind);
            return window;
        }

        /// <summary>Fill the per-driver cells every box shows the same way.</summary>
        protected Row BaseRow(Car car, Car player, float rel, float classFastestLap)
        {
            bool isPlayer = car.CarIdx == player.CarIdx;
            var st = CarStatusMonitor.Instance.StatusOf(car.CarIdx);
            var tags = DriverTags.Instance;
            return new Row
            {
                CarIdx = car.CarIdx,
                TagColor = isPlayer || !ShowsDriverTags ? -1 : tags.ColorOf(tags.TagOf(car)),
                ClassColor = Multiclass ? car.ClassColor : -1,
                Name = car.DriverName,
                License = car.IsAi ? string.Empty : car.LicString,
                LicColor = car.IsAi ? -1 : car.LicColor,
                LowSafety = !car.IsAi && car.LicSubLevel > 0 && car.LicSubLevel < 200,
                IRating = car.IsAi ? "AI" : car.IRating > 0 ? car.IRating.ToString() : string.Empty,
                LastLap = FormatLapTime(car.LastLapTime),
                LastCmp = CompareLastLap(car, player, isPlayer),
                BestLap = FormatLapTime(car.BestLapTime),
                BestIsFastest = car.BestLapTime > 0 && Math.Abs(car.BestLapTime - classFastestLap) < 0.0005f,
                Rel = isPlayer ? string.Empty : FormatRelative(car, player, rel),
                IsPlayer = isPlayer,
                Sectors = SectorCodesFor(car.CarIdx),
                Status = st.Pit switch
                {
                    PitState.Pit => "PIT",
                    PitState.PitIn => "PIT IN",
                    PitState.PitOut => "PIT OUT",
                    PitState.OutLap => "OUT LAP",
                    _ => string.Empty,
                },
                Hazard = st.YellowHazard,
                Slow = st.SlowHazard,
                // CarIdxTireCompound: 0 = the car's dry compound, higher = wets
                // (the original's S/W); -1 = series doesn't report it.
                Tire = car.TireCompound < 0 ? string.Empty : car.TireCompound == 0 ? "S" : "W",
                Stint = string.Empty,
            };
        }

        /// <summary>
        /// Fastest official best lap in the player's class, and who set it.
        /// (The original took it across every class, so in multiclass the
        /// fastest class always "held" it.)
        /// </summary>
        protected static (float Time, string Driver) ClassFastestLap(IReadOnlyList<Car> all, Car player)
        {
            float best = float.PositiveInfinity;
            string who = string.Empty;
            foreach (var c in all)
            {
                if (player.CarClassId >= 0 && c.CarClassId != player.CarClassId) continue;
                if (c.BestLapTime > 0 && c.BestLapTime < best) { best = c.BestLapTime; who = c.DriverName; }
            }
            return (best, who);
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
        /// The sign always agrees with the row's place in the window (the
        /// lap-fraction <paramref name="rel"/>), so a car listed above you
        /// never shows as behind.
        /// </summary>
        private static string FormatRelative(Car car, Car player, float rel)
        {
            if (car.EstTime <= 0 || player.EstTime <= 0) return "--";

            float delta = car.EstTime - player.EstTime;

            // When the pair straddles the start/finish line the raw delta is
            // off by a whole lap (one EstTime just reset to ~0, the other is
            // near a full lap). Fold it back by one lap in the direction the
            // track position says. The fold length must be the class
            // reference lap (CarClassEstLapTime) — the scale EstTime itself is
            // computed against — not a driver's best lap.
            float lapTime = player.ClassEstLapTime > 0 ? player.ClassEstLapTime
                          : car.ClassEstLapTime > 0 ? car.ClassEstLapTime
                          : player.BestLapTime > 0 ? player.BestLapTime
                          : car.BestLapTime;
            if (lapTime > 0)
            {
                if (rel > 0 && delta < 0) delta += lapTime;
                else if (rel <= 0 && delta > 0) delta -= lapTime;
            }

            return delta.ToString("+0.0;-0.0;0.0");
        }

        protected static string FormatLapTime(float seconds)
        {
            if (seconds <= 0) return "--:--.---";
            int minutes = (int)(seconds / 60f);
            float rest = seconds - minutes * 60f;
            return $"{minutes}:{rest:00.000}";
        }

        protected static XnaColor FromRgb(int rgb, int alpha = 255) =>
            new XnaColor((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF, alpha);

        /// <summary>Dark or light text, whichever reads on the given background.</summary>
        protected static XnaColor TextOn(int rgb)
        {
            double lum = 0.299 * ((rgb >> 16) & 0xFF) + 0.587 * ((rgb >> 8) & 0xFF) + 0.114 * (rgb & 0xFF);
            return lum > 140 ? DarkText : XnaColor.White;
        }

        // ----- Render ----------------------------------------------------------

        public override void Render(GameTime gameTime)
        {
            const int Radius = 16;
            const int RowRadius = 8;

            GraphicsDevice.Clear(XnaColor.Transparent);

            // Invisible (collapsed pill included) outside this box's sessions;
            // edit mode still draws the panel so it can be found and dragged.
            if (!_active && !EditMode) return;

            if (IsCollapsed)
            {
                _sb.Begin();
                var pill = new XnaRectangle(0, 0, CollapsedWidth, CollapsedHeight);
                MonoXRDraw.RoundedRect(_sb, pill, CollapsedHeight / 2, TitleBg);
                MonoXRDraw.RoundedRectOutline(_sb, pill, CollapsedHeight / 2, 2, Border);
                int dotR = 6;
                _sb.Draw(MonoXRDraw.Circle(GraphicsDevice, dotR),
                    new XnaRectangle(20 - dotR, CollapsedHeight / 2 - dotR, dotR * 2, dotR * 2), Accent);
                _sb.DrawString(Font, DisplayName, new XnaVector2(34, (CollapsedHeight - Font.LineSpacing) / 2f), TitleText);
                _sb.End();
                return;
            }

            // Rounded panel over a transparent canvas, so the corners are
            // see-through in VR.
            _sb.Begin();

            var panel = new XnaRectangle(0, 0, Width, Height);
            MonoXRDraw.RoundedRect(_sb, panel, Radius, PanelBg);

            // Title bar shows the current session type, with a sheen, a
            // drop-shadowed title and a glowing accent strip underneath.
            MonoXRDraw.RoundedRect(_sb, new XnaRectangle(0, 0, Width, TitleBarHeight), Radius,
                                   TitleBg, roundBottom: false);
            MonoXRDraw.VerticalFade(_sb, new XnaRectangle(0, 0, Width, TitleBarHeight / 2), XnaColor.White * 0.05f);
            // "Race - Race" / "Qualifying - Qualify" read as a stutter, so the
            // session label is dropped whenever it already says what the box is.
            string boxName = DisplayName;
            string title = boxName.StartsWith(_sessionLabel, StringComparison.OrdinalIgnoreCase)
                        || _sessionLabel.StartsWith(boxName, StringComparison.OrdinalIgnoreCase)
                ? boxName : $"{boxName} - {_sessionLabel}";
            _sb.DrawString(Font, title, new XnaVector2(20, 10), XnaColor.Black * 0.45f);
            _sb.DrawString(Font, title, new XnaVector2(20, 8), TitleText);
            _sb.Draw(_white, new XnaRectangle(0, TitleBarHeight - 3, Width, 3), Accent);
            MonoXRDraw.VerticalFade(_sb, new XnaRectangle(0, TitleBarHeight, Width, 12), Accent * 0.25f);

            if (_infoBarHeight > 0 && _active)
                DrawInfoBar(new XnaRectangle(Pad + 8, TitleBarHeight, Width - (Pad + 8) * 2, _infoBarHeight));

            // Column headers in the user's order (right-aligned labels line up
            // with the right-aligned values beneath them), with a thin rule
            // under the header row. In edit mode each header gets a faint box
            // (drag handle hint) and a dragged column is highlighted.
            var cells = ColumnCells();
            int headTop = HeaderTop;
            int headY = headTop + (HeaderRowHeight - FontHead.LineSpacing) / 2;
            int rowsBottom = headTop + HeaderRowHeight + (RowOffset + Rows.Count) * (RowHeight + RowSpacing);

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
                float lx = RightAligned(col) ? cl + cw - FontHead.MeasureString(label).X : cl;
                _sb.DrawString(FontHead, label, new XnaVector2(lx, headY), dragging ? Accent : RowTextDim);
            }
            MonoXRDraw.HorizontalFade(_sb,
                new XnaRectangle(Pad + 4, headTop + HeaderRowHeight - 4, Width - (Pad + 4) * 2, 2), Border);

            if (Rows.Count == 0)
            {
                string msg = _active ? "Waiting for iRacing..." : EmptyMessage;
                _sb.DrawString(FontBody, msg,
                    new XnaVector2(ColContentLeft + 10, headTop + HeaderRowHeight + 10), RowTextDim);
            }

            int y = headTop + HeaderRowHeight + RowOffset * (RowHeight + RowSpacing);
            foreach (var row in Rows)
            {
                DrawRow(row, y, cells, RowRadius);
                y += RowHeight + RowSpacing;
            }

            if (_footerHeight > 0 && _active)
                DrawFooter(new XnaRectangle(Pad + 10, Height - _footerHeight, Width - (Pad + 10) * 2, _footerHeight));

            // Anything that has to sit above the rows (the standings board's
            // driver-tag menu) draws last.
            DrawPopups();

            // Rounded panel outline.
            MonoXRDraw.RoundedRectOutline(_sb, panel, Radius, 2, Border);

            _sb.End();
        }

        private void DrawRow(Row row, int y, (Col Col, int Left, int Width)[] cells, int rowRadius)
        {
            if (row.Divider)
            {
                // Standings window break: the field is longer than the panel,
                // so the rows jump from the leaders to the player's group.
                const string Dots = ". . .";
                var size = FontHead.MeasureString(Dots);
                _sb.DrawString(FontHead, Dots,
                    new XnaVector2((Width - size.X) / 2f, y + (RowHeight - FontHead.LineSpacing) / 2f), RowTextDim);
                return;
            }

            XnaColor bg, badge;
            if (row.IsPlayer) { bg = PlayerBg; badge = new XnaColor(0x2E, 0x8C, 0x57); }
            else if (row.LapDelta >= 1) { bg = LapAheadBg; badge = new XnaColor(0xA8, 0x30, 0x30); }
            else if (row.LapDelta <= -1) { bg = LapBehindBg; badge = new XnaColor(0x30, 0x50, 0xA8); }
            else { bg = RowShades[Math.Abs(row.Pos) % RowShades.Length]; badge = new XnaColor(0x32, 0x32, 0x48); }

            // A tagged driver's color replaces the plain row shade, mixed in
            // by the strength set in HUD settings, so a friend or a rival is
            // findable in a full field without the text becoming unreadable.
            if (row.TagColor >= 0 && !row.IsPlayer)
                bg = XnaColor.Lerp(bg, FromRgb(row.TagColor, bg.A), DriverTags.Instance.TintStrength);

            var rowRect = new XnaRectangle(Pad, y, Width - Pad * 2, RowHeight);
            MonoXRDraw.RoundedRect(_sb, rowRect, rowRadius, bg);
            // Subtle top sheen so rows read as raised cards.
            MonoXRDraw.VerticalFade(_sb, new XnaRectangle(rowRect.X + rowRadius, y, rowRect.Width - rowRadius * 2, RowHeight / 2),
                                    XnaColor.White * 0.04f);
            // Left edge marker: green for you, the tag color for a tagged
            // driver — visible even with the tint turned all the way down.
            if (row.IsPlayer)
                MonoXRDraw.RoundedRect(_sb, new XnaRectangle(rowRect.X, y + 4, 5, RowHeight - 8), 2, SecGreen);
            else if (row.TagColor >= 0)
                MonoXRDraw.RoundedRect(_sb, new XnaRectangle(rowRect.X, y + 4, 5, RowHeight - 8), 2,
                                       FromRgb(row.TagColor));

            if (row.Highlight)
                MonoXRDraw.RoundedRectOutline(_sb, rowRect, rowRadius, 2, Accent);

            float textY = y + (RowHeight - FontBody.LineSpacing) / 2f;
            float smallY = y + (RowHeight - FontHead.LineSpacing) / 2f;

            foreach (var (col, cl, cw) in cells)
            {
                switch (col)
                {
                    case Col.Pos:
                    {
                        // Position in a rounded badge; in multiclass the badge
                        // takes the iRacing class color (the original's class
                        // badge) and shows the class position.
                        var badgeRect = new XnaRectangle(cl, y + 5, Math.Min(42, cw), RowHeight - 10);
                        XnaColor fill = row.ClassColor >= 0 ? FromRgb(row.ClassColor) : badge;
                        XnaColor fg = row.ClassColor >= 0 ? TextOn(row.ClassColor) : RowText;
                        MonoXRDraw.RoundedRect(_sb, badgeRect, 6, fill);
                        if (row.Pos > 0)
                        {
                            string pos = row.Pos.ToString();
                            var posSize = FontBody.MeasureString(pos);
                            _sb.DrawString(FontBody, pos,
                                new XnaVector2(badgeRect.X + (badgeRect.Width - posSize.X) / 2f, textY), fg);
                        }
                        break;
                    }
                    case Col.License:
                    {
                        if (string.IsNullOrEmpty(row.License)) break;
                        var r = new XnaRectangle(cl, y + 7, cw, RowHeight - 14);
                        int lic = row.LicColor >= 0 ? row.LicColor : 0x505068;
                        MonoXRDraw.RoundedRect(_sb, r, 5, FromRgb(lic));
                        var size = FontHead.MeasureString(row.License);
                        _sb.DrawString(FontHead, row.License,
                            new XnaVector2(r.X + (r.Width - size.X) / 2f, smallY), TextOn(lic));
                        // Safety rating under 2.00: a red underline (the
                        // original's red text vanished on a red rookie badge).
                        if (row.LowSafety)
                            _sb.Draw(_white, new XnaRectangle(r.X + 4, r.Bottom - 4, r.Width - 8, 3), SecRed);
                        break;
                    }
                    case Col.IRating:
                        if (!string.IsNullOrEmpty(row.IRating))
                            DrawRight(FontBody, row.IRating, cl + cw, textY, row.IRating == "AI" ? RowTextDim : RowText);
                        break;
                    case Col.Driver:
                        _sb.DrawString(FontBody, TruncateText(row.Name, cw), new XnaVector2(cl, textY), RowText);
                        break;
                    case Col.Status:
                        DrawStatusCell(row, cl, cw, y, smallY);
                        break;
                    case Col.Sectors:
                    {
                        // One thin vertical bar per sector, right-aligned in
                        // the cell, colored by that car's last pass.
                        if (row.Sectors == null || row.Sectors.Length == 0) break;
                        const int BarW = 10, BarGap = 5;
                        int barH = RowHeight - 14;
                        int barX = cl + cw - row.Sectors.Length * (BarW + BarGap) + BarGap;
                        foreach (byte code in row.Sectors)
                        {
                            MonoXRDraw.RoundedRect(_sb, new XnaRectangle(barX, y + 7, BarW, barH), 3,
                                                   SectorColor(code));
                            barX += BarW + BarGap;
                        }
                        break;
                    }
                    case Col.LastLap:
                    {
                        // Faster last lap than yours = red (threat), slower =
                        // green, no comparison = dim.
                        XnaColor c = row.LastCmp < 0 ? SecRed : row.LastCmp > 0 ? SecGreen : RowTextDim;
                        DrawRight(FontBody, row.LastLap, cl + cw, textY, c);
                        break;
                    }
                    case Col.BestLap:
                        DrawRight(FontBody, row.BestLap, cl + cw, textY, row.BestIsFastest ? SecPurple : RowText);
                        break;
                    case Col.CurLap:
                    {
                        // Time into the lap being driven; a dim placeholder
                        // when the car is not on one (pits, garage, no data).
                        bool onLap = !string.IsNullOrEmpty(row.CurLap);
                        DrawRight(FontBody, onLap ? row.CurLap : "--:--.---", cl + cw, textY,
                                  onLap ? RowText : RowTextDim);
                        break;
                    }
                    case Col.LiveSectors:
                    {
                        // This lap's sector times, one sub-cell each: completed
                        // sectors colored by merit, the sector being driven
                        // ticking in white, the ones still ahead blank.
                        var live = row.LiveSectors;
                        if (live == null || live.Length == 0) break;
                        int subW = cw / live.Length;
                        for (int i = 0; i < live.Length; i++)
                        {
                            var cell = live[i];
                            string text = cell.Time > 0 ? $"{cell.Time:00.00}" : "--.--";
                            XnaColor c = cell.Active ? RowText
                                : cell.Code == SecNone ? RowTextDim
                                : SectorColor(cell.Code);
                            DrawRight(FontHead, text, cl + (i + 1) * subW - 8, smallY, c);
                        }
                        break;
                    }
                    case Col.Rel:
                        if (!string.IsNullOrEmpty(row.Rel))
                            DrawRight(FontBody, row.Rel, cl + cw, textY, RowText);
                        break;
                    case Col.Gap:
                        if (!string.IsNullOrEmpty(row.Gap))
                            DrawRight(FontBody, row.Gap, cl + cw, textY,
                                row.GapCmp < 0 ? SecGreen : row.GapCmp > 0 ? SecYellow : RowText);
                        break;
                    case Col.Tire:
                        if (!string.IsNullOrEmpty(row.Tire))
                        {
                            var c = row.Tire == "W" ? new XnaColor(0x8C, 0xC8, 0xFF) : SecRed;
                            var size = FontBody.MeasureString(row.Tire);
                            _sb.DrawString(FontBody, row.Tire, new XnaVector2(cl + (cw - size.X) / 2f, textY), c);
                        }
                        break;
                    case Col.Gained:
                        if (row.Gained is int g)
                        {
                            string text = g == 0 ? "=" : g > 0 ? $"+{g}" : g.ToString();
                            _sb.DrawString(FontBody, text, new XnaVector2(cl, textY),
                                g >= 0 ? SecGreen : new XnaColor(0xFF, 0x60, 0x40));
                        }
                        break;
                    case Col.Stint:
                        if (!string.IsNullOrEmpty(row.Stint))
                            DrawRight(FontBody, row.Stint, cl + cw, textY, RowTextDim);
                        break;
                }
            }
        }

        /// <summary>
        /// Status cell (the original's "info" slot plus its PIT / OUTLAP /
        /// checkered marker): a flashing yellow box while this car triggers
        /// the yellow-flag card, then pit phase text, SLOW (the original's
        /// "*"), a checkered finish marker, or FL for the class fastest lap.
        /// </summary>
        private void DrawStatusCell(Row row, int cl, int cw, int y, float smallY)
        {
            bool boxOn = row.Hazard && Flasher > 1f;
            if (boxOn)
                MonoXRDraw.RoundedRect(_sb, new XnaRectangle(cl - 2, y + 5, cw + 4, RowHeight - 10), 6, SecYellow);

            if (!string.IsNullOrEmpty(row.Status))
            {
                XnaColor c = boxOn ? DarkText
                    : row.Status == "PIT OUT" ? SecGreen
                    : row.Status == "PIT IN" ? SecYellow
                    : row.Status == "OUT LAP" ? Accent
                    : RowTextDim;
                _sb.DrawString(FontHead, row.Status, new XnaVector2(cl + 4, smallY), c);
            }
            else if (row.Slow)
            {
                _sb.DrawString(FontHead, "SLOW", new XnaVector2(cl + 4, smallY), boxOn ? DarkText : SlowAmber);
            }
            else if (row.Finished)
            {
                // Small checkered flag: 4x3 alternating squares.
                const int Sq = 7;
                int fx = cl + 4, fy = y + (RowHeight - Sq * 3) / 2;
                for (int r = 0; r < 3; r++)
                    for (int c = 0; c < 4; c++)
                        _sb.Draw(_white, new XnaRectangle(fx + c * Sq, fy + r * Sq, Sq, Sq),
                                 (r + c) % 2 == 0 ? XnaColor.White : XnaColor.Black);
                _sb.DrawString(FontHead, "FIN", new XnaVector2(fx + Sq * 4 + 6, smallY), RowText);
            }
            else if (row.BestIsFastest)
            {
                var r = new XnaRectangle(cl, y + 9, 36, RowHeight - 18);
                MonoXRDraw.RoundedRect(_sb, r, 5, SecPurple);
                var size = FontHead.MeasureString("FL");
                _sb.DrawString(FontHead, "FL", new XnaVector2(r.X + (r.Width - size.X) / 2f, smallY), XnaColor.White);
            }
        }

        protected void DrawRight(SpriteFont font, string text, float rightEdge, float y, XnaColor color) =>
            _sb.DrawString(font, text, new XnaVector2(rightEdge - font.MeasureString(text).X, y), color);

        /// <summary>Trim text (with an ellipsis) so it never overflows its column cell.</summary>
        private string TruncateText(string name, float maxWidth)
        {
            if (string.IsNullOrEmpty(name) || FontBody.MeasureString(name).X <= maxWidth)
                return name;
            for (int len = name.Length - 1; len > 0; len--)
            {
                string candidate = name.Substring(0, len) + "..";
                if (FontBody.MeasureString(candidate).X <= maxWidth)
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
            FontHead.Texture.Dispose();
            FontBody.Texture.Dispose();
            Font.Texture.Dispose();
            _white.Dispose();
            _sb.Dispose();
            base.Dispose();
        }
    }
}
