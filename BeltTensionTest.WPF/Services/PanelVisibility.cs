using System;
using System.Collections.Generic;
using MonoXR.Client;

namespace BeltTensionTest.WPF.Services
{
    /// <summary>When a VR overlay panel applies (IrachingHud's ShowSetting).</summary>
    public enum PanelShowMode
    {
        /// <summary>Whenever the overlay is up, iRacing running or not.</summary>
        Always,
        /// <summary>Only while sitting in the car, not while watching a replay.</summary>
        InCar,
        /// <summary>Only while watching the replay.</summary>
        Replay,
    }

    /// <summary>What the sim is doing, as far as panel show modes are concerned.</summary>
    public enum SimState
    {
        NotRunning,
        InCar,
        Replay,
    }

    /// <summary>A panel that starts on something other than <see cref="PanelShowMode.Always"/>.</summary>
    public interface IPanelShowDefault
    {
        PanelShowMode DefaultShowMode { get; }
    }

    /// <summary>
    /// Per-panel "when is this shown" setting: in the car, in the replay, or
    /// always. Ported from IrachingHud's ShowSetting, which each box set in
    /// code; here every panel carries its own, chosen with the Show button on
    /// its edit-mode bar and remembered by panel name in the app settings.
    ///
    /// <see cref="Apply"/> runs on the overlay's own timer and translates the
    /// choice into <see cref="OverlayRenderTarget.Available"/>, which the host
    /// treats like the panel not existing — except in edit mode, where
    /// everything is shown so it can still be placed.
    /// </summary>
    public sealed class PanelVisibility
    {
        private readonly Dictionary<string, string> _modes; // panel name -> mode
        private readonly Func<IEnumerable<OverlayRenderTarget>> _panels;
        private readonly Action _invalidateCanvas;

        /// <summary>Raised after a panel's mode is changed, so the owner can save.</summary>
        public event Action? Changed;

        public PanelVisibility(Dictionary<string, string> modes,
                               Func<IEnumerable<OverlayRenderTarget>> panels, Action invalidateCanvas)
        {
            _modes = modes;
            _panels = panels;
            _invalidateCanvas = invalidateCanvas;
        }

        /// <summary>The panel's saved mode, or the one it ships with.</summary>
        public PanelShowMode ModeOf(OverlayRenderTarget panel)
        {
            if (_modes.TryGetValue(panel.Name, out string? saved) &&
                Enum.TryParse(saved, out PanelShowMode mode))
                return mode;
            return panel is IPanelShowDefault d ? d.DefaultShowMode : PanelShowMode.Always;
        }

        /// <summary>Edit-mode Show button text and highlight for a panel.</summary>
        public (string Label, bool Active) LabelFor(OverlayRenderTarget panel)
        {
            var mode = ModeOf(panel);
            return ("Show: " + Describe(mode), mode != PanelShowMode.Always);
        }

        public static string Describe(PanelShowMode mode) => mode switch
        {
            PanelShowMode.InCar => "In car",
            PanelShowMode.Replay => "Replay",
            _ => "Always",
        };

        // ----- Edit mode: arranging the layout for one state --------------------

        /// <summary>Options for the "editing for" choice, in <see cref="EditViewIndex"/> order.</summary>
        public static readonly string[] EditViewOptions = { "Current", "In car", "Replay" };

        /// <summary>The state the sim is in right now.</summary>
        public static SimState CurrentSimState()
        {
            var svc = IracingService.Instance;
            if (!svc.IsConnected) return SimState.NotRunning;
            return svc.InReplay ? SimState.Replay : SimState.InCar;
        }

        public static string Describe(SimState state) => state switch
        {
            SimState.InCar => "In car",
            SimState.Replay => "Replay",
            _ => "Not running",
        };

        /// <summary>
        /// The state the layout is being arranged for in edit mode: 0 follows the
        /// sim, 1 is the in-car layout, 2 the replay layout.
        /// </summary>
        public int EditViewIndex { get; set; }

        /// <summary>
        /// True while the desktop preview is open: the chosen <see cref="EditViewIndex"/>
        /// then stands in for the sim's real state in <see cref="Apply"/>, so only the
        /// panels that can show in that state are shown (In car / Replay), without
        /// needing the sim in it. Current (0) always follows the sim.
        /// </summary>
        public bool Emulate { get; set; }

        private SimState ChosenState() => EditViewIndex switch
        {
            1 => SimState.InCar,
            2 => SimState.Replay,
            _ => CurrentSimState(),
        };

        /// <summary>
        /// Edit mode shows every panel so it can be found, which would pile the
        /// in-car and replay panels on top of each other. This marks the ones
        /// that do not apply to the state being edited; <see cref="Apply"/> sets
        /// them <see cref="OverlayRenderTarget.EditHidden"/> so they are left out.
        /// </summary>
        public bool HideInEdit(OverlayRenderTarget panel)
        {
            var state = ChosenState();
            if (state == SimState.NotRunning) return false;

            var mode = ModeOf(panel);
            return mode != PanelShowMode.Always
                && mode != (state == SimState.Replay ? PanelShowMode.Replay : PanelShowMode.InCar);
        }

        /// <summary>Step a panel to the next mode: Always -> In car -> Replay -> Always.</summary>
        public void Cycle(OverlayRenderTarget panel)
        {
            var next = ModeOf(panel) switch
            {
                PanelShowMode.Always => PanelShowMode.InCar,
                PanelShowMode.InCar => PanelShowMode.Replay,
                _ => PanelShowMode.Always,
            };
            _modes[panel.Name] = next.ToString();
            Apply();
            _invalidateCanvas();
            Changed?.Invoke();
        }

        /// <summary>
        /// Push the current situation onto every panel. Cheap enough to call
        /// on every overlay tick: it only touches panels whose answer changed.
        /// </summary>
        public void Apply()
        {
            var state = Emulate ? ChosenState() : CurrentSimState();
            bool replay = state == SimState.Replay;
            bool inCar = state == SimState.InCar;

            bool any = false;
            foreach (var panel in _panels())
            {
                bool available = ModeOf(panel) switch
                {
                    PanelShowMode.InCar => inCar,
                    PanelShowMode.Replay => replay,
                    _ => true,
                };
                panel.EditHidden = HideInEdit(panel);
                if (panel.Available == available) continue;
                panel.Available = available;
                any = true;
            }
            if (any) _invalidateCanvas();
        }
    }
}
