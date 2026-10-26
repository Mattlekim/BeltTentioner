using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace BeltTensionTest.WPF.Views
{
    /// <summary>
    /// A bare native child window for the overlay preview to draw into: MonoGame
    /// presents a swap chain straight to its handle (see
    /// MonoGameOverlayHost.PresentPreview), so the canvas never goes through the
    /// CPU. It also reports what a WPF Image would have: clicks on it, and when
    /// it needs repainting.
    ///
    /// WPF mouse events never reach a hosted child window, so clicks are caught
    /// here in its own window procedure. There is only ever one preview open, so
    /// the state is static (the window procedure has no instance to reach).
    /// </summary>
    internal sealed class PreviewHwndHost : HwndHost
    {
        private const string ClassName = "BbtOverlayPreviewSurface";

        private const uint WS_CHILD = 0x40000000, WS_VISIBLE = 0x10000000,
                           WS_CLIPSIBLINGS = 0x04000000, WS_CLIPCHILDREN = 0x02000000;
        private const uint WM_ERASEBKGND = 0x0014, WM_PAINT = 0x000F,
                           WM_LBUTTONDOWN = 0x0201, WM_RBUTTONDOWN = 0x0204;
        private const int IDC_ARROW = 32512;

        private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        // Kept in a static so the delegate (and the native thunk) outlives the window class.
        private static readonly WndProcDelegate WndProcInstance = WndProc;
        private static bool _classRegistered;

        private static volatile bool _leftClick, _rightClick, _repaint;

        private IntPtr _hwnd;

        /// <summary>The native window, or zero before it has been created.</summary>
        public IntPtr SurfaceHandle => _hwnd;

        /// <summary>True once since the last call if the window was exposed and needs redrawing.</summary>
        public static bool ConsumeRepaint()
        {
            bool r = _repaint;
            _repaint = false;
            return r;
        }

        /// <summary>True once per left press on the surface since the last call (so a fast click is never missed).</summary>
        public static bool ConsumeLeftClick()
        {
            bool r = _leftClick;
            _leftClick = false;
            return r;
        }

        public static bool ConsumeRightClick()
        {
            bool r = _rightClick;
            _rightClick = false;
            return r;
        }

        /// <summary>Clear any press that was recorded before the host was looking (e.g. a click that opened the preview).</summary>
        public static void ClearClicks() { _leftClick = false; _rightClick = false; }

        protected override HandleRef BuildWindowCore(HandleRef hwndParent)
        {
            EnsureClass();
            _hwnd = CreateWindowEx(0, ClassName, string.Empty,
                WS_CHILD | WS_VISIBLE | WS_CLIPSIBLINGS | WS_CLIPCHILDREN,
                0, 0, 0, 0, hwndParent.Handle, IntPtr.Zero, GetModuleHandle(null), IntPtr.Zero);
            if (_hwnd == IntPtr.Zero)
                throw new InvalidOperationException("Could not create the preview surface window.");
            return new HandleRef(this, _hwnd);
        }

        protected override void DestroyWindowCore(HandleRef hwnd)
        {
            DestroyWindow(hwnd.Handle);
            _hwnd = IntPtr.Zero;
        }

        private static void EnsureClass()
        {
            if (_classRegistered) return;
            var wc = new WNDCLASSEX
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(WndProcInstance),
                hInstance = GetModuleHandle(null),
                hCursor = LoadCursor(IntPtr.Zero, (IntPtr)IDC_ARROW),
                lpszClassName = ClassName,
            };
            if (RegisterClassEx(ref wc) == 0 && Marshal.GetLastWin32Error() != 1410) // 1410 = already registered
                throw new InvalidOperationException("Could not register the preview surface window class.");
            _classRegistered = true;
        }

        private static IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            switch (msg)
            {
                case WM_ERASEBKGND:
                    return (IntPtr)1; // MonoGame paints every pixel; erasing would flicker
                case WM_PAINT:
                    ValidateRect(hWnd, IntPtr.Zero);
                    _repaint = true;
                    return IntPtr.Zero;
                case WM_LBUTTONDOWN:
                    _leftClick = true;
                    break;
                case WM_RBUTTONDOWN:
                    _rightClick = true;
                    break;
            }
            return DefWindowProc(hWnd, msg, wParam, lParam);
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WNDCLASSEX
        {
            public uint cbSize;
            public uint style;
            public IntPtr lpfnWndProc;
            public int cbClsExtra;
            public int cbWndExtra;
            public IntPtr hInstance;
            public IntPtr hIcon;
            public IntPtr hCursor;
            public IntPtr hbrBackground;
            [MarshalAs(UnmanagedType.LPWStr)] public string? lpszMenuName;
            [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;
            public IntPtr hIconSm;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern ushort RegisterClassEx(ref WNDCLASSEX lpwcx);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateWindowEx(uint dwExStyle, string lpClassName, string lpWindowName, uint dwStyle,
            int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

        [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] private static extern bool ValidateRect(IntPtr hWnd, IntPtr lpRect);
        [DllImport("user32.dll")] private static extern IntPtr LoadCursor(IntPtr hInstance, IntPtr lpCursorName);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? lpModuleName);
    }
}
