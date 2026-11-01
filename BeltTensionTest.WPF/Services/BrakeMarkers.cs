using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using BeltTensionTest.WPF.Models;
using MonoXR.Client;

namespace BeltTensionTest.WPF.Services
{
    /// <summary>
    /// Brake markers: a button drops a marker where the car is, and from then on a red strip 5 m wide
    /// and half a metre high stands on the road there, facing the way the car faced - solid red at the
    /// bottom fading to clear at the top - so you can see where you braked (or want to).
    ///
    /// The strips are not drawn on the overlay canvas. This class owns a few OpenXR overlays of its own
    /// (<see cref="MonoGameOverlayHost.CreateWorldOverlay"/>) and moves them every frame to where the
    /// markers are relative to the car, in the same cockpit-fixed space the canvas is placed in (the
    /// origin the overlay is recentred to). The car's position comes from the sim's GPS telemetry
    /// (Lat / Lon / Alt / YawNorth), so a marker is a real place on the track and is kept per track.
    ///
    /// Pressing the button within <see cref="ReplaceRadiusMeters"/> of an existing marker moves that
    /// marker to the new spot instead of adding another.
    /// </summary>
    public sealed class BrakeMarkers : IDisposable
    {
        public const double ReplaceRadiusMeters = 20.0;
        public const float StripWidthMeters = 5f;
        public const float StripHeightMeters = 0.5f;

        // Only markers this far ahead are drawn: closer ones would be under the dash (an OpenXR quad has
        // no depth test, so it would paint over the cockpit), further ones are too small to matter.
        private const double MinDistanceMeters = 6.0;
        private const double MaxDistanceMeters = 300.0;
        private const int MaxShown = 4;
        private const double MinFacing = 0.3; // cosine of about 72 degrees: more edge-on than that is not drawn
        private const double EarthRadius = 6371000.0;
        private const double DegToRad = Math.PI / 180.0;

        // Texture of the strip: 128 x 32, red, opaque along the bottom row and clear along the top.
        private const int TexW = 128, TexH = 32;

        private readonly MonoGameOverlayHost _host;
        private readonly AppSettings _settings;
        private readonly Action _saved;
        private readonly Action<string> _log;
        private readonly List<MonoGameOverlayHost.WorldOverlay> _overlays = new();
        private readonly object _lock = new();
        private bool _markRequested;

        // The sim's yaw may count either way round; which one is worked out from the direction the car
        // actually travels (see Calibrate), so the markers cannot come out mirrored.
        private double _signScore;
        private IracingService.WorldPose? _calibrationPrev;

        /// <summary>The controller button that drops a marker.</summary>
        public RecenterBinding Button { get; }

        public BrakeMarkers(MonoGameOverlayHost host, AppSettings settings, Action saved, Action<string> log)
        {
            _host = host;
            _settings = settings;
            _saved = saved;
            _log = log;
            Button = new RecenterBinding(settings.OverlayBrakeMarkerButton, RequestMark);
            Button.Changed += () => { if (!Button.Capturing) log(Button.Binding.Length > 0 ? "Brake marker button: " + RecenterBinding.Short(Button.Binding) : "Brake marker button: none"); };

            var pixels = new byte[TexW * TexH * 4];
            for (int y = 0; y < TexH; y++)
            {
                // Row 0 is the top: clear. The last row is the bottom: opaque.
                byte alpha = (byte)Math.Round(255.0 * y / (TexH - 1));
                for (int x = 0; x < TexW; x++)
                {
                    int i = (y * TexW + x) * 4;
                    pixels[i] = 255; pixels[i + 1] = 0; pixels[i + 2] = 0; pixels[i + 3] = alpha;
                }
            }
            // A failure here (no free overlay slot) must not stop the overlay from starting: fewer strips, that is all.
            try
            {
                for (int i = 0; i < MaxShown; i++)
                    _overlays.Add(host.CreateWorldOverlay(TexW, TexH, pixels));
            }
            catch (Exception ex) { log("Brake markers: could not create all overlays - " + ex.Message); }
        }

