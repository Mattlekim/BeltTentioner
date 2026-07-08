using System;

namespace BeltTensionTest.WPF.Services.Overlays
{
    public enum OverlayNavAction
    {
        Up,
        Down,
        Increase,
        Decrease,
    }

    /// <summary>
    /// Routes navigation keybindings (up/down/increase/decrease) from wherever
    /// they are detected (global hotkey, window key press, gamepad) to whoever
    /// is showing navigable UI (e.g. <see cref="BeltSettingsOverlay"/>).
    /// </summary>
    public static class OverlayNavigation
    {
        public static event Action<OverlayNavAction>? Navigated;

        public static void Raise(OverlayNavAction action) => Navigated?.Invoke(action);
    }
}
