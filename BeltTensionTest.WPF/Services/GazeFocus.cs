using System;
using System.Collections.Generic;
using System.Diagnostics;
using MonoXR.Client;
using MonoXR.Shared;

namespace BeltTensionTest.WPF.Services
{
    /// <summary>Where the "what am I looking at" direction comes from.</summary>
    public enum GazeSource
    {
        /// <summary>Nothing is tracked; panels keep the size and opacity you set.</summary>
        Off,
        /// <summary>The headset's own line of sight — works on every headset.</summary>
        Head,
        /// <summary>
        /// True eye gaze (XR_EXT_eye_gaze_interaction), read by the MonoXR
        /// OpenXR layer. Needs an eye-tracked headset; while no gaze is coming
        /// in it falls back to <see cref="Head"/> so the panels still react.
        /// </summary>
        Eye,
    }

    /// <summary>What looking at a panel does to it.</summary>
    public enum GazeEffect
    {
        None,
        /// <summary>Other panels dim; the one you look at comes back to full.</summary>
        Brighten,
        /// <summary>The panel you look at scales up.</summary>
        Enlarge,
        Both,
    }

    /// <summary>
    /// Makes the panel you are looking at stand out: the others dim, and/or it
    /// grows. The gaze direction is a ray from the headset through the overlay
    /// canvas (<see cref="MonoGameOverlayHost.TryGetGazeCanvasPoint"/>); the
    /// panel under that point is the focused one.
    ///
    /// Two timings keep it from twitching as the head moves: a panel has to be
    /// looked at for <see cref="DwellMs"/> before it takes focus, and it keeps
    /// focus for <see cref="HoldMs"/> after the gaze leaves, so a glance back
    /// at the road does not drop the panel you were reading. The change itself
    /// is eased rather than snapped.
    ///
    /// Panels are driven by writing their Opacity and Scale, which are also the
    /// user's own persisted settings — so the values the user set are kept here
    /// as the baseline and are what <c>BaseOpacityOf</c>/<c>BaseScaleOf</c>
    /// report for saving. Whenever the user changes one in edit mode, call
    /// <see cref="Recapture"/> so the new value becomes the baseline. Edit mode
    /// itself suspends the whole thing: panels must hold still while they are
    /// being arranged.
    /// </summary>
    public sealed class GazeFocus
    {
        /// <summary>Time the gaze has to rest on a panel before it takes focus.</summary>
        public const double DwellMs = 120;

        /// <summary>Time a panel keeps focus after the gaze leaves it.</summary>
        public const double HoldMs = 450;

        // Eye trackers are good for a degree or two, which is a few dozen
        // canvas pixels at the default distance: an eye gaze that lands just
        // outside a panel still counts as looking at it. Head direction is
        // exact, so it gets no margin.
        private const float EyeSlackPx = 32f;

        // Focus amount moves this much per second, so a full transition takes
        // about a third of a second either way.
        private const float EaseRate = 3.5f;

        private readonly MonoGameOverlayHost _host;
        private readonly Func<IEnumerable<OverlayRenderTarget>> _panels;
        private readonly Stopwatch _clock = Stopwatch.StartNew();

        /// <summary>Per-panel baseline (what the user set) and current focus amount, 0..1.</summary>
        private sealed class Entry
        {
            public float BaseOpacity = 1f;
            public float BaseScale = 1f;
            public float Focus;         // 0 = not looked at, 1 = fully focused
        }

        private readonly Dictionary<OverlayRenderTarget, Entry> _entries = new();

        // Panels switched off with their eye button in edit mode: they never take
        // focus, are never dimmed or grown, and stay exactly as the user set them.
        private readonly HashSet<string> _disabled = new();

        private OverlayRenderTarget? _focused;   // panel that currently holds focus
        private OverlayRenderTarget? _candidate; // panel the gaze is resting on
        private double _candidateSince;
        private double _focusLostAt = double.NegativeInfinity;
        private double _lastTick;

        // True while the panels carry the effect. Reset() puts them back to
        // their baselines without going through the factors, so Recapture has
        // to know whether there is anything to divide back out.
        private bool _applied;

        public GazeFocus(MonoGameOverlayHost host, Func<IEnumerable<OverlayRenderTarget>> panels)
        {
            _host = host;
            _panels = panels;
            _lastTick = _clock.Elapsed.TotalSeconds;
        }

        public GazeSource Source { get; set; } = GazeSource.Off;

        /// <summary>
        /// Master switch (Preferences > OpenXR > Enable eye tracking). When off the
        /// feature does nothing whatever <see cref="Source"/> and <see cref="Effect"/>
        /// are set to, and those choices are kept for when it is turned back on.
        /// </summary>
        public bool Enabled { get; set; } = true;
        public GazeEffect Effect { get; set; } = GazeEffect.Brighten;

