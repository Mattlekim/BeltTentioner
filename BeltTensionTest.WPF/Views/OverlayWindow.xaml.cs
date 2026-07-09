using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using BeltTensionTest.WPF.Services.Overlays;
using BeltTensionTest.WPF.ViewModels;

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

        // 30 fps target; the host also caps via MaxFrameRate, so late/early
        // DispatcherTimer ticks can never push the overlay above that rate.
        private readonly DispatcherTimer _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
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
            var rows = new[]
            {
                new BeltSettingRow("Surge Strength", () => _vm.BrakingStrength,   v => _vm.BrakingStrength   = v, 1f,   200f, 5f),
                new BeltSettingRow("Surge Curve",    () => _vm.BrakingCurve,      v => _vm.BrakingCurve      = v, 0.1f, 5f,   0.1f, "0.0"),
                new BeltSettingRow("Sway Strength",  () => _vm.CorneringStrength, v => _vm.CorneringStrength = v, 1f,   200f, 5f),
                new BeltSettingRow("Sway Curve",     () => _vm.CorneringCurve,    v => _vm.CorneringCurve    = v, 0.1f, 10f,  0.1f, "0.0"),
                new BeltSettingRow("Heave Strength", () => _vm.VerticalStrength,  v => _vm.VerticalStrength  = v, 1f,   200f, 5f),
                new BeltSettingRow("Max Power",      () => _vm.MaxOutput,         v => _vm.MaxOutput         = v, 1f,   100f, 5f),
            };

            // Invert checkboxes, one per force axis shown above. Same write-
            // through path as the sliders so the WPF checkboxes stay in sync.
            var toggles = new[]
            {
                new BeltToggleRow("Invert Surge", () => _vm.InvertSurge, v => _vm.InvertSurge = v),
                new BeltToggleRow("Invert Sway",  () => _vm.InvertSway,  v => _vm.InvertSway  = v),
                new BeltToggleRow("Invert Heave", () => _vm.InvertHeave, v => _vm.InvertHeave = v),
            };

            const int panelWidth = 768, panelHeight = 656;
            int x = (_host.CanvasWidth - panelWidth) / 2, y = (_host.CanvasHeight - panelHeight) / 2;

            // Restore the last dragged position (clamped in case the canvas shrank).
            var s = _vm.AppSettings;
            if (s != null && s.OverlayPanelX >= 0 && s.OverlayPanelY >= 0)
            {
                x = Math.Min(s.OverlayPanelX, Math.Max(0, _host.CanvasWidth - panelWidth));
                y = Math.Min(s.OverlayPanelY, Math.Max(0, _host.CanvasHeight - panelHeight));
            }

            _beltPanel = _host.AddRenderTarget(new BeltSettingsOverlay(
                _host.GraphicsDevice, panelWidth, panelHeight, x, y, rows, toggles));
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
        }

        private void OnClosed(object? sender, EventArgs e)
        {
            _timer.Stop();
            _host?.Dispose();
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
