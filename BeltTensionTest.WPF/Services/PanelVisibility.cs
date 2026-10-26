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
        /// <summary>Only while sitting in the car (any session), not while watching a replay.</summary>
        InCar,
        /// <summary>Only while watching the replay.</summary>
        Replay,
        /// <summary>Only in the car during a practice session (also warm-up and testing).</summary>
        Practice,
        /// <summary>Only in the car during qualifying, lone or open.</summary>
        Qualifying,
        /// <summary>Only in the car during the race.</summary>
        Race,
    }

    /// <summary>What the sim is doing, as far as panel show modes are concerned.</summary>
    public enum SimState
    {
        NotRunning,
        /// <summary>In the car, in a session none of the specific states below covers.</summary>
        InCar,
        Replay,
        Practice,
        Qualifying,
        Race,
    }

    /// <summary>A panel that starts on something other than <see cref="PanelShowMode.Always"/>.</summary>
    public interface IPanelShowDefault
    {
        PanelShowMode DefaultShowMode { get; }
    }

    /// <summary>
    /// Per-panel "when is this shown" setting: in the car, in practice,
    /// qualifying or the race, in the replay, or always. Ported from IrachingHud's ShowSetting, which each box set in
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
            PanelShowMode.Practice => "Practice",
            PanelShowMode.Qualifying => "Qualifying",
            PanelShowMode.Race => "Race",
            _ => "Always",
        };

        /// <summary>
        /// Whether a panel with this mode applies in this state. In car covers
        /// every session while sitting in the car; Practice / Qualifying / Race
        /// each cover only their own session (and, like In car, never a replay).
        /// </summary>
        public static bool AppliesIn(PanelShowMode mode, SimState state) => mode switch
        {
            PanelShowMode.InCar => state is SimState.InCar or SimState.Practice
                                         or SimState.Qualifying or SimState.Race,
            PanelShowMode.Replay => state == SimState.Replay,
            PanelShowMode.Practice => state == SimState.Practice,
            PanelShowMode.Qualifying => state == SimState.Qualifying,
            PanelShowMode.Race => state == SimState.Race,
            _ => true,
        };

        /// <summary>
        /// iRacing's session type as a state: "Practice" (also Warmup, Offline
        /// Testing), "Open Qualify" and "Lone Qualify" as Qualifying, "Race".
        /// Anything unrecognised, or not yet known, is plain InCar.
        /// </summary>
        public static SimState SessionState(string sessionType)
        {
            if (sessionType.Contains("Qualify", StringComparison.OrdinalIgnoreCase)) return SimState.Qualifying;
            if (sessionType.Contains("Race", StringComparison.OrdinalIgnoreCase)) return SimState.Race;
            if (sessionType.Contains("Practice", StringComparison.OrdinalIgnoreCase) ||
                sessionType.Contains("Warmup", StringComparison.OrdinalIgnoreCase) ||
                sessionType.Contains("Test", StringComparison.OrdinalIgnoreCase)) return SimState.Practice;
            return SimState.InCar;
        }

        // ----- Edit mode: arranging the layout for one state --------------------

        /// <summary>Options for the "editing for" choice, in <see cref="EditViewIndex"/> order.</summary>
        public static readonly string[] EditViewOptions =
            { "Current", "In car", "Practice", "Qualifying", "Race", "Replay" };

        /// <summary>The state the sim is in right now.</summary>
        public static SimState CurrentSimState()
        {
            var svc = IracingService.Instance;
            if (!svc.IsConnected) return SimState.NotRunning;
            return svc.InReplay ? SimState.Replay : SessionState(svc.SessionType);
        }

        public static string Describe(SimState state) => state switch
        {
            SimState.InCar => "In car",
            SimState.Replay => "Replay",
            SimState.Practice => "Practice",
            SimState.Qualifying => "Qualifying",
            SimState.Race => "Race",
            _ => "Not running",
        };

        /// <summary>
        /// The state the layout is being arranged for in edit mode: an index into
        /// <see cref="EditViewOptions"/> - 0 follows the sim, then in car, practice,
        /// qualifying, race and replay.
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
            2 => SimState.Practice,
            3 => SimState.Qualifying,
            4 => SimState.Race,
            5 => SimState.Replay,
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
            return !AppliesIn(mode, state);
        }

        /// <summary>Step a panel to the next mode: Always -> In car -> Practice -> Qualifying -> Race -> Replay -> Always.</summary>
        public void Cycle(OverlayRenderTarget panel)
        {
            var next = ModeOf(panel) switch
            {
                PanelShowMode.Always => PanelShowMode.InCar,
                PanelShowMode.InCar => PanelShowMode.Practice,
                PanelShowMode.Practice => PanelShowMode.Qualifying,
                PanelShowMode.Qualifying => PanelShowMode.Race,
                PanelShowMode.Race => PanelShowMode.Replay,
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

            bool any = false;
            foreach (var panel in _panels())
            {
                bool available = AppliesIn(ModeOf(panel), state);
                panel.EditHidden = HideInEdit(panel);
                if (panel.Available == available) continue;
                panel.Available = available;
                any = true;
            }
            if (any) _invalidateCanvas();
        }
    }
}
