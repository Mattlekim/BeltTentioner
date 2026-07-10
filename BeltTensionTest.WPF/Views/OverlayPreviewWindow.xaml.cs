using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Xna.Framework.Graphics;

namespace BeltTensionTest.WPF.Views
{
    /// <summary>
    /// Desktop preview of the VR overlay: shows the composed overlay canvas in
    /// a normal window, so overlays can be checked without a headset. Fed each
    /// tick from OverlayWindow via <see cref="UpdateFrame"/>; the readback only
    /// happens on frames where the canvas was actually recomposed.
    /// </summary>
    public partial class OverlayPreviewWindow : Window
    {
        private WriteableBitmap? _bitmap;
        private byte[] _pixels = Array.Empty<byte>();
        private int _lastVersion = -1;

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
        public void UpdateFrame(Texture2D canvas, int version)
        {
            if (version == _lastVersion) return;
            _lastVersion = version;

            int w = canvas.Width, h = canvas.Height;
            if (_bitmap == null || _bitmap.PixelWidth != w || _bitmap.PixelHeight != h)
            {
                _bitmap = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null);
                _pixels = new byte[w * h * 4];
                PreviewImage.Source = _bitmap;
            }

            canvas.GetData(_pixels);

            // MonoGame's SurfaceFormat.Color is RGBA; WPF wants BGRA.
            for (int i = 0; i < _pixels.Length; i += 4)
            {
                byte r = _pixels[i];
                _pixels[i] = _pixels[i + 2];
                _pixels[i + 2] = r;
            }

            _bitmap.WritePixels(new Int32Rect(0, 0, w, h), _pixels, w * 4, 0);
        }

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
            int canvasW = _bitmap.PixelWidth, canvasH = _bitmap.PixelHeight;
            if (areaW <= 0 || areaH <= 0) return null;

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
