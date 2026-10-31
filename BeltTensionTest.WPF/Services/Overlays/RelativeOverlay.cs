using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BeltTensionTest.WPF.Services.Data;
using Microsoft.Xna.Framework.Graphics;
using MonoXR.Client;
using XnaColor = Microsoft.Xna.Framework.Color;
using XnaRectangle = Microsoft.Xna.Framework.Rectangle;
using XnaVector2 = Microsoft.Xna.Framework.Vector2;

namespace BeltTensionTest.WPF.Services.Overlays
{
    /// <summary>
    /// The in-VR relative box: one panel that changes what it shows with the
    /// session. It merges the old race box and practice/qualifying box (both
    /// ported from IrachingHud's RelativeBox).
    ///
    /// Rows are the cars physically around you in every session. In a race
    /// they carry class position, lap-up (red) / lap-down (blue) colors, the
    /// race gap, tire, positions gained and stint; the info bar shows the
    /// grid / clutch status before the start and the class fastest lap after,
    /// and the footer has SOF, an iRating estimate, brake bias, fuel and
    /// temperatures. Everywhere else the rows show the timed standing, best
    /// lap and best-lap gap, the info bar shows the class fastest lap and
    /// temperatures, and the footer is your sector strip with the projected
    /// lap and pace delta.
    ///
    /// Right-click a column header to hide it (and bring hidden ones back) or,
    /// on the driver header, change how names are written.
    ///
    /// Fixes over the original: grid/"positions gained" use class grid
    /// positions (not overall vs class), the grid count ignores the pace car
    /// and spectators, class colors come from iRacing instead of a 10-entry
    /// table, SOF and the iRating estimate are per class using iRacing's
    /// formula, the fuel laps readout it computed but never showed now works,
    /// cars sitting in their stall behind you are dropped as in the original,
    /// the finished flag no longer marks cars that merely sit near the line,
    /// qualifying is handled (the original only knew Race and Practice), and
    /// the pace delta no longer shows a double minus sign or a bogus value
    /// before a reference lap exists.
    /// </summary>
    public sealed class RelativeOverlay : StandingsOverlayBase
    {
        private const int InfoBarH = 44;
        private const int RaceFooterH = 56;
        private const int SectorStripHeight = 110;
        private const int SectorColWidth = 110;

        // Every column either mode can show; ColumnApplies picks per session.
        private static readonly Col[] Columns =
        {
            Col.Pos, Col.License, Col.IRating, Col.Driver, Col.Status, Col.BestLap, Col.LastLap,
            Col.Rel, Col.Gap, Col.Tire, Col.Gained, Col.Sectors,
        };
        protected override Col[] DefaultColumns => Columns;

        /// <summary>True while the session on screen is a race.</summary>
        private bool IsRace => IsRaceSession(CurrentSessionType);

        private static bool IsRaceSession(string sessionType) =>
            sessionType.IndexOf("Race", StringComparison.OrdinalIgnoreCase) >= 0;

        protected override bool ColumnApplies(Col c) => c switch
        {
            Col.BestLap => !IsRace,
            Col.Tire or Col.Gained => IsRace,
            Col.Stint => false, // the laps on the tire are part of the Tire column now
            _ => true,
        };

        protected override string EmptyMessage => "Shown during practice, qualifying and races";

        /// <summary>
        /// The title names the session actually on screen: "Race", "Practice",
        /// "Qualifying" and so on. The panel's <see cref="Name"/> stays
        /// "Relative" - it keys the saved toggle binding and show mode.
        /// </summary>
        protected override string DisplayName
        {
            get
            {
                string type = CurrentSessionType;
                if (type.IndexOf("Race", StringComparison.OrdinalIgnoreCase) >= 0) return "Race";
                if (type.IndexOf("Qual", StringComparison.OrdinalIgnoreCase) >= 0) return "Qualifying";
                if (type.IndexOf("Practice", StringComparison.OrdinalIgnoreCase) >= 0) return "Practice";
                if (type.IndexOf("Warmup", StringComparison.OrdinalIgnoreCase) >= 0) return "Warmup";
                if (type.IndexOf("Test", StringComparison.OrdinalIgnoreCase) >= 0) return "Testing";
                return "Relative";
            }
        }

        // UI-thread state (Update/BuildRows/Render all run on the UI thread).
        private readonly Dictionary<int, int> _stintStartLap = new();

        private readonly List<float> _fuelPerLap = new();
        private int _fuelLap = -1;
        private float _fuelAtLapStart = -1f;
        private bool _lapHadPit;

