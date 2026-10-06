using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using BeltTensionTest.WPF.Services.Overlays;
using BeltTensionTest.WPF.ViewModels;
using MonoXR.Client;

namespace BeltTensionTest.WPF.Views
{
    /// <summary>
    /// Hosts the OpenXR overlay. The window itself only shows the layer/log
    /// status; all visual content comes from MonoGame render targets that are
    /// composited onto the overlay canvas (see the render section below).
    /// </summary>
    public partial class OverlayWindow : Window
    {
        // Initial canvas size only — ApplySavedLayout immediately re-derives
        // the resolution as display size (m) × DPI (px/m).
        private const int CanvasXSize = 1920;
        private const int CanvasYSize = 1024;

        private const double DefaultDpi = 768; // 1920 px across the default 2.5 m width
        // Smallest canvas the edit UI stays usable on (the Display panel alone
        // is 700 px wide); a size/DPI combination below it raises the effective
        // DPI rather than shrinking the canvas.
        private const int MinCanvasSize = 480, MaxCanvasSize = 8192;
        // Smallest overlay the Width/Height sliders can reach, in meters.
        // (The depth-only distance mode is exempt: it shrinks the quad in
        // meters precisely to keep its APPARENT size, which is what this floor
        // is really protecting.)
        private const float MinDisplayMeters = 0.5f;
        private double _dpi = DefaultDpi;

        private MonoGameOverlayHost? _host;
        private readonly MainViewModel _vm;
        private readonly Services.SettingsService _settingsSvc = new();
        private BeltSettingsOverlay? _beltPanel;
        private RaceOverlay? _racePanel;
        private QualifyingOverlay? _qualifyingPanel;
        private StandingsOverlay? _standingsPanel;
        private HudSettingsOverlay? _hudSettingsPanel;
        private WarningOverlay? _warningPanel;
        private NearbyCarsOverlay? _nearbyPanel;
        private SlowCarOverlay? _slowCarPanel;
        private CarBehindOverlay? _carBehindPanel;
        private YouTubeOverlay? _youtubePanel;
        private GpuStatsOverlay? _gpuPanel;
        private IncidentsOverlay? _incidentPanel;
        private DisplaySettingsOverlay? _displayPanel;
        private OverlayPreviewWindow? _preview;
        private Services.GazeFocus? _gazeFocus;
        private Services.PanelToggleBindings? _toggleBindings;
        private Services.PanelVisibility? _panelVisibility;

        // Cars shown in the Race / Qualifying panels (player included) —
        // change this to make the panels taller/shorter.
        private const int StandingsCarCount = 7;

        // ~60 Hz ticks so edit-mode input (cursor, clicks) is sampled quickly;
        // rendering itself is still capped at 30 fps by the host's
        // MaxFrameRate, so capped ticks cost only the input poll.
        private readonly DispatcherTimer _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
        // Debounces canvas re-creation while a size/DPI slider is being
        // dragged (SetCanvasResolution rebuilds the shared overlay texture).
        private readonly DispatcherTimer _resolutionTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        private bool _lastAttached;
        private bool _statusLogged;

        public OverlayWindow(MainViewModel vm)
        {
            _vm = vm;
            InitializeComponent();

            _resolutionTimer.Tick += OnResolutionTimerTick;
            _timer.Tick += OnTick;
            Closing += OnClosing;
            Closed += OnClosed;
            UpdateRunningUi();
        }

        /// <summary>True while the overlay is running (Preferences > OpenXR > Run OpenXR overlay).</summary>
        public bool IsRunning => _host != null;

        /// <summary>
        /// Start or stop the overlay. This is driven only by the Preferences
        /// setting — showing or closing this window never starts or stops it.
        /// </summary>
        public void SetRunning(bool run)
        {
            if (_closed) return;
            if (run && _host == null) Start();
            else if (!run && _host != null) Stop();
        }

        private void Start()
        {
            _lastAttached = false;
            _statusLogged = false;
            try
            {
                Log("Creating MonoGame overlay host...");
                _host = new MonoGameOverlayHost(CanvasXSize, CanvasYSize);
                Log("Host ready: MonoGame device up, overlay published (World, 3m ahead, 2.5m).");

                ApplySavedLayout();

                // Friend / rival tags and their colours, before the panels
                // that read them are built. DriverTags keeps the settings
                // dictionary by reference, so a tag set in VR is already in
                // the settings object when SaveLayout runs.
                var settings = _vm.AppSettings;
                if (settings != null)
                    Services.Data.DriverTags.Instance.Load(
                        settings.OverlayDriverTags, settings.OverlayFriendColor, settings.OverlayRivalColor,
                        settings.OverlayTagTint, settings.OverlayTagsInRelativeBoxes);

                SetupRenderTargets();
                _host.DragCompleted += OnDragCompleted;
                _host.ScaleChanged += OnScaleChanged;
                _host.OpacityChanged += OnOpacityChanged;

                // Controller/wheel buttons (or hold+press combos) that toggle
                // a panel's visibility, assigned with each panel's edit-mode
                // Bind button.
                _toggleBindings = new Services.PanelToggleBindings(
                    _vm.AppSettings?.OverlayPanelToggleBindings ?? new Dictionary<string, string>(),
                    AllPanels, () => _host?.InvalidateCanvas());
                _toggleBindings.Changed += SaveLayout;
                _toggleBindings.Status += Log;
                _host.BindButtonLabel = _toggleBindings.LabelFor;
                _host.BindRequested += _toggleBindings.BeginCapture;

                // When each panel applies (always / in car / replay), chosen
                // with its edit-mode Show button.
                _panelVisibility = new Services.PanelVisibility(
                    _vm.AppSettings?.OverlayPanelShowModes ?? new Dictionary<string, string>(),
                    AllPanels, () => _host?.InvalidateCanvas());
                _panelVisibility.Changed += SaveLayout;
                _host.ShowButtonLabel = _panelVisibility.LabelFor;
                _host.ShowModeCycleRequested += _panelVisibility.Cycle;
                _panelVisibility.Apply();
                // Gaze focus runs off the head pose the MonoXR layer
                // publishes; edit mode suspends it so panels hold still while
                // they are arranged.
                _host.EditModeChanged += _ => _gazeFocus?.Reset();

                _host.CursorCalibrationCompleted += OnCursorCalibrationCompleted;
                // Edit mode can also be toggled from the icon in VR — mirror it here.
                _host.EditModeChanged += editing => EditButton.IsChecked = editing;
                _host.EditFont = Services.RuntimeSpriteFont.Bake(_host.GraphicsDevice, "Segoe UI", 22f);

                // When the mouse is inside the preview window, drive the edit
                // cursor from it 1:1 instead of the whole-monitor mapping, and
                // take the button state from its WPF events (latched, so fast
                // clicks can't fall between host polls).
                _host.CursorOverride = () => _preview?.TryGetCanvasCursor();
                _host.LeftButtonOverride = () => _preview?.TryGetLeftButton();
                _host.RightButtonOverride = () => _preview?.TryGetRightButton();
            }
            catch (Exception ex)
            {
                Log("INIT FAILED: " + ex);
                Stop();
                StatusLabel.Text = "Init failed — see log.";
                return;
            }

            RecenterRequested += Recenter;
            _timer.Start();
            UpdateRunningUi();
        }

