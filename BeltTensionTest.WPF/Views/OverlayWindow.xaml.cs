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
        private const float MinDisplayMeters = 1f; // the overlay is never smaller than 1 m x 1 m
        private double _dpi = DefaultDpi;

        private MonoGameOverlayHost? _host;
        private readonly MainViewModel _vm;
        private readonly Services.SettingsService _settingsSvc = new();
        private BeltSettingsOverlay? _beltPanel;
        private RelativeOverlay? _relativePanel;
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
        private Services.RecenterBinding? _recenterBinding;
        private Services.PanelVisibility? _panelVisibility;

        // Cars the relative box shows (player included) until it is dragged
        // taller or shorter in edit mode.
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

        /// <summary>True while the overlay is running (Preferences > OpenXR > Enable OpenXR overlay).</summary>
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

        /// <summary>
        /// Rebuild the running overlay if Preferences → Features changed which panels
        /// should exist (the belt panel is only created while that feature is on).
        /// </summary>
        public void RefreshFeatures()
        {
            if (_closed || _host == null) return;
            if (_gazeFocus != null)
            {
                _gazeFocus.Enabled = _vm.AppSettings?.EnableEyeTracking ?? true;
                if (Enum.TryParse(_vm.AppSettings?.OverlayGazeSource, out Services.GazeSource gazeSource))
                    _gazeFocus.Source = gazeSource;
            }
            ApplyHeadTracking();
            SetColorBlind(_vm.AppSettings?.ColorBlindMode);
            if ((_beltPanel != null) == _vm.BeltFeatureEnabled) return;
            Stop();
            Start();
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
                ApplyHeadTracking();
                SetColorBlind(_vm.AppSettings?.ColorBlindMode);
                s_active = this;

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
                ApplySavedPanelStates();
                _host.StateChanged += OnPanelStateChanged;
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

                // When each panel applies (any mix of practice / qualifying /
                // race / replay, or always), chosen with its edit-mode Show chips.
                _panelVisibility = new Services.PanelVisibility(
                    _vm.AppSettings?.OverlayPanelShowModes ?? new Dictionary<string, string>(),
                    AllPanels, () => _host?.InvalidateCanvas());
                _panelVisibility.Changed += SaveLayout;
                _host.ShowChips = _panelVisibility.ChipsFor;
                _host.ShowChipClicked += _panelVisibility.Toggle;
                _panelVisibility.EditViewIndex = _editView;
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
            if (ReferenceEquals(s_active, this)) s_active = null;
            _timer.Stop();
            _resolutionTimer.Stop();
            EditButton.IsChecked = false;
            _preview?.Close();
            _toggleBindings?.Dispose();
            _toggleBindings = null;
            _recenterBinding?.Dispose();
            _recenterBinding = null;
            _panelVisibility = null;
            _gazeFocus = null;
            _beltPanel = null; _relativePanel = null; _standingsPanel = null;
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
                StatusLabel.Text = "OpenXR overlay is off — turn on Settings → Preferences → OpenXR → Enable OpenXR overlay.";
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
            var surge = Services.Overlays.ColorBlind.Map(Services.Overlays.CbRole.Info,  new Microsoft.Xna.Framework.Color(0x64, 0x96, 0xFF));
            var sway  = Services.Overlays.ColorBlind.Map(Services.Overlays.CbRole.Good,  new Microsoft.Xna.Framework.Color(0x50, 0xC8, 0x78));
            var heave = Services.Overlays.ColorBlind.Map(Services.Overlays.CbRole.Amber, new Microsoft.Xna.Framework.Color(0xFF, 0x80, 0x40));
            var power = Services.Overlays.ColorBlind.Map(Services.Overlays.CbRole.Bad,   new Microsoft.Xna.Framework.Color(0xDC, 0x3C, 0x3C));
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

            // Not created at all when the Belt Tentioner feature is off in Preferences → Features.
            if (_vm.BeltFeatureEnabled)
                _beltPanel = _host.AddRenderTarget(new BeltSettingsOverlay(
                    _host.GraphicsDevice, panelWidth, panelHeight, x, y, groups));

            // Relative box (ported from IrachingHud's RelativeBox): one panel
            // that shows the race or the practice/qualifying layout depending
            // on the session. It keeps the old Race box's saved position and
            // size; the first time it inherits the old combined "Main" panel's.
            int boxX = 16, boxY = 16;
            if (s != null && s.OverlayMainPanelX >= 0 && s.OverlayMainPanelY >= 0)
                (boxX, boxY) = (s.OverlayMainPanelX, s.OverlayMainPanelY);

            int raceX = boxX, raceY = boxY;
            if (s != null && s.OverlayRacePanelX >= 0 && s.OverlayRacePanelY >= 0)
                (raceX, raceY) = (s.OverlayRacePanelX, s.OverlayRacePanelY);
            _relativePanel = _host.AddRenderTarget(new RelativeOverlay(_host.GraphicsDevice,
                Math.Min(raceX, Math.Max(0, _host.CanvasWidth - 100)),
                Math.Min(raceY, Math.Max(0, _host.CanvasHeight - 100)),
                s?.OverlayRaceRows > 0 ? s.OverlayRaceRows : StandingsCarCount));
            if (!string.IsNullOrEmpty(s?.OverlayRaceColumnOrder))
                _relativePanel.ColumnOrder = s.OverlayRaceColumnOrder;
            if (s != null)
            {
                _relativePanel.HiddenColumns = s.OverlayRaceHiddenColumns;
                if (Enum.TryParse(s.OverlayRaceNameFormat, out RelativeNameFormat relFmt))
                    _relativePanel.NameFormat = relFmt;
            }
            _relativePanel.ColumnOrderChanged += SaveLayout; // persist header drags and menu changes like panel moves


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
            if (s != null)
            {
                _standingsPanel.HiddenColumns = s.OverlayStandingsHiddenColumns;
                if (Enum.TryParse(s.OverlayStandingsNameFormat, out RelativeNameFormat standFmt))
                    _standingsPanel.NameFormat = standFmt;
            }
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
                _gazeFocus.FadedOpacity = (float)s.OverlayGazeFadedOpacity;
                _gazeFocus.GrowFactor = (float)s.OverlayGazeGrow;
                _gazeFocus.Enabled = s.EnableEyeTracking;
                _gazeFocus.SetEnabledPanels(s.OverlayGazeEnabledPanels);
            }

            // Edit-mode eye button on each panel: whether it reacts to gaze.
            _host.GazeButtonState = panel => _gazeFocus?.IsPanelEnabled(panel) ?? true;
            _host.GazeToggleRequested += panel =>
            {
                if (_gazeFocus == null) return;
                _gazeFocus.TogglePanel(panel);
                SaveLayout();
                Log($"{panel.Name} {(_gazeFocus.IsPanelEnabled(panel) ? "now reacts to" : "now ignores")} gaze — saved.");
            };

            // HUD settings (tag colours, standings size). Not edit-mode only:
            // these are changed while sitting in the pits. It starts collapsed
            // to a pill near the bottom-left, which opens it when clicked.
            _recenterBinding = new Services.RecenterBinding(s?.OverlayRecenterButton ?? string.Empty, Recenter);
            var hudOptions = new HudOptions
            {
                Recenter = _recenterBinding,
                GetEyeTracking = () => _vm.AppSettings?.EnableEyeTracking ?? true,
                SetEyeTracking = on =>
                {
                    if (_vm.AppSettings != null) _vm.AppSettings.EnableEyeTracking = on;
                    if (_gazeFocus != null) _gazeFocus.Enabled = on;
                },
                GetColorBlindIndex = () => Math.Max(0, Array.IndexOf(ColorBlindModeNames, _vm.AppSettings?.ColorBlindMode)),
                SetColorBlindIndex = i =>
                {
                    string mode = ColorBlindModeNames[Math.Clamp(i, 0, ColorBlindModeNames.Length - 1)];
                    if (_vm.AppSettings != null) _vm.AppSettings.ColorBlindMode = mode;
                    SetColorBlind(mode);
                },
            };

            int hudX = 16, hudY = Math.Max(0, _host.CanvasHeight - 400); // above the GPU panel's corner
            if (s != null && s.OverlayHudSettingsPanelX >= 0 && s.OverlayHudSettingsPanelY >= 0)
            {
                hudX = Math.Min(s.OverlayHudSettingsPanelX, Math.Max(0, _host.CanvasWidth - 100));
                hudY = Math.Min(s.OverlayHudSettingsPanelY, Math.Max(0, _host.CanvasHeight - 100));
            }
            _hudSettingsPanel = _host.AddRenderTarget(new HudSettingsOverlay(
                _host.GraphicsDevice, hudX, hudY, hudOptions, _gazeFocus, SaveLayout));
            // A tab on the left edge, edit mode only: opens the panel, which the host places.
            _hudSettingsPanel.EditModeOnly = true;
            _hudSettingsPanel.DockSide = MonoXR.Client.OverlayDockSide.Left;

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
                new BeltSettingRow("Curve (%)",    () => host.CurveAmount * 100f, v => ApplyDisplaySetting(DisplaySetting.Curve, v),  10f, 150f,  5f,   "0"),
                new BeltSettingRow("Center (%)",   () => host.TripleCenterPercent, v => ApplyDisplaySetting(DisplaySetting.TripleCenter, v), 20f, 90f, 1f, "0"),
                new BeltSettingRow("Side angle",   () => host.TripleSideAngle,     v => ApplyDisplaySetting(DisplaySetting.TripleAngle, v),  0f,  90f, 1f, "0"),
                new BeltSettingRow("Top height (m)", () => host.TripleTopHeight, v => ApplyDisplaySetting(DisplaySetting.TopHeight, v), 0.2f, 4f, 0.05f, "0.00"),
                new BeltSettingRow("Top tilt",     () => host.TripleTopAngle,  v => ApplyDisplaySetting(DisplaySetting.TopAngle, v),  0f,  90f, 1f, "0"),
                new BeltSettingRow("X offset (m)", () => host.OffsetX,       v => ApplyDisplaySetting(DisplaySetting.OffsetX, v),  -5f,   5f,   0.05f, "0.00"),
                new BeltSettingRow("Y offset (m)", () => host.OffsetY,       v => ApplyDisplaySetting(DisplaySetting.OffsetY, v),  -5f,   5f,   0.05f, "0.00"),
                new BeltSettingRow("DPI (px/m)",   () => (float)_dpi,        v => ApplyDisplaySetting(DisplaySetting.Dpi, v),      128f, 2048f, 16f,   "0"),
            };
            var display = new DisplaySettingsOverlay(_host.GraphicsDevice, 0, 0, displayRows,
                () => $"Resolution  {host.CanvasWidth} x {host.CanvasHeight} px     Sim: {Services.PanelVisibility.Describe(Services.PanelVisibility.CurrentSimState())} ({Services.PanelVisibility.Diagnostics()})",
                Services.PanelVisibility.EditViewOptions, () => _editView, SetEditView,
                () => CurrentShape, SetShape);

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
            // A tab on the right edge, edit mode only: opens the panel, which the host places.
            display.DockSide = MonoXR.Client.OverlayDockSide.Right;
            _displayPanel = _host.AddRenderTarget(display);

            // Restore each panel's saved size (edit-mode -/+ buttons / corner
            // grip), then pull anything the new size pushed off the canvas back on.
            if (s != null)
            {
                RestoreScale(_beltPanel, s.OverlayPanelScale);
                double boxScale = s.OverlayMainPanelScale;
                RestoreScale(_relativePanel, s.OverlayRacePanelScale > 0 ? s.OverlayRacePanelScale : boxScale);
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
                RestoreOpacity(_relativePanel, s.OverlayRacePanelOpacity);
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
                    _host.DisplaySize = new System.Numerics.Vector2(
                        Math.Max(MinDisplayMeters, (float)s.OverlaySizeX), Math.Max(MinDisplayMeters, (float)s.OverlaySizeY));
                if (s.OverlayDistance > 0)
                    _host.Distance = (float)s.OverlayDistance;
                _host.CurveAmount = (float)s.OverlayCurveAmount;
                _host.Curved = s.OverlayCurved;
                _host.TripleCenterPercent = (float)s.OverlayTripleCenter;
                _host.TripleSideAngle = (float)s.OverlayTripleAngle;
                _host.TripleTopHeight = (float)s.OverlayTopHeight;
                _host.TripleTopAngle = (float)s.OverlayTopAngle;
                _host.TripleTop = s.OverlayTripleTop;
                _host.Triple = s.OverlayTriple;
                _host.OffsetX = (float)s.OverlayOffsetX;
                _host.OffsetY = (float)s.OverlayOffsetY;
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
            double rawW = _host.DisplaySize.X * _dpi, rawH = (_host.DisplaySize.Y + _host.CanvasExtraHeightMeters) * _dpi;
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

        /// <summary>
        /// Pull panels fully back onto the canvas after it shrank (also at startup):
        /// one that is bigger than the canvas is made smaller first. The docked
        /// settings panels are placed by the host.
        /// </summary>
        private void ClampPanelsToCanvas()
        {
            if (_host == null) return;
            int cw = _host.CanvasWidth, ch = _host.CanvasHeight;
            foreach (var p in AllPanels())
            {
                if (p.IsDocked) continue;
                if (p.DisplayWidth > cw || p.DisplayHeight > ch)
                    p.Scale *= Math.Min(cw / (float)p.DisplayWidth, ch / (float)p.DisplayHeight);
                p.X = Math.Clamp(p.X, 0, Math.Max(0, cw - p.DisplayWidth));
                p.Y = Math.Clamp(p.Y, 0, Math.Max(0, ch - p.DisplayHeight));
            }
        }

        /// <summary>Every panel that exists (the YouTube one only when enabled).</summary>
        private IEnumerable<OverlayRenderTarget> AllPanels()
        {
            var panels = new OverlayRenderTarget?[]
                { _beltPanel, _relativePanel, _standingsPanel, _hudSettingsPanel, _warningPanel,
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
                if (_relativePanel != null)
                {
                    s.OverlayRacePanelX = _relativePanel.X;
                    s.OverlayRacePanelY = _relativePanel.Y;
                    s.OverlayRacePanelScale = SavedScaleOf(_relativePanel);
                    s.OverlayRacePanelOpacity = SavedOpacityOf(_relativePanel);
                    s.OverlayRaceColumnOrder = _relativePanel.ColumnOrder;
                    s.OverlayRaceRows = _relativePanel.RowCapacity;
                    s.OverlayRaceHiddenColumns = _relativePanel.HiddenColumns;
                    s.OverlayRaceNameFormat = _relativePanel.NameFormat.ToString();
                }
                if (_standingsPanel != null)
                {
                    s.OverlayStandingsPanelX = _standingsPanel.X;
                    s.OverlayStandingsPanelY = _standingsPanel.Y;
                    s.OverlayStandingsPanelScale = SavedScaleOf(_standingsPanel);
                    s.OverlayStandingsPanelOpacity = SavedOpacityOf(_standingsPanel);
                    s.OverlayStandingsColumnOrder = _standingsPanel.ColumnOrder;
                    s.OverlayStandingsHiddenColumns = _standingsPanel.HiddenColumns;
                    s.OverlayStandingsNameFormat = _standingsPanel.NameFormat.ToString();
                    s.OverlayStandingsRows = _standingsPanel.MaxRows;
                }
                if (_hudSettingsPanel != null)
                {
                    s.OverlayHudSettingsPanelX = _hudSettingsPanel.X;
                    s.OverlayHudSettingsPanelY = _hudSettingsPanel.Y;
                    s.OverlayHudSettingsPanelScale = SavedScaleOf(_hudSettingsPanel);
                    s.OverlayHudSettingsPanelOpacity = SavedOpacityOf(_hudSettingsPanel);
                }
                if (_recenterBinding != null) s.OverlayRecenterButton = _recenterBinding.Binding;
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
                    s.OverlayGazeFadedOpacity = _gazeFocus.FadedOpacity;
                    s.OverlayGazeGrow = _gazeFocus.GrowFactor;
                    s.OverlayGazeEnabledPanels = new List<string>(_gazeFocus.EnabledPanels);
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
                    s.OverlayCarBehindPanelScale = SavedScaleOf(_carBehindPanel);
                    s.OverlayCarBehindPanelOpacity = SavedOpacityOf(_carBehindPanel);
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
                s.OverlayCurved = _host.CurvedRequested; // keep the choice even while the runtime cannot show it
                s.OverlayCurveAmount = _host.CurveAmount;
                s.OverlayTriple = _host.Triple;
                s.OverlayTripleTop = _host.TripleTop;
                s.OverlayTopHeight = _host.TripleTopHeight;
                s.OverlayTopAngle = _host.TripleTopAngle;
                s.OverlayTripleCenter = _host.TripleCenterPercent;
                s.OverlayTripleAngle = _host.TripleSideAngle;
                s.OverlayOffsetX = _host.OffsetX;
                s.OverlayOffsetY = _host.OffsetY;
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
        /// <summary>Put each panel back in the hidden / collapsed / shown state it was left in.</summary>
        private void ApplySavedPanelStates()
        {
            var states = _vm.AppSettings?.OverlayPanelStates;
            if (states == null) return;
            foreach (var panel in AllPanels())
                if (states.TryGetValue(panel.Name, out var saved) &&
                    Enum.TryParse(saved, out MonoXR.Client.OverlayTargetState state))
                    panel.State = state;
        }

        // Hide / Collapse / Show pressed in edit mode: remember it. (Toggling a
        // panel with a bound controller button is a live toggle and is not kept.)
        private void OnPanelStateChanged(OverlayRenderTarget target)
        {
            var settings = _vm.AppSettings;
            if (settings == null) return;
            settings.OverlayPanelStates ??= new Dictionary<string, string>();
            settings.OverlayPanelStates[target.Name] = target.State.ToString();
            SaveLayout();
            Log($"{target.Name} is now {target.State.ToString().ToLowerInvariant()} — saved.");
        }

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
            UpdateSimStateLabel();
            LogSimStateChange();

            // Fade / grow whatever the headset is pointed at. Runs before
            // RenderFrame so this tick's frame already shows the change.
            _gazeFocus?.Update(_host.EditMode);
            UpdateCurvedSupport();

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
                _preview.SetEmulateIndex(_editView);
                _preview.EmulateChanged += SetEditView;
                _preview.Closed += (_, _) =>
                {
                    _preview = null;
                    PreviewButton.IsChecked = false;
                    // Back to the sim's real state.
                    if (_panelVisibility != null) { _panelVisibility.Emulate = false; _panelVisibility.Apply(); }
                };
                if (_panelVisibility != null) { _panelVisibility.Emulate = true; _panelVisibility.Apply(); }
                _preview.Show();
                Log("Preview window opened — mirrors the overlay canvas on the desktop. Use Emulate to preview the In car, Practice, Qualifying, Race or Replay state.");
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

        private static OverlayWindow? s_active;

        /// <summary>The headset position (OpenXR LOCAL space, meters) the layer last reported; false when no game is running with the layer.</summary>
        public static bool TryGetHeadPosition(out System.Numerics.Vector3 position)
        {
            position = default;
            var host = s_active?._host;
            return host != null && host.TryGetHeadPose(out position, out _);
        }

        /// <summary>Hand the saved head-tracking filter settings (Preferences > Head Tracking) to the layer.</summary>
        /// <summary>Show a colour-blind mode on the open overlay right away, without saving it (Preferences does this as its choice changes).</summary>
        public static void PreviewColorBlind(string? mode) => s_active?.SetColorBlind(mode);

        /// <summary>Go back to the saved colour-blind mode (Preferences closed).</summary>
        public static void ReapplyColorBlind() => s_active?.SetColorBlind(s_active._vm.AppSettings?.ColorBlindMode);

        // The panels read their colours through ColorBlind each time they draw, so
        // switching the mode only needs them redrawn.
        private void SetColorBlind(string? mode)
        {
            var parsed = Services.Overlays.ColorBlind.Parse(mode);
            if (parsed == Services.Overlays.ColorBlind.Mode && _host != null) return;
            Services.Overlays.ColorBlind.Mode = parsed;
            foreach (var p in AllPanels()) p.Invalidate();
            _host?.InvalidateCanvas();
        }

        /// <summary>Apply head-tracking settings to the open overlay right away, without saving them (Preferences does this as its controls change).</summary>
        public static void PreviewHeadTracking(HeadTrackingSettings settings)
        {
            if (s_active?._host is { } host) host.HeadTracking = settings;
        }

        /// <summary>Go back to the saved head-tracking settings (Preferences closed).</summary>
        public static void ReapplyHeadTracking() => s_active?.ApplyHeadTracking();

        private void ApplyHeadTracking()
        {
            var s = _vm.AppSettings;
            if (_host == null || s == null) return;
            _host.HeadTracking = new HeadTrackingSettings
            {
                Smooth = s.HeadSmoothEnabled,
                Smoothing = (float)s.HeadSmoothAmount,
                MinMove = s.HeadMinMoveEnabled,
                MinMovePositionMm = (float)s.HeadMinMovePositionMm,
                MinMoveRotationDeg = (float)s.HeadMinMoveRotationDeg,
                LockHeadPosition = s.HeadLockEnabled,
                LockPosition = new System.Numerics.Vector3((float)s.HeadLockX, (float)s.HeadLockY, (float)s.HeadLockZ),
            };
        }

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

        // Which state edit mode is arranging the layout for (index into
        // PanelVisibility.EditViewOptions): 0 follows the sim, then in car, practice, qualifying, race, replay.
        private int _editView;

        /// <summary>Choose the state to arrange the layout for (this window's box or the in-VR Display panel).</summary>
        private void SetEditView(int index)
        {
            index = Math.Clamp(index, 0, Services.PanelVisibility.EditViewOptions.Length - 1);
            if (_panelVisibility != null) _panelVisibility.EditViewIndex = index;
            if (index == _editView) return;

            _editView = index;
            _syncingEditUi = true;
            EditViewBox.SelectedIndex = index;
            _syncingEditUi = false;
            _preview?.SetEmulateIndex(index);
            _panelVisibility?.Apply(); // the preview emulates this state
            _host?.InvalidateCanvas();
            Log($"Editing / emulating: {Services.PanelVisibility.EditViewOptions[index]} — panels that only show in the other state are hidden, in edit mode too.");
        }

        // Whether the runtime has cylinder layers is only known once a game is
        // attached; until the layer says no, Curved stays selectable.
        private bool? _curvedSupportShown = true;
        private object? _shapeBoxTip;

        private void UpdateCurvedSupport()
        {
            if (_host == null) return;
            bool? supported = _host.CurvedSupported;
            if (supported == _curvedSupportShown) return;
            _curvedSupportShown = supported;
            bool allow = supported != false;
            _shapeBoxTip ??= ShapeBox.ToolTip;
            ((System.Windows.Controls.ComboBoxItem)ShapeBox.Items[1]).IsEnabled = allow;
            CurveSlider.IsEnabled = allow;
            ShapeBox.ToolTip = allow ? _shapeBoxTip
                : "Curved needs cylinder layers, and the headset's OpenXR runtime (SteamVR has none) doesn't have them.";
            if (!allow) Log("Curved overlay is off: the headset's OpenXR runtime has no cylinder layers.");
            SyncEditPanel();
        }

        private void ShapeBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (_host == null || _syncingEditUi || ShapeBox.SelectedIndex < 0) return;
            SetShape(ShapeBox.SelectedIndex);
        }

        /// <summary>0 flat, 1 curved, 2 triple: what the overlay currently is.</summary>
        private int CurrentShape => _host == null ? 0 : _host.Triple ? (_host.TripleTop ? 3 : 2) : _host.Curved ? 1 : 0;

        private static readonly string[] ColorBlindModeNames = { "Off", "Deuteranopia", "Tritanomaly" };
        private static readonly string[] ShapeKeys = { "Flat", "Curved", "Triple", "TripleTop" };

        /// <summary>Each live panel with the settings prefix its position, scale and opacity are saved under.</summary>
        private (string Prefix, OverlayRenderTarget? Panel)[] PanelPrefixes() => new (string, OverlayRenderTarget?)[]
        {
            ("OverlayPanel", _beltPanel), ("OverlayRacePanel", _relativePanel), ("OverlayStandingsPanel", _standingsPanel),
            ("OverlayHudSettingsPanel", _hudSettingsPanel), ("OverlayWarningPanel", _warningPanel),
            ("OverlayNearbyPanel", _nearbyPanel), ("OverlaySlowCarPanel", _slowCarPanel),
            ("OverlayCarBehindPanel", _carBehindPanel), ("OverlayYouTubePanel", _youtubePanel),
            ("OverlayGpuPanel", _gpuPanel), ("OverlayIncidentPanel", _incidentPanel), ("OverlayDisplayPanel", _displayPanel),
        };

        /// <summary>Move the live panels to the positions, sizes and row counts in the settings.</summary>
        private void ApplyPanelLayoutFromSettings()
        {
            var s = _vm.AppSettings;
            if (s == null) return;
            var props = typeof(Models.AppSettings);
            double Get(string name) => Convert.ToDouble(props.GetProperty(name)?.GetValue(s) ?? 0);

            foreach (var (prefix, panel) in PanelPrefixes())
            {
                if (panel == null || panel.IsDocked) continue;
                if (Get(prefix + "X") >= 0 && Get(prefix + "Y") >= 0)
                {
                    panel.X = (int)Get(prefix + "X");
                    panel.Y = (int)Get(prefix + "Y");
                }
                RestoreScale(panel, Get(prefix + "Scale"));
                RestoreOpacity(panel, Get(prefix + "Opacity"));
            }
            if (_relativePanel != null && s.OverlayRaceRows > 0) _relativePanel.RowCapacity = s.OverlayRaceRows;
            if (_standingsPanel != null && s.OverlayStandingsRows > 0) _standingsPanel.MaxRows = s.OverlayStandingsRows;
            ClampPanelsToCanvas();
            _host?.InvalidateCanvas();
        }

        /// <summary>Flat, curved or triple overlay, from this window's box or the in-VR Display panel.</summary>
        private void SetShape(int shape)
        {
            if (_host == null || CurrentShape == shape) return;
            if (shape == 1 && _host.CurvedSupported == false)
            {
                Log("Curved overlay is not available: the headset's OpenXR runtime has no cylinder layers.");
                SyncEditPanel();
                return;
            }

            // Each shape keeps its own panel layout: file the current one under
            // the shape being left, and bring back the one saved for the new
            // shape (a shape never used before keeps the current layout).
            SaveLayout();
            bool restored = false;
            var settings = _vm.AppSettings;
            if (settings != null)
            {
                settings.OverlayShapeLayouts[ShapeKeys[CurrentShape]] = Services.OverlayLayoutService.SnapshotPanels(settings);
                if (settings.OverlayShapeLayouts.TryGetValue(ShapeKeys[shape], out var saved))
                {
                    Services.OverlayLayoutService.RestorePanels(settings, saved);
                    restored = true;
                }
            }

            _host.Triple = shape >= 2;
            _host.TripleTop = shape == 3;
            _host.Curved = shape == 1;
            if (restored) ApplyPanelLayoutFromSettings();
            SyncEditPanel();
            SaveLayout();
            // The canvas gains or loses the top screen's band, so it gets rebuilt.
            _resolutionTimer.Stop();
            _resolutionTimer.Start();
            Log(shape == 3 ? "Overlay split into three screens with a fourth above the middle one."
              : shape == 2 ? "Overlay split into three screens."
              : shape == 1 ? "Overlay curved around you."
              : "Overlay flat.");
        }

        private void EditViewBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (_syncingEditUi || EditViewBox == null || EditViewBox.SelectedIndex < 0) return;
            SetEditView(EditViewBox.SelectedIndex);
        }

        // Live sim state next to the "editing for" choice, so it is clear which
        // layout the panels currently follow.
        private string _lastSimStateLog = string.Empty;

        // One log line whenever the detected state or its raw readings change,
        // so a wrong detection can be read back from the log.
        private void LogSimStateChange()
        {
            var svc = Services.IracingService.Instance;
            string key = $"{Services.PanelVisibility.CurrentSimState()}|{svc.SessionType}|{svc.IsReplayPlaying}|{svc.InReplay}";
            if (key == _lastSimStateLog) return;
            _lastSimStateLog = key;
            Log($"Sim state: {Services.PanelVisibility.Describe(Services.PanelVisibility.CurrentSimState())} ({Services.PanelVisibility.Diagnostics()})");
        }

        private void UpdateSimStateLabel()
        {
            if (EditPanel.Visibility != Visibility.Visible) return;
            SimStateValue.Text = Services.PanelVisibility.Describe(Services.PanelVisibility.CurrentSimState());
        }

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
            EditViewBox.SelectedIndex = _editView;
            SizeXSlider.Value = _host.DisplaySize.X;
            SizeYSlider.Value = _host.DisplaySize.Y;
            DistanceSlider.Value = _host.Distance;
            ShapeBox.SelectedIndex = CurrentShape;
            CurveSlider.Value = _host.CurveAmount * 100;
            TripleCenterSlider.Value = _host.TripleCenterPercent;
            TripleAngleSlider.Value = _host.TripleSideAngle;
            TripleCenterSlider.IsEnabled = TripleAngleSlider.IsEnabled = _host.Triple;
            TopHeightSlider.Value = _host.TripleTopHeight;
            TopAngleSlider.Value = _host.TripleTopAngle;
            TopHeightSlider.IsEnabled = TopAngleSlider.IsEnabled = _host.Triple && _host.TripleTop;
            // Only the settings of the selected shape are shown.
            CurveLabel.Visibility = CurvePanel.Visibility = _host.Curved && !_host.Triple ? Visibility.Visible : Visibility.Collapsed;
            TripleLabel.Visibility = TriplePanel.Visibility = _host.Triple ? Visibility.Visible : Visibility.Collapsed;
            TopLabel.Visibility = TopPanel.Visibility = _host.Triple && _host.TripleTop ? Visibility.Visible : Visibility.Collapsed;
            OffsetXSlider.Value = _host.OffsetX;
            OffsetYSlider.Value = _host.OffsetY;
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
            OffsetXValue.Text = _host.OffsetX.ToString("0.00");
            CurveValue.Text = (_host.CurveAmount * 100).ToString("0") + "%";
            TripleCenterValue.Text = _host.TripleCenterPercent.ToString("0") + "%";
            TripleAngleValue.Text = _host.TripleSideAngle.ToString("0") + "°";
            TopHeightValue.Text = _host.TripleTopHeight.ToString("0.00");
            TopAngleValue.Text = _host.TripleTopAngle.ToString("0") + "°";
            OffsetYValue.Text = _host.OffsetY.ToString("0.00");
            DpiValue.Text = _dpi.ToString("0");
            ResolutionValue.Text = $"{_host.CanvasWidth} × {_host.CanvasHeight} px";
        }

        /// <summary>One of the overlay display values adjustable in edit mode.</summary>
        private enum DisplaySetting { Width, Height, Distance, Dpi, OffsetX, OffsetY, Curve, TripleCenter, TripleAngle, TopHeight, TopAngle }

        private void DisplaySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_host == null || _syncingEditUi) return;
            ApplyDisplaySetting(
                sender == SizeXSlider ? DisplaySetting.Width
                : sender == SizeYSlider ? DisplaySetting.Height
                : sender == DpiSlider ? DisplaySetting.Dpi
                : sender == CurveSlider ? DisplaySetting.Curve
                : sender == TripleCenterSlider ? DisplaySetting.TripleCenter
                : sender == TripleAngleSlider ? DisplaySetting.TripleAngle
                : sender == TopHeightSlider ? DisplaySetting.TopHeight
                : sender == TopAngleSlider ? DisplaySetting.TopAngle
                : sender == OffsetXSlider ? DisplaySetting.OffsetX
                : sender == OffsetYSlider ? DisplaySetting.OffsetY
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
                case DisplaySetting.Curve:
                    _host.CurveAmount = (float)value / 100f; // the slider and in-VR row are in percent
                    resolutionMayChange = false;
                    break;
                case DisplaySetting.TripleCenter:
                    _host.TripleCenterPercent = (float)value;
                    resolutionMayChange = false;
                    break;
                case DisplaySetting.TripleAngle:
                    _host.TripleSideAngle = (float)value;
                    resolutionMayChange = false;
                    break;
                case DisplaySetting.TopHeight:
                    _host.TripleTopHeight = (float)value; // changes the canvas height too
                    break;
                case DisplaySetting.TopAngle:
                    _host.TripleTopAngle = (float)value;
                    resolutionMayChange = false;
                    break;
                case DisplaySetting.OffsetX:
                    _host.OffsetX = (float)value;
                    resolutionMayChange = false;
                    break;
                case DisplaySetting.OffsetY:
                    _host.OffsetY = (float)value;
                    resolutionMayChange = false;
                    break;
                case DisplaySetting.Distance when DepthOnlyCheck.IsChecked == true && _host.Distance > 0:
                    // Depth only: scale the quad about the view origin, so it covers
                    // exactly the same angle (looks the same size, the mouse
                    // calibration stays valid) and only its stereo depth changes.
                    // DPI shrinks by the same factor so the canvas resolution — and
                    // with it the panel layout — is untouched.
                    float factor = (float)value / _host.Distance;
                    factor = Math.Max(factor, MinDisplayMeters / Math.Min(size.X, size.Y)); // never below the minimum size
                    _host.Distance = _host.Distance * factor;
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
