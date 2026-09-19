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
            var svc = IracingService.Instance;
            bool replay = svc.IsConnected && svc.InReplay;
            bool inCar = svc.IsConnected && !replay;

            bool any = false;
            foreach (var panel in _panels())
            {
                bool available = ModeOf(panel) switch
                {
                    PanelShowMode.InCar => inCar,
                    PanelShowMode.Replay => replay,
                    _ => true,
                };
                if (panel.Available == available) continue;
                panel.Available = available;
                any = true;
            }
            if (any) _invalidateCanvas();
        }
    }
}
