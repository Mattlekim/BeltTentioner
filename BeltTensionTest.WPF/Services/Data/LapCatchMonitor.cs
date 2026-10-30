using System;
using System.Collections.Generic;

namespace BeltTensionTest.WPF.Services.Data
{
    public enum CatchState : byte
    {
        NoCar,       // nobody to measure (alone, or you are in the pits)
        Unknown,     // a car is there but one of you has not set enough valid laps yet
        NotCatching, // the gap is steady or growing
        Catching,    // the gap is closing; LapsToCatch is the estimate
    }

    /// <summary>One side of the <see cref="LapCatchMonitor"/> estimate.</summary>
    public readonly struct CatchEstimate
    {
        public CatchState State { get; init; }
        public int CarIdx { get; init; }

        /// <summary>On-track gap to the car in seconds (always positive).</summary>
        public float GapSeconds { get; init; }

        /// <summary>Seconds per lap the gap is closing by (positive = the gap is shrinking).</summary>
        public float ClosingPerLap { get; init; }

        /// <summary>Laps until the gap is gone; only meaningful when <see cref="State"/> is Catching.</summary>
        public float LapsToCatch { get; init; }

        public static CatchEstimate None => new() { State = CatchState.NoCar, CarIdx = -1 };
    }

    /// <summary>
    /// Works out how many laps it takes you to catch the car in front, and the car behind to catch
    /// you. The gap is the on-track time gap (CarIdxEstTime, wrap-corrected like
    /// <see cref="CarStatusMonitor"/>) and the closing speed is the difference in recent lap pace,
    /// so laps = gap / (pace difference). A car is "not catching" when the pace difference is
    /// negligible or against you, or the estimate runs past <see cref="MaxLaps"/>.
    ///
    /// Pace is the median of each car's last few laps. Laps with an off-track moment or contact
    /// count, since they are part of how the car is really going; only laps that are not racing
    /// laps are dropped: any time on pit road or in the garage, or under the grid / pace car. The
    /// median keeps one bad lap from flipping the answer.
    ///
    /// Fed from CarsUpdated on the SDK telemetry thread; the accessors are safe from any thread.
    /// </summary>
    public sealed class LapCatchMonitor
    {
        public static LapCatchMonitor Instance { get; } = new();

        /// <summary>Pace differences under this many seconds per lap count as not catching.</summary>
        public const float MinClosingPerLap = 0.05f;

        /// <summary>An estimate beyond this many laps is shown as not catching.</summary>
        public const float MaxLaps = 99f;

        private const int PaceLaps = 5;           // laps in the pace median
        private const int MinPaceLaps = 2;        // laps a car needs before its pace is trusted

        private const int SurfaceNotInWorld = -1;
        private const int SurfaceOffTrack = 0;
        private const int SurfaceOnTrack = 3;

        private sealed class Pace
        {
            public readonly Queue<float> Laps = new();
            public float LastSeen;

            // Whether the lap in progress / the lap just finished was not a racing lap: the car touched
            // pit road or the garage, or ran under the grid / pace car. Off-track moments and contact
            // do not count against a lap. A car first seen mid-lap has an unknown last lap, so that
            // is left out too.
            private int _lapCompleted = -1;
            private bool _currentInvalid;
            private bool _previousInvalid = true;

            public void Add(Car car, bool gridOrPace)
            {
                if (car.LapCompleted != _lapCompleted)
                {
                    if (_lapCompleted >= 0) _previousInvalid = _currentInvalid;
                    _currentInvalid = false;
                    _lapCompleted = car.LapCompleted;
                }
                if (gridOrPace || car.OnPitRoad || car.IsInGarage)
                    _currentInvalid = true;

                float lap = car.LastLapTime;
                if (lap <= 0 || lap == LastSeen) return;
                LastSeen = lap;
                if (_previousInvalid) return;
                Laps.Enqueue(lap);
                while (Laps.Count > PaceLaps) Laps.Dequeue();
            }

            public bool TryGet(out float pace)
            {
                pace = 0f;
                if (Laps.Count < MinPaceLaps) return false;
                var sorted = Laps.ToArray();
                Array.Sort(sorted);
                int n = sorted.Length;
                pace = n % 2 == 1 ? sorted[n / 2] : (sorted[n / 2 - 1] + sorted[n / 2]) / 2f;
                return true;
            }
        }