        // Info bar / footer content, rebuilt each Update.
        private bool _preRace;
        private string _startType = string.Empty;
        private int _waitingToGrid;
        private string _remaining = string.Empty; // time or laps left in the session
        private bool _remainingUrgent;
        private string _countdown = string.Empty; // "Start in 1:23" while the race is counting down to its start
        private int _clutchPct;
        private string _fastestText = string.Empty;
        private string _sof = string.Empty;
        private int? _irDelta;
        private string _brakeBias = string.Empty;
        private string _fuel = string.Empty;
        private string _temps = string.Empty;

        public RelativeOverlay(GraphicsDevice device, int x, int y, int carsToDisplay = DefaultCarsToDisplay)
            : base(device, "Relative", x, y, carsToDisplay, InfoBarH, SectorStripHeight)
        {
        }

        // Every session type; the title and columns adapt instead.
        protected override bool IsSessionActive(string sessionType) => !string.IsNullOrEmpty(sessionType);

        protected override void OnSessionReset()
        {
            // Called from the SDK thread on a session restart; the UI thread
            // picks the reset up on its next Update via this flag.
            _resetRequested = true;
        }
        private volatile bool _resetRequested;

        private void ResetState()
        {
            _stintStartLap.Clear();
            ResetFinishTracking();
            _fuelPerLap.Clear();
            _fuelLap = -1;
            _fuelAtLapStart = -1f;
            _lapHadPit = false;
        }

        protected override void BuildRows(IReadOnlyList<Car> all, Car player)
        {
            if (_resetRequested) { _resetRequested = false; ResetState(); }

            // The footer is the sector strip outside races and a thin stats
            // bar in them, so the panel resizes as the session changes.
            SetFooterHeight(IsRace ? RaceFooterH : SectorStripHeight);

            if (IsRace) BuildRaceRows(all, player);
            else BuildPracticeRows(all, player);
        }

        private void BuildPracticeRows(IReadOnlyList<Car> all, Car player)
        {
            var (fastest, fastestDriver) = ClassFastestLap(all, player);

            // Timed standing within each class, for cars iRacing hasn't
            // classified yet.
            var rankByIdx = all
                .Where(c => c.BestLapTime > 0)
                .GroupBy(c => c.CarClassId)
                .SelectMany(g => g.OrderBy(c => c.BestLapTime).ThenBy(c => c.CarIdx)
                                  .Select((c, i) => (c.CarIdx, Rank: i + 1)))
                .ToDictionary(t => t.CarIdx, t => t.Rank);

            foreach (var (car, rel) in RelativeWindow(all, player))
            {
                var row = BaseRow(car, player, rel, fastest);
                bool sameClass = player.CarClassId < 0 || car.CarClassId == player.CarClassId;

                // No position until the car has set a time (the original's rule).
                if (car.BestLapTime > 0)
                    row.Pos = car.ClassPosition > 0 ? car.ClassPosition
                        : rankByIdx.TryGetValue(car.CarIdx, out int rank) ? rank : 0;

                if (!row.IsPlayer && sameClass)
                    row.Gap = car.BestLapTime <= 0 || player.BestLapTime <= 0 ? "--"
                        : (car.BestLapTime - player.BestLapTime).ToString("+0.000;-0.000;0.000");

                Rows.Add(row);
            }

            var svc = IracingService.Instance;
            _fastestText = fastest < float.PositiveInfinity ? $"{FormatName(fastestDriver)}  {FormatLapTime(fastest)}" : string.Empty;
            _brakeBias = BrakeBiasText(svc);
            _remaining = RemainingText(svc, player, out _remainingUrgent);
            _temps = float.IsNaN(svc.TrackTempC) ? string.Empty
                : $"Track {svc.TrackTempC:0}C  Air {svc.AirTempC:0}C";
        }

