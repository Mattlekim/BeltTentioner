using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using Microsoft.Xna.Framework.Graphics;
using MonoXR.Client;

namespace BeltTensionTest.WPF.Views
{
    /// <summary>
    /// Desktop preview of the VR overlay: shows the composed overlay canvas in
    /// a normal window, so overlays can be checked without a headset. Fed each
    /// tick from OverlayWindow via <see cref="UpdateFrame"/>. The canvas is
    /// drawn by MonoGame straight into a swap chain bound to a native child
    /// window (<see cref="PreviewHwndHost"/>), so it never leaves the GPU — the
    /// earlier path read it back to the CPU, swapped the channels pixel by pixel
    /// and copied it into a WPF image every frame, which was what made it lag.
    /// </summary>
    public partial class OverlayPreviewWindow : Window
    {
        private SwapChainRenderTarget? _swapChain;

        // Full canvas size, for mapping the mouse position back onto the canvas.
        private int _canvasW, _canvasH;

        private bool _syncingEmulate;

        /// <summary>Raised when the user picks a state to emulate (0 current, 1 in car, 2 replay).</summary>
        public event Action<int>? EmulateChanged;

        /// <summary>Show the current choice without raising <see cref="EmulateChanged"/>.</summary>
        public void SetEmulateIndex(int index)
        {
            _syncingEmulate = true;
            EmulateBox.SelectedIndex = index;
            _syncingEmulate = false;
        }

        private void EmulateBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (_syncingEmulate || EmulateBox == null || EmulateBox.SelectedIndex < 0) return;
            EmulateChanged?.Invoke(EmulateBox.SelectedIndex);
        }

        public OverlayPreviewWindow()
        {
            InitializeComponent();
            PreviewHwndHost.ClearClicks();
        }

        protected override void OnClosed(EventArgs e)
        {
            // Before the host goes: the swap chain belongs to its device.
            _swapChain?.Dispose();
            _swapChain = null;
            base.OnClosed(e);
        }

        /// <summary>
        /// Left-button state for the overlay host while the mouse is over the
        /// previewed canvas (null otherwise, so the host falls back to its global
        /// polling). Pairs with <see cref="TryGetCanvasCursor"/>.
        /// </summary>
        public bool? TryGetLeftButton()
        {
            if (TryGetCanvasCursor() == null) return null;
            // The press is also latched by the surface's own window procedure, so a
            // click shorter than the host's poll interval is still seen.
            bool pressed = (GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0;
            bool latched = PreviewHwndHost.ConsumeLeftClick();
            return pressed || latched;
        }

        /// <summary>Right-button state, latched like <see cref="TryGetLeftButton"/>.</summary>
        public bool? TryGetRightButton()
        {
            if (TryGetCanvasCursor() == null) return null;
            bool pressed = (GetAsyncKeyState(VK_RBUTTON) & 0x8000) != 0;
            bool latched = PreviewHwndHost.ConsumeRightClick();
            return pressed || latched;
        }

        /// <summary>
        /// Draw the overlay canvas into the preview if it changed since the last
        /// call (or the window was resized / exposed). Must run on the thread
        /// that renders the host (the UI thread — both share the OverlayWindow
        /// DispatcherTimer).
        /// </summary>
        public void UpdateFrame(MonoGameOverlayHost host)
        {
            IntPtr hwnd = Surface.SurfaceHandle;
            if (hwnd == IntPtr.Zero || !GetClientRect(hwnd, out var rect)) return;
            int w = rect.Right - rect.Left, h = rect.Bottom - rect.Top;
            if (w <= 0 || h <= 0) return;

            _canvasW = host.CanvasWidth;
            _canvasH = host.CanvasHeight;

            bool force = PreviewHwndHost.ConsumeRepaint();
            if (_swapChain == null || _swapChain.Width != w || _swapChain.Height != h)
            {
                _swapChain?.Dispose();
                // Immediate: a vsync'd Present would block the UI thread, which
                // also drives the overlay's own render timer.
                _swapChain = new SwapChainRenderTarget(host.GraphicsDevice, hwnd, w, h, false,
                    SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.DiscardContents,
                    PresentInterval.Immediate);
            }

            host.PresentPreview(_swapChain, force);
        }

        /// <summary>
        /// Canvas-pixel position of the mouse when it is over the previewed
        /// canvas, or null when it isn't (outside the window, over the title bar,
        /// behind another window, or in the letterbox bars). Fed to the overlay
        /// host as a CursorOverride so edit-mode dragging can be done in the
        /// preview with 1:1 mouse mapping instead of the whole-monitor mapping.
        /// </summary>
        public (int X, int Y)? TryGetCanvasCursor()
        {
            IntPtr hwnd = Surface.SurfaceHandle;
            int canvasW = _canvasW, canvasH = _canvasH;
            if (hwnd == IntPtr.Zero || canvasW <= 0 || canvasH <= 0) return null;

            if (!GetCursorPos(out var pt)) return null;
            // Not covered by another window (the old WPF IsMouseOver check).
            if (WindowFromPoint(pt) != hwnd) return null;
            if (!ScreenToClient(hwnd, ref pt) || !GetClientRect(hwnd, out var rect)) return null;

            double areaW = rect.Right - rect.Left, areaH = rect.Bottom - rect.Top;
            if (areaW <= 0 || areaH <= 0) return null;

            // The canvas is letterboxed in the middle of the surface; undo that.
            double scale = Math.Min(areaW / canvasW, areaH / canvasH);
            if (scale <= 0) return null;
            double offX = (areaW - canvasW * scale) / 2;
            double offY = (areaH - canvasH * scale) / 2;

            double cx = (pt.X - offX) / scale;
            double cy = (pt.Y - offY) / scale;
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

        private const int VK_LBUTTON = 0x01, VK_RBUTTON = 0x02;

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X, Y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);
        [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT lpPoint);
        [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(POINT point);
        [DllImport("user32.dll")] private static extern bool ScreenToClient(IntPtr hWnd, ref POINT lpPoint);
        [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);
    }
}
