using System;
using System.Collections.Generic;

namespace BeltTensionTest.WPF.Services.Data
{
    /// <summary>
    /// Every car's total distance round the track (laps plus the fraction of the lap), worked out
    /// every telemetry tick, plus its last lap time. iRacing's own race position and F2Time only
    /// change when a car crosses the line, so the boxes use these instead to place cars and to work
    /// out gaps as they happen.
    ///
    /// The lap count is kept here, by watching the lap fraction wrap at the start / finish line,
    /// rather than read from the sim's lap counter: the two do not always tick in the same frame,
    /// and one frame out would drop a car a whole lap down the order. The sim's count is followed
    /// again when the two stay apart for a second (a tow, a reset, joining mid-session).
    ///
    /// Last lap time is the sim's CarIdxLastLapTime, but that can be blank or stale after an
    /// invalid lap, so each line crossing is timed here too and used when the sim's value is
    /// missing or is not the lap just driven.
    ///
    /// State is guarded by one lock; the accessors are safe from any thread.
    /// </summary>
    public sealed class TrackPositionMonitor
    {
        public static TrackPositionMonitor Instance { get; } = new();

        private const int PaceLaps = 5;
        private const double ResyncSeconds = 1.0;
        private const float MinLapSeconds = 10f, MaxLapSeconds = 1800f;
        private const float SimLapAgrees = 0.25f; // the sim's last lap is "the lap just driven" within this of our timing

        private sealed class State
        {
            public bool Seen;
            public int Laps;
            public float LastPct;
            public double LastTime;
            public float Distance;
            public bool InWorld;

            public double CrossTime = double.NaN; // session time of the last line crossing
            public float MeasuredLap;             // the lap that crossing closed (0 = none)
            public bool PitThisLap;
            public readonly Queue<float> Recent = new(); // laps without a pit visit, for pace
            public double MismatchSince = -1;
        }

        private readonly object _lock = new();
        private readonly Dictionary<int, State> _cars = new();
        private double _lastSessionTime;

        private TrackPositionMonitor()
        {
            var svc = Services.IracingService.Instance;
            svc.CarsUpdated += OnCarsUpdated;
            svc.Disconnected += () => { lock (_lock) _cars.Clear(); };
            svc.SessionTypeChanged += _ => { lock (_lock) _cars.Clear(); };
        }

        /// <summary>Laps plus lap fraction, or NaN while the car has not been seen.</summary>
        public float Distance(int carIdx)
        {
            lock (_lock) return _cars.TryGetValue(carIdx, out var s) && s.Seen ? s.Distance : float.NaN;
        }

        /// <summary>Line crossings counted so far (the whole laps of <see cref="Distance"/>), or -1.</summary>
        public int Laps(int carIdx)
        {
            lock (_lock) return _cars.TryGetValue(carIdx, out var s) && s.Seen ? s.Laps : -1;
        }

        /// <summary>False for a car in the garage or not in the session.</summary>
        public bool InWorld(int carIdx)
        {
            lock (_lock) return _cars.TryGetValue(carIdx, out var s) && s.Seen && s.InWorld;
        }

        /// <summary>
        /// The time of the lap this car has just driven, valid or not (0 when it has not driven one).
        /// The sim's own figure when it is that lap, otherwise the crossing-to-crossing time.
        /// </summary>
        public float LastLap(Car car)
        {
            float measured;
            lock (_lock) measured = _cars.TryGetValue(car.CarIdx, out var s) ? s.MeasuredLap : 0f;
            if (car.LastLapTime > 0 && (measured <= 0 || Math.Abs(car.LastLapTime - measured) <= SimLapAgrees))
                return car.LastLapTime;
            return measured > 0 ? measured : Math.Max(0f, car.LastLapTime);
        }

        /// <summary>
        /// Seconds this car takes for a lap, for turning a distance into a time: the median of its
        /// last laps without a pit visit, else the class reference lap, else its best lap.
        /// </summary>
        public float Pace(Car car)
        {
            lock (_lock)
            {
                if (_cars.TryGetValue(car.CarIdx, out var s) && s.Recent.Count > 0)
                {
                    var sorted = s.Recent.ToArray();
                    Array.Sort(sorted);
                    int n = sorted.Length;
                    return n % 2 == 1 ? sorted[n / 2] : (sorted[n / 2 - 1] + sorted[n / 2]) / 2f;
                }
            }
            return car.ClassEstLapTime > 0 ? car.ClassEstLapTime : car.BestLapTime;
        }

        // SDK telemetry thread.
        private void OnCarsUpdated(IReadOnlyList<Car> cars)
        {
            var svc = Services.IracingService.Instance;
            double time = svc.SessionTime;

            lock (_lock)
            {
                if (time - _lastSessionTime < -1) _cars.Clear(); // session restart / replay jumped back
                _lastSessionTime = time;

                foreach (var car in cars)
                {
                    if (car.CarIdx < 0) continue;
                    if (!_cars.TryGetValue(car.CarIdx, out var st)) _cars[car.CarIdx] = st = new State();

                    st.InWorld = car.TrackSurface >= 0 && !car.IsInGarage && car.LapDistPct >= 0f;
                    if (car.LapDistPct < 0f) continue; // no position to follow
                    float pct = Math.Clamp(car.LapDistPct, 0f, 1f);

                    if (car.OnPitRoad) st.PitThisLap = true;

                    if (!st.Seen)
                    {
                        st.Seen = true;
                        st.Laps = Math.Max(0, car.Lap);
                    }
                    else
                    {
                        float d = pct - st.LastPct;
                        if (d < -0.5f)
                        {
                            st.Laps++;
                            Crossed(st, time, pct);
                        }
                        else if (d > 0.5f)
                        {
                            st.Laps--; // back over the line
                            st.CrossTime = double.NaN;
                        }

                        // Follow the sim's count if the two stay apart.
                        int diff = car.Lap < 0 ? 0 : car.Lap - st.Laps;
                        if (diff == 0) st.MismatchSince = -1;
                        else if (Math.Abs(diff) >= 2)
                        {
                            st.Laps = car.Lap;
                            st.MismatchSince = -1;
                            st.CrossTime = double.NaN;
                        }
                        else if (st.MismatchSince < 0) st.MismatchSince = time;
                        else if (time - st.MismatchSince > ResyncSeconds)
                        {
                            st.Laps = car.Lap;
                            st.MismatchSince = -1;
                        }
                    }

                    st.LastPct = pct;
                    st.LastTime = time;
                    st.Distance = st.Laps + pct;
                }
            }
        }

        // The car has just wrapped past the line to <pct>: when exactly it crossed, and the lap that closed.
        private static void Crossed(State st, double time, float pct)
        {
            float before = 1f - st.LastPct; // what was left of the lap at the previous tick
            double dt = time - st.LastTime;
            double cross = before + pct > 1e-6f ? st.LastTime + dt * before / (before + pct) : time;

            if (!double.IsNaN(st.CrossTime))
            {
                float lap = (float)(cross - st.CrossTime);
                if (lap >= MinLapSeconds && lap <= MaxLapSeconds)
                {
                    st.MeasuredLap = lap;
                    if (!st.PitThisLap)
                    {
                        st.Recent.Enqueue(lap);
                        while (st.Recent.Count > PaceLaps) st.Recent.Dequeue();
                    }
                }
                else st.MeasuredLap = 0f;
            }
            st.CrossTime = cross;
            st.PitThisLap = false;
        }
    }
}
