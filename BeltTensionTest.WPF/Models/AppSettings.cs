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
        // Legacy combined "Main" standings panel (replaced by the Race and
        // Qualifying panels). Only read to seed their position/size the
        // first time; no longer written.
        public int OverlayMainPanelX { get; set; } = -1;
        public int OverlayMainPanelY { get; set; } = -1;
        public string OverlayMainColumnOrder { get; set; } = "";
        public int OverlayRacePanelX { get; set; } = -1;
        public int OverlayRacePanelY { get; set; } = -1;
        public int OverlayQualifyingPanelX { get; set; } = -1;
        public int OverlayQualifyingPanelY { get; set; } = -1;
        public int OverlayStandingsPanelX { get; set; } = -1;
        public int OverlayStandingsPanelY { get; set; } = -1;
        public int OverlayHudSettingsPanelX { get; set; } = -1;
        public int OverlayHudSettingsPanelY { get; set; } = -1;
        // Race / Qualifying panel column order (comma-separated column
        // names); empty = the coded default order.
        public string OverlayRaceColumnOrder { get; set; } = "";
        public string OverlayQualifyingColumnOrder { get; set; } = "";
        public string OverlayStandingsColumnOrder { get; set; } = "";
        // Drivers marked as a friend or a rival, keyed by iRacing customer id
        // ("name:<driver>" for AI): "Friend" or "Rival" (Services.Data.DriverTag).
        // Set by clicking a row on the in-VR standings board.
        public Dictionary<string, string> OverlayDriverTags { get; set; } = new();
        // Row colours those tags paint (0xRRGGBB), how strongly the colour
        // fills the row (0..0.8), and whether the Race / Qualifying relative
        // boxes use them too. All set from the in-VR HUD Settings panel.
        public int OverlayFriendColor { get; set; } = 0x2E8CD8;
        public int OverlayRivalColor { get; set; } = 0xD8462E;
        public double OverlayTagTint { get; set; } = 0.35;
        public bool OverlayTagsInRelativeBoxes { get; set; } = true;
        // Rows the standings board grows to before it starts leaving cars out.
        public int OverlayStandingsRows { get; set; } = 26;
        // Gaze focus: what looking at a panel does to it (Services.GazeEffect),
        // where the look direction comes from (Services.GazeSource), how far
        // the other panels dim (0..0.7) and how much the looked-at one grows
        // (1.0..1.6). Set from the in-VR HUD Settings panel.
        public string OverlayGazeEffect { get; set; } = "Brighten";
        public string OverlayGazeSource { get; set; } = "Off";
        public double OverlayGazeDim { get; set; } = 0.45;
        public double OverlayGazeGrow { get; set; } = 1.25;
        // OpenXR overlay preferences: whether the YouTube chat overlay exists
        // at all, and whether the overlay runs.
        public bool EnableYouTubeOverlay { get; set; } = false;
        // Run the OpenXR overlay (from app start, saved). Independent of the
        // overlay window: opening/closing it neither starts nor stops the overlay.
        public bool EnableOpenXrOverlay { get; set; } = false;
        public int OverlayWarningPanelX { get; set; } = -1;
        public int OverlayWarningPanelY { get; set; } = -1;
        public int OverlayNearbyPanelX { get; set; } = -1;
        public int OverlayNearbyPanelY { get; set; } = -1;
        // Nearby-cars strip width in canvas pixels; 0 = default (minimum).
        public int OverlayNearbyWidth { get; set; } = 0;
        public int OverlaySlowCarPanelX { get; set; } = -1;
        public int OverlaySlowCarPanelY { get; set; } = -1;
        public int OverlayCarBehindPanelX { get; set; } = -1;
        public int OverlayCarBehindPanelY { get; set; } = -1;
        // Car-behind box (IrachingHud's "Delta Behind"): shown while the car
        // behind is within ShowGap seconds; the readout turns red (and
        // flashes, if enabled) inside CloseGap seconds. 0 = default.
        public double OverlayCarBehindShowGap { get; set; } = 2.0;
        public double OverlayCarBehindCloseGap { get; set; } = 0.2;
        public bool OverlayCarBehindFlashClose { get; set; } = false;
        public int OverlayYouTubePanelX { get; set; } = -1;
        public int OverlayYouTubePanelY { get; set; } = -1;
        public int OverlayGpuPanelX { get; set; } = -1;
        public int OverlayGpuPanelY { get; set; } = -1;
        public int OverlayIncidentPanelX { get; set; } = -1;
        public int OverlayIncidentPanelY { get; set; } = -1;
        // In-VR display panel (edit mode only: overlay width/height/distance/DPI).
        public int OverlayDisplayPanelX { get; set; } = -1;
        public int OverlayDisplayPanelY { get; set; } = -1;
        // Per-panel size multiplier set with the edit-mode -/+ buttons or the
        // corner resize grip; 0 = unset (100%).
        public double OverlayPanelScale { get; set; } = 0;
        public double OverlayMainPanelScale { get; set; } = 0; // legacy, see OverlayMainPanelX
        public double OverlayRacePanelScale { get; set; } = 0;
        public double OverlayQualifyingPanelScale { get; set; } = 0;
        public double OverlayStandingsPanelScale { get; set; } = 0;
        public double OverlayHudSettingsPanelScale { get; set; } = 0;
        public double OverlayWarningPanelScale { get; set; } = 0;
        public double OverlayNearbyPanelScale { get; set; } = 0;
        public double OverlaySlowCarPanelScale { get; set; } = 0;
        public double OverlayCarBehindPanelScale { get; set; } = 0;
        public double OverlayYouTubePanelScale { get; set; } = 0;
        public double OverlayGpuPanelScale { get; set; } = 0;
        public double OverlayIncidentPanelScale { get; set; } = 0;
        public double OverlayDisplayPanelScale { get; set; } = 0;
        // Per-panel opacity set with the edit-mode opacity -/+ buttons;
        // 0 = unset (100%).
        public double OverlayPanelOpacity { get; set; } = 0;
        public double OverlayRacePanelOpacity { get; set; } = 0;
        public double OverlayQualifyingPanelOpacity { get; set; } = 0;
        public double OverlayStandingsPanelOpacity { get; set; } = 0;
        public double OverlayHudSettingsPanelOpacity { get; set; } = 0;
        public double OverlayWarningPanelOpacity { get; set; } = 0;
        public double OverlayNearbyPanelOpacity { get; set; } = 0;
        public double OverlaySlowCarPanelOpacity { get; set; } = 0;
        public double OverlayCarBehindPanelOpacity { get; set; } = 0;
        public double OverlayYouTubePanelOpacity { get; set; } = 0;
        public double OverlayGpuPanelOpacity { get; set; } = 0;
        public double OverlayIncidentPanelOpacity { get; set; } = 0;
        public double OverlayDisplayPanelOpacity { get; set; } = 0;
        // Controller/wheel buttons that toggle a VR overlay panel's
        // visibility, keyed by panel name: "Button5" or a hold+press combo
        // "Button5+Button12" (GamepadService control names). Assigned with
        // the panel's Bind button in the overlay's edit mode.
        public Dictionary<string, string> OverlayPanelToggleBindings { get; set; } = new();
        // When each VR overlay panel is shown, keyed by panel name:
        // "Always", "InCar" or "Replay" (Services.PanelShowMode). Chosen with
        // the panel's Show button in the overlay's edit mode; a panel with no
        // entry uses the mode it ships with.
        public Dictionary<string, string> OverlayPanelShowModes { get; set; } = new();
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