        /// <summary>How far below the recentred origin (the headset when the overlay was recentred) the road is.</summary>
        public float Height => (float)Math.Clamp(_settings.BrakeMarkerHeight, -3.0, 1.0);

        /// <summary>How many markers this track has.</summary>
        public int CountOnTrack
        {
            get
            {
                string key = IracingService.Instance.TrackKey;
                return key.Length > 0 && _settings.BrakeMarkers.TryGetValue(key, out var list) ? list.Count : 0;
            }
        }

        /// <summary>The button was pressed (any thread); the marker is dropped on the next <see cref="Update"/>.</summary>
        public void RequestMark() { lock (_lock) _markRequested = true; }

        /// <summary>Remove this track's markers.</summary>
        public void ClearTrack()
        {
            string key = IracingService.Instance.TrackKey;
            if (key.Length == 0 || !_settings.BrakeMarkers.Remove(key)) return;
            _log("Brake markers cleared for this track.");
            _saved();
        }

        public void SetHeight(float meters)
        {
            _settings.BrakeMarkerHeight = Math.Clamp(meters, -3f, 1f);
            _saved();
        }

        /// <summary>Copy the binding into the settings (the markers themselves already live there).</summary>
        public void Export() => _settings.OverlayBrakeMarkerButton = Button.Binding;

        // Where the car is "now": the last sample carried forward along its heading by the speed, so the
        // strips glide between the sim's 60 Hz samples instead of stepping.
        private (double Lat, double Lon, float Alt, double SignedYaw) CarNow(IracingService.WorldPose p)
        {
            double yaw = YawSign * p.YawNorth;
            double age = Math.Clamp((Stopwatch.GetTimestamp() - p.Ticks) / (double)Stopwatch.Frequency, 0.0, 0.05);
            double dist = Math.Max(0f, p.Speed) * age;
            double east = -Math.Sin(yaw) * dist, north = Math.Cos(yaw) * dist;
            double lat = p.Lat + north / (EarthRadius * DegToRad);
            double lon = p.Lon + east / (EarthRadius * Math.Cos(p.Lat * DegToRad) * DegToRad);
            return (lat, lon, p.Alt, yaw);
        }

        /// <summary>Metres east and north of <paramref name="lat0"/>,<paramref name="lon0"/> that <paramref name="lat"/>,<paramref name="lon"/> lies.</summary>
        private static (double East, double North) Offset(double lat0, double lon0, double lat, double lon) =>
            ((lon - lon0) * DegToRad * EarthRadius * Math.Cos(lat0 * DegToRad),
             (lat - lat0) * DegToRad * EarthRadius);

        // The yaw sign: sample the car's movement and see which way round makes "forward" match it.
        private void Calibrate(IracingService.WorldPose p)
        {
            var prev = _calibrationPrev;
            if (prev == null) { _calibrationPrev = p; return; }
            if (p.Ticks == prev.Ticks) return;
            var (east, north) = Offset(prev.Lat, prev.Lon, p.Lat, p.Lon);
            double moved = Math.Sqrt(east * east + north * north);
            if (moved < 1.5) return; // too little to tell a direction from
            _calibrationPrev = p;
            if (p.Speed < 8f) return; // slow / reversing: the direction of travel is not the direction it faces
            double travel = Math.Atan2(-east, north); // counter-clockwise from north, like the sim's yaw if it counts that way
            double yaw = p.YawNorth;
            _signScore = Math.Clamp(_signScore + Math.Cos(travel - yaw) - Math.Cos(travel + yaw), -50, 50);
        }

        private double YawSign => _signScore >= 0 ? 1.0 : -1.0;