        /// <summary>How far unfocused panels dim, 0 (not at all) to 0.7.</summary>
        public float DimAmount
        {
            get => _dim;
            set => _dim = Math.Clamp(value, 0f, 0.7f);
        }
        private float _dim = 0.45f;

        /// <summary>How much the focused panel grows, 1.0 (not at all) to 1.6.</summary>
        public float GrowFactor
        {
            get => _grow;
            set => _grow = Math.Clamp(value, 1f, 1.6f);
        }
        private float _grow = 1.25f;

        /// <summary>True when the feature is doing anything at all.</summary>
        public bool IsActive => Enabled && Source != GazeSource.Off && Effect != GazeEffect.None;

        /// <summary>Name of the panel currently in focus, for the settings readout.</summary>
        public string FocusedName => _focused?.Name ?? string.Empty;

        /// <summary>
        /// Whether a gaze ray was available on the last tick. False means no
        /// OpenXR app is attached, or the layer is older than head-pose
        /// publishing — the settings panel says so rather than looking broken.
        /// </summary>
        public bool HasSignal { get; private set; }

        /// <summary>
        /// True when the last tick was driven by real eye gaze. False for head
        /// tracking, including <see cref="GazeSource.Eye"/> that has fallen
        /// back to the head because no gaze is coming in.
        /// </summary>
        public bool UsingEyes { get; private set; }

        /// <summary>What the overlay layer reports about the headset's eye tracker, for the settings readout.</summary>
        public MonoXrGazeState EyeState => _host.EyeGazeState;

        /// <summary>Whether a panel reacts to being looked at (its eye button in edit mode).</summary>
        public bool IsPanelEnabled(OverlayRenderTarget panel) => !_disabled.Contains(panel.Name);

        /// <summary>Names of the panels that ignore gaze, for saving.</summary>
        public IReadOnlyCollection<string> DisabledPanels => _disabled;

        /// <summary>Replace the set of panels that ignore gaze (settings load).</summary>
        public void SetDisabledPanels(IEnumerable<string>? names)
        {
            _disabled.Clear();
            if (names == null) return;
            foreach (var name in names) _disabled.Add(name);
        }

        /// <summary>Flip whether a panel reacts to gaze; a panel switched off goes straight back to the user's own values.</summary>
        public void TogglePanel(OverlayRenderTarget panel)
        {
            if (!_disabled.Remove(panel.Name)) _disabled.Add(panel.Name);
            if (ReferenceEquals(_focused, panel)) _focused = null;
            if (ReferenceEquals(_candidate, panel)) _candidate = null;
            if (_entries.TryGetValue(panel, out var entry))
            {
                entry.Focus = 0f;
                panel.Opacity = entry.BaseOpacity;
                panel.Scale = entry.BaseScale;
            }
        }

        /// <summary>The user's own opacity for a panel, ignoring any gaze dimming.</summary>
        public float BaseOpacityOf(OverlayRenderTarget panel) =>
            _entries.TryGetValue(panel, out var e) ? e.BaseOpacity : panel.Opacity;

        /// <summary>The user's own scale for a panel, ignoring any gaze growth.</summary>
        public float BaseScaleOf(OverlayRenderTarget panel) =>
            _entries.TryGetValue(panel, out var e) ? e.BaseScale : panel.Scale;

        /// <summary>
        /// Take the panel's current opacity and scale as the new baseline —
        /// call after the user changes either one themselves.
        /// </summary>
        public void Recapture(OverlayRenderTarget panel)
        {
            var entry = EntryFor(panel);
            // Undo whatever share of the effect is applied right now, so a
            // panel edited mid-fade does not bake the fade into its baseline.
            float opacityFactor = _applied ? OpacityFactor(entry.Focus) : 1f;
            float scaleFactor = _applied ? ScaleFactor(entry.Focus) : 1f;
            entry.BaseOpacity = Math.Clamp(panel.Opacity / opacityFactor, OverlayRenderTarget.MinOpacity, 1f);
            entry.BaseScale = Math.Clamp(panel.Scale / scaleFactor,
                                         OverlayRenderTarget.MinScale, OverlayRenderTarget.MaxScale);
        }

        /// <summary>Put every panel back to exactly what the user set.</summary>
        public void Reset()
        {
            foreach (var panel in _panels())
            {
                if (!_entries.TryGetValue(panel, out var entry)) continue;
                entry.Focus = 0f;
                panel.Opacity = entry.BaseOpacity;
                panel.Scale = entry.BaseScale;
            }
            _focused = null;
            _candidate = null;
            _applied = false;
        }