        private void BuildRaceRows(IReadOnlyList<Car> all, Car player)
        {
            var svc = IracingService.Instance;
            int state = svc.SessionState;
            bool racing = state >= IracingService.SessionStateRacing;
            _preRace = state > 0 && !racing;
            var grid = svc.GridPositions;

            UpdateStints(all);
            UpdateFuel(player, svc);

            var (fastest, fastestDriver) = ClassFastestLap(all, player);
            float playerDist = DistanceOf(player);

            // Like the original: a car parked in its stall that is behind you
            // in the race is irrelevant to a relative and is dropped.
            var window = RelativeWindow(all, player, (c, rel) =>
                !(CarStatusMonitor.Instance.StatusOf(c.CarIdx).Pit == PitState.Pit &&
                  DistanceOf(c) < playerDist));

            foreach (var (car, rel) in window)
            {
                var row = BaseRow(car, player, rel, fastest);
                bool isPlayer = row.IsPlayer;
                bool sameClass = player.CarClassId < 0 || car.CarClassId == player.CarClassId;
                grid.TryGetValue(car.CarIdx, out var g);

                // Class position; before the green (and for anyone iRacing
                // hasn't classified yet) the class grid slot.
                int livePos = RacePositionOf(car).Class;
                row.Pos = racing && livePos > 0 ? livePos : g.ClassPos;

                // Whole laps ahead/behind you in the race: total race
                // distance difference minus the on-track separation.
                if (racing && !isPlayer && car.Lap >= 0 && player.Lap >= 0)
                {
                    float dist = DistanceOf(car);
                    row.LapDelta = (int)Math.Round(dist - playerDist - rel);
                }

                if (!isPlayer && racing && sameClass)
                    row.Gap = FormatGap(car, player, row.LapDelta);

                if (racing && g.ClassPos > 0 && livePos > 0)
                    row.Gained = g.ClassPos - livePos;

                if (racing && _stintStartLap.TryGetValue(car.CarIdx, out int start) && car.Lap >= start)
                    row.Stint = (car.Lap - start).ToString();

                Rows.Add(row);
            }

            // ----- Info bar -----
            _startType = svc.StandingStart ? "Standing Start" : "Rolling Start";
            _waitingToGrid = all.Count(c => !c.IsOnTrack);
            // The clock counts down to the start while the field is getting in the cars and warming up.
            double toStart = svc.SessionTimeRemain;
            _countdown = _preRace && svc.SessionState <= IracingService.SessionStateWarmup && toStart > 0 && toStart < 3600
                ? $"Start in {(int)toStart / 60}:{(int)toStart % 60:00}" : string.Empty;
            _clutchPct = (int)Math.Round(svc.ClutchPressed * 100f);
            _fastestText = racing && fastest < float.PositiveInfinity
                ? $"{FormatName(fastestDriver)}  {FormatLapTime(fastest)}" : string.Empty;

            // ----- Footer -----
            var classCars = all.Where(c => player.CarClassId < 0 || c.CarClassId == player.CarClassId).ToList();
            double sof = StrengthOfField(classCars);
            _sof = sof > 0 ? $"SOF {sof:0}" : "SOF --";
            // Where each car is in its class: the race order, or the grid slot before the green.
            int PosOf(Car c) => racing ? RacePositionOf(c).Class
                                       : (grid.TryGetValue(c.CarIdx, out var cg) ? cg.ClassPos : 0);
            int playerPos = PosOf(player);
            _irDelta = player.IsAi || player.IRating <= 0 || playerPos <= 0 ? null
                : EstimateIRatingChange(classCars, player, playerPos, PosOf);
            _brakeBias = BrakeBiasText(svc);
            _remaining = RemainingText(svc, player, out _remainingUrgent);
            float perLap = _fuelPerLap.Count > 0 ? _fuelPerLap.Average() : 0f;
            _fuel = perLap > 0.01f
                ? $"Fuel {svc.FuelLevel:0.0} L  {svc.FuelLevel / perLap:0.0} laps"
                : $"Fuel {svc.FuelLevel:0.0} L";
            _temps = float.IsNaN(svc.TrackTempC) ? string.Empty
                : $"Track {svc.TrackTempC:0}C  Air {svc.AirTempC:0}C";
        }

        /// <summary>
        /// The stint: laps since each car last left pit road, or since the start of the race.
        /// Whether a stop changed the tires is not something the sim tells us, so every visit
        /// to pit road starts a new stint (the count holds at 0 while the car is in the pits).
        /// </summary>
        private void UpdateStints(IReadOnlyList<Car> all)
        {
            foreach (var c in all)
            {
                if (c.Lap < 0) continue;
                // In the pits (or the garage), first seen, or the replay was scrubbed back before the stint began.
                if (c.OnPitRoad || c.IsInGarage || !_stintStartLap.TryGetValue(c.CarIdx, out int start) || c.Lap < start)
                    _stintStartLap[c.CarIdx] = c.Lap;
            }
        }

