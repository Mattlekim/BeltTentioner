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

        /// <summary>
        /// Cars that did not score but were probably in it: within a car length of a car that
        /// did, at some point in the seconds before it scored (see
        /// <see cref="IncidentTracker.InvolvedGapMeters"/>), closest first. An estimate from
        /// track position alone, so a car in the next lane over can turn up here too.
        /// </summary>
        public int[] InvolvedCars { get; internal set; } = Array.Empty<int>();

        /// <summary>Closest approach in meters, around the lap, for each of <see cref="InvolvedCars"/>.</summary>
        public float[] InvolvedGaps { get; internal set; } = Array.Empty<float>();

        /// <summary>Who the driver held responsible: CarIdx, or -1 for nobody yet.</summary>
        public int BlameCarIdx { get; internal set; } = -1;

        /// <summary>True when the driver took the blame themselves.</summary>
        public bool BlameIsMine { get; internal set; }

        /// <summary>Why, e.g. "Reckless"; empty when no reason was picked.</summary>
        public string BlameReason { get; internal set; } = string.Empty;

        // The driver key and reason this incident has added to the driver's blame count (see DriverBlame), if any.
        internal string CountedKey = string.Empty;
        internal string CountedReason = string.Empty;

        /// <summary>True when the driver flagged it as a racing incident: nobody's fault.</summary>
        public bool BlameIsRacing { get; internal set; }

        /// <summary>True once the incident has been judged: blame taken, given to another driver, or called a racing incident.</summary>
        public bool HasBlame => BlameIsMine || BlameIsRacing || BlameCarIdx >= 0;

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

        /// <summary>A car this close (around the lap, in meters) to a car that scored, shortly before it did, was probably in the incident: about a car length.</summary>
        public const float InvolvedGapMeters = 6f;

        /// <summary>How far back before an incident is scored to look for cars that touched.</summary>
        public const double InvolvedWindowSeconds = 3.0;

        /// <summary>Most cars named as probably involved.</summary>
        private const int MaxInvolved = 3;

        // Where every car was, a few times a second, for the last few seconds: the car that
        // caused an incident has often moved on by the time it is scored.
        private const double HistorySeconds = 6.0;
        private const double SampleIntervalSeconds = 0.1;

        private sealed class Sample
        {
            public double Time;
            public float[] Meters = Array.Empty<float>(); // by CarIdx; NaN = not on the track
        }

        private readonly List<Sample> _history = new();

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
                if (_incidents.Count == 0 && _lastPoints.Count == 0 && _history.Count == 0) return;
                _incidents.Clear();
                _lastPoints.Clear();
                _activeUntil.Clear();
                _history.Clear();
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
                RecordSample(cars, time, trackLen);

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
                    {
                        var created = new Incident(car.CarIdx, scored, car.Lap, time, frame, meters,
                                                   TypeOf(car, scored, time),
                                                   NearbyTo(cars, meters, trackLen));
                        var (involved, gaps) = InvolvedWith(car.CarIdx, time, trackLen);
                        created.InvolvedCars = involved;
                        created.InvolvedGaps = gaps;
                        _incidents.Add(created);
                    }

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

                // It scored itself, so it is no longer a guess.
                int at = Array.IndexOf(inc.InvolvedCars, car.CarIdx);
                if (at >= 0)
                {
                    var cars = new List<int>(inc.InvolvedCars);
                    var gaps = new List<float>(inc.InvolvedGaps);
                    cars.RemoveAt(at);
                    gaps.RemoveAt(at);
                    inc.InvolvedCars = cars.ToArray();
                    inc.InvolvedGaps = gaps.ToArray();
                }
                return true;
            }
            return false;
        }

        // Keep this moment's positions, a few times a second, and drop what is older than the window.
        private void RecordSample(IReadOnlyList<Car> cars, double time, float trackLen)
        {
            // Time going backwards is a replay scrub or a reset: what was recorded no longer leads up to now.
            if (_history.Count > 0 && time < _history[^1].Time - 0.5) _history.Clear();
            if (_history.Count > 0 && time - _history[^1].Time < SampleIntervalSeconds) return;

            int size = 0;
            foreach (var car in cars) if (car.CarIdx >= size) size = car.CarIdx + 1;
            var sample = new Sample { Time = time, Meters = new float[size] };
            Array.Fill(sample.Meters, float.NaN);
            foreach (var car in cars)
            {
                if (car.CarIdx < 0 || !car.IsOnTrack || car.OnPitRoad || car.LapDistPct < 0) continue;
                sample.Meters[car.CarIdx] = Math.Clamp(car.LapDistPct, 0f, 1f) * trackLen;
            }
            _history.Add(sample);
            _history.RemoveAll(s => s.Time < time - HistorySeconds);
        }

        /// <summary>
        /// The cars that came within <see cref="InvolvedGapMeters"/> of the scoring car in the
        /// <see cref="InvolvedWindowSeconds"/> before it scored, closest first, with how close.
        /// Compares positions around the lap at the same instant, so a car that has carried on
        /// since still shows. Cars already in the incident are left out.
        /// </summary>
        private (int[] Cars, float[] Gaps) InvolvedWith(int scorer, double time, float trackLen)
        {
            var closest = new Dictionary<int, float>();
            foreach (var sample in _history)
            {
                if (sample.Time < time - InvolvedWindowSeconds || sample.Time > time) continue;
                if (scorer >= sample.Meters.Length || float.IsNaN(sample.Meters[scorer])) continue;
                float mine = sample.Meters[scorer];
                for (int other = 0; other < sample.Meters.Length; other++)
                {
                    if (other == scorer || float.IsNaN(sample.Meters[other])) continue;
                    float d = Math.Abs(sample.Meters[other] - mine);
                    d = Math.Min(d, trackLen - d); // across the start/finish line
                    if (d > InvolvedGapMeters) continue;
                    if (!closest.TryGetValue(other, out float best) || d < best) closest[other] = d;
                }
            }

            var list = new List<KeyValuePair<int, float>>(closest);
            list.Sort((a, b) => a.Value.CompareTo(b.Value));
            if (list.Count > MaxInvolved) list.RemoveRange(MaxInvolved, list.Count - MaxInvolved);
            var cars = new int[list.Count];
            var gaps = new float[list.Count];
            for (int i = 0; i < list.Count; i++) { cars[i] = list[i].Key; gaps[i] = list[i].Value; }
            return (cars, gaps);
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
            // Another driver blamed with a reason adds to that driver's count (and a blame it replaces comes off theirs).
            string key = string.Empty;
            if (!mine && carIdx >= 0)
                foreach (var car in IracingService.Instance.Cars)
                    if (car.CarIdx == carIdx) { key = DriverTags.KeyFor(car); break; }
            Recount(incident, key, reason ?? string.Empty);

            lock (_lock)
            {
                incident.BlameIsRacing = false;
                incident.BlameIsMine = mine;
                incident.BlameCarIdx = mine ? -1 : carIdx;
                incident.BlameReason = reason ?? string.Empty;
                Version++;
            }
            Changed?.Invoke();
        }

        // Take back what this incident added to a driver's blame count, then add the new blame (if any).
        private static void Recount(Incident incident, string key, string reason)
        {
            var blame = DriverBlame.Instance;
            if (incident.CountedKey.Length > 0)
                blame.Remove(incident.CountedKey, incident.CountedReason);
            incident.CountedKey = string.Empty;
            incident.CountedReason = string.Empty;
            if (key.Length == 0 || reason.Length == 0) return;
            blame.Add(key, reason);
            incident.CountedKey = key;
            incident.CountedReason = reason;
        }

        /// <summary>Flag an incident as a racing incident: nobody to blame.</summary>
        public void SetRacingIncident(Incident incident)
        {
            Recount(incident, string.Empty, string.Empty);
            lock (_lock)
            {
                incident.BlameIsRacing = true;
                incident.BlameIsMine = false;
                incident.BlameCarIdx = -1;
                incident.BlameReason = string.Empty;
                Version++;
            }
            Changed?.Invoke();
        }

        /// <summary>Take a blame assignment back off an incident.</summary>
        public void ClearBlame(Incident incident)
        {
            Recount(incident, string.Empty, string.Empty);
            lock (_lock)
            {
                incident.BlameIsRacing = false;
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