        /// <summary>
        /// One tick: work out what is being looked at and ease every panel
        /// toward its target. Safe to call every frame; cheap when off.
        /// </summary>
        public void Update(bool editMode)
        {
            double now = _clock.Elapsed.TotalSeconds;
            float dt = (float)Math.Clamp(now - _lastTick, 0.0, 0.25);
            _lastTick = now;

            // Arranging panels: hold everything at the user's own values, so
            // what they drag and size is what they get.
            if (editMode || !IsActive)
            {
                HasSignal = false;
                UsingEyes = false;
                _host.EyeGazeRequested = false;
                if (_focused != null || _candidate != null || AnyFocus()) Reset();
                return;
            }

            // The layer only polls the eye tracker while asked to.
            _host.EyeGazeRequested = Source == GazeSource.Eye;

            UpdateFocusTarget(now);

            foreach (var panel in _panels())
            {
                var entry = EntryFor(panel);
                if (_disabled.Contains(panel.Name))
                {
                    // Not part of gaze focus: hold the user's own values.
                    entry.Focus = 0f;
                    panel.Opacity = entry.BaseOpacity;
                    panel.Scale = entry.BaseScale;
                    continue;
                }
                float target = ReferenceEquals(panel, _focused) ? 1f : 0f;
                if (Math.Abs(entry.Focus - target) > 0.001f)
                {
                    float step = EaseRate * dt;
                    entry.Focus = target > entry.Focus
                        ? MathF.Min(target, entry.Focus + step)
                        : MathF.Max(target, entry.Focus - step);
                }
                else
                {
                    entry.Focus = target;
                }

                // Writing the same value back is free (the setters early-out),
                // so an idle panel never marks itself dirty.
                panel.Opacity = Math.Clamp(entry.BaseOpacity * OpacityFactor(entry.Focus),
                                           OverlayRenderTarget.MinOpacity, 1f);
                panel.Scale = Math.Clamp(entry.BaseScale * ScaleFactor(entry.Focus),
                                         OverlayRenderTarget.MinScale, OverlayRenderTarget.MaxScale);
            }
            _applied = true;
        }

        // Which panel should hold focus right now, applying the dwell before
        // taking it and the hold before letting it go.
        private void UpdateFocusTarget(double now)
        {
            float gx = 0f, gy = 0f;
            UsingEyes = Source == GazeSource.Eye && _host.TryGetEyeGazeCanvasPoint(out gx, out gy);
            HasSignal = UsingEyes || _host.TryGetGazeCanvasPoint(out gx, out gy);
            var looking = HasSignal ? PanelAt(gx, gy, UsingEyes ? EyeSlackPx : 0f) : null;

            if (!ReferenceEquals(looking, _candidate))
            {
                _candidate = looking;
                _candidateSince = now;
            }

            if (looking != null && (now - _candidateSince) * 1000.0 >= DwellMs)
            {
                _focused = looking;
                _focusLostAt = double.PositiveInfinity;
                return;
            }

            if (_focused == null) return;

            // Still on the focused panel (just not long enough to re-arm) —
            // nothing to do.
            if (ReferenceEquals(looking, _focused)) { _focusLostAt = double.PositiveInfinity; return; }

            if (double.IsPositiveInfinity(_focusLostAt)) _focusLostAt = now;
            if ((now - _focusLostAt) * 1000.0 >= HoldMs) { _focused = null; _focusLostAt = double.NegativeInfinity; }
        }

        /// <summary>
        /// Topmost shown panel whose displayed bounds contain a canvas point —
        /// the same rule the host's pointer hit-test uses, so what you look at
        /// and what you click agree. Panels the gaze must not grab (the belt
        /// and HUD settings panels are used, not glanced at) still qualify:
        /// leaving them out would make them dim permanently.
        /// </summary>
        private OverlayRenderTarget? PanelAt(float x, float y, float slack)
        {
            OverlayRenderTarget? hit = null;
            foreach (var panel in _panels())
            {
                if (!panel.Visible || !panel.IsLive) continue;
                if (_disabled.Contains(panel.Name)) continue;
                if (x < panel.X - slack || x >= panel.X + panel.DisplayWidth + slack) continue;
                if (y < panel.Y - slack || y >= panel.Y + panel.DisplayHeight + slack) continue;
                hit = panel; // later panels composite on top, so the last wins
            }
            return hit;
        }

        private float OpacityFactor(float focus) =>
            Effect is GazeEffect.Brighten or GazeEffect.Both ? 1f - _dim * (1f - focus) : 1f;

        private float ScaleFactor(float focus) =>
            Effect is GazeEffect.Enlarge or GazeEffect.Both ? 1f + (_grow - 1f) * focus : 1f;

        private bool AnyFocus()
        {
            foreach (var entry in _entries.Values)
                if (entry.Focus > 0.001f) return true;
            return false;
        }

        private Entry EntryFor(OverlayRenderTarget panel)
        {
            if (_entries.TryGetValue(panel, out var entry)) return entry;
            entry = new Entry { BaseOpacity = panel.Opacity, BaseScale = panel.Scale };
            _entries[panel] = entry;
            return entry;
        }
    }
}