        /// <summary>Average fuel burned per green lap over the last five (laps with a pit visit are skipped).</summary>
        private void UpdateFuel(Car player, IracingService svc)
        {
            if (player.OnPitRoad) _lapHadPit = true;
            float fuel = svc.FuelLevel;
            if (_fuelLap < 0 || _fuelAtLapStart < 0)
            {
                _fuelLap = player.LapCompleted;
                _fuelAtLapStart = fuel;
                return;
            }
            if (player.LapCompleted == _fuelLap) return;

            float used = _fuelAtLapStart - fuel;
            if (!_lapHadPit && player.LapCompleted == _fuelLap + 1 && used > 0.01f)
            {
                _fuelPerLap.Add(used);
                if (_fuelPerLap.Count > 5) _fuelPerLap.RemoveAt(0);
            }
            _fuelLap = player.LapCompleted;
            _fuelAtLapStart = fuel;
            _lapHadPit = player.OnPitRoad;
        }

        /// <summary>Signed race gap to the player: negative = ahead of you, positive = behind you.</summary>
        private string FormatGap(Car car, Car player, int lapDelta)
        {
            if (lapDelta >= 1) return $"-{lapDelta}L";
            if (lapDelta <= -1) return $"+{-lapDelta}L";
            // From the distance between the two cars, so it moves all lap (iRacing's F2Time only changes at the line).
            bool ahead = DistanceOf(car) > DistanceOf(player);
            float seconds = ahead ? GapSeconds(player, car) : GapSeconds(car, player);
            if (float.IsNaN(seconds)) return "--";
            return (ahead ? -seconds : seconds).ToString("+0.0;-0.0;0.0");
        }

        private const double Br1 = 1600.0 / 0.69314718055994530942; // 1600 / ln 2

        /// <summary>iRacing strength of field: 1600/ln2 * ln(N / sum(exp(-iR / (1600/ln2)))).</summary>
        private static double StrengthOfField(List<Car> classCars)
        {
            var ratings = classCars.Where(c => !c.IsAi && c.IRating > 0).Select(c => (double)c.IRating).ToList();
            if (ratings.Count == 0) return 0;
            double sum = ratings.Sum(r => Math.Exp(-r / Br1));
            return Br1 * Math.Log(ratings.Count / sum);
        }

        /// <summary>
        /// Estimated iRating change if the race finished now, using the community-derived iRacing
        /// formula (iRacing has not published it; this is the widely used reverse-engineered one):
        ///
        ///   chance(a, b)  = (1 - e^(-a/br)) e^(-b/br) / ((1 - e^(-b/br)) e^(-a/br) + (1 - e^(-a/br)) e^(-b/br)),
        ///                   with br = 1600 / ln 2: the odds that a driver rated a beats one rated b
        ///   expected      = the sum of chance(you, d) over the other drivers: the finishes your rating
        ///                   "should" be worth, counted from the front (the lower, the better)
        ///   fudge         = ((N / 2) - rank) / 100
        ///   change        = (N - rank - expected - fudge) * 200 / N
        ///
        /// N and rank count only the human drivers with an iRating in the player's class: AI cars
        /// and unrated drivers are not part of the calculation, so they do not push your rank back.
        /// </summary>
        private static int? EstimateIRatingChange(List<Car> classCars, Car player, int playerPos, Func<Car, int> positionOf)
        {
            var field = classCars.Where(c => !c.IsAi && c.IRating > 0).ToList();
            int n = field.Count;
            if (n < 2) return null;

            // Your place among those drivers only.
            int rank = 1 + field.Count(c => c.CarIdx != player.CarIdx && positionOf(c) > 0 && positionOf(c) < playerPos);
            playerPos = rank;

            static double Chance(double a, double b)
            {
                double ea = Math.Exp(-a / Br1), eb = Math.Exp(-b / Br1);
                return (1 - ea) * eb / ((1 - eb) * ea + (1 - ea) * eb);
            }

            double me = player.IRating;
            double expected = -0.5; // the sum below includes the player against themself (0.5)
            foreach (var c in field) expected += Chance(me, c.IRating);

            double fudge = (n / 2.0 - playerPos) / 100.0;
            double change = (n - playerPos - expected - fudge) * 200.0 / n;
            return (int)Math.Round(change);
        }