        /// <summary>Call once per frame, before the host renders.</summary>
        public void Update()
        {
            var svc = IracingService.Instance;
            var pose = svc.CarWorldPose;
            bool drivable = svc.IsConnected && !svc.InReplay && pose != null;
            bool trackMode = drivable && !pose!.HasGps;
            bool live = drivable && (pose!.HasGps || (pose.LapDist >= 0 && svc.TrackLengthMeters > 0));

            if (drivable && pose!.HasGps) Calibrate(pose);
            else if (trackMode) TrackStep(pose!, svc.TrackLengthMeters, svc.TrackKey);
            else ResetTrackMap();

            bool mark;
            lock (_lock) { mark = _markRequested; _markRequested = false; }
            if (mark && live) Drop(pose!, svc.TrackKey);
            else if (mark)
                _log("Brake marker button pressed, but no marker was dropped: "
                     + (!svc.IsConnected ? "iRacing is not connected."
                        : svc.InReplay ? "the sim is in a replay."
                        : pose == null ? "the car's heading is not available."
                        : svc.TrackLengthMeters <= 0 ? "the track length is not known yet."
                        : "the car is not on the track."));

            if (!live || svc.TrackKey.Length == 0 || !_settings.BrakeMarkers.TryGetValue(svc.TrackKey, out var markers))
            {
                HideAll(0);
                return;
            }

            // Each marker as the car sees it: metres ahead and to the right, how far up, and how far its
            // heading differs from the car's.
            var seen = new List<(double Forward, double Right, float Up, float Turn)>();
            if (pose!.HasGps)
            {
                var car = CarNow(pose);
                double carYaw = car.SignedYaw;
                double sin = Math.Sin(carYaw), cos = Math.Cos(carYaw);
                foreach (var m in markers)
                {
                    if (m.Lat == 0 && m.Lon == 0) continue; // dropped without GPS: only a lap distance
                    var (east, north) = Offset(car.Lat, car.Lon, m.Lat, m.Lon);
                    // Into the car's frame: forward is (-sin, cos) in (east, north), right is (cos, sin).
                    seen.Add((-east * sin + north * cos, east * cos + north * sin,
                              (float)(m.Alt - car.Alt) + Height + StripHeightMeters / 2f,
                              (float)(YawSign * m.Yaw - carYaw)));
                }
            }
            else
            {
                double carYaw = YawSign * pose.YawNorth;
                double sin = Math.Sin(carYaw), cos = Math.Cos(carYaw);
                double age = Math.Clamp((Stopwatch.GetTimestamp() - pose.Ticks) / (double)Stopwatch.Frequency, 0.0, 0.05);
                double carE = _e - sin * pose.Speed * age, carN = _n + cos * pose.Speed * age;
                foreach (var m in markers)
                {
                    if (!TryTrackMarker(m, pose.LapDist, carE, carN, carYaw, out double e, out double n, out double yaw)) continue;
                    double east = e - carE, north = n - carN;
                    seen.Add((-east * sin + north * cos, east * cos + north * sin,
                              Height + StripHeightMeters / 2f, (float)(yaw - carYaw)));
                }
            }

            // The cockpit-fixed frame the canvas is placed in: the recentred origin, turned by its yaw.
            var frame = Quaternion.CreateFromAxisAngle(Vector3.UnitY, _host.OriginYaw);

            var shown = new List<(double Dist, Vector3 Pos, Quaternion Rot)>();
            foreach (var (forward, right, up, turn) in seen)
            {
                double dist = Math.Sqrt(forward * forward + right * right);
                if (forward <= 0 || dist < MinDistanceMeters || dist > MaxDistanceMeters) continue;

                // The strip is only a picture on one side: it faces back along the marker's heading, in the
                // direction (sin turn, cos turn) across (right, back). Seen edge-on or from behind it is no
                // use, so it is not drawn then. cos is the angle between that and the line from the marker
                // to the driver.
                double facing = (-Math.Sin(turn) * right + Math.Cos(turn) * forward) / dist;
                if (facing < MinFacing) continue;

                // Cockpit space: +X right, +Y up, -Z forward.
                var local = new Vector3((float)right, up, (float)-forward);
                // The strip faces back along the marker's heading, so its +Z is the reverse of that.
                shown.Add((dist,
                           _host.OriginPosition + Vector3.Transform(local, frame),
                           frame * Quaternion.CreateFromAxisAngle(Vector3.UnitY, turn)));
            }

            LogDiagnostics(pose, markers.Count, seen, shown.Count);

            shown.Sort((a, b) => a.Dist.CompareTo(b.Dist));
            int count = Math.Min(shown.Count, _overlays.Count);
            for (int i = 0; i < count; i++)
            {
                var o = _overlays[i];
                o.Position = shown[i].Pos;
                o.Rotation = shown[i].Rot;
                o.Size = new Vector2(StripWidthMeters, StripHeightMeters);
                o.Visible = true;
            }
            HideAll(count);
        }

