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
        private const int MinCanvasSize = 16, MaxCanvasSize = 8192;
        private double _dpi = DefaultDpi;

        private MonoGameOverlayHost? _host;
        private readonly MainViewModel _vm;
        private readonly Services.SettingsService _settingsSvc = new();
        private BeltSettingsOverlay? _beltPanel;
        private MainOverlay? _mainPanel;
        private WarningOverlay? _warningPanel;
        private NearbyCarsOverlay? _nearbyPanel;
        private SlowCarOverlay? _slowCarPanel;
        private YouTubeOverlay? _youtubePanel;
        private OverlayPreviewWindow? _preview;

        // Cars shown in the "Main" standings panel (player included) — change
        // this to make the panel taller/shorter.
        private const int MainOverlayCarCount = 7;

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

            try
            {
                Log("Creating MonoGame overlay host...");
                _host = new MonoGameOverlayHost(CanvasXSize, CanvasYSize);
                Log("Host ready: MonoGame device up, overlay published (World, 3m ahead, 2.5m).");

                ApplySavedLayout();
                SetupRenderTargets();
                _host.DragCompleted += OnDragCompleted;
                _host.ScaleChanged += OnScaleChanged;
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
            }
            catch (Exception ex)
            {
                Log("INIT FAILED: " + ex);
                StatusLabel.Text = "Init failed — see log.";
            }

            RecenterRequested += Recenter;
            _resolutionTimer.Tick += OnResolutionTimerTick;
            _timer.Tick += OnTick;
            _timer.Start();
            Closing += OnClosing;
            Closed += OnClosed;
        }

        /// <summary>
        /// True = the overlay keeps running in the background when this window
        /// is closed (Preferences > OpenXR > Enable OpenXR overlay): closing
        /// only hides the window. The window may also never be shown at all —
        /// everything (host, render timer, panels) runs without it being visible.
        /// </summary>
        public bool KeepRunningWhenClosed { get; set; }

        /// <summary>True once the overlay has really shut down (not just hidden).</summary>
        public bool IsShutDown { get; private set; }

        private bool _shuttingDown;

        /// <summary>Really stop the overlay and close the window, even when <see cref="KeepRunningWhenClosed"/>.</summary>
        public void Shutdown()
        {
            _shuttingDown = true;
            Cleanup(); // a never-shown window may not raise Closed
            try { Close(); } catch { }
        }

        private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!KeepRunningWhenClosed || _shuttingDown) return;

            // Background mode: hide instead of closing. Leave edit mode first so
            // the red border/cursor and button bars don't stay up in VR.
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

            // "Main" standings panel: session-type-aware standings around the
            // player (see MainOverlay). Defaults to the top-left corner; the
            // last dragged position is restored like the belt panel above.
            int mainX = 16, mainY = 16;
            if (s != null && s.OverlayMainPanelX >= 0 && s.OverlayMainPanelY >= 0)
            {
                mainX = Math.Min(s.OverlayMainPanelX, Math.Max(0, _host.CanvasWidth - 100));
                mainY = Math.Min(s.OverlayMainPanelY, Math.Max(0, _host.CanvasHeight - 100));
            }
            _mainPanel = _host.AddRenderTarget(new MainOverlay(
                _host.GraphicsDevice, mainX, mainY, MainOverlayCarCount));
            if (!string.IsNullOrEmpty(s?.OverlayMainColumnOrder))
                _mainPanel.ColumnOrder = s.OverlayMainColumnOrder;
            _mainPanel.ColumnOrderChanged += SaveLayout; // persist header drags like panel moves

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

            // Restore each panel's saved size (edit-mode -/+ buttons / corner
            // grip), then pull anything the new size pushed off the canvas back on.
            if (s != null)
            {
                RestoreScale(_beltPanel, s.OverlayPanelScale);
                RestoreScale(_mainPanel, s.OverlayMainPanelScale);
                RestoreScale(_warningPanel, s.OverlayWarningPanelScale);
                RestoreScale(_nearbyPanel, s.OverlayNearbyPanelScale);
                RestoreScale(_slowCarPanel, s.OverlaySlowCarPanelScale);
                RestoreScale(_youtubePanel, s.OverlayYouTubePanelScale);
                ClampPanelsToCanvas();
            }
        }

        private static void RestoreScale(OverlayRenderTarget? panel, double savedScale)
        {
            if (panel != null && savedScale > 0) panel.Scale = (float)savedScale;
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
            int w = Math.Clamp((int)Math.Round(_host.DisplaySize.X * _dpi), MinCanvasSize, MaxCanvasSize);
            int h = Math.Clamp((int)Math.Round(_host.DisplaySize.Y * _dpi), MinCanvasSize, MaxCanvasSize);
            if (w == _host.CanvasWidth && h == _host.CanvasHeight) return;

            try
            {
                _host.SetCanvasResolution(w, h);
                ClampPanelsToCanvas();
                Log($"Canvas resolution set to {w}×{h} ({_dpi:0} px/m).");
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
            var panels = new OverlayRenderTarget?[]
                { _beltPanel, _mainPanel, _warningPanel, _nearbyPanel, _slowCarPanel, _youtubePanel };
            foreach (var p in panels)
            {
                if (p == null) continue;
                p.X = Math.Min(p.X, Math.Max(0, _host.CanvasWidth - Math.Min(100, p.DisplayWidth)));
                p.Y = Math.Min(p.Y, Math.Max(0, _host.CanvasHeight - Math.Min(100, p.DisplayHeight)));
            }
        }

        private void OnResolutionTimerTick(object? sender, EventArgs e)
        {
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
                    s.OverlayPanelScale = _beltPanel.Scale;
                }
                if (_mainPanel != null)
                {
                    s.OverlayMainPanelX = _mainPanel.X;
                    s.OverlayMainPanelY = _mainPanel.Y;
                    s.OverlayMainPanelScale = _mainPanel.Scale;
                    s.OverlayMainColumnOrder = _mainPanel.ColumnOrder;
                }
                if (_warningPanel != null)
                {
                    s.OverlayWarningPanelX = _warningPanel.X;
                    s.OverlayWarningPanelY = _warningPanel.Y;
                    s.OverlayWarningPanelScale = _warningPanel.Scale;
                }
                if (_nearbyPanel != null)
                {
                    s.OverlayNearbyPanelX = _nearbyPanel.X;
                    s.OverlayNearbyPanelY = _nearbyPanel.Y;
                    s.OverlayNearbyPanelScale = _nearbyPanel.Scale;
                    s.OverlayNearbyWidth = _nearbyPanel.BoxWidth;
                }
                if (_slowCarPanel != null)
                {
                    s.OverlaySlowCarPanelX = _slowCarPanel.X;
                    s.OverlaySlowCarPanelY = _slowCarPanel.Y;
                    s.OverlaySlowCarPanelScale = _slowCarPanel.Scale;
                }
                if (_youtubePanel != null)
                {
                    s.OverlayYouTubePanelX = _youtubePanel.X;
                    s.OverlayYouTubePanelY = _youtubePanel.Y;
                    s.OverlayYouTubePanelScale = _youtubePanel.Scale;
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
            SaveLayout();
            Log($"{target.Name} resized to {target.Scale * 100:0}% — saved.");
        }

        private void OnTick(object? sender, EventArgs e)
        {
            if (_host == null) return;

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
            if (IsShutDown) return;
            IsShutDown = true;
            RecenterRequested -= Recenter;
            _timer.Stop();
            _resolutionTimer.Stop();
            _preview?.Close();
            _host?.Dispose();
            _host = null;
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

        private void DisplaySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_host == null || _syncingEditUi) return;

            // Only the slider that moved is applied — the others may be showing
            // a snapped approximation of an exact depth-only value.
            bool resolutionMayChange = true;
            var size = _host.DisplaySize;
            if (sender == SizeXSlider) _host.DisplaySize = new System.Numerics.Vector2((float)e.NewValue, size.Y);
            else if (sender == SizeYSlider) _host.DisplaySize = new System.Numerics.Vector2(size.X, (float)e.NewValue);
            else if (sender == DpiSlider) _dpi = e.NewValue;
            else if (sender == DistanceSlider && DepthOnlyCheck.IsChecked == true && _host.Distance > 0)
            {
                // Depth only: scale the quad about the view origin, so it covers
                // exactly the same angle (looks the same size, the mouse
                // calibration stays valid) and only its stereo depth changes.
                // DPI shrinks by the same factor so the canvas resolution — and
                // with it the panel layout — is untouched.
                float factor = (float)e.NewValue / _host.Distance;
                _host.Distance = (float)e.NewValue;
                _host.DisplaySize = size * factor;
                _dpi /= factor;
                resolutionMayChange = false;

                _syncingEditUi = true;
                SizeXSlider.Value = _host.DisplaySize.X;
                SizeYSlider.Value = _host.DisplaySize.Y;
                DpiSlider.Value = _dpi;
                _syncingEditUi = false;
            }
            else if (sender == DistanceSlider) _host.Distance = (float)e.NewValue;

            UpdateEditValueLabels();
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
