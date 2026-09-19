using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Runtime.InteropServices;
using MonoXR.Client;

namespace BeltTensionTest.WPF.Views
{
    /// <summary>
    /// Desktop preview of the VR overlay: shows the composed overlay canvas in
    /// a normal window, so overlays can be checked without a headset. Fed each
    /// tick from OverlayWindow via <see cref="UpdateFrame"/>; the readback only
    /// happens on frames where the canvas was actually recomposed, and reads a
    /// GPU-downscaled copy sized to this window rather than the full canvas
    /// (which is tens of megabytes at VR resolutions — far too slow per frame).
    /// </summary>
    public partial class OverlayPreviewWindow : Window
    {
        private WriteableBitmap? _bitmap;
        private byte[] _pixels = Array.Empty<byte>();
        private int _lastVersion = -1;
        private int _lastMaxW, _lastMaxH;

        // Full canvas size — the bitmap is a shrunk copy, so cursor mapping
        // must use these, not the bitmap's pixel size.
        private int _canvasW, _canvasH;

        // Set from the WPF event on any press over the canvas image, consumed
        // by the next TryGetLeftButton poll — so a click faster than the
        // host's poll interval can never be missed.
        private bool _clickLatch;

        public OverlayPreviewWindow()
        {
            InitializeComponent();
            PreviewMouseLeftButtonDown += (_, _) =>
            {
                if (TryGetCanvasCursor() != null) _clickLatch = true;
            };
        }

        /// <summary>
        /// Left-button state for the overlay host while the mouse is over the
        /// previewed canvas image (null otherwise, so the host falls back to
        /// its global polling). Pairs with <see cref="TryGetCanvasCursor"/>.
        /// </summary>
        public bool? TryGetLeftButton()
        {
            if (TryGetCanvasCursor() == null) return null;
            bool pressed = Mouse.LeftButton == MouseButtonState.Pressed;
            bool result = pressed || _clickLatch;
            if (!pressed) _clickLatch = false;
            return result;
        }

        /// <summary>
        /// Copy the overlay canvas into the preview if it changed since the
        /// last call. Must run on the thread that renders the host (the UI
        /// thread — both share the OverlayWindow DispatcherTimer).
        /// </summary>
        public void UpdateFrame(MonoGameOverlayHost host)
        {
            // Size the copy to the pixels this window can actually show
            // (rounded up to 64 px steps so resizing the window doesn't
            // reallocate on every pixel).
            var dpi = VisualTreeHelper.GetDpi(this);
            int maxW = RoundUp((int)(Math.Max(1, PreviewArea.ActualWidth) * dpi.DpiScaleX), 64);
            int maxH = RoundUp((int)(Math.Max(1, PreviewArea.ActualHeight) * dpi.DpiScaleY), 64);

            int version = host.CanvasVersion;
            if (version == _lastVersion && maxW == _lastMaxW && maxH == _lastMaxH) return;
            _lastVersion = version;
            _lastMaxW = maxW;
            _lastMaxH = maxH;
            _canvasW = host.CanvasWidth;
            _canvasH = host.CanvasHeight;

            var small = host.GetPreviewTexture(maxW, maxH);
            int w = small.Width, h = small.Height;
            if (_bitmap == null || _bitmap.PixelWidth != w || _bitmap.PixelHeight != h)
            {
                // Pbgra32: the canvas holds premultiplied alpha, and it is
                // WPF's native format (no per-frame conversion).
                _bitmap = new WriteableBitmap(w, h, 96, 96, PixelFormats.Pbgra32, null);
                _pixels = new byte[w * h * 4];
                PreviewImage.Source = _bitmap;
            }

            small.GetData(_pixels);

            // MonoGame's SurfaceFormat.Color is RGBA; WPF wants BGRA. Swap R/B a pixel at a time.
            var px = MemoryMarshal.Cast<byte, uint>(_pixels.AsSpan());
            for (int i = 0; i < px.Length; i++)
            {
                uint v = px[i];
                px[i] = (v & 0xFF00FF00u) | ((v & 0xFFu) << 16) | ((v >> 16) & 0xFFu);
            }

            _bitmap.WritePixels(new Int32Rect(0, 0, w, h), _pixels, w * 4, 0);
        }

        private static int RoundUp(int value, int step) => (value + step - 1) / step * step;

        /// <summary>
        /// Canvas-pixel position of the mouse when it is over the previewed
        /// canvas image, or null when it isn't (outside the window, over the
        /// title bar, or in the letterbox bars). Fed to the overlay host as a
        /// CursorOverride so edit-mode dragging can be done in the preview
        /// with 1:1 mouse mapping instead of the whole-monitor mapping.
        /// </summary>
        public (int X, int Y)? TryGetCanvasCursor()
        {
            if (_bitmap == null || !IsMouseOver) return null;

            double areaW = PreviewImage.ActualWidth, areaH = PreviewImage.ActualHeight;
            int canvasW = _canvasW, canvasH = _canvasH;
            if (areaW <= 0 || areaH <= 0 || canvasW <= 0 || canvasH <= 0) return null;

            // Stretch=Uniform centers the bitmap in the element; undo that.
            double scale = Math.Min(areaW / canvasW, areaH / canvasH);
            if (scale <= 0) return null;
            double offX = (areaW - canvasW * scale) / 2;
            double offY = (areaH - canvasH * scale) / 2;

            var pos = Mouse.GetPosition(PreviewImage);
            double cx = (pos.X - offX) / scale;
            double cy = (pos.Y - offY) / scale;
            if (cx < 0 || cy < 0 || cx >= canvasW || cy >= canvasH) return null;
            return ((int)cx, (int)cy);
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                try { DragMove(); } catch { }
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
    }
}
