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

    /// <summary>Lap relation of the car behind, which tints the car-behind box.</summary>
    public enum BehindLap : byte
    {
        SameLap,  // racing for position (black)
        LappedBy, // you are 500 m+ ahead of it: a lapped car behind (blue)
        LapsUp,   // it is 500 m+ ahead of you: a leader closing to lap you (red)
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
    ///  - the nearest car behind the player on track (IrachingHud's
    ///    DeltaToCarBehind) and whether it is on the same lap, for
    ///    CarBehindOverlay,
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
        private const float StopSpeedKmh = 4f;     // under this on pit road the car is stopped...
        private const float StopHoldSeconds = 0.4f;//   ...once it has been for this long
        private const float GoSpeedKmh = 10f;      // over this on pit road the car is moving...
        private const float GoHoldSeconds = 0.4f;  //   ...once it has been for this long

        // CarIdxTrackSurface values (irsdk TrkLoc enum).
        private const int SurfaceNotInWorld = -1;
        private const int SurfaceOffTrack = 0;
        private const int SurfaceOnTrack = 3;

        private sealed class Watch
        {
            public float LastDist = float.NaN; // laps + pct, for speed derivation
            public float SpeedKmh;
            public float BelowTime;
            public bool Yellow;
            public double SampleTime = -1;     // when SampleGap was taken
            public float SampleGap = float.NaN;// est-time gap to the player ~1 s ago
            public float Rate;                 // closing rate over that baseline, s/s
            public bool Slow;
            public bool YellowHazard;
            public bool SlowHazard;
            public PitState Pit;
            public float StoppedFor;           // seconds in a row on pit road below StopSpeedKmh
            public float MovingFor;            // seconds in a row on pit road above GoSpeedKmh
            public bool StoppedThisVisit;      // has stopped since it came onto pit road (a drive-through never does)
            public int ExitLap = -1;           // lap number when the car rejoined the track
            public bool Seen;                  // had at least one tick (first tick seeds state)
        }

        private readonly object _lock = new();
        private readonly Dictionary<int, Watch> _watch = new();
        private double _lastTime;
        private bool _yellowActive, _slowActive;
        private float _yellowGap, _slowGap;
        private bool _behindActive;
        private int _behindCarIdx = -1;
        private float _behindGap;
        private BehindLap _behindLap;

        /// <summary>Nearest car currently triggering the yellow-flag warning (gap in seconds).</summary>
        public (bool Active, float Gap) Yellow { get { lock (_lock) return (_yellowActive, _yellowGap); } }

        /// <summary>Nearest car currently triggering the slow-car warning (gap in seconds).</summary>
        public (bool Active, float Gap) Slow { get { lock (_lock) return (_slowActive, _slowGap); } }

        /// <summary>
        /// Nearest car behind the player on track: gap in seconds (negative,
        /// e.g. -0.85) and its lap relation. Only cars on or just off the
        /// racing surface count, and only while the player is on track or on
        /// pit road, matching IrachingHud. Callers apply their own show window.
        /// </summary>
        public (bool Active, float Gap, BehindLap Lap) Behind
        {
            get { lock (_lock) return (_behindActive, _behindGap, _behindLap); }
        }

        /// <summary>CarIdx of the car <see cref="Behind"/> is measuring; -1 when there is none.</summary>
        public int BehindCarIdx
        {
            get { lock (_lock) return _behindActive ? _behindCarIdx : -1; }
        }

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
            _yellowActive = _slowActive = _behindActive = false;
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

                // On the grid and on the pace lap nothing counts as a hazard (cars
                // creep and bunch up) and nobody is on an out lap.
                bool gridOrPace = svc.OnGridOrPaceLap;
                _yellowActive = _slowActive = _behindActive = false;
                _yellowGap = _slowGap = float.PositiveInfinity;
                _behindGap = float.NegativeInfinity;
                if (!svc.IsConnected || trackLen <= 0 || player.CarIdx < 0) return;

                // IrachingHud only measures the car behind while the player is
                // on track, off track or on pit road (not in the garage).
                bool playerRacing = player.TrackSurface != SurfaceNotInWorld && !player.IsInGarage;
                float playerMeters = (player.Lap + Math.Clamp(player.LapDistPct, 0f, 1f)) * trackLen;

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

                    UpdatePitState(w, car, gridOrPace, dt);

                    // Yellow: sustained low speed on track.
                    if (gridOrPace || !car.IsOnTrack || car.OnPitRoad)
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
                        w.SampleGap = float.NaN;
                        w.SampleTime = -1;
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

                    // Car behind: the closest negative gap among cars on (or
                    // just off) the racing surface. Tinted by lap relation
                    // using total distance, with IrachingHud's 500 m margin.
                    if (playerRacing && gap < 0 && gap > _behindGap &&
                        (car.TrackSurface == SurfaceOnTrack || car.TrackSurface == SurfaceOffTrack))
                    {
                        float carMeters = dist * trackLen;
                        _behindActive = true;
                        _behindCarIdx = car.CarIdx;
                        _behindGap = gap;
                        _behindLap = playerMeters - carMeters > 500f ? BehindLap.LappedBy
                                   : carMeters - playerMeters > 500f ? BehindLap.LapsUp
                                   : BehindLap.SameLap;
                    }

                    // Slow-car: closing rate measured over a ~1 s baseline.
                    // Remote-car EstTime moves in steps, so tick-to-tick
                    // deltas are noise; a longer baseline gives a stable s/s
                    // figure. Wrap flips of the gap produce absurd rates —
                    // reset the baseline instead of ingesting those.
                    if (w.SampleTime < 0 || float.IsNaN(w.SampleGap))
                    {
                        w.SampleTime = time;
                        w.SampleGap = gap;
                    }
                    else if (time - w.SampleTime >= 1.0)
                    {
                        float rate = (w.SampleGap - gap) / (float)(time - w.SampleTime);
                        if (Math.Abs(rate) < 50f) w.Rate = rate;
                        w.SampleTime = time;
                        w.SampleGap = gap;
                    }

                    if (gridOrPace || !car.IsOnTrack || car.OnPitRoad || w.Yellow)
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

        // Pit phase on pit road, from how the car moves (its speed is worked out from its lap
        // distance, which is noisy, so a change needs to hold for a moment):
        //   PIT IN   arrived from the track and has not stopped yet; a drive-through stays
        //            PIT IN all the way until the car is back on the track,
        //   PIT      stopped,
        //   PIT OUT  moving again after having stopped, until it is back on the track.
        private static void UpdatePitState(Watch w, Car car, bool gridOrPace, float dt)
        {
            if (car.IsInGarage)
            {
                w.Pit = PitState.Pit;
                w.StoppedThisVisit = true;
                w.StoppedFor = w.MovingFor = 0f;
            }
            else if (car.OnPitRoad)
            {
                bool arriving = w.Pit == PitState.None || w.Pit == PitState.OutLap;
                if (!w.Seen)
                {
                    // No history (session join): assume parked.
                    w.Pit = PitState.Pit;
                    w.StoppedThisVisit = true;
                }
                else if (arriving)
                {
                    w.Pit = PitState.PitIn;
                    w.StoppedThisVisit = false;
                    w.StoppedFor = w.MovingFor = 0f;
                }

                if (dt > 0f && dt < 1f)
                {
                    if (w.SpeedKmh < StopSpeedKmh) { w.StoppedFor += dt; w.MovingFor = 0f; }
                    else if (w.SpeedKmh > GoSpeedKmh) { w.MovingFor += dt; w.StoppedFor = 0f; }
                }

                if (w.StoppedFor >= StopHoldSeconds)
                {
                    w.Pit = PitState.Pit;
                    w.StoppedThisVisit = true;
                }
                else if (w.MovingFor >= GoHoldSeconds && w.StoppedThisVisit)
                {
                    w.Pit = PitState.PitOut;
                }
                // Otherwise keep the phase it is in: PIT IN until it stops, PIT while it creeps, PIT OUT once rolling.
            }
            else if (gridOrPace)
            {
                // Placed on the grid (from the garage or pit road) is not an out lap.
                w.Pit = PitState.None;
            }
            else
            {
                // Back on track: the first full lap after leaving pit road is
                // the out lap — flagged until the lap counter ticks over.
                if (w.Pit == PitState.PitOut || w.Pit == PitState.Pit || w.Pit == PitState.PitIn)
                {
                    w.Pit = PitState.OutLap;
                    w.ExitLap = car.Lap;
                    w.StoppedThisVisit = false;
                    w.StoppedFor = w.MovingFor = 0f;
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
