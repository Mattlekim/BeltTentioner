using System;
using System.Collections.Generic;
using MonoXR.Client;

namespace BeltTensionTest.WPF.Services
{
    /// <summary>
    /// When a VR overlay panel applies (IrachingHud's ShowSetting): any
    /// combination of iRacing session types. No flag set means always, whenever
    /// the overlay is up, iRacing running or not.
    /// </summary>
    [Flags]
    public enum PanelShowMode
    {
        Always = 0,
        /// <summary>In the car during a practice session (also warm-up and testing).</summary>
        Practice = 1,
        /// <summary>In the car during qualifying, lone or open.</summary>
        Qualifying = 2,
        /// <summary>In the car during the race.</summary>
        Race = 4,
        /// <summary>While watching the replay.</summary>
        Replay = 8,
        /// <summary>Every in-car session; what the old single "In car" choice meant.</summary>
        InCar = Practice | Qualifying | Race,
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
    /// Per-panel "when is this shown" setting: any mix of practice,
    /// qualifying, the race and the replay, or always. Ported from IrachingHud's ShowSetting, which each box set in
    /// code; here every panel carries its own, chosen with the Show chips on
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
                return mode & (PanelShowMode.InCar | PanelShowMode.Replay);
            return panel is IPanelShowDefault d ? d.DefaultShowMode : PanelShowMode.Always;
        }

        /// <summary>Edit-mode chips: "All" (nothing picked) then one per session type, in <see cref="Flags"/> order.</summary>
        public IReadOnlyList<(string Label, bool On)> ChipsFor(OverlayRenderTarget panel)
        {
            var mode = ModeOf(panel);
            var chips = new List<(string, bool)> { ("All", mode == PanelShowMode.Always) };
            foreach (var (label, flag) in Flags)
                chips.Add((label, (mode & flag) != 0));
            return chips;
        }

        private static readonly (string Label, PanelShowMode Flag)[] Flags =
        {
            ("Practice", PanelShowMode.Practice),
            ("Qualify", PanelShowMode.Qualifying),
            ("Race", PanelShowMode.Race),
            ("Replay", PanelShowMode.Replay),
        };

        /// <summary>
        /// Whether a panel with this mode applies in this state. Each flag covers
        /// only its own session (and, like the old In car choice, never a replay).
        /// An in-car session of an unrecognised type counts for any in-car flag.
        /// </summary>
        public static bool AppliesIn(PanelShowMode mode, SimState state)
        {
            if (mode == PanelShowMode.Always) return true;
            return state switch
            {
                SimState.Practice => (mode & PanelShowMode.Practice) != 0,
                SimState.Qualifying => (mode & PanelShowMode.Qualifying) != 0,
                SimState.Race => (mode & PanelShowMode.Race) != 0,
                SimState.Replay => (mode & PanelShowMode.Replay) != 0,
                SimState.InCar => (mode & PanelShowMode.InCar) != 0,
                _ => false,
            };
        }

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

        /// <summary>
        /// The raw readings <see cref="CurrentSimState"/> is derived from, for
        /// finding out why the sim is seen in the wrong state.
        /// </summary>
        public static string Diagnostics()
        {
            var svc = IracingService.Instance;
            if (!svc.IsConnected) return "not connected";
            return $"session type \"{svc.SessionType}\", IsReplayPlaying {svc.IsReplayPlaying}, " +
                   $"replay frame {svc.ReplayFrameNum} / end {svc.ReplayFrameNumEnd}, session state {svc.SessionState}";
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

        /// <summary>Click on chip <paramref name="index"/> of <see cref="ChipsFor"/>: "All" clears the set, a session type toggles in or out of it.</summary>
        public void Toggle(OverlayRenderTarget panel, int index)
        {
            PanelShowMode next;
            if (index <= 0 || index > Flags.Length) next = PanelShowMode.Always;
            else next = ModeOf(panel) ^ Flags[index - 1].Flag;
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
