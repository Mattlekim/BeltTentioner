using System.Collections.Generic;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace BeltTensionTest.WPF.Models
{
    /// <summary>
    /// Persisted application settings (mirrors WinForms AppSettings).
    /// </summary>
    public class AppSettings
    {
        public bool AutoConnectOnStartup { get; set; } = false;
        public bool UseSimHub { get; set; } = false;
        public bool UseIracing { get; set; } = true;
        public List<string> CollapsedGroups { get; set; } = new();
        // Global resting wind power (stored per-application rather than per-car)
        public int WindRestingPower { get; set; } = 0;
        // Start the application when the user logs into Windows
        public bool StartWithWindows { get; set; } = false;
        // When the window is closed, minimize to the taskbar (tray) instead of exiting
        public bool MinimizeToTaskbarOnClose { get; set; } = false;
        // Keybinding gestures (stored as strings like "Ctrl+F")
        public string ToggleFanKey { get; set; } = string.Empty;
        public string IncreaseWindRestingKey { get; set; } = string.Empty;
        public string DecreaseWindRestingKey { get; set; } = string.Empty;
        // Whether the shortcut is registered globally (system-wide) instead of only when app has focus
        public bool ToggleFanGlobal { get; set; } = false;
        public bool IncreaseWindRestingGlobal { get; set; } = false;
        public bool DecreaseWindRestingGlobal { get; set; } = false;
        // Overlay navigation keybindings (up/down move selection, increase/decrease change the selected value)
        public string NavUpKey { get; set; } = string.Empty;
        public string NavDownKey { get; set; } = string.Empty;
        public string NavIncreaseKey { get; set; } = string.Empty;
        public string NavDecreaseKey { get; set; } = string.Empty;
        public string NavNextControlKey { get; set; } = string.Empty;
        public string NavPrevControlKey { get; set; } = string.Empty;
        public bool NavUpGlobal { get; set; } = false;
        public bool NavDownGlobal { get; set; } = false;
        public bool NavIncreaseGlobal { get; set; } = false;
        public bool NavDecreaseGlobal { get; set; } = false;
        public bool NavNextControlGlobal { get; set; } = false;
        public bool NavPrevControlGlobal { get; set; } = false;
        // VR overlay layout (edit mode). Panel position is in canvas pixels,
        // -1 = never dragged (use the coded default). Size/distance/resolution
        // 0 = unset (keep the host defaults).
        public int OverlayPanelX { get; set; } = -1;
        public int OverlayPanelY { get; set; } = -1;
        public double OverlaySizeX { get; set; } = 0;
        public double OverlaySizeY { get; set; } = 0;
        public double OverlayDistance { get; set; } = 0;
        public int OverlayCanvasWidth { get; set; } = 0;
        public int OverlayCanvasHeight { get; set; } = 0;
        // Window placement / size
        public double WindowWidth { get; set; } = 1100;
        public double WindowHeight { get; set; } = 400;
        public double WindowLeft { get; set; } = double.NaN;
        public double WindowTop { get; set; } = double.NaN;
        // Stored as string to keep JSON simple (e.g. "Normal", "Maximized")
        public string WindowState { get; set; } = "Normal";
        // Whether the Effects section on the Belt Tensioner tab is expanded
        public bool EffectsExpanded { get; set; } = true;
    }
}
