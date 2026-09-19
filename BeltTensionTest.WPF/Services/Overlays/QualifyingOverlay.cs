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
    /// In-VR qualifying/practice box, ported from the practice half of
    /// IrachingHud's RelativeBox. Shown in every non-race session (practice,
    /// qualifying, warmup, testing).
    ///
    /// Rows are the cars physically around you with their timed standing
    /// (hidden until a car has a lap, like the original), best lap (purple =
    /// class fastest), last lap, relative and best-lap gap to you. The info
    /// bar shows the class fastest lap and temperatures; the footer is your
    /// sector strip: live time in the current sector, then each sector's
    /// last time colored by merit with its delta to the session's fastest
    /// lap underneath, plus the projected lap and your pace delta.
    ///
    /// Fixes over the original: qualifying sessions are handled (the
    /// original only knew Race and Practice, so in qualifying positions,
    /// the fastest lap and the sector strip were all missing and the race
    /// lap-up/down colors were applied), and the pace delta no longer shows
    /// a double minus sign or a bogus value before a reference lap exists.
    /// </summary>
    public sealed class QualifyingOverlay : StandingsOverlayBase
    {
        private const int InfoBarH = 44;
        private const int SectorStripHeight = 110;
        private const int SectorColWidth = 110;

        private static readonly Col[] Columns =
        {
            Col.Pos, Col.License, Col.IRating, Col.Driver, Col.Status, Col.BestLap,
            Col.LastLap, Col.Rel, Col.Gap, Col.Sectors,
        };
        protected override Col[] DefaultColumns => Columns;

        protected override string EmptyMessage => "Shown during practice and qualifying";

        private string _fastestText = string.Empty;
        private string _temps = string.Empty;

        public QualifyingOverlay(GraphicsDevice device, int x, int y, int carsToDisplay = DefaultCarsToDisplay)
            : base(device, "Qualifying", x, y, carsToDisplay, InfoBarH, SectorStripHeight)
        {
        }

        protected override bool IsSessionActive(string sessionType) =>
            !string.IsNullOrEmpty(sessionType) &&
            sessionType.IndexOf("Race", StringComparison.OrdinalIgnoreCase) < 0;

        protected override void BuildRows(IReadOnlyList<Car> all, Car player)
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
            _fastestText = fastest < float.PositiveInfinity ? $"{fastestDriver}  {FormatLapTime(fastest)}" : string.Empty;
            _temps = float.IsNaN(svc.TrackTempC) ? string.Empty
                : $"Track {svc.TrackTempC:0}C  Air {svc.AirTempC:0}C";
        }

        protected override void AppendSnapshot(StringBuilder sb)
        {
            sb.Append('#').Append(_fastestText).Append(_temps);

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
            float y = area.Y + (area.Height - FontBody.LineSpacing) / 2f;
            if (!string.IsNullOrEmpty(_fastestText))
            {
                var chip = new XnaRectangle(area.X, area.Y + 9, 36, area.Height - 18);
                MonoXRDraw.RoundedRect(Batch, chip, 5, SecPurple);
                var fl = FontHead.MeasureString("FL");
                Batch.DrawString(FontHead, "FL",
                    new XnaVector2(chip.X + (chip.Width - fl.X) / 2f, area.Y + (area.Height - FontHead.LineSpacing) / 2f),
                    XnaColor.White);
                Batch.DrawString(FontBody, _fastestText, new XnaVector2(chip.Right + 10, y), RowText);
            }
            else
            {
                Batch.DrawString(FontBody, "No lap times yet", new XnaVector2(area.X, y), RowTextDim);
            }
            DrawRight(FontBody, _temps, area.Right, y, RowTextDim);
            MonoXRDraw.HorizontalFade(Batch, new XnaRectangle(area.X - 4, area.Bottom - 2, area.Width + 8, 2), Border);
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
        protected override void DrawFooter(XnaRectangle area)
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
