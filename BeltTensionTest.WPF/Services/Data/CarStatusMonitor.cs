using System;
using System.Collections.Generic;

namespace BeltTensionTest.WPF.Services.Data
{
    /// <summary>Estimated pit phase of a car (see <see cref="CarStatusMonitor"/>).</summary>
    public enum PitState : byte
    {
        None,
        PitIn,  // on pit road, arrived from the track (heading for the stall)
        Pit,    // stationary in the stall / in the garage
        PitOut, // on pit road after being stopped, heading out
        OutLap, // back on track, still on the first lap out of the pits
    }

    public struct CarStatus
    {
        public PitState Pit;
        public bool YellowHazard; // this car is currently triggering the yellow-flag warning
        public bool SlowHazard;   // this car is currently triggering the slow-car warning
    }

    /// <summary>
    /// Shared per-car status detector, fed from CarsUpdated on the SDK
    /// telemetry thread. Tracks, for every car:
    ///
    ///  - Yellow: sustained sub-50 km/h on track (IrachingHud's YellowWaring),
    ///  - Slow: the on-track gap to the player closing faster than 0.25 s/s
    ///    with hysteresis (IrachingHud's IsSlowCar),
    ///  - whether that car is *triggering* a warning right now (ahead of the
    ///    player and within 10 seconds), plus the aggregate nearest gaps that
    ///    WarningOverlay / SlowCarOverlay display,
    ///  - an estimated pit phase: entering pit road from the track = PitIn,
    ///    stopped in the stall or in the garage = Pit, moving on pit road
    ///    after a stop (or freshly appeared there) = PitOut, then OutLap for
    ///    the whole first lap back on track (until the lap counter ticks).
    ///
    /// All state is guarded by one lock; the public accessors are safe from
    /// any thread. Resets when iRacing disconnects or the session clock jumps
    /// backwards (restart).
    /// </summary>
    public sealed class CarStatusMonitor
    {
        public static CarStatusMonitor Instance { get; } = new();

        private const float SlowSpeedKmh = 50f;    // yellow: below this a car is "slow"
        private const float SlowHoldSeconds = 1f;  // ...for at least this long
        private const float CatchRateOn = 0.25f;   // slow-car: s/s closing → slow
        private const float CatchRateOff = 0.125f; // slow-car: s/s → clear again
        private const float WarnWindowSeconds = 10f;
        private const float StallSpeedKmh = 5f;    // under this on pit road = in the stall

        private sealed class Watch
        {
            public float LastDist = float.NaN; // laps + pct, for speed derivation
            public float SpeedKmh;
            public float BelowTime;
            public bool Yellow;
            public float LastGap = float.NaN;  // est-time gap to the player last tick
            public float Rate;                 // smoothed closing rate, s/s
            public bool Slow;
            public bool YellowHazard;
            public bool SlowHazard;
            public PitState Pit;
            public int ExitLap = -1;           // lap number when the car rejoined the track
            public bool Seen;                  // had at least one tick (first tick seeds state)
        }

        private readonly object _lock = new();
        private readonly Dictionary<int, Watch> _watch = new();
        private double _lastTime;
        private bool _yellowActive, _slowActive;
        private float _yellowGap, _slowGap;

        /// <summary>Nearest car currently triggering the yellow-flag warning (gap in seconds).</summary>
        public (bool Active, float Gap) Yellow { get { lock (_lock) return (_yellowActive, _yellowGap); } }

        /// <summary>Nearest car currently triggering the slow-car warning (gap in seconds).</summary>
        public (bool Active, float Gap) Slow { get { lock (_lock) return (_slowActive, _slowGap); } }

        public CarStatus StatusOf(int carIdx)
        {
            lock (_lock)
            {
                if (!_watch.TryGetValue(carIdx, out var w)) return default;
                return new CarStatus { Pit = w.Pit, YellowHazard = w.YellowHazard, SlowHazard = w.SlowHazard };
            }
        }

        private CarStatusMonitor()
        {
            Services.IracingService.Instance.CarsUpdated += OnCarsUpdated;
            Services.IracingService.Instance.Disconnected += () => { lock (_lock) ResetLocked(); };
        }

        private void ResetLocked()
        {
            _watch.Clear();
            _yellowActive = _slowActive = false;
        }