        // ----- Without GPS: the lap distance and a dead-reckoned path -----------------------------------------
        // Some sim versions send no Lat / Lon / Alt. The car's path is then worked out from its speed and heading
        // (dead reckoning, in a frame of our own) and remembered by the distance along the lap, in bins of
        // BinMeters. A marker is a lap distance; where it is = where the car was at that distance on its last
        // pass, moved by how far this pass has drifted from the last one right here (the anchor). Until the car
        // has driven that stretch once, the marker is put straight ahead along the car's heading instead.
        private const double BinMeters = 2.0;
        private const int MaxBinsPerStep = 60;

        private struct Bin { public double E, N, Yaw; public int Lap; public bool Valid; }

        private Bin[] _bins = Array.Empty<Bin>();
        private double _trackLen, _e, _n, _lastLapDist, _anchorE, _anchorN;
        private long _lastTicks;
        private int _lastBin = -1, _lapCounter;
        private bool _trackState;
        private string _binsTrack = "";

        // Where this run of the overlay dropped each marker in the dead-reckoned frame, valid until the frame is
        // lost (_epoch changes) or the car crosses the line (the map from the lap just driven is better by then).
        // It keeps a marker where it was dropped whatever path the car takes - round in circles, off the track.
        private readonly Dictionary<BrakeMarkerData, (int Epoch, int Lap, double E, double N, double Yaw)> _dropped = new();
        private int _epoch;

        private void ResetTrackMap()
        {
            _epoch++;
            if (_bins.Length > 0) Array.Clear(_bins);
            _trackState = false;
            _lastBin = -1;
            _lapCounter = 0;
            _e = _n = _anchorE = _anchorN = 0;
        }

