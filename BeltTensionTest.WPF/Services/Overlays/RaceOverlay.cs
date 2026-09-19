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
    /// In-VR race box, ported from the race half of IrachingHud's
    /// RelativeBox. Only shown during race sessions.
    ///
    /// Rows are the cars physically around you (all classes; in multiclass
    /// the position badge takes the class color and shows class position),
    /// colored red when a car is a lap or more ahead of you in the race and
    /// blue when it is a lap or more down. Columns: position, license,
    /// iRating, driver, status (yellow/slow/pit/finished/fastest lap), last
    /// lap, relative, gap, tire, positions gained, stint length, sectors.
    ///
    /// Before the start the info bar shows the start type, how many cars
    /// still have to grid and your clutch; once racing it shows the class
    /// fastest lap. The footer has SOF, an iRating estimate, brake bias, fuel
    /// (with laps remaining) and track/air temperature.
    ///
    /// Fixes over the original: grid/"positions gained" use class grid
    /// positions (not overall vs class), the grid count ignores the pace car
    /// and spectators and counts the player properly, class colors come from
    /// iRacing instead of a 10-entry table that crashed past 10 classes and
    /// painted the first class black, SOF and the iRating estimate are per
    /// class using iRacing's formula (the original mixed classes, included
    /// the driver in their own field and was off by one on position), the
    /// fuel laps readout it computed but never showed now works, cars
    /// sitting in their stall behind you are dropped as in the original, and
    /// the finished flag no longer marks cars that merely sit near the line.
    /// </summary>
    public sealed class RaceOverlay : StandingsOverlayBase
    {
        private const int InfoBarH = 44;
        private const int FooterH = 56;

        private static readonly Col[] Columns =
        {
            Col.Pos, Col.License, Col.IRating, Col.Driver, Col.Status, Col.LastLap,
            Col.Rel, Col.Gap, Col.Tire, Col.Gained, Col.Stint, Col.Sectors,
        };
        protected override Col[] DefaultColumns => Columns;

        protected override string EmptyMessage => "Shown during race sessions";

        // UI-thread state (Update/BuildRows/Render all run on the UI thread).
        private readonly Dictionary<int, int> _stintStartLap = new();
        private readonly Dictionary<int, int> _lapAtCheckered = new();
        private readonly HashSet<int> _finished = new();
        private bool _checkeredSeen;

        private readonly List<float> _fuelPerLap = new();
        private int _fuelLap = -1;
        private float _fuelAtLapStart = -1f;
        private bool _lapHadPit;

        // Info bar / footer content, rebuilt each Update.
        private bool _preRace;
        private string _startType = string.Empty;
        private int _waitingToGrid;
        private int _clutchPct;
        private string _fastestText = string.Empty;
        private string _sof = string.Empty;
        private int? _irDelta;
        private string _brakeBias = string.Empty;
        private string _fuel = string.Empty;
        private string _temps = string.Empty;

        public RaceOverlay(GraphicsDevice device, int x, int y, int carsToDisplay = DefaultCarsToDisplay)
            : base(device, "Race", x, y, carsToDisplay, InfoBarH, FooterH)
        {
        }

        protected override bool IsSessionActive(string sessionType) =>
            sessionType.IndexOf("Race", StringComparison.OrdinalIgnoreCase) >= 0;

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
            _lapAtCheckered.Clear();
            _finished.Clear();
            _checkeredSeen = false;
            _fuelPerLap.Clear();
            _fuelLap = -1;
            _fuelAtLapStart = -1f;
            _lapHadPit = false;
        }

        protected override void BuildRows(IReadOnlyList<Car> all, Car player)
        {
            if (_resetRequested) { _resetRequested = false; ResetState(); }

            var svc = IracingService.Instance;
            int state = svc.SessionState;
            bool racing = state >= IracingService.SessionStateRacing;
            _preRace = state > 0 && !racing;
            var grid = svc.GridPositions;

            UpdateStints(all);
            UpdateFinished(all, state);
            UpdateFuel(player, svc);

            var (fastest, fastestDriver) = ClassFastestLap(all, player);
            float playerDist = player.Lap + Math.Clamp(player.LapDistPct, 0f, 1f);

            // Like the original: a car parked in its stall that is behind you
            // in the race is irrelevant to a relative and is dropped.
            var window = RelativeWindow(all, player, (c, rel) =>
                !(CarStatusMonitor.Instance.StatusOf(c.CarIdx).Pit == PitState.Pit &&
                  c.Lap + Math.Clamp(c.LapDistPct, 0f, 1f) < playerDist));

            foreach (var (car, rel) in window)
            {
                var row = BaseRow(car, player, rel, fastest);
                bool isPlayer = row.IsPlayer;
                bool sameClass = player.CarClassId < 0 || car.CarClassId == player.CarClassId;
                grid.TryGetValue(car.CarIdx, out var g);

                // Class position; before the green (and for anyone iRacing
                // hasn't classified yet) the class grid slot.
                row.Pos = racing && car.ClassPosition > 0 ? car.ClassPosition : g.ClassPos;

                // Whole laps ahead/behind you in the race: total race
                // distance difference minus the on-track separation.
                if (racing && !isPlayer && car.Lap >= 0 && player.Lap >= 0)
                {
                    float dist = car.Lap + Math.Clamp(car.LapDistPct, 0f, 1f);
                    row.LapDelta = (int)Math.Round(dist - playerDist - rel);
                }

                if (!isPlayer && racing && sameClass)
                    row.Gap = FormatGap(car, player, row.LapDelta);

                if (racing && g.ClassPos > 0 && car.ClassPosition > 0)
                    row.Gained = g.ClassPos - car.ClassPosition;

                if (racing && _stintStartLap.TryGetValue(car.CarIdx, out int start) && car.Lap >= start)
                    row.Stint = (car.Lap - start).ToString();

                row.Finished = _finished.Contains(car.CarIdx);
                Rows.Add(row);
            }

            // ----- Info bar -----
            _startType = svc.StandingStart ? "Standing Start" : "Rolling Start";
            _waitingToGrid = all.Count(c => !c.IsOnTrack);
            _clutchPct = (int)Math.Round(svc.ClutchPressed * 100f);
            _fastestText = racing && fastest < float.PositiveInfinity
                ? $"{fastestDriver}  {FormatLapTime(fastest)}" : string.Empty;

            // ----- Footer -----
            var classCars = all.Where(c => player.CarClassId < 0 || c.CarClassId == player.CarClassId).ToList();
            double sof = StrengthOfField(classCars);
            _sof = sof > 0 ? $"SOF {sof:0}" : "SOF --";
            int playerPos = racing ? player.ClassPosition : (grid.TryGetValue(player.CarIdx, out var pg) ? pg.ClassPos : 0);
            _irDelta = player.IsAi || player.IRating <= 0 || playerPos <= 0 ? null
                : EstimateIRatingChange(classCars, player, playerPos);
            _brakeBias = float.IsNaN(svc.BrakeBias) || svc.BrakeBias <= 0 ? string.Empty : $"BB {svc.BrakeBias:0.0}%";
            float perLap = _fuelPerLap.Count > 0 ? _fuelPerLap.Average() : 0f;
            _fuel = perLap > 0.01f
                ? $"Fuel {svc.FuelLevel:0.0} L  {svc.FuelLevel / perLap:0.0} laps"
                : $"Fuel {svc.FuelLevel:0.0} L";
            _temps = float.IsNaN(svc.TrackTempC) ? string.Empty
                : $"Track {svc.TrackTempC:0}C  Air {svc.AirTempC:0}C";
        }

        /// <summary>Laps since each car last left pit road (the original's stint counter).</summary>
        private void UpdateStints(IReadOnlyList<Car> all)
        {
            foreach (var c in all)
            {
                if (c.Lap < 0) continue;
                if (c.OnPitRoad || c.IsInGarage || !_stintStartLap.ContainsKey(c.CarIdx))
                    _stintStartLap[c.CarIdx] = c.Lap;
            }
        }

        /// <summary>
        /// Finished = crossed the line after the checkered came out: the lap
        /// count each car had when the session went checkered is remembered,
        /// and a car has finished once it completes another lap. The car that
        /// triggered the flag (the overall leader) has finished immediately.
        /// </summary>
        private void UpdateFinished(IReadOnlyList<Car> all, int state)
        {
            if (state < IracingService.SessionStateCheckered)
            {
                if (_checkeredSeen) { _lapAtCheckered.Clear(); _finished.Clear(); _checkeredSeen = false; }
                return;
            }
            if (!_checkeredSeen)
            {
                _checkeredSeen = true;
                foreach (var c in all)
                {
                    _lapAtCheckered[c.CarIdx] = c.LapCompleted;
                    if (c.Position == 1) _finished.Add(c.CarIdx);
                }
            }
            foreach (var c in all)
            {
                if (!_lapAtCheckered.TryGetValue(c.CarIdx, out int lap))
                    _lapAtCheckered[c.CarIdx] = lap = c.LapCompleted;
                if (c.LapCompleted > lap) _finished.Add(c.CarIdx);
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
        private static string FormatGap(Car car, Car player, int lapDelta)
        {
            if (lapDelta >= 1) return $"-{lapDelta}L";
            if (lapDelta <= -1) return $"+{-lapDelta}L";
            if (car.F2Time <= 0 && player.F2Time <= 0) return "--";
            float diff = car.F2Time - player.F2Time; // more time behind the leader = behind you
            return diff.ToString("+0.0;-0.0;0.0");
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
        /// Estimated iRating change if the race finished now, using the
        /// community-derived iRacing formula: expected score from pairwise win
        /// chances against every rated driver in the class, a fudge term, and
        /// scaling by field size. Only the player's class counts.
        /// </summary>
        private static int? EstimateIRatingChange(List<Car> classCars, Car player, int playerPos)
        {
            var field = classCars.Where(c => !c.IsAi && c.IRating > 0).ToList();
            int n = field.Count;
            if (n < 2) return null;

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
            sb.Append('#').Append(_preRace ? 'P' : 'p').Append(_startType).Append(_waitingToGrid)
              .Append(',').Append(_clutchPct).Append(_fastestText).Append(_sof).Append(_irDelta?.ToString() ?? "~")
              .Append(_brakeBias).Append(_fuel).Append(_temps);
        }

        protected override void DrawInfoBar(XnaRectangle area)
        {
            float y = area.Y + (area.Height - FontBody.LineSpacing) / 2f;
            if (_preRace)
            {
                // The original's gridding panel: start type, cars left to
                // grid, and clutch travel for launching a standing start.
                Batch.DrawString(FontBody, _startType, new XnaVector2(area.X, y), TitleText);
                string grid = _waitingToGrid <= 0 ? "Everyone gridded" : $"Waiting for {_waitingToGrid} to grid";
                var size = FontBody.MeasureString(grid);
                Batch.DrawString(FontBody, grid, new XnaVector2(area.X + (area.Width - size.X) / 2f, y),
                    _waitingToGrid <= 0 ? SecGreen : SecRed);
                DrawRight(FontBody, $"Clutch {_clutchPct}%", area.Right, y, RowText);
            }
            else if (!string.IsNullOrEmpty(_fastestText))
            {
                var chip = new XnaRectangle(area.X, area.Y + 9, 36, area.Height - 18);
                MonoXRDraw.RoundedRect(Batch, chip, 5, SecPurple);
                var fl = FontHead.MeasureString("FL");
                Batch.DrawString(FontHead, "FL",
                    new XnaVector2(chip.X + (chip.Width - fl.X) / 2f, area.Y + (area.Height - FontHead.LineSpacing) / 2f),
                    XnaColor.White);
                Batch.DrawString(FontBody, _fastestText, new XnaVector2(chip.Right + 10, y), RowText);
            }
            MonoXRDraw.HorizontalFade(Batch, new XnaRectangle(area.X - 4, area.Bottom - 2, area.Width + 8, 2), Border);
        }

        protected override void DrawFooter(XnaRectangle area)
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
            if (_irDelta is int d)
                Item(d >= 0 ? $"iR +{d}" : $"iR {d}", d >= 0 ? SecGreen : SecRed);
            Item(_brakeBias, RowText);
            Item(_fuel, RowText);
            DrawRight(FontBody, _temps, area.Right, y, RowTextDim);
        }
    }
}