        private readonly object _lock = new();
        private readonly Dictionary<int, Pace> _pace = new();
        private double _lastTime;
        private CatchEstimate _ahead = CatchEstimate.None;
        private CatchEstimate _behind = CatchEstimate.None;

        /// <summary>Laps for you to catch the nearest car in front on track.</summary>
        public CatchEstimate Ahead { get { lock (_lock) return _ahead; } }

        /// <summary>Laps for the nearest car behind on track to catch you.</summary>
        public CatchEstimate Behind { get { lock (_lock) return _behind; } }

        private LapCatchMonitor()
        {
            Services.IracingService.Instance.CarsUpdated += OnCarsUpdated;
            Services.IracingService.Instance.Disconnected += () => { lock (_lock) ResetLocked(); };
        }

        private void ResetLocked()
        {
            _pace.Clear();
            _ahead = _behind = CatchEstimate.None;
        }

        // SDK telemetry thread.
        private void OnCarsUpdated(IReadOnlyList<Car> cars)
        {
            var svc = Services.IracingService.Instance;
            double time = svc.SessionTime;
            var player = svc.PlayerCar;

            lock (_lock)
            {
                if (time - _lastTime < -1) ResetLocked(); // session restart
                _lastTime = time;

                foreach (var car in cars)
                {
                    if (car.CarIdx < 0) continue;
                    if (!_pace.TryGetValue(car.CarIdx, out var p)) _pace[car.CarIdx] = p = new Pace();
                    p.Add(car, svc.OnGridOrPaceLap);
                }

                _ahead = _behind = CatchEstimate.None;
                bool playerRacing = svc.IsConnected && player.CarIdx >= 0 && player.EstTime > 0 &&
                                    player.TrackSurface != SurfaceNotInWorld && !player.IsInGarage && !player.OnPitRoad &&
                                    !svc.OnGridOrPaceLap;
                if (!playerRacing) return;

                // Nearest car ahead / behind on track, wrap-corrected at the start/finish line.
                Car? ahead = null, behind = null;
                float aheadGap = float.PositiveInfinity, behindGap = float.PositiveInfinity;
                foreach (var car in cars)
                {
                    if (car.CarIdx < 0 || car.CarIdx == player.CarIdx || car.EstTime <= 0) continue;
                    if (car.OnPitRoad || car.IsInGarage) continue;
                    if (car.TrackSurface != SurfaceOnTrack && car.TrackSurface != SurfaceOffTrack) continue;

                    float gap = car.EstTime - player.EstTime;
                    float lapTime = player.ClassEstLapTime > 0 ? player.ClassEstLapTime
                                  : car.ClassEstLapTime > 0 ? car.ClassEstLapTime : 0f;
                    if (lapTime > 0)
                    {
                        if (gap > lapTime * 0.5f) gap -= lapTime;
                        else if (gap < -lapTime * 0.5f) gap += lapTime;
                    }

                    if (gap > 0 && gap < aheadGap) { aheadGap = gap; ahead = car; }
                    else if (gap < 0 && -gap < behindGap) { behindGap = -gap; behind = car; }
                }

                _pace.TryGetValue(player.CarIdx, out var mine);
                bool haveMine = mine != null && mine.TryGet(out _);
                float myPace = 0f;
                if (mine != null) mine.TryGet(out myPace);

                // Ahead: you gain when its laps are slower than yours. Behind: it gains when yours are slower than its.
                if (ahead != null)
                    _ahead = Estimate(ahead, aheadGap, haveMine, other => other - myPace);
                if (behind != null)
                    _behind = Estimate(behind, behindGap, haveMine, other => myPace - other);
            }
        }

        private CatchEstimate Estimate(Car other, float gap, bool haveMine, Func<float, float> closing)
        {
            if (!haveMine || !_pace.TryGetValue(other.CarIdx, out var p) || !p.TryGet(out float otherPace))
                return new CatchEstimate { State = CatchState.Unknown, CarIdx = other.CarIdx, GapSeconds = gap };

            float perLap = closing(otherPace);
            float laps = perLap > 0 ? gap / perLap : float.PositiveInfinity;
            bool catching = perLap >= MinClosingPerLap && laps <= MaxLaps;
            return new CatchEstimate
            {
                State = catching ? CatchState.Catching : CatchState.NotCatching,
                CarIdx = other.CarIdx,
                GapSeconds = gap,
                ClosingPerLap = perLap,
                LapsToCatch = catching ? laps : 0f,
            };
        }
    }
}
