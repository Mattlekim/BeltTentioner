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
        // Recenter the VR overlay on the headset (same as the overlay window's Recenter button).
        // Always registered system-wide — it is only ever pressed while the sim has focus.
        public string RecenterOverlayKey { get; set; } = string.Empty;
        // VR overlay layout (edit mode). Panel position is in canvas pixels,
        // -1 = never dragged (use the coded default). Size/distance/DPI
        // 0 = unset (keep the host defaults).
        public int OverlayPanelX { get; set; } = -1;
        public int OverlayPanelY { get; set; } = -1;
        public int OverlayMainPanelX { get; set; } = -1;
        public int OverlayMainPanelY { get; set; } = -1;
        // Main panel column order (comma-separated MainOverlay column names);
        // empty = the coded default order.
        public string OverlayMainColumnOrder { get; set; } = "";
        // OpenXR overlay preferences: whether the YouTube chat overlay exists
        // at all, and whether the overlay window opens itself on app start.
        public bool EnableYouTubeOverlay { get; set; } = false;
        public bool AutoStartOpenXrOverlay { get; set; } = false;
        // Run the OpenXR overlay in the background from app start, without the
        // overlay window having to be open (the window is then only for editing).
        public bool EnableOpenXrOverlay { get; set; } = false;
        public int OverlayWarningPanelX { get; set; } = -1;
        public int OverlayWarningPanelY { get; set; } = -1;
        public int OverlayNearbyPanelX { get; set; } = -1;
        public int OverlayNearbyPanelY { get; set; } = -1;
        // Nearby-cars strip width in canvas pixels; 0 = default (minimum).
        public int OverlayNearbyWidth { get; set; } = 0;
        public int OverlaySlowCarPanelX { get; set; } = -1;
        public int OverlaySlowCarPanelY { get; set; } = -1;
        public int OverlayYouTubePanelX { get; set; } = -1;
        public int OverlayYouTubePanelY { get; set; } = -1;
        // Per-panel size multiplier set with the edit-mode -/+ buttons or the
        // corner resize grip; 0 = unset (100%).
        public double OverlayPanelScale { get; set; } = 0;
        public double OverlayMainPanelScale { get; set; } = 0;
        public double OverlayWarningPanelScale { get; set; } = 0;
        public double OverlayNearbyPanelScale { get; set; } = 0;
        public double OverlaySlowCarPanelScale { get; set; } = 0;
        public double OverlayYouTubePanelScale { get; set; } = 0;
        public double OverlaySizeX { get; set; } = 0;
        public double OverlaySizeY { get; set; } = 0;
        public double OverlayDistance { get; set; } = 0;
        // View origin captured by the overlay's Recenter button/binding: the
        // headset position (meters, OpenXR LOCAL space) and look direction
        // (yaw/pitch, radians) the overlay is centered on. Unset = the
        // runtime's own origin, looking level.
        public bool OverlayOriginSet { get; set; } = false;
        public double OverlayOriginX { get; set; } = 0;
        public double OverlayOriginY { get; set; } = 0;
        public double OverlayOriginZ { get; set; } = 0;
        public double OverlayOriginYaw { get; set; } = 0;
        public double OverlayOriginPitch { get; set; } = 0;
        // Edit-cursor calibration (overlay window > Edit > Calibrate mouse):
        // canvas fraction = Scale * game-window fraction + Offset, per axis, so
        // the red cross lands on the game's own in-VR mouse cursor.
        public bool OverlayCursorCalSet { get; set; } = false;
        public double OverlayCursorCalScaleX { get; set; } = 1;
        public double OverlayCursorCalOffsetX { get; set; } = 0;
        public double OverlayCursorCalScaleY { get; set; } = 1;
        public double OverlayCursorCalOffsetY { get; set; } = 0;
        // Canvas pixels per meter of VR display size; the canvas resolution is
        // derived as size × DPI. 0 = unset (use the coded default).
        public double OverlayDpi { get; set; } = 0;
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