        // SDK telemetry thread.
        private void OnCarsUpdated(IReadOnlyList<Car> cars)
        {
            var svc = Services.IracingService.Instance;
            double time = svc.SessionTime;
            float trackLen = svc.TrackLengthMeters;
            var player = svc.PlayerCar;

            lock (_lock)
            {
                float dt = (float)(time - _lastTime);
                if (dt < -1) ResetLocked(); // session restart
                _lastTime = time;
                if (dt <= 0 || dt > 2) return; // paused / first tick / huge jump

                _yellowActive = _slowActive = false;
                _yellowGap = _slowGap = float.PositiveInfinity;
                if (!svc.IsConnected || trackLen <= 0 || player.CarIdx < 0) return;

                foreach (var car in cars)
                {
                    if (car.CarIdx < 0) continue;
                    if (!_watch.TryGetValue(car.CarIdx, out var w))
                        _watch[car.CarIdx] = w = new Watch();

                    bool isPlayer = car.CarIdx == player.CarIdx;
                    float pct = Math.Clamp(car.LapDistPct, 0f, 1f);

                    // Speed from lap-distance progress; Lap+Pct is continuous
                    // across the line so no wrap handling is needed.
                    float dist = car.Lap + pct;
                    if (!float.IsNaN(w.LastDist))
                    {
                        float kmh = Math.Max(0f, (dist - w.LastDist) * trackLen / dt * 3.6f);
                        if (kmh < 400f) // ignore teleports (tow, session resets)
                            w.SpeedKmh = w.SpeedKmh * 0.7f + kmh * 0.3f;
                    }
                    w.LastDist = dist;

                    UpdatePitState(w, car);

                    // Yellow: sustained low speed on track.
                    if (!car.IsOnTrack || car.OnPitRoad)
                    {
                        w.Yellow = false;
                        w.BelowTime = 0;
                    }
                    else if (w.SpeedKmh < SlowSpeedKmh)
                    {
                        w.BelowTime += dt;
                        if (w.BelowTime > SlowHoldSeconds) w.Yellow = true;
                    }
                    else
                    {
                        w.Yellow = false;
                        w.BelowTime = 0;
                    }

                    // On-track gap to the player (positive = ahead), wrap-
                    // corrected at the start/finish line.
                    w.YellowHazard = w.SlowHazard = false;
                    if (isPlayer || car.EstTime <= 0 || player.EstTime <= 0)
                    {
                        w.LastGap = float.NaN;
                        continue;
                    }
                    float gap = car.EstTime - player.EstTime;
                    float lapTime = player.ClassEstLapTime > 0 ? player.ClassEstLapTime
                                  : car.ClassEstLapTime > 0 ? car.ClassEstLapTime : 0f;
                    if (lapTime > 0)
                    {
                        if (gap > lapTime * 0.5f) gap -= lapTime;
                        else if (gap < -lapTime * 0.5f) gap += lapTime;
                    }

                    // Slow-car: smoothed closing rate with hysteresis. Wrap
                    // flips of the gap produce absurd rates — skip those.
                    if (!float.IsNaN(w.LastGap))
                    {
                        float rate = (w.LastGap - gap) / dt;
                        if (Math.Abs(rate) < 50f)
                            w.Rate = w.Rate * 0.8f + rate * 0.2f;
                    }
                    w.LastGap = gap;

                    if (!car.IsOnTrack || car.OnPitRoad || w.Yellow)
                    {
                        w.Slow = false;
                        w.Rate = 0;
                    }
                    else if (w.Rate > CatchRateOn) w.Slow = true;
                    else if (w.Rate < CatchRateOff) w.Slow = false;

                    bool inWindow = gap > 0 && gap < WarnWindowSeconds;
                    if (w.Yellow && inWindow)
                    {
                        w.YellowHazard = true;
                        _yellowActive = true;
                        if (gap < _yellowGap) _yellowGap = gap;
                    }
                    if (w.Slow && inWindow)
                    {
                        w.SlowHazard = true;
                        _slowActive = true;
                        if (gap < _slowGap) _slowGap = gap;
                    }
                }
            }
        }

        private static void UpdatePitState(Watch w, Car car)
        {
            if (car.IsInGarage)
            {
                w.Pit = PitState.Pit;
            }
            else if (car.OnPitRoad)
            {
                if (!w.Seen)
                    w.Pit = PitState.Pit; // no history (session join): assume parked
                else if (w.SpeedKmh < StallSpeedKmh)
                    w.Pit = PitState.Pit;
                else if (w.Pit == PitState.Pit || w.Pit == PitState.PitOut)
                    w.Pit = PitState.PitOut; // moving again after a stop → leaving
                else
                    w.Pit = PitState.PitIn;  // arrived moving from the track
            }
            else
            {
                // Back on track: the first full lap after leaving pit road is
                // the out lap — flagged until the lap counter ticks over.
                if (w.Pit == PitState.PitOut || w.Pit == PitState.Pit || w.Pit == PitState.PitIn)
                {
                    w.Pit = PitState.OutLap;
                    w.ExitLap = car.Lap;
                }
                else if (w.Pit == PitState.OutLap && car.Lap > w.ExitLap)
                {
                    w.Pit = PitState.None;
                }
                else if (w.Pit != PitState.OutLap)
                {
                    w.Pit = PitState.None;
                }
            }
            w.Seen = true;
        }
    }
}
