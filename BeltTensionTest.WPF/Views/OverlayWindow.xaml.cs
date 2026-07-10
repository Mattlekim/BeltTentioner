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
        private const int CanvasXSize = 1920; // pixel size of the overlay canvas
        private const int CanvasYSize = 1024; // pixel size of the overlay canvas

        private MonoGameOverlayHost? _host;
        private readonly MainViewModel _vm;
        private readonly Services.SettingsService _settingsSvc = new();
        private BeltSettingsOverlay? _beltPanel;
        private MainOverlay? _mainPanel;
        private WarningOverlay? _warningPanel;
        private NearbyCarsOverlay? _nearbyPanel;
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

            _timer.Tick += OnTick;
            _timer.Start();
            Closed += OnClosed;
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
                x = Math.Min(s.OverlayPanelX, Math.Max(0, _host.CanvasWidth - panelWidth));
                y = Math.Min(s.OverlayPanelY, Math.Max(0, _host.CanvasHeight - panelHeight));
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
        }
        // ===================== END MONOGAME RENDER SECTION ===================

        /// <summary>Apply persisted overlay size/distance/resolution to the freshly created host.</summary>
        private void ApplySavedLayout()
        {
            var s = _vm.AppSettings;
            if (_host == null || s == null) return;

            if (s.OverlayCanvasWidth >= 16 && s.OverlayCanvasHeight >= 16)
                _host.SetCanvasResolution(s.OverlayCanvasWidth, s.OverlayCanvasHeight);
            if (s.OverlaySizeX > 0 && s.OverlaySizeY > 0)
                _host.DisplaySize = new System.Numerics.Vector2((float)s.OverlaySizeX, (float)s.OverlaySizeY);
            if (s.OverlayDistance > 0)
                _host.Distance = (float)s.OverlayDistance;
        }

        /// <summary>Persist the current overlay layout (panel position, size, distance, resolution).</summary>
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
                }
                if (_mainPanel != null)
                {
                    s.OverlayMainPanelX = _mainPanel.X;
                    s.OverlayMainPanelY = _mainPanel.Y;
                    s.OverlayMainColumnOrder = _mainPanel.ColumnOrder;
                }
                if (_warningPanel != null)
                {
                    s.OverlayWarningPanelX = _warningPanel.X;
                    s.OverlayWarningPanelY = _warningPanel.Y;
                }
                if (_nearbyPanel != null)
                {
                    s.OverlayNearbyPanelX = _nearbyPanel.X;
                    s.OverlayNearbyPanelY = _nearbyPanel.Y;
                    s.OverlayNearbyWidth = _nearbyPanel.BoxWidth;
                }
                if (_youtubePanel != null)
                {
                    s.OverlayYouTubePanelX = _youtubePanel.X;
                    s.OverlayYouTubePanelY = _youtubePanel.Y;
                }
                s.OverlaySizeX = _host.DisplaySize.X;
                s.OverlaySizeY = _host.DisplaySize.Y;
                s.OverlayDistance = _host.Distance;
                s.OverlayCanvasWidth = _host.CanvasWidth;
                s.OverlayCanvasHeight = _host.CanvasHeight;
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
                try { _preview.UpdateFrame(_host.CanvasTexture, _host.CanvasVersion); }
                catch (Exception ex) { Log("PREVIEW FAILED: " + ex.Message); _preview.Close(); }
            }
        }

        private void OnClosed(object? sender, EventArgs e)
        {
            _timer.Stop();
            _preview?.Close();
            _host?.Dispose();
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
            ResWidthBox.Text = _host.CanvasWidth.ToString();
            ResHeightBox.Text = _host.CanvasHeight.ToString();
            _syncingEditUi = false;
            UpdateEditValueLabels();
        }

        private void UpdateEditValueLabels()
        {
            SizeXValue.Text = SizeXSlider.Value.ToString("0.00");
            SizeYValue.Text = SizeYSlider.Value.ToString("0.00");
            DistanceValue.Text = DistanceSlider.Value.ToString("0.00");
        }

        private void DisplaySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_host == null || _syncingEditUi) return;
            _host.DisplaySize = new System.Numerics.Vector2((float)SizeXSlider.Value, (float)SizeYSlider.Value);
            _host.Distance = (float)DistanceSlider.Value;
            UpdateEditValueLabels();
            SaveLayout();
        }

        private void ApplyResolution_Click(object sender, RoutedEventArgs e)
        {
            if (_host == null) return;
            if (!int.TryParse(ResWidthBox.Text, out int w) || !int.TryParse(ResHeightBox.Text, out int h)
                || w < 16 || h < 16 || w > 8192 || h > 8192)
            {
                Log("Invalid resolution — enter width/height between 16 and 8192.");
                return;
            }

            try
            {
                _host.SetCanvasResolution(w, h);
                Log($"Canvas resolution set to {w}×{h} (VR display size unchanged).");
                SaveLayout();
            }
            catch (Exception ex)
            {
                Log("RESOLUTION CHANGE FAILED: " + ex);
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