        private void TrackStep(IracingService.WorldPose p, double length, string track)
        {
            if (length <= 0) { ResetTrackMap(); return; }
            int binCount = (int)Math.Ceiling(length / BinMeters);
            if (_bins.Length != binCount || track != _binsTrack)
            {
                _bins = new Bin[binCount];
                _binsTrack = track;
                _trackLen = length;
                ResetTrackMap();
            }
            if (p.LapDist < 0) { ResetTrackMap(); return; } // in the garage / off the world: no continuity
            if (_trackState && p.Ticks == _lastTicks) return; // the same sample again

            double lapDist = Math.Min(p.LapDist, length - 1e-3);
            double yaw = YawSign * p.YawNorth;
            double prevE = _e, prevN = _n;
            bool onPath = true; // false while the car is not following the track (circling, off, spinning)

            if (_trackState)
            {
                double dt = (p.Ticks - _lastTicks) / (double)Stopwatch.Frequency;
                double move = lapDist - _lastLapDist;
                bool wrapped = false;
                if (move < -length / 2) { move += length; wrapped = true; }
                else if (move > length / 2) move -= length;
                double expected = p.Speed * dt;
                if (dt > 0.25 || Math.Abs(move) > 15 + 1.5 * Math.Abs(expected))
                {
                    // Reset to the pits, a tow, a long hiccup: the lap distance jumped further than the car can
                    // have gone, so the path so far no longer joins up with this.
                    ResetTrackMap();
                    prevE = prevN = 0;
                }
                else
                {
                    _e += -Math.Sin(yaw) * expected;
                    _n += Math.Cos(yaw) * expected;
                    if (wrapped && move > 0) _lapCounter++;
                    // Moving much faster than the lap distance advances: not on the racing line, so what is
                    // passing is not the track and must not be written into its map.
                    onPath = move >= 0.5 * expected - 2;
                }
            }

            _trackState = true;
            _lastTicks = p.Ticks;
            _lastLapDist = lapDist;
            if (!onPath) { _lastBin = -1; return; }

            int b = Math.Clamp((int)(lapDist / BinMeters), 0, _bins.Length - 1);
            int steps = _lastBin < 0 ? 0 : (b - _lastBin + _bins.Length) % _bins.Length;
            if (steps > MaxBinsPerStep || steps > _bins.Length / 2)
            {
                Array.Clear(_bins);
                steps = 0;
                _lastBin = -1;
                _anchorE = _anchorN = 0;
            }

            // Every bin the car passed since the last sample (usually one), the position spread along the way.
            int first = _lastBin < 0 ? b : _lastBin;
            for (int k = steps == 0 ? 0 : 1; k <= steps; k++)
            {
                int idx = (first + k) % _bins.Length;
                double f = steps == 0 ? 1.0 : k / (double)steps;
                double e = prevE + (_e - prevE) * f, n = prevN + (_n - prevN) * f;
                var old = _bins[idx];
                if (old.Valid && old.Lap < _lapCounter)
                {
                    // Last pass' position here against where the car is now: how far the frame has drifted.
                    double targetE = e - old.E, targetN = n - old.N;
                    bool haveAnchor = _anchorE != 0 || _anchorN != 0;
                    _anchorE = haveAnchor ? _anchorE + 0.35 * (targetE - _anchorE) : targetE;
                    _anchorN = haveAnchor ? _anchorN + 0.35 * (targetN - _anchorN) : targetN;
                }
                _bins[idx] = new Bin { E = e, N = n, Yaw = yaw, Lap = _lapCounter, Valid = true };
            }
            _lastBin = b;
        }

        private bool TryTrackMarker(BrakeMarkerData m, double lapDistNow, double carE, double carN, double carYaw,
                                    out double east, out double north, out double yaw)
        {
            east = north = yaw = 0;
            if (m.LapDist < 0 || _bins.Length == 0) return false;

            if (_dropped.TryGetValue(m, out var w) && w.Epoch == _epoch && w.Lap == _lapCounter)
            {
                east = w.E; north = w.N; yaw = w.Yaw;
                return true;
            }

            int bm = Math.Clamp((int)(m.LapDist / BinMeters), 0, _bins.Length - 1);
            var s = _bins[bm];
            if (s.Valid)
            {
                bool lastPass = s.Lap < _lapCounter; // from before this lap: bring it into this lap's frame
                east = s.E + (lastPass ? _anchorE : 0);
                north = s.N + (lastPass ? _anchorN : 0);
                yaw = s.Yaw;
                return true;
            }

            // Not driven yet: straight ahead of the car by the lap distance between them.
            double ahead = m.LapDist - lapDistNow;
            if (ahead < 0) ahead += _trackLen;
            if (ahead > MaxDistanceMeters) return false;
            east = carE - Math.Sin(carYaw) * ahead;
            north = carN + Math.Cos(carYaw) * ahead;
            yaw = carYaw;
            return true;
        }