        protected override void AppendSnapshot(StringBuilder sb)
        {
            sb.Append('#').Append(IsRace ? 'R' : 'r').Append(_fastestText).Append(_temps).Append(_brakeBias).Append(_remaining);
            if (IsRace)
            {
                sb.Append(_preRace ? 'P' : 'p').Append(_startType).Append(_waitingToGrid).Append(_countdown)
                  .Append(',').Append(_clutchPct).Append(_sof).Append(_irDelta?.ToString() ?? "~")
                  .Append(_brakeBias).Append(_fuel);
                return;
            }

            // Sector strip state: the live sector time ticks while driving, so
            // this keeps the panel repainting during an active sector.
            lock (SectorLock)
            {
                foreach (var s in PlayerSectors.Sectors)
                    sb.Append('|').Append(s.Active ? 'A' : 'i')
                      .Append((s.Active ? s.Current : s.Last).ToString("0.00"))
                      .Append(s.Valid ? 'v' : 'x').Append(s.Best.ToString("0.00"));
                sb.Append('|').Append(FastestLapFromSectors.ToString("0.000"));
            }
        }

        protected override void DrawInfoBar(XnaRectangle area)
        {
            if (IsRace) DrawRaceInfoBar(area); else DrawPracticeInfoBar(area);
        }

        protected override void DrawFooter(XnaRectangle area)
        {
            if (IsRace) DrawRaceFooter(area); else DrawPracticeFooter(area);
        }

        private void DrawRaceInfoBar(XnaRectangle area)
        {
            float y = area.Y + (area.Height - FontBody.LineSpacing) / 2f;
            if (_preRace)
            {
                // The original's gridding panel: start type, cars left to
                // grid, and clutch travel for launching a standing start.
                Batch.DrawString(FontBody, _startType, new XnaVector2(area.X, y), TitleText);
                string grid = _waitingToGrid <= 0 ? "Everyone gridded" : $"Waiting for {_waitingToGrid} to grid";
                // The countdown to the start goes right after it, centered together.
                var size = FontBody.MeasureString(grid);
                float countW = _countdown.Length > 0 ? FontBody.MeasureString(_countdown).X + 24 : 0f;
                float gx = area.X + (area.Width - size.X - countW) / 2f;
                Batch.DrawString(FontBody, grid, new XnaVector2(gx, y), _waitingToGrid <= 0 ? SecGreen : SecRed);
                if (_countdown.Length > 0)
                    Batch.DrawString(FontBody, _countdown, new XnaVector2(gx + size.X + 24, y), XnaColor.White);
                string clutch = $"Clutch {_clutchPct}%";
                DrawRight(FontBody, clutch, area.Right, y, RowText);
                DrawBrakeBias(_brakeBias, area, area.Right - FontBody.MeasureString(clutch).X - 30);
            }
            else
            {
                DrawFastestLapInfo(area, _fastestText);
                DrawRemaining(area, _remaining, _remainingUrgent);
                DrawBrakeBias(_brakeBias, area, area.Right);
            }
            MonoXRDraw.HorizontalFade(Batch, new XnaRectangle(area.X - 4, area.Bottom - 2, area.Width + 8, 2), Border);
        }


        private void DrawPracticeInfoBar(XnaRectangle area)
        {
            float y = area.Y + (area.Height - FontBody.LineSpacing) / 2f;
            DrawFastestLapInfo(area, _fastestText);
            DrawRemaining(area, _remaining, _remainingUrgent);
            DrawRight(FontBody, _temps, area.Right, y, RowTextDim);
            DrawBrakeBias(_brakeBias, area, area.Right - FontBody.MeasureString(_temps).X - 30);
            MonoXRDraw.HorizontalFade(Batch, new XnaRectangle(area.X - 4, area.Bottom - 2, area.Width + 8, 2), Border);
        }

        private void DrawRaceFooter(XnaRectangle area)
        {
            MonoXRDraw.HorizontalFade(Batch, new XnaRectangle(area.X - 6, area.Y, area.Width + 12, 2), Border);
            float y = area.Y + (area.Height - FontBody.LineSpacing) / 2f;
            float x = area.X;

            void Item(string text, XnaColor color)
            {
                if (string.IsNullOrEmpty(text)) return;
                Batch.DrawString(FontBody, text, new XnaVector2(x, y), color);
                x += FontBody.MeasureString(text).X + 34;
            }

            Item(_sof, RowText);
            // The estimated iRating change if the race ended now; "iR --" when it cannot be worked out
            // (no rating, an AI driver, or fewer than two rated drivers in the class).
            if (_irDelta is int d)
                Item(d >= 0 ? $"iR +{d}" : $"iR {d}", d >= 0 ? SecGreen : SecRed);
            else
                Item("iR --", RowTextDim);
            Item(_fuel, RowText);
            DrawRight(FontBody, _temps, area.Right, y, RowTextDim);
        }

