using System;
using System.Collections.Generic;

namespace BeltTensionTest.WPF.Services.Data
{
    /// <summary>What an incident looks like to have been (see <see cref="IncidentTracker"/>).</summary>
    public enum IncidentType
    {
        /// <summary>Off-track excursion (the car was off the racing surface).</summary>
        OffTrack,
        /// <summary>A solo mistake: spin, loss of control, wall contact.</summary>
        SoloIncident,
        /// <summary>Contact with another car.</summary>
        CarContact,
    }

    /// <summary>
    /// One scored incident: when and where it happened, what it cost, and who
    /// was in it. Ported from IrachingHud's Incident. Instances are handed to
    /// the UI through <see cref="IncidentTracker.Snapshot"/> and are never
    /// mutated in place afterwards — <see cref="Cars"/> is replaced wholesale,
    /// so a reader always sees a complete list.
    /// </summary>
    public sealed class Incident
    {
        /// <summary>Incident points scored (1x, 2x, 4x...).</summary>
        public int Points { get; internal set; }

        /// <summary>Lap the first car involved was on.</summary>
        public int Lap { get; }

        /// <summary>Session clock when it was detected, in seconds.</summary>
        public double Time { get; }

        /// <summary>Replay frame at detection (ReplayFrameNumEnd); -1 when unknown.</summary>
        public int Frame { get; }

        /// <summary>Distance around the lap where it happened, in meters.</summary>
        public float TrackPosition { get; }

        public IncidentType Type { get; internal set; }

        /// <summary>CarIdx of every car that scored points here, in the order they did.</summary>
        public int[] Cars { get; internal set; }

        /// <summary>
        /// Cars that were in the world near this spot when it happened,
        /// closest first — the candidates for blaming someone else. Includes
        /// the cars that actually scored.
        /// </summary>
        public int[] NearbyCars { get; }

        /// <summary>Who the driver held responsible: CarIdx, or -1 for nobody yet.</summary>
        public int BlameCarIdx { get; internal set; } = -1;

        /// <summary>True when the driver took the blame themselves.</summary>
        public bool BlameIsMine { get; internal set; }

        /// <summary>Why, e.g. "Reckless"; empty when no reason was picked.</summary>
        public string BlameReason { get; internal set; } = string.Empty;

        /// <summary>True once blame has been assigned either way.</summary>
        public bool HasBlame => BlameIsMine || BlameCarIdx >= 0;

        internal Incident(int carIdx, int points, int lap, double time, int frame,
                          float trackPosition, IncidentType type, int[] nearbyCars)
        {
            Cars = new[] { carIdx };
            NearbyCars = nearbyCars;
            Points = points;
            Lap = lap;
            Time = time;
            Frame = frame;
            TrackPosition = trackPosition;
            Type = type;
        }
    }

    /// <summary>
    /// Builds the session's incident list from each driver's incident count,
    /// ported from IrachingHud's IncidentTracker. A car's count going up means
    /// it just scored; if another car scored the same amount within
    /// <see cref="TimeRangeSeconds"/> and <see cref="TrackRangeMeters"/> of an
    /// existing incident, the two are the same incident and it becomes
    /// <see cref="IncidentType.CarContact"/> — that is how contact is told
    /// apart from a solo mistake without any collision data.
    ///
    /// Fed from CarsUpdated on the SDK telemetry thread; all state is under one
    /// lock and <see cref="Snapshot"/> is safe from any thread. The list is
    /// cleared when iRacing connects or disconnects, and survives replay
    /// scrubbing (a count going backwards just re-baselines that car).
    /// </summary>
    public sealed class IncidentTracker
    {
        public static IncidentTracker Instance { get; } = new();

        /// <summary>Two cars score the same incident within this many seconds of each other.</summary>
        public const float TimeRangeSeconds = 4f;

        /// <summary>...and within this many meters of each other around the lap.</summary>
        public const float TrackRangeMeters = 20f;

        /// <summary>
        /// How far around the lap to look for cars that might be to blame.
        /// Wider than <see cref="TrackRangeMeters"/>: the car that put you off
        /// has usually carried on a fair way by the time the incident is scored.
        /// </summary>
        public const float NearbyRangeMeters = 50f;

        /// <summary>iRacing keeps an incident "active" for ten seconds after it scores.</summary>
        private const double ActiveIncidentSeconds = 10;

        private readonly object _lock = new();
        private readonly List<Incident> _incidents = new();
        private readonly Dictionary<int, int> _lastPoints = new();   // CarIdx -> last seen count
        private readonly Dictionary<int, double> _activeUntil = new(); // CarIdx -> session time

        /// <summary>Bumped on every change, so a UI can skip work when nothing moved.</summary>
        public int Version { get; private set; }

        /// <summary>Raised on the telemetry thread when an incident is added or grows.</summary>
        public event Action? Changed;

        private IncidentTracker()
        {
            IracingService.Instance.CarsUpdated += OnCarsUpdated;
            IracingService.Instance.Connected += Clear;
            IracingService.Instance.Disconnected += Clear;
        }

        /// <summary>The incidents so far, oldest first.</summary>
        public Incident[] Snapshot()
        {
            lock (_lock) return _incidents.ToArray();
        }

        public void Clear()
        {
            lock (_lock)
            {
                if (_incidents.Count == 0 && _lastPoints.Count == 0) return;
                _incidents.Clear();
                _lastPoints.Clear();
                _activeUntil.Clear();
                Version++;
            }
            Changed?.Invoke();
        }