        private void DropAtLapDistance(IracingService.WorldPose pose, string track)
        {
            if (track.Length == 0) { _log("Brake marker: track not known yet."); return; }
            if (!_settings.BrakeMarkers.TryGetValue(track, out var list))
                _settings.BrakeMarkers[track] = list = new List<BrakeMarkerData>();

            double length = _trackLen > 0 ? _trackLen : IracingService.Instance.TrackLengthMeters;
            BrakeMarkerData? nearest = null;
            double best = double.MaxValue;
            foreach (var m in list)
            {
                if (m.LapDist < 0) continue;
                double d = Math.Abs(m.LapDist - pose.LapDist);
                if (length > 0 && d > length / 2) d = length - d;
                if (d < best) { best = d; nearest = m; }
            }

            bool replace = nearest != null && best <= ReplaceRadiusMeters;
            var target = replace ? nearest! : new BrakeMarkerData();
            target.LapDist = pose.LapDist;
            target.Yaw = pose.YawNorth;
            if (!replace) list.Add(target);
            if (_trackState) _dropped[target] = (_epoch, _lapCounter, _e, _n, YawSign * pose.YawNorth);
            else _dropped.Remove(target);

            _log(replace ? $"Brake marker moved ({best:0} m from the old one)." : $"Brake marker added ({list.Count} on this track).");
            _saved();
        }

        // Once a second while markers exist: what the car and the nearest markers look like, to chase a strip that
        // does not move as it should (the lines go to the log file).
        private long _nextDiag;

        private void LogDiagnostics(IracingService.WorldPose pose, int markerCount, List<(double Forward, double Right, float Up, float Turn)> seen, int drawn)
        {
            long now = Stopwatch.GetTimestamp();
            if (now < _nextDiag) return;
            _nextDiag = now + Stopwatch.Frequency;
            var nearest = seen.Where(s => s.Forward > 0).OrderBy(s => s.Forward).Take(2)
                .Select(s => $"ahead {s.Forward:0.0} m right {s.Right:0.0} m turn {s.Turn * 180 / Math.PI:0} deg");
            _log($"Brake markers: {(pose.HasGps ? "gps" : "lap distance")}, lap dist {pose.LapDist:0.0} m, speed {pose.Speed * 2.23694:0} mph, "
                 + $"{markerCount} on track, {drawn} drawn, lap {_lapCounter}, drift {_anchorE:0.0},{_anchorN:0.0} m"
                 + (nearest.Any() ? "; nearest: " + string.Join(" | ", nearest) : "; none ahead"));
        }

        private void HideAll(int from)
        {
            for (int i = from; i < _overlays.Count; i++) _overlays[i].Visible = false;
        }

        // Drop a marker at the car, or move the one within ReplaceRadiusMeters of it.
        private void Drop(IracingService.WorldPose pose, string track)
        {
            if (!pose.HasGps) { DropAtLapDistance(pose, track); return; }
            if (track.Length == 0) { _log("Brake marker: track not known yet."); return; }
            if (!_settings.BrakeMarkers.TryGetValue(track, out var list))
                _settings.BrakeMarkers[track] = list = new List<BrakeMarkerData>();

            BrakeMarkerData? nearest = null;
            double best = double.MaxValue;
            foreach (var m in list)
            {
                var (east, north) = Offset(pose.Lat, pose.Lon, m.Lat, m.Lon);
                double d = Math.Sqrt(east * east + north * north);
                if (d < best) { best = d; nearest = m; }
            }

            bool replace = nearest != null && best <= ReplaceRadiusMeters;
            var target = replace ? nearest! : new BrakeMarkerData();
            target.Lat = pose.Lat;
            target.Lon = pose.Lon;
            target.Alt = pose.Alt;
            target.Yaw = pose.YawNorth;
            if (!replace) list.Add(target);

            _log(replace ? $"Brake marker moved ({best:0} m from the old one)." : $"Brake marker added ({list.Count} on this track).");
            _saved();
        }

        public void Dispose()
        {
            Button.Dispose();
            foreach (var o in _overlays) _host.RemoveWorldOverlay(o);
            _overlays.Clear();
        }
    }
}