        /// <summary>
        /// The player's sector strip, as in the original RelativeBox: one
        /// column per sector (live time while in the sector; afterwards the
        /// last time colored red = invalid, purple = matches the session's
        /// fastest lap sector, green = personal sector best, yellow = slower,
        /// with the delta to the fastest lap's sector underneath) and a
        /// projected-lap column on the right (completed sectors as driven,
        /// bests for the rest) with the pace delta to the fastest lap.
        /// </summary>
        private void DrawPracticeFooter(XnaRectangle area)
        {
            MonoXRDraw.HorizontalFade(Batch, new XnaRectangle(area.X - 6, area.Y, area.Width + 12, 2), Border);

            int headerY = area.Y + 8;
            int timeY = headerY + 30;
            int deltaY = timeY + 32;

            lock (SectorLock)
            {
                var sectors = PlayerSectors.Sectors;
                if (sectors.Count == 0)
                {
                    Batch.DrawString(FontBody, "Waiting for sector data...", new XnaVector2(area.X, timeY), RowTextDim);
                    return;
                }

                var reference = FastestLapSectors;
                double projected = 0, currentPace = 0, referencePace = 0;
                bool pastActive = false, projectedKnown = true;

                for (int i = 0; i < sectors.Count; i++)
                {
                    var s = sectors[i];
                    int x = area.X + i * SectorColWidth;
                    Batch.DrawString(FontBody, $"S{i + 1}", new XnaVector2(x + 10, headerY), RowTextDim);

                    double bls = reference != null && i < reference.Length ? reference[i] : double.PositiveInfinity;

                    if (s.Active || pastActive)
                    {
                        // The sector being driven and the ones still ahead
                        // this lap count at their best in the projection.
                        if (double.IsPositiveInfinity(s.Best)) projectedKnown = false;
                        else projected += s.Best;
                    }
                    else
                    {
                        // Completed this lap: as driven, and part of the pace delta.
                        if (s.Last > 0) projected += s.Last; else projectedKnown = false;
                        if (s.Last > 0 && !double.IsPositiveInfinity(bls))
                        {
                            currentPace += s.Last;
                            referencePace += bls;
                        }
                    }

                    if (s.Active)
                    {
                        // Live ticking time for the sector being driven.
                        Batch.DrawString(FontBody, $"{s.Current:00.00}", new XnaVector2(x, timeY), RowText);
                        pastActive = true;
                        continue;
                    }

                    // Last time, colored by merit.
                    XnaColor timeColor = !s.Valid ? SecRed
                        : Math.Abs(s.Last - bls) < 0.005 ? SecPurple
                        : Math.Abs(s.Last - s.Best) < 0.005 ? SecGreen
                        : SecYellow;
                    Batch.DrawString(FontBody, s.Last > 0 ? $"{s.Last:00.00}" : "--.--",
                        new XnaVector2(x, timeY), s.Last > 0 ? timeColor : RowTextDim);

                    // Delta to the fastest lap's sector.
                    if (s.Last <= 0 || double.IsPositiveInfinity(bls))
                        Batch.DrawString(FontBody, "-----", new XnaVector2(x, deltaY), s.Last <= 0 ? SecRed : RowTextDim);
                    else
                    {
                        double delta = s.Last - bls;
                        Batch.DrawString(FontBody, delta.ToString("+0.00;-0.00;0.00"),
                            new XnaVector2(x, deltaY), delta <= 0 ? SecGreen : SecYellow);
                    }
                }

                // Projected lap on the right, plus the pace delta to the
                // fastest lap over the sectors completed so far.
                float rightX = area.Right;
                DrawRight(FontBody, "Projected", rightX, headerY, RowTextDim);
                string projText = projectedKnown && projected > 0 && projected < 2000
                    ? FormatLapTime((float)projected) : "--:--.---";
                DrawRight(FontBody, projText, rightX, timeY, RowText);

                if (currentPace > 0 && referencePace > 0)
                {
                    double deltaToBest = currentPace - referencePace;
                    DrawRight(FontBody, deltaToBest.ToString("+0.000;-0.000;0.000"), rightX, deltaY,
                        deltaToBest <= 0 ? SecGreen : SecYellow);
                }
                else
                {
                    DrawRight(FontBody, "-----", rightX, deltaY, RowTextDim);
                }
            }
        }
    }
}