        private void Stop()
        {
            RecenterRequested -= Recenter;
            _timer.Stop();
            _resolutionTimer.Stop();
            EditButton.IsChecked = false;
            _preview?.Close();
            _toggleBindings?.Dispose();
            _toggleBindings = null;
            _panelVisibility = null;
            _gazeFocus = null;
            _beltPanel = null; _racePanel = null; _qualifyingPanel = null; _standingsPanel = null;
            _hudSettingsPanel = null; _warningPanel = null; _nearbyPanel = null; _slowCarPanel = null; _carBehindPanel = null;
            _youtubePanel = null; _gpuPanel = null; _incidentPanel = null; _displayPanel = null;
            bool wasRunning = _host != null;
            _host?.Dispose();
            _host = null;
            if (wasRunning) Log("Overlay stopped.");
            UpdateRunningUi();
        }

        /// <summary>Title-bar state and which controls work, for running vs. disabled.</summary>
        private void UpdateRunningUi()
        {
            bool running = IsRunning;
            TitleState.Text = running ? "— Running" : "— Disabled";
            TitleState.Foreground = running
                ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x50, 0xC8, 0x78))
                : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xDC, 0x3C, 0x3C));
            EditButton.IsEnabled = PreviewButton.IsEnabled = RecenterButton.IsEnabled = running;
            if (!running)
                StatusLabel.Text = "OpenXR overlay is off — turn on Settings → Preferences → OpenXR → Run OpenXR overlay.";
        }

        private bool _closed, _shuttingDown;

        /// <summary>Stop the overlay and really close the window (app exit).</summary>
        public void Shutdown()
        {
            _shuttingDown = true;
            Cleanup(); // a never-shown window may not raise Closed
            try { Close(); } catch { }
        }

        private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_shuttingDown) return;

            // Closing the window only hides it; the overlay keeps its current
            // running state. Leave edit mode first so the red border/cursor and
            // button bars don't stay up in VR.
            e.Cancel = true;
            EditButton.IsChecked = false;
            _preview?.Close();
            Hide();
        }

        // =====================================================================
        //  MONOGAME RENDER SECTION
        //
        //  Subclass OverlayRenderTarget (see Services/Overlays), override
        //  Update (game logic) and Render (drawing, target already bound),
        //  then register it with
        //      _host.AddRenderTarget(new MyTarget(_host.GraphicsDevice, ...));
        //  (x, y) is the pixel location of the target inside the overlay
        //  canvas, changeable at runtime via rt.X / rt.Y.
        // =====================================================================
        private void SetupRenderTargets()
        {
            if (_host == null) return;

            // Belt settings panel, centered on the overlay canvas. Rows read
            // and write MainViewModel properties so the desktop sliders stay
            // in sync and changes are saved through the normal path. Both the
            // nav events and the render timer run on the UI thread.
            // One group per force axis, like the MainWindow layout: each holds
            // its sliders and invert checkbox. Slider fills mirror the
            // FillBrush of the matching row in MainWindow.xaml.
            var surge = new Microsoft.Xna.Framework.Color(0x64, 0x96, 0xFF);
            var sway  = new Microsoft.Xna.Framework.Color(0x50, 0xC8, 0x78);
            var heave = new Microsoft.Xna.Framework.Color(0xFF, 0x80, 0x40);
            var power = new Microsoft.Xna.Framework.Color(0xDC, 0x3C, 0x3C);
            var groups = new[]
            {
                new BeltSettingGroup("Surge",
                    new[]
                    {
                        new BeltSettingRow("Strength", () => _vm.BrakingStrength, v => _vm.BrakingStrength = v, 1f,   200f, 5f,   "0",   surge),
                        new BeltSettingRow("Curve",    () => _vm.BrakingCurve,    v => _vm.BrakingCurve    = v, 0.1f, 5f,   0.1f, "0.0", surge),
                    },
                    new[] { new BeltToggleRow("Invert", () => _vm.InvertSurge, v => _vm.InvertSurge = v) }),

                new BeltSettingGroup("Sway",
                    new[]
                    {
                        new BeltSettingRow("Strength", () => _vm.CorneringStrength, v => _vm.CorneringStrength = v, 1f,   200f, 5f,   "0",   sway),
                        new BeltSettingRow("Curve",    () => _vm.CorneringCurve,    v => _vm.CorneringCurve    = v, 0.1f, 10f,  0.1f, "0.0", sway),
                    },
                    new[] { new BeltToggleRow("Invert", () => _vm.InvertSway, v => _vm.InvertSway = v) }),

                new BeltSettingGroup("Heave",
                    new[]
                    {
                        new BeltSettingRow("Strength", () => _vm.VerticalStrength, v => _vm.VerticalStrength = v, 1f, 200f, 5f, "0", heave),
                    },
                    new[] { new BeltToggleRow("Invert", () => _vm.InvertHeave, v => _vm.InvertHeave = v) }),

                new BeltSettingGroup("Power",
                    new[]
                    {
                        new BeltSettingRow("Max Power", () => _vm.MaxOutput, v => _vm.MaxOutput = v, 1f, 100f, 5f, "0", power),
                    }),
            };

            const int panelWidth = 768, panelHeight = 736;
            int x = (_host.CanvasWidth - panelWidth) / 2, y = (_host.CanvasHeight - panelHeight) / 2;

            // Restore the last dragged position (clamped in case the canvas shrank).
            var s = _vm.AppSettings;
            if (s != null && s.OverlayPanelX >= 0 && s.OverlayPanelY >= 0)
            {
                // Clamp against the size it will actually be shown at (its saved scale).
                double beltScale = s.OverlayPanelScale > 0 ? s.OverlayPanelScale : 1.0;
                x = Math.Min(s.OverlayPanelX, Math.Max(0, _host.CanvasWidth - (int)(panelWidth * beltScale)));
                y = Math.Min(s.OverlayPanelY, Math.Max(0, _host.CanvasHeight - (int)(panelHeight * beltScale)));
            }

            _beltPanel = _host.AddRenderTarget(new BeltSettingsOverlay(
                _host.GraphicsDevice, panelWidth, panelHeight, x, y, groups));

            // Race and Qualifying relative boxes (ported from IrachingHud's
            // RelativeBox): each only shows during its own session types, so
            // they share the top-left corner by default. The qualifying box
            // starts offset a little so both title bars can be grabbed in
            // edit mode. The first time, both inherit the old combined "Main"
            // panel's position and size.
            int boxX = 16, boxY = 16;
            if (s != null && s.OverlayMainPanelX >= 0 && s.OverlayMainPanelY >= 0)
                (boxX, boxY) = (s.OverlayMainPanelX, s.OverlayMainPanelY);

            int raceX = boxX, raceY = boxY;
            if (s != null && s.OverlayRacePanelX >= 0 && s.OverlayRacePanelY >= 0)
                (raceX, raceY) = (s.OverlayRacePanelX, s.OverlayRacePanelY);
            _racePanel = _host.AddRenderTarget(new RaceOverlay(_host.GraphicsDevice,
                Math.Min(raceX, Math.Max(0, _host.CanvasWidth - 100)),
                Math.Min(raceY, Math.Max(0, _host.CanvasHeight - 100)), StandingsCarCount));
            if (!string.IsNullOrEmpty(s?.OverlayRaceColumnOrder))
                _racePanel.ColumnOrder = s.OverlayRaceColumnOrder;
            _racePanel.ColumnOrderChanged += SaveLayout; // persist header drags like panel moves

            int qualX = boxX + 60, qualY = boxY + 60;
            if (s != null && s.OverlayQualifyingPanelX >= 0 && s.OverlayQualifyingPanelY >= 0)
                (qualX, qualY) = (s.OverlayQualifyingPanelX, s.OverlayQualifyingPanelY);
            _qualifyingPanel = _host.AddRenderTarget(new QualifyingOverlay(_host.GraphicsDevice,
                Math.Min(qualX, Math.Max(0, _host.CanvasWidth - 100)),
                Math.Min(qualY, Math.Max(0, _host.CanvasHeight - 100)), StandingsCarCount));
            if (!string.IsNullOrEmpty(s?.OverlayQualifyingColumnOrder))
                _qualifyingPanel.ColumnOrder = s.OverlayQualifyingColumnOrder;
            _qualifyingPanel.ColumnOrderChanged += SaveLayout;

            // Full-field standings board. It is much wider and taller than the
            // relative boxes, so it defaults to the right-hand side at 70% —
            // the size a full grid fits a default canvas at.
            const float StandingsDefaultScale = 0.7f;
            int standX = Math.Max(0, _host.CanvasWidth - (int)(StandingsOverlay.BoardWidth * StandingsDefaultScale) - 16);
            int standY = 16;
            if (s != null && s.OverlayStandingsPanelX >= 0 && s.OverlayStandingsPanelY >= 0)
            {
                standX = Math.Min(s.OverlayStandingsPanelX, Math.Max(0, _host.CanvasWidth - 100));
                standY = Math.Min(s.OverlayStandingsPanelY, Math.Max(0, _host.CanvasHeight - 100));
            }
            _standingsPanel = _host.AddRenderTarget(new StandingsOverlay(
                _host.GraphicsDevice, standX, standY));
            _standingsPanel.Scale = StandingsDefaultScale; // RestoreScale overrides it when one was saved
            if (!string.IsNullOrEmpty(s?.OverlayStandingsColumnOrder))
                _standingsPanel.ColumnOrder = s.OverlayStandingsColumnOrder;
            _standingsPanel.ColumnOrderChanged += SaveLayout;
            _standingsPanel.MaxRows = s?.OverlayStandingsRows ?? StandingsOverlay.MaxRowSetting;

            // Gaze focus has to exist before the settings panel that drives
            // it; the panels it acts on come from the same AllPanels list.
            _gazeFocus = new Services.GazeFocus(_host, AllPanels);
            if (s != null)
            {
                if (Enum.TryParse(s.OverlayGazeEffect, out Services.GazeEffect gazeEffect))
                    _gazeFocus.Effect = gazeEffect;
                if (Enum.TryParse(s.OverlayGazeSource, out Services.GazeSource gazeSource))
                    _gazeFocus.Source = gazeSource;
                _gazeFocus.DimAmount = (float)s.OverlayGazeDim;
                _gazeFocus.GrowFactor = (float)s.OverlayGazeGrow;
            }

            // HUD settings (tag colours, standings size). Not edit-mode only:
            // these are changed while sitting in the pits. It starts collapsed
            // to a pill near the bottom-left, which opens it when clicked.
            var standingsRowsSetting = new BeltSettingRow(
                "Rows on the board",
                () => _standingsPanel?.MaxRows ?? 0,
                v => { if (_standingsPanel != null) _standingsPanel.MaxRows = (int)v; },
                StandingsOverlay.MinRowSetting, StandingsOverlay.MaxRowSetting, 1f, "0");

            int hudX = 16, hudY = Math.Max(0, _host.CanvasHeight - 400); // above the GPU panel's corner
            if (s != null && s.OverlayHudSettingsPanelX >= 0 && s.OverlayHudSettingsPanelY >= 0)
            {
                hudX = Math.Min(s.OverlayHudSettingsPanelX, Math.Max(0, _host.CanvasWidth - 100));
                hudY = Math.Min(s.OverlayHudSettingsPanelY, Math.Max(0, _host.CanvasHeight - 100));
            }
            _hudSettingsPanel = _host.AddRenderTarget(new HudSettingsOverlay(
                _host.GraphicsDevice, hudX, hudY, standingsRowsSetting, _gazeFocus, SaveLayout));
            _hudSettingsPanel.State = MonoXR.Client.OverlayTargetState.Collapsed;

            // Yellow-flag warning card: defaults to top-center, above where
            // the eye already is for flags.
            int warnX = (_host.CanvasWidth - 360) / 2, warnY = 16;
            if (s != null && s.OverlayWarningPanelX >= 0 && s.OverlayWarningPanelY >= 0)
            {
                warnX = Math.Min(s.OverlayWarningPanelX, Math.Max(0, _host.CanvasWidth - 100));
                warnY = Math.Min(s.OverlayWarningPanelY, Math.Max(0, _host.CanvasHeight - 100));
            }
            _warningPanel = _host.AddRenderTarget(new WarningOverlay(
                _host.GraphicsDevice, warnX, warnY));

            // Car-alongside spotter (NearbyCarsOverlay): defaults to bottom-
            // center, roughly where your peripheral vision expects a spotter.
            int nearX = (_host.CanvasWidth - 500) / 2, nearY = _host.CanvasHeight - 200;
            if (s != null && s.OverlayNearbyPanelX >= 0 && s.OverlayNearbyPanelY >= 0)
            {
                nearX = Math.Min(s.OverlayNearbyPanelX, Math.Max(0, _host.CanvasWidth - 100));
                nearY = Math.Min(s.OverlayNearbyPanelY, Math.Max(0, _host.CanvasHeight - 100));
            }
            _nearbyPanel = _host.AddRenderTarget(new NearbyCarsOverlay(
                _host.GraphicsDevice, nearX, nearY));

            // Slow-car warning card: defaults just below the yellow-flag card
            // (both live top-center; a slow car and a stopped car are cousins).
            int slowX = (_host.CanvasWidth - 300) / 2, slowY = 16 + 120 + 12;
            if (s != null && s.OverlaySlowCarPanelX >= 0 && s.OverlaySlowCarPanelY >= 0)
            {
                slowX = Math.Min(s.OverlaySlowCarPanelX, Math.Max(0, _host.CanvasWidth - 100));
                slowY = Math.Min(s.OverlaySlowCarPanelY, Math.Max(0, _host.CanvasHeight - 100));
            }
            _slowCarPanel = _host.AddRenderTarget(new SlowCarOverlay(
                _host.GraphicsDevice, slowX, slowY));

            // Distance-to-car-behind box: defaults just above the spotter
            // strip, bottom-center (where a mirror glance would go).
            int behindX = (_host.CanvasWidth - 160) / 2, behindY = Math.Max(0, _host.CanvasHeight - 200 - 80 - 12);
            if (s != null && s.OverlayCarBehindPanelX >= 0 && s.OverlayCarBehindPanelY >= 0)
            {
                behindX = Math.Min(s.OverlayCarBehindPanelX, Math.Max(0, _host.CanvasWidth - 100));
                behindY = Math.Min(s.OverlayCarBehindPanelY, Math.Max(0, _host.CanvasHeight - 100));
            }
            _carBehindPanel = _host.AddRenderTarget(new CarBehindOverlay(
                _host.GraphicsDevice, behindX, behindY, () => _vm.AppSettings));
            if (s != null && s.OverlayNearbyWidth > 0)
                _nearbyPanel.BoxWidth = s.OverlayNearbyWidth;
            _nearbyPanel.BoxWidthChanged += SaveLayout; // persist slider resizes like drags

            // YouTube live-chat panel: only exists when enabled in Preferences
            // (OpenXR tab). Defaults to the top-right corner.
            if (s?.EnableYouTubeOverlay == true)
            {
                int ytX = Math.Max(0, _host.CanvasWidth - 720 - 16), ytY = 16;
                if (s.OverlayYouTubePanelX >= 0 && s.OverlayYouTubePanelY >= 0)
                {
                    ytX = Math.Min(s.OverlayYouTubePanelX, Math.Max(0, _host.CanvasWidth - 100));
                    ytY = Math.Min(s.OverlayYouTubePanelY, Math.Max(0, _host.CanvasHeight - 100));
                }
                _youtubePanel = _host.AddRenderTarget(new YouTubeOverlay(
                    _host.GraphicsDevice, ytX, ytY));
            }

            // GPU load / temperature / VRAM panel: defaults to the bottom-left corner.
            int gpuX = 16, gpuY = Math.Max(0, _host.CanvasHeight - 250 - 16);
            if (s != null && s.OverlayGpuPanelX >= 0 && s.OverlayGpuPanelY >= 0)
            {
                gpuX = Math.Min(s.OverlayGpuPanelX, Math.Max(0, _host.CanvasWidth - 100));
                gpuY = Math.Min(s.OverlayGpuPanelY, Math.Max(0, _host.CanvasHeight - 100));
            }
            _gpuPanel = _host.AddRenderTarget(new GpuStatsOverlay(
                _host.GraphicsDevice, gpuX, gpuY));

            // Incident list (replay only): defaults to the bottom-right corner.
            var incidents = new IncidentsOverlay(_host.GraphicsDevice, 0, 0);
            int incX = Math.Max(0, _host.CanvasWidth - incidents.Width - 24);
            int incY = Math.Max(0, _host.CanvasHeight - incidents.Height - 24);
            if (s != null && s.OverlayIncidentPanelX >= 0 && s.OverlayIncidentPanelY >= 0)
            {
                incX = Math.Min(s.OverlayIncidentPanelX, Math.Max(0, _host.CanvasWidth - 100));
                incY = Math.Min(s.OverlayIncidentPanelY, Math.Max(0, _host.CanvasHeight - 100));
            }
            incidents.X = incX;
            incidents.Y = incY;
            _incidentPanel = _host.AddRenderTarget(incidents);

            // In-VR twin of this window's edit panel: overlay width/height,
            // distance and canvas DPI. Rows write through ApplyDisplaySetting,
            // the same path as the desktop sliders, so both stay in sync and
            // the values are saved the usual way. The panel only exists in
            // edit mode (EditModeOnly), which is also when it is useful.
            var host = _host;
            var displayRows = new[]
            {
                new BeltSettingRow("Width (m)",    () => host.DisplaySize.X, v => ApplyDisplaySetting(DisplaySetting.Width, v),    MinDisplayMeters,  8f,   0.05f, "0.00"),
                new BeltSettingRow("Height (m)",   () => host.DisplaySize.Y, v => ApplyDisplaySetting(DisplaySetting.Height, v),   MinDisplayMeters,  8f,   0.05f, "0.00"),
                new BeltSettingRow("Distance (m)", () => host.Distance,      v => ApplyDisplaySetting(DisplaySetting.Distance, v), 0.25f, 10f,   0.05f, "0.00"),
                new BeltSettingRow("DPI (px/m)",   () => (float)_dpi,        v => ApplyDisplaySetting(DisplaySetting.Dpi, v),      128f, 2048f, 16f,   "0"),
            };
            var display = new DisplaySettingsOverlay(_host.GraphicsDevice, 0, 0, displayRows,
                () => $"Resolution  {host.CanvasWidth} x {host.CanvasHeight} px");

            // Defaults to the right-hand edge, vertically centered (the corner
            // panels are all taken); then the saved position, clamped.
            int dispX = Math.Max(0, _host.CanvasWidth - display.Width - 24);
            int dispY = Math.Max(0, (_host.CanvasHeight - display.Height) / 2);
            if (s != null && s.OverlayDisplayPanelX >= 0 && s.OverlayDisplayPanelY >= 0)
            {
                dispX = Math.Min(s.OverlayDisplayPanelX, Math.Max(0, _host.CanvasWidth - 100));
                dispY = Math.Min(s.OverlayDisplayPanelY, Math.Max(0, _host.CanvasHeight - 100));
            }
            display.X = dispX;
            display.Y = dispY;
            _displayPanel = _host.AddRenderTarget(display);

            // Restore each panel's saved size (edit-mode -/+ buttons / corner
            // grip), then pull anything the new size pushed off the canvas back on.
            if (s != null)
            {
                RestoreScale(_beltPanel, s.OverlayPanelScale);
                double boxScale = s.OverlayMainPanelScale;
                RestoreScale(_racePanel, s.OverlayRacePanelScale > 0 ? s.OverlayRacePanelScale : boxScale);
                RestoreScale(_qualifyingPanel, s.OverlayQualifyingPanelScale > 0 ? s.OverlayQualifyingPanelScale : boxScale);
                RestoreScale(_standingsPanel, s.OverlayStandingsPanelScale);
                RestoreScale(_hudSettingsPanel, s.OverlayHudSettingsPanelScale);
                RestoreScale(_warningPanel, s.OverlayWarningPanelScale);
                RestoreScale(_nearbyPanel, s.OverlayNearbyPanelScale);
                RestoreScale(_slowCarPanel, s.OverlaySlowCarPanelScale);
                RestoreScale(_carBehindPanel, s.OverlayCarBehindPanelScale);
                RestoreScale(_youtubePanel, s.OverlayYouTubePanelScale);
                RestoreScale(_gpuPanel, s.OverlayGpuPanelScale);
                RestoreScale(_incidentPanel, s.OverlayIncidentPanelScale);
                RestoreScale(_displayPanel, s.OverlayDisplayPanelScale);

                RestoreOpacity(_beltPanel, s.OverlayPanelOpacity);
                RestoreOpacity(_racePanel, s.OverlayRacePanelOpacity);
                RestoreOpacity(_qualifyingPanel, s.OverlayQualifyingPanelOpacity);
                RestoreOpacity(_standingsPanel, s.OverlayStandingsPanelOpacity);
                RestoreOpacity(_hudSettingsPanel, s.OverlayHudSettingsPanelOpacity);
                RestoreOpacity(_warningPanel, s.OverlayWarningPanelOpacity);
                RestoreOpacity(_nearbyPanel, s.OverlayNearbyPanelOpacity);
                RestoreOpacity(_slowCarPanel, s.OverlaySlowCarPanelOpacity);
                RestoreOpacity(_carBehindPanel, s.OverlayCarBehindPanelOpacity);
                RestoreOpacity(_youtubePanel, s.OverlayYouTubePanelOpacity);
                RestoreOpacity(_gpuPanel, s.OverlayGpuPanelOpacity);
                RestoreOpacity(_incidentPanel, s.OverlayIncidentPanelOpacity);
                RestoreOpacity(_displayPanel, s.OverlayDisplayPanelOpacity);
                ClampPanelsToCanvas();
            }
        }

        private static void RestoreScale(OverlayRenderTarget? panel, double savedScale)
        {
            if (panel != null && savedScale > 0) panel.Scale = (float)savedScale;
        }

        /// <summary>
        /// A panel's own opacity / scale, with any gaze dimming or growth
        /// taken back off — these are what gets saved, so a panel that was
        /// being looked at when the layout was saved comes back the same size.
        /// </summary>
        private double SavedScaleOf(OverlayRenderTarget panel) =>
            _gazeFocus?.BaseScaleOf(panel) ?? panel.Scale;

        private double SavedOpacityOf(OverlayRenderTarget panel) =>
            _gazeFocus?.BaseOpacityOf(panel) ?? panel.Opacity;

        private static void RestoreOpacity(OverlayRenderTarget? panel, double savedOpacity)
        {
            if (panel != null && savedOpacity > 0) panel.Opacity = (float)savedOpacity;
        }
        // ===================== END MONOGAME RENDER SECTION ===================

        /// <summary>Apply persisted overlay size/distance/DPI to the freshly created host.</summary>
        private void ApplySavedLayout()
        {
            var s = _vm.AppSettings;
            if (_host == null) return;

            if (s != null)
            {
                if (s.OverlaySizeX > 0 && s.OverlaySizeY > 0)
                    _host.DisplaySize = new System.Numerics.Vector2((float)s.OverlaySizeX, (float)s.OverlaySizeY);
                if (s.OverlayDistance > 0)
                    _host.Distance = (float)s.OverlayDistance;
                if (s.OverlayDpi > 0)
                    _dpi = s.OverlayDpi;
                if (s.OverlayCursorCalSet)
                    _host.SetCursorCalibration((float)s.OverlayCursorCalScaleX, (float)s.OverlayCursorCalOffsetX,
                                               (float)s.OverlayCursorCalScaleY, (float)s.OverlayCursorCalOffsetY);
                if (s.OverlayOriginSet)
                    _host.SetOrigin(
                        new System.Numerics.Vector3((float)s.OverlayOriginX, (float)s.OverlayOriginY, (float)s.OverlayOriginZ),
                        (float)s.OverlayOriginYaw, (float)s.OverlayOriginPitch);
            }
            ApplyCanvasResolution();
        }

        /// <summary>
        /// Derive the canvas pixel resolution from the VR display size and the
        /// DPI (pixels per meter) and apply it, so pixels stay square and the
        /// panels keep their physical size when the overlay is resized.
        /// </summary>
        private void ApplyCanvasResolution()
        {
            if (_host == null) return;
            // Size x DPI, with both axes scaled by the SAME factor when that
            // lands outside the canvas limits — clamping them one at a time
            // would squash the aspect and stop the pixels being square.
            double rawW = _host.DisplaySize.X * _dpi, rawH = _host.DisplaySize.Y * _dpi;
            double fit = 1.0;
            double smallest = Math.Max(1e-6, Math.Min(rawW, rawH));
            if (smallest < MinCanvasSize) fit = MinCanvasSize / smallest;
            double largest = Math.Max(rawW, rawH) * fit;
            if (largest > MaxCanvasSize) fit *= MaxCanvasSize / largest;

            int w = Math.Clamp((int)Math.Round(rawW * fit), MinCanvasSize, MaxCanvasSize);
            int h = Math.Clamp((int)Math.Round(rawH * fit), MinCanvasSize, MaxCanvasSize);
            if (w == _host.CanvasWidth && h == _host.CanvasHeight) return;

            try
            {
                _host.SetCanvasResolution(w, h);
                ClampPanelsToCanvas();
                // The effective DPI, which is the requested one unless the
                // limits above scaled it.
                Log($"Canvas resolution set to {w}×{h} ({w / Math.Max(0.01f, _host.DisplaySize.X):0} px/m).");
            }
            catch (Exception ex)
            {
                Log("RESOLUTION CHANGE FAILED: " + ex);
            }
        }

        /// <summary>Pull panels back onto the canvas after it shrank (same rule as the startup restore).</summary>
        private void ClampPanelsToCanvas()
        {
            if (_host == null) return;
            foreach (var p in AllPanels())
            {
                p.X = Math.Min(p.X, Math.Max(0, _host.CanvasWidth - Math.Min(100, p.DisplayWidth)));
                p.Y = Math.Min(p.Y, Math.Max(0, _host.CanvasHeight - Math.Min(100, p.DisplayHeight)));
            }
        }

        /// <summary>Every panel that exists (the YouTube one only when enabled).</summary>
        private IEnumerable<OverlayRenderTarget> AllPanels()
        {
            var panels = new OverlayRenderTarget?[]
                { _beltPanel, _racePanel, _qualifyingPanel, _standingsPanel, _hudSettingsPanel, _warningPanel,
                  _nearbyPanel, _slowCarPanel, _carBehindPanel, _youtubePanel, _gpuPanel, _incidentPanel, _displayPanel };
            foreach (var p in panels)
                if (p != null) yield return p;
        }

        private void OnResolutionTimerTick(object? sender, EventArgs e)
        {
            // The in-VR sliders are dragged in canvas pixels, so rebuilding the
            // canvas mid-drag slides the slider out from under the pointer —
            // which moves the value, which rebuilds the canvas again. Keep
            // ticking until the gesture ends, then apply it once.
            if (_host?.IsPointerGestureActive == true) return;

            _resolutionTimer.Stop();
            ApplyCanvasResolution();
            UpdateEditValueLabels();
            SaveLayout();
        }

        /// <summary>Persist the current overlay layout (panel position, size, distance, DPI).</summary>
        private void SaveLayout()
        {
            var s = _vm.AppSettings;
            if (_host == null || s == null) return;
            try
            {
                if (_beltPanel != null)
                {
                    s.OverlayPanelX = _beltPanel.X;
                    s.OverlayPanelY = _beltPanel.Y;
                    s.OverlayPanelScale = SavedScaleOf(_beltPanel);
                    s.OverlayPanelOpacity = SavedOpacityOf(_beltPanel);
                }
                if (_racePanel != null)
                {
                    s.OverlayRacePanelX = _racePanel.X;
                    s.OverlayRacePanelY = _racePanel.Y;
                    s.OverlayRacePanelScale = SavedScaleOf(_racePanel);
                    s.OverlayRacePanelOpacity = SavedOpacityOf(_racePanel);
                    s.OverlayRaceColumnOrder = _racePanel.ColumnOrder;
                }
                if (_qualifyingPanel != null)
                {
                    s.OverlayQualifyingPanelX = _qualifyingPanel.X;
                    s.OverlayQualifyingPanelY = _qualifyingPanel.Y;
                    s.OverlayQualifyingPanelScale = SavedScaleOf(_qualifyingPanel);
                    s.OverlayQualifyingPanelOpacity = SavedOpacityOf(_qualifyingPanel);
                    s.OverlayQualifyingColumnOrder = _qualifyingPanel.ColumnOrder;
                }
                if (_standingsPanel != null)
                {
                    s.OverlayStandingsPanelX = _standingsPanel.X;
                    s.OverlayStandingsPanelY = _standingsPanel.Y;
                    s.OverlayStandingsPanelScale = SavedScaleOf(_standingsPanel);
                    s.OverlayStandingsPanelOpacity = SavedOpacityOf(_standingsPanel);
                    s.OverlayStandingsColumnOrder = _standingsPanel.ColumnOrder;
                    s.OverlayStandingsRows = _standingsPanel.MaxRows;
                }
                if (_hudSettingsPanel != null)
                {
                    s.OverlayHudSettingsPanelX = _hudSettingsPanel.X;
                    s.OverlayHudSettingsPanelY = _hudSettingsPanel.Y;
                    s.OverlayHudSettingsPanelScale = SavedScaleOf(_hudSettingsPanel);
                    s.OverlayHudSettingsPanelOpacity = SavedOpacityOf(_hudSettingsPanel);
                }
                // Driver tags and their colours live in DriverTags while the
                // overlay runs; push them back into the settings before saving.
                var tags = Services.Data.DriverTags.Instance;
                s.OverlayFriendColor = tags.FriendColor;
                s.OverlayRivalColor = tags.RivalColor;
                s.OverlayTagTint = tags.TintStrength;
                s.OverlayTagsInRelativeBoxes = tags.ShowInRelativeBoxes;
                if (_gazeFocus != null)
                {
                    s.OverlayGazeEffect = _gazeFocus.Effect.ToString();
                    s.OverlayGazeSource = _gazeFocus.Source.ToString();
                    s.OverlayGazeDim = _gazeFocus.DimAmount;
                    s.OverlayGazeGrow = _gazeFocus.GrowFactor;
                }
                if (_warningPanel != null)
                {
                    s.OverlayWarningPanelX = _warningPanel.X;
                    s.OverlayWarningPanelY = _warningPanel.Y;
                    s.OverlayWarningPanelScale = SavedScaleOf(_warningPanel);
                    s.OverlayWarningPanelOpacity = SavedOpacityOf(_warningPanel);
                }
                if (_nearbyPanel != null)
                {
                    s.OverlayNearbyPanelX = _nearbyPanel.X;
                    s.OverlayNearbyPanelY = _nearbyPanel.Y;
                    s.OverlayNearbyPanelScale = SavedScaleOf(_nearbyPanel);
                    s.OverlayNearbyPanelOpacity = SavedOpacityOf(_nearbyPanel);
                    s.OverlayNearbyWidth = _nearbyPanel.BoxWidth;
                }
                if (_slowCarPanel != null)
                {
                    s.OverlaySlowCarPanelX = _slowCarPanel.X;
                    s.OverlaySlowCarPanelY = _slowCarPanel.Y;
                    s.OverlaySlowCarPanelScale = SavedScaleOf(_slowCarPanel);
                    s.OverlaySlowCarPanelOpacity = SavedOpacityOf(_slowCarPanel);
                }
                if (_carBehindPanel != null)
                {
                    s.OverlayCarBehindPanelX = _carBehindPanel.X;
                    s.OverlayCarBehindPanelY = _carBehindPanel.Y;
                    s.OverlayCarBehindPanelScale = _carBehindPanel.Scale;
                    s.OverlayCarBehindPanelOpacity = _carBehindPanel.Opacity;
                }
                if (_youtubePanel != null)
                {
                    s.OverlayYouTubePanelX = _youtubePanel.X;
                    s.OverlayYouTubePanelY = _youtubePanel.Y;
                    s.OverlayYouTubePanelScale = SavedScaleOf(_youtubePanel);
                    s.OverlayYouTubePanelOpacity = SavedOpacityOf(_youtubePanel);
                }
                if (_gpuPanel != null)
                {
                    s.OverlayGpuPanelX = _gpuPanel.X;
                    s.OverlayGpuPanelY = _gpuPanel.Y;
                    s.OverlayGpuPanelScale = SavedScaleOf(_gpuPanel);
                    s.OverlayGpuPanelOpacity = SavedOpacityOf(_gpuPanel);
                }
                if (_incidentPanel != null)
                {
                    s.OverlayIncidentPanelX = _incidentPanel.X;
                    s.OverlayIncidentPanelY = _incidentPanel.Y;
                    s.OverlayIncidentPanelScale = SavedScaleOf(_incidentPanel);
                    s.OverlayIncidentPanelOpacity = SavedOpacityOf(_incidentPanel);
                }
                if (_displayPanel != null)
                {
                    s.OverlayDisplayPanelX = _displayPanel.X;
                    s.OverlayDisplayPanelY = _displayPanel.Y;
                    s.OverlayDisplayPanelScale = SavedScaleOf(_displayPanel);
                    s.OverlayDisplayPanelOpacity = SavedOpacityOf(_displayPanel);
                }
                s.OverlaySizeX = _host.DisplaySize.X;
                s.OverlaySizeY = _host.DisplaySize.Y;
                s.OverlayDistance = _host.Distance;
                s.OverlayDpi = _dpi;
                s.OverlayCursorCalSet = _host.CursorCalibrated;
                if (_host.CursorCalibrated)
                {
                    var cal = _host.CursorCalibration;
                    s.OverlayCursorCalScaleX = cal.ScaleX;
                    s.OverlayCursorCalOffsetX = cal.OffsetX;
                    s.OverlayCursorCalScaleY = cal.ScaleY;
                    s.OverlayCursorCalOffsetY = cal.OffsetY;
                }
                if (_originSet)
                {
                    s.OverlayOriginSet = true;
                    s.OverlayOriginX = _host.OriginPosition.X;
                    s.OverlayOriginY = _host.OriginPosition.Y;
                    s.OverlayOriginZ = _host.OriginPosition.Z;
                    s.OverlayOriginYaw = _host.OriginYaw;
                    s.OverlayOriginPitch = _host.OriginPitch;
                }
                _settingsSvc.Save(s);
            }
            catch (Exception ex)
            {
                Log("LAYOUT SAVE FAILED: " + ex.Message);
            }
        }

        // Fires on the UI thread (RenderFrame runs on the DispatcherTimer).
        private void OnDragCompleted(OverlayRenderTarget target)
        {
            SaveLayout();
            Log($"Panel moved to ({target.X}, {target.Y}) — saved.");
        }

        // Fires on the UI thread, once per -/+/reset click or grip release.
        private void OnScaleChanged(OverlayRenderTarget target)
        {
            _gazeFocus?.Recapture(target); // the new size is the user's baseline now
            SaveLayout();
            Log($"{target.Name} resized to {target.Scale * 100:0}% — saved.");
        }

        // Fires on the UI thread, once per opacity -/+/reset click.
        private void OnOpacityChanged(OverlayRenderTarget target)
        {
            _gazeFocus?.Recapture(target);
            SaveLayout();
            Log($"{target.Name} opacity set to {target.Opacity * 100:0}% — saved.");
        }

        private void OnTick(object? sender, EventArgs e)
        {
            if (_host == null) return;

            // Panels that only apply in the car or in the replay follow the
            // sim's state, which can change between any two ticks.
            _panelVisibility?.Apply();

            // Fade / grow whatever the headset is pointed at. Runs before
            // RenderFrame so this tick's frame already shows the change.
            _gazeFocus?.Update(_host.EditMode);

            try
            {
                _host.RenderFrame((float)_clock.Elapsed.TotalSeconds);
            }
            catch (Exception ex)
            {
                _timer.Stop();
                Log("RENDER FAILED: " + ex);
                StatusLabel.Text = "Render failed — see log.";
                return;
            }

            // Log the attach state once at startup and then only on changes.
            // (Don't key this off FramesPublished — with dirty-flag rendering
            // it stays constant while idle, so "== 1" would repeat every tick.)
            bool attached = _host.LayerAttached;
            if (attached != _lastAttached || !_statusLogged)
            {
                _statusLogged = true;
                _lastAttached = attached;
                Log(attached ? "Layer attached — overlay is live in VR."
                             : "Waiting for an OpenXR app (layer not attached yet).");
            }
            StatusLabel.Text = (attached ? "Layer attached — live in VR." : "Waiting for OpenXR app…")
                               + $"   Frames: {_host.FramesPublished}";

            // Mirror the composed canvas into the desktop preview window (the
            // readback is skipped when the canvas didn't change this tick).
            if (_preview != null)
            {
                try { _preview.UpdateFrame(_host); }
                catch (Exception ex) { Log("PREVIEW FAILED: " + ex.Message); _preview.Close(); }
            }
        }

        private void OnClosed(object? sender, EventArgs e) => Cleanup();

        private void Cleanup()
        {
            if (_closed) return;
            _closed = true;
            Stop();
        }

        /// <summary>Open/close the desktop preview of the overlay canvas (no VR needed).</summary>
        private void PreviewButton_Changed(object sender, RoutedEventArgs e)
        {
            if (PreviewButton.IsChecked == true)
            {
                if (_preview != null) return;
                _preview = new OverlayPreviewWindow { Owner = this };
                _preview.Closed += (_, _) =>
                {
                    _preview = null;
                    PreviewButton.IsChecked = false;
                };
                _preview.Show();
                Log("Preview window opened — mirrors the overlay canvas on the desktop.");
            }
            else
            {
                _preview?.Close();
            }
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                try { DragMove(); } catch { }
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

        // True once the user has recentered in this session; only then does
        // SaveLayout write the origin (a never-recentered layout stays unset).
        private bool _originSet;

        // Lets the recenter key/gamepad binding (MainWindow, SettingsWindow)
        // reach whichever overlay window is open without holding a reference.
        private static event Action? RecenterRequested;

        /// <summary>Recenter the open overlay, if any. False when no overlay window is open.</summary>
        public static bool RequestRecenter()
        {
            var handler = RecenterRequested;
            if (handler == null) return false;
            handler();
            return true;
        }

        private void RecenterButton_Click(object sender, RoutedEventArgs e) => Recenter();

        /// <summary>Center the overlay on where the headset is looking right now.</summary>
        private void Recenter()
        {
            if (_host == null) return;
            if (!_host.Recenter())
            {
                Log(_host.LayerAttached
                    ? "Recenter failed — no headset pose from the layer. Restart the VR game so it loads the updated layer."
                    : "Recenter needs a running OpenXR app — start the game, look straight ahead, then press Recenter.");
                return;
            }

            _originSet = true;
            SaveLayout();
            var p = _host.OriginPosition;
            Log($"Recentered on headset gaze: head at ({p.X:0.00}, {p.Y:0.00}, {p.Z:0.00}) m, " +
                $"heading {_host.OriginYaw * 180 / Math.PI:0}°, pitch {_host.OriginPitch * 180 / Math.PI:0}° — saved.");
            if (_host.CursorCalibrated)
                Log("The overlay moved — if the red cursor no longer sits on the game's cursor, run Edit > Calibrate mouse again.");
        }

        /// <summary>Start the two-click calibration that lines the red edit cursor up with the game's own cursor.</summary>
        private void CalibrateMouse_Click(object sender, RoutedEventArgs e)
        {
            if (_host == null) return;
            if (_host.IsCalibratingCursor)
            {
                _host.CancelCursorCalibration();
                return;
            }
            if (!_host.BeginCursorCalibration())
            {
                Log("Mouse calibration needs the VR game running (its window is what gets matched) and edit mode on.");
                return;
            }
            Log("Mouse calibration started — in VR: put the GAME'S mouse cursor on the green cross and left-click, twice. " +
                "Panels are hidden until it finishes. Press Calibrate mouse again to cancel.");
        }

        private void ResetMouseCalibration_Click(object sender, RoutedEventArgs e)
        {
            if (_host == null) return;
            _host.CancelCursorCalibration();
            _host.ResetCursorCalibration();
            SaveLayout();
            Log("Mouse calibration reset — the game window maps onto the whole overlay again.");
        }

        // Fires on the UI thread (RenderFrame runs on the DispatcherTimer).
        private void OnCursorCalibrationCompleted(bool applied)
        {
            if (applied)
            {
                SaveLayout();
                Log("Mouse calibrated — the red cursor now follows the game's cursor. Saved.");
            }
            else
            {
                Log("Mouse calibration cancelled or unusable (the two clicks were too close together) — previous mapping kept.");
            }
        }

        private bool _syncingEditUi;

        private void EditButton_Changed(object sender, RoutedEventArgs e)
        {
            if (_host == null) return;
            bool editing = EditButton.IsChecked == true;
            _host.EditMode = editing;
            EditPanel.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
            if (editing) SyncEditPanel();
            Log(editing ? "Edit mode ON — red border shown around overlay canvas."
                        : "Edit mode OFF.");
        }

        /// <summary>Populate the edit panel from the host's current state.</summary>
        private void SyncEditPanel()
        {
            if (_host == null) return;
            _syncingEditUi = true;
            SizeXSlider.Value = _host.DisplaySize.X;
            SizeYSlider.Value = _host.DisplaySize.Y;
            DistanceSlider.Value = _host.Distance;
            DpiSlider.Value = _dpi;
            _syncingEditUi = false;
            UpdateEditValueLabels();
        }

        // Labels show the host's real values: in depth-only mode they are exact
        // and can sit between (or beyond) the sliders' snap ticks.
        private void UpdateEditValueLabels()
        {
            if (_host == null) return;
            SizeXValue.Text = _host.DisplaySize.X.ToString("0.00");
            SizeYValue.Text = _host.DisplaySize.Y.ToString("0.00");
            DistanceValue.Text = _host.Distance.ToString("0.00");
            DpiValue.Text = _dpi.ToString("0");
            ResolutionValue.Text = $"{_host.CanvasWidth} × {_host.CanvasHeight} px";
        }

        /// <summary>One of the overlay display values adjustable in edit mode.</summary>
        private enum DisplaySetting { Width, Height, Distance, Dpi }

        private void DisplaySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_host == null || _syncingEditUi) return;
            ApplyDisplaySetting(
                sender == SizeXSlider ? DisplaySetting.Width
                : sender == SizeYSlider ? DisplaySetting.Height
                : sender == DpiSlider ? DisplaySetting.Dpi
                : DisplaySetting.Distance,
                e.NewValue);
        }

        /// <summary>
        /// Apply one display value — from this window's sliders or from the
        /// in-VR Display panel — then mirror the result back into this
        /// window's sliders, save it, and debounce the canvas rebuild.
        /// </summary>
        private void ApplyDisplaySetting(DisplaySetting setting, double value)
        {
            if (_host == null) return;

            bool resolutionMayChange = true;
            var size = _host.DisplaySize;
            switch (setting)
            {
                case DisplaySetting.Width:
                    _host.DisplaySize = new System.Numerics.Vector2(
                        Math.Max(MinDisplayMeters, (float)value), size.Y);
                    break;
                case DisplaySetting.Height:
                    _host.DisplaySize = new System.Numerics.Vector2(
                        size.X, Math.Max(MinDisplayMeters, (float)value));
                    break;
                case DisplaySetting.Dpi:
                    _dpi = value;
                    break;
                case DisplaySetting.Distance when DepthOnlyCheck.IsChecked == true && _host.Distance > 0:
                    // Depth only: scale the quad about the view origin, so it covers
                    // exactly the same angle (looks the same size, the mouse
                    // calibration stays valid) and only its stereo depth changes.
                    // DPI shrinks by the same factor so the canvas resolution — and
                    // with it the panel layout — is untouched.
                    float factor = (float)value / _host.Distance;
                    _host.Distance = (float)value;
                    _host.DisplaySize = size * factor;
                    _dpi /= factor;
                    resolutionMayChange = false;
                    break;
                default:
                    _host.Distance = (float)value;
                    break;
            }

            // Show what the host really ended up with: the sliders snap to
            // their ticks, a depth-only distance change moves three values at
            // once, and the in-VR panel writes here too.
            SyncEditPanel();
            SaveLayout();

            // The canvas resolution follows size × DPI; (re)start the debounce
            // so it is rebuilt once the slider settles.
            if (resolutionMayChange)
            {
                _resolutionTimer.Stop();
                _resolutionTimer.Start();
            }
        }

        // Log to the window and to %TEMP%\MonoXR\client.log (the native layer
        // logs separately to %TEMP%\MonoXR\layer.log).
        private void Log(string message)
        {
            string stamped = $"{DateTime.Now:HH:mm:ss.fff} {message}";
            LogBox.AppendText(stamped + Environment.NewLine);
            LogBox.ScrollToEnd();
            try
            {
                string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "MonoXR");
                System.IO.Directory.CreateDirectory(dir);
                using var fs = new System.IO.FileStream(
                    System.IO.Path.Combine(dir, "client.log"),
                    System.IO.FileMode.Append, System.IO.FileAccess.Write, System.IO.FileShare.ReadWrite);
                using var sw = new System.IO.StreamWriter(fs);
                sw.Write($"{DateTime.Now:HH:mm:ss.fff} [pid {Environment.ProcessId}] {message}{Environment.NewLine}");
            }
            catch { /* logging must never throw */ }
        }
    }
}