        // SDK telemetry thread.
        private void OnCarsUpdated(IReadOnlyList<Car> cars)
        {
            var svc = IracingService.Instance;
            double time = svc.SessionTime;
            float trackLen = svc.TrackLengthMeters;
            int frame = svc.ReplayFrameNumEnd;
            if (!svc.IsConnected || trackLen <= 0 || time < 0) return;

            bool changed = false;
            lock (_lock)
            {
                foreach (var car in cars)
                {
                    if (car.CarIdx < 0) continue;
                    int points = car.IncidentPoints;
                    if (points < 0) continue;

                    // First sighting seeds the baseline; a count going
                    // backwards (replay scrub, session reset) re-seeds it.
                    if (!_lastPoints.TryGetValue(car.CarIdx, out int last) || points < last)
                    {
                        _lastPoints[car.CarIdx] = points;
                        continue;
                    }
                    if (points == last) continue;

                    int scored = points - last;
                    _lastPoints[car.CarIdx] = points;

                    float meters = Math.Clamp(car.LapDistPct, 0f, 1f) * trackLen;
                    if (meters < 0) continue;

                    if (!TryMerge(car, scored, time, meters))
                        _incidents.Add(new Incident(car.CarIdx, scored, car.Lap, time, frame, meters,
                                                    TypeOf(car, scored, time),
                                                    NearbyTo(cars, meters, trackLen)));

                    _activeUntil[car.CarIdx] = time + ActiveIncidentSeconds;
                    changed = true;
                }

                if (changed) Version++;
            }

            if (changed) Changed?.Invoke();
        }

        /// <summary>
        /// Fold this car into an incident another car already scored: same
        /// points, close in time and in track position, and not one this car
        /// is already part of. Two cars scoring together means contact.
        /// </summary>
        private bool TryMerge(Car car, int scored, double time, float meters)
        {
            for (int i = _incidents.Count - 1; i >= 0; i--) // newest first
            {
                var inc = _incidents[i];
                double age = time - inc.Time;
                if (age > TimeRangeSeconds) break;    // this one and everything older
                if (age < -TimeRangeSeconds) continue; // ahead of us (replay scrub)
                if (inc.Points != scored) continue;
                if (Array.IndexOf(inc.Cars, car.CarIdx) >= 0) continue;
                if (Math.Abs(inc.TrackPosition - meters) > TrackRangeMeters) continue;

                var grown = new int[inc.Cars.Length + 1];
                Array.Copy(inc.Cars, grown, inc.Cars.Length);
                grown[inc.Cars.Length] = car.CarIdx;
                inc.Cars = grown;
                inc.Type = IncidentType.CarContact;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Cars in the world (not in the pits) within
        /// <see cref="NearbyRangeMeters"/> of a point on the lap, closest
        /// first. Ported from the original's GetNearbyCars, with the
        /// start/finish line handled: 10 m either side of it is 20 m apart,
        /// not a lap.
        /// </summary>
        private static int[] NearbyTo(IReadOnlyList<Car> cars, float meters, float trackLen)
        {
            var near = new List<(int CarIdx, float Distance)>();
            foreach (var car in cars)
            {
                if (car.CarIdx < 0 || !car.IsOnTrack || car.OnPitRoad) continue;
                float pos = Math.Clamp(car.LapDistPct, 0f, 1f) * trackLen;
                float d = Math.Abs(pos - meters);
                d = Math.Min(d, trackLen - d); // across the start/finish line
                if (d > NearbyRangeMeters) continue;
                near.Add((car.CarIdx, d));
            }
            near.Sort((a, b) => a.Distance.CompareTo(b.Distance));

            var result = new int[near.Count];
            for (int i = 0; i < near.Count; i++) result[i] = near[i].CarIdx;
            return result;
        }

        /// <summary>Record who was to blame for an incident, and why.</summary>
        public void SetBlame(Incident incident, bool mine, int carIdx, string reason)
        {
            lock (_lock)
            {
                incident.BlameIsMine = mine;
                incident.BlameCarIdx = mine ? -1 : carIdx;
                incident.BlameReason = reason ?? string.Empty;
                Version++;
            }
            Changed?.Invoke();
        }

        /// <summary>Take a blame assignment back off an incident.</summary>
        public void ClearBlame(Incident incident)
        {
            lock (_lock)
            {
                incident.BlameIsMine = false;
                incident.BlameCarIdx = -1;
                incident.BlameReason = string.Empty;
                Version++;
            }
            Changed?.Invoke();
        }

        /// <summary>
        /// Classify a fresh incident the way the original does: off the
        /// surface is an off-track, and the bigger point scores mean contact.
        /// A car that is already inside iRacing's ten-second incident window
        /// scoring 2x again has hit something rather than spun on its own.
        /// </summary>
        private IncidentType TypeOf(Car car, int scored, double time)
        {
            bool active = _activeUntil.TryGetValue(car.CarIdx, out double until) && time < until;
            var type = car.IsOffTrack ? IncidentType.OffTrack : IncidentType.SoloIncident;

            switch (scored)
            {
                case 1: if (active) type = IncidentType.SoloIncident; break;
                case 2: type = active ? IncidentType.CarContact : IncidentType.SoloIncident; break;
                default: if (scored >= 3) type = IncidentType.CarContact; break;
            }
            return type;
        }
    }
}
