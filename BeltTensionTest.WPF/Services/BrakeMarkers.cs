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
        private void RequestMark() { lock (_lock) _markRequested = true; }

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
            bool live = svc.IsConnected && !svc.InReplay && pose != null;

            if (live) Calibrate(pose!);

            bool mark;
            lock (_lock) { mark = _markRequested; _markRequested = false; }
            if (mark && live) Drop(pose!, svc.TrackKey);

            if (!live || svc.TrackKey.Length == 0 || !_settings.BrakeMarkers.TryGetValue(svc.TrackKey, out var markers))
            {
                HideAll(0);
                return;
            }

            var car = CarNow(pose!);
            double carYaw = car.SignedYaw;
            double sin = Math.Sin(carYaw), cos = Math.Cos(carYaw);

            // The cockpit-fixed frame the canvas is placed in: the recentred origin, turned by its yaw.
            var frame = Quaternion.CreateFromAxisAngle(Vector3.UnitY, _host.OriginYaw);

            var shown = new List<(double Dist, Vector3 Pos, Quaternion Rot)>();
            foreach (var m in markers)
            {
                var (east, north) = Offset(car.Lat, car.Lon, m.Lat, m.Lon);
                // Into the car's frame: forward is (-sin, cos) in (east, north), right is (cos, sin).
                double forward = -east * sin + north * cos;
                double right = east * cos + north * sin;
                double dist = Math.Sqrt(forward * forward + right * right);
                if (forward <= 0 || dist < MinDistanceMeters || dist > MaxDistanceMeters) continue;

                float up = (float)(m.Alt - car.Alt) + Height + StripHeightMeters / 2f;
                // Cockpit space: +X right, +Y up, -Z forward.
                var local = new Vector3((float)right, up, (float)-forward);
                // The strip faces back along the marker's heading, so its +Z is the reverse of that.
                float turn = (float)(YawSign * m.Yaw - carYaw);
                shown.Add((dist,
                           _host.OriginPosition + Vector3.Transform(local, frame),
                           frame * Quaternion.CreateFromAxisAngle(Vector3.UnitY, turn)));
            }

            shown.Sort((a, b) => a.Dist.CompareTo(b.Dist));
            int n = Math.Min(shown.Count, _overlays.Count);
            for (int i = 0; i < n; i++)
            {
                var o = _overlays[i];
                o.Position = shown[i].Pos;
                o.Rotation = shown[i].Rot;
                o.Size = new Vector2(StripWidthMeters, StripHeightMeters);
                o.Visible = true;
            }
            HideAll(n);
        }

        private void HideAll(int from)
        {
            for (int i = from; i < _overlays.Count; i++) _overlays[i].Visible = false;
        }

        // Drop a marker at the car, or move the one within ReplaceRadiusMeters of it.
        private void Drop(IracingService.WorldPose pose, string track)
        {
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
