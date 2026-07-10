using System;
using System.Collections.Generic;

namespace BeltTensionTest.WPF.Services.Data
{
    /// <summary>
    /// One track sector, timed by watching the car's lap-distance percentage
    /// cross the sector boundaries against the session clock. Ported from
    /// IrachingHud's Sector: a crossing only counts if it is seen within 1%
    /// (start) / 2% (end) of the boundary — a bigger jump (tow, teleport,
    /// missed frames) invalidates the sector so bad times never register.
    /// </summary>
    public sealed class SectorSplit
    {
        public double Best = double.PositiveInfinity;
        public double Current;
        public double Last;

        public float StartPct;
        public float EndPct;

        public bool Valid { get; private set; }

        /// <summary>Fires when the sector is exited with a valid time (Last just updated).</summary>
        public Action? Ended;

        private bool _started;
        private double _startTime;

        public bool Active => _started;

        public SectorSplit(float startPct)
        {
            StartPct = startPct;
        }

        private bool InSector(float loc) => loc > StartPct && loc <= EndPct;

        public void Update(double time, float carLoc)
        {
            if (!_started)
            {
                if (InSector(carLoc))
                {
                    _started = true;
                    _startTime = time;
                    Current = 0;
                    // Entered too far past the line (jump/teleport) → the time
                    // is meaningless for this pass.
                    Valid = Math.Abs(carLoc - StartPct) < 0.01f;
                }
            }
            else if (!InSector(carLoc))
            {
                if (EndPct >= 1f)
                {
                    // Last sector ends at the line; the car wraps to ~0.
                    if (carLoc > 0.01f) { Last = 0; Valid = false; }
                }
                else if (Math.Abs(carLoc - EndPct) >= 0.02f)
                {
                    Valid = false;
                    Last = 0;
                }

                _started = false;
                if (Valid)
                {
                    Last = Current;
                    if (Last < Best) Best = Last;
                    Ended?.Invoke();
                }
            }
            else
            {
                Current = time - _startTime;
            }
        }
    }

    /// <summary>
    /// Times every sector of a lap for one car (IrachingHud's TrackSectors).
    /// Feed it the sector start percentages from the session's SplitTimeInfo
    /// plus the session time and the car's lap-distance pct each telemetry
    /// tick; <see cref="LapCompleted"/> fires when the final sector ends with
    /// a valid time.
    /// </summary>
    public sealed class SectorTimer
    {
        public readonly List<SectorSplit> Sectors = new();

        /// <summary>Fires as the last sector completes (all Last values are for this lap).</summary>
        public Action? LapCompleted;

        public void Reset() => Sectors.Clear();

        public void Update(IReadOnlyList<float> startPcts, double sessionTime, float lapDistPct)
        {
            if (startPcts.Count == 0) return;

            if (Sectors.Count != startPcts.Count)
            {
                Sectors.Clear();
                foreach (float s in startPcts)
                    Sectors.Add(new SectorSplit(s));
                for (int i = 0; i < Sectors.Count - 1; i++)
                    Sectors[i].EndPct = Sectors[i + 1].StartPct;
                Sectors[Sectors.Count - 1].EndPct = 1f;
                Sectors[Sectors.Count - 1].Ended = () => LapCompleted?.Invoke();
            }

            foreach (var s in Sectors)
                s.Update(sessionTime, lapDistPct);
        }
    }
}
