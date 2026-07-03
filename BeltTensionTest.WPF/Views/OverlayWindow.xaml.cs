using System;
using System.Numerics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MonoXR.Client;
using MonoXR.Shared;

namespace BeltTensionTest.WPF.Views
{
    /// <summary>
    /// Example window: renders an animated square and publishes it as an OpenXR
    /// overlay via MonoXR. MonoXR's client takes raw RGBA pixels, so no MonoGame
    /// host is needed here — we draw the square straight into the overlay buffer
    /// and show a live preview.
    /// </summary>
    public partial class OverlayWindow : Window
    {
        private const int Size = 512;

        private OverlayManager? _mgr;
        private Overlay? _overlay;
        private readonly byte[] _rgba = new byte[Size * Size * 4];
        private readonly byte[] _bgra = new byte[Size * Size * 4];
        private WriteableBitmap? _preview;

        private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(16) };
        private double _time;
        private ulong _frames;
        private string? _initError;

        public OverlayWindow()
        {
            InitializeComponent();

            _preview = new WriteableBitmap(Size, Size, 96, 96, PixelFormats.Bgra32, null);
            PreviewImage.Source = _preview;

            try
            {
                ClientLog("OverlayWindow init: creating OverlayManager...");
                _mgr = new OverlayManager();
                ClientLog(_mgr.AttachedToExisting
                    ? "OverlayManager attached to existing control block; creating overlay..."
                    : "OverlayManager created new control block; creating overlay...");
                _overlay = _mgr.CreateOverlay(Size, Size);
                // World space = the recentered headset origin (OpenXR LOCAL space:
                // origin and forward are set when the user recenters). -Z is
                // forward, so this places the panel ~3 m ahead of the recenter
                // point, at recenter eye height, facing the driver.
                _overlay.Space = MonoXrSpace.World;
                _overlay.Position = new Vector3(0f, 0f, -3f);
                _overlay.Size = new Vector2(2.5f, 2.5f); // large (2.5 m) so it's unmistakable while debugging visibility
                _overlay.Visible = true;
                ClientLog("MonoXR init OK: overlay published (World, -3m, 1x1m).");
            }
            catch (Exception ex)
            {
                _initError = ex.Message;
                ClientLog("MonoXR INIT FAILED: " + ex);
            }

            _timer.Tick += OnTick;
            _timer.Start();

            Closed += OnClosed;
        }

        private void OnTick(object? sender, EventArgs e)
        {
            _time += _timer.Interval.TotalSeconds;
            DrawSquare(_time);
            UpdatePreview();

            if (_overlay != null && _mgr != null)
            {
                if (SpinCheck.IsChecked == true)
                    _overlay.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, (float)Math.Sin(_time) * 0.7f);
                else
                    _overlay.Rotation = Quaternion.Identity;

                _overlay.Update(_rgba);
                _mgr.Heartbeat();
                _frames++;
            }

            StatusLabel.Text = _initError != null
                ? "MonoXR init failed: " + _initError
                : (_mgr!.LayerAttached
                    ? "Layer attached — overlay is live in VR."
                    : "Waiting for an OpenXR app (layer not attached yet).");
            FrameLabel.Text = "Frames: " + _frames;
        }

        // Draw a dark translucent panel with a pulsing bordered square in the centre.
        private void DrawSquare(double t)
        {
            Array.Clear(_rgba, 0, _rgba.Length);
            FillRect(0, 0, Size, Size, 20, 24, 48, 255);            // opaque panel background (easy to spot)

            int s = (int)(Size * 0.45);
            int x0 = (Size - s) / 2;
            byte pulse = (byte)(120 + 120 * (0.5 + 0.5 * Math.Sin(t * 2.0)));
            FillRect(x0, x0, s, s, pulse, 90, 230, 255);            // square
            DrawBorder(x0, x0, s, s, 6, 255, 255, 255, 255);       // white border
        }

        private void FillRect(int x, int y, int w, int h, byte r, byte g, byte b, byte a)
        {
            int x1 = Math.Min(x + w, Size), y1 = Math.Min(y + h, Size);
            for (int py = Math.Max(0, y); py < y1; py++)
            {
                int row = py * Size * 4;
                for (int px = Math.Max(0, x); px < x1; px++)
                {
                    int i = row + px * 4;
                    _rgba[i] = r; _rgba[i + 1] = g; _rgba[i + 2] = b; _rgba[i + 3] = a;
                }
            }
        }

        private void DrawBorder(int x, int y, int w, int h, int t, byte r, byte g, byte b, byte a)
        {
            FillRect(x, y, w, t, r, g, b, a);
            FillRect(x, y + h - t, w, t, r, g, b, a);
            FillRect(x, y, t, h, r, g, b, a);
            FillRect(x + w - t, y, t, h, r, g, b, a);
        }

        // Copy the RGBA overlay buffer into the BGRA preview bitmap.
        private void UpdatePreview()
        {
            if (_preview == null) return;
            for (int i = 0; i < Size * Size; i++)
            {
                int s = i * 4;
                _bgra[s + 0] = _rgba[s + 2]; // B
                _bgra[s + 1] = _rgba[s + 1]; // G
                _bgra[s + 2] = _rgba[s + 0]; // R
                _bgra[s + 3] = _rgba[s + 3]; // A
            }
            _preview.WritePixels(new Int32Rect(0, 0, Size, Size), _bgra, Size * 4, 0);
        }

        private void Space_Changed(object sender, RoutedEventArgs e)
        {
            if (_overlay == null) return;
            _overlay.Space = HeadRadio.IsChecked == true ? MonoXrSpace.Head : MonoXrSpace.World;
            // Move a head-locked overlay off to the side so it doesn't fill the view.
            _overlay.Position = _overlay.Space == MonoXrSpace.Head
                ? new Vector3(0.35f, -0.2f, -1.0f)
                : new Vector3(0f, 0f, -3f);
        }

        private void Start_Click(object sender, RoutedEventArgs e)
        {
            if (_overlay != null) _overlay.Visible = true;
            if (!_timer.IsEnabled) _timer.Start();
        }

        private void Stop_Click(object sender, RoutedEventArgs e)
        {
            if (_overlay != null) _overlay.Visible = false;
        }

        private void OnClosed(object? sender, EventArgs e)
        {
            _timer.Stop();
            _overlay?.Dispose();
            _mgr?.Dispose();
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                try { DragMove(); } catch { }
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

        // Client-side log (separate from the native layer's %TEMP%\MonoXR\layer.log).
        // Captures init failures with full exception detail so they can be read
        // after the fact. Opened shared so it can be tailed live.
        private static void ClientLog(string message)
        {
            try
            {
                string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "MonoXR");
                System.IO.Directory.CreateDirectory(dir);
                string line = $"{DateTime.Now:HH:mm:ss.fff} [pid {Environment.ProcessId}] {message}{Environment.NewLine}";
                using var fs = new System.IO.FileStream(
                    System.IO.Path.Combine(dir, "client.log"),
                    System.IO.FileMode.Append, System.IO.FileAccess.Write, System.IO.FileShare.ReadWrite);
                using var sw = new System.IO.StreamWriter(fs);
                sw.Write(line);
            }
            catch { /* logging must never throw */ }
        }
    }
}
