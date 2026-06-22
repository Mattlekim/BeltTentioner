using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;

namespace BeltTensionTest.WPF.Views
{
    // Minimal shell tray icon implementation using Shell_NotifyIcon and a hidden HwndSource window.
    internal class TrayIcon : IDisposable
    {
        private const int WM_USER = 0x0400;
        private const int NIM_ADD = 0x00000000;
        private const int NIM_MODIFY = 0x00000001;
        private const int NIM_DELETE = 0x00000002;
        private const int NIF_MESSAGE = 0x00000001;
        private const int NIF_ICON = 0x00000002;
        private const int NIF_TIP = 0x00000004;

        private const int WM_LBUTTONUP = 0x0202;
        private const int WM_RBUTTONUP = 0x0205;
        private const int WM_CONTEXTMENU = 0x007B;

        private readonly HwndSource _source;
        private readonly uint _id = 1000;
        private IntPtr _iconHandle = IntPtr.Zero;
        private readonly Action _onOpen;
        private readonly Action _onExit;

        public TrayIcon(string tooltip, Action onOpen, Action onExit)
        {
            _onOpen = onOpen ?? throw new ArgumentNullException(nameof(onOpen));
            _onExit = onExit ?? throw new ArgumentNullException(nameof(onExit));

            var parameters = new HwndSourceParameters("TrayIconWindow") { Width = 0, Height = 0, WindowStyle = 0x800000 }; // WS_POPUP
            _source = new HwndSource(parameters);
            _source.AddHook(WndProc);

            // Load application icon from running exe
            try
            {
                var exe = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrEmpty(exe))
                {
                    var ico = System.Drawing.Icon.ExtractAssociatedIcon(exe);
                    _iconHandle = ico?.Handle ?? IntPtr.Zero;
                }
            }
            catch { }

            var data = new NOTIFYICONDATA();
            data.cbSize = (uint)Marshal.SizeOf(typeof(NOTIFYICONDATA));
            data.hWnd = _source.Handle;
            data.uID = _id;
            data.uFlags = (uint)(NIF_MESSAGE | NIF_TIP | (_iconHandle != IntPtr.Zero ? NIF_ICON : 0));
            data.uCallbackMessage = WM_USER + 1;
            if (_iconHandle != IntPtr.Zero)
                data.hIcon = _iconHandle;
            var bytes = Encoding.Unicode.GetBytes(tooltip ?? "");
            data.szTip = tooltip?.Length > 0 ? tooltip : string.Empty;

            Shell_NotifyIcon(NIM_ADD, ref data);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_USER + 1)
            {
                var m = lParam.ToInt32();
                if (m == WM_RBUTTONUP || m == WM_CONTEXTMENU)
                {
                    ShowContextMenu();
                    handled = true;
                }
                else if (m == WM_LBUTTONUP)
                {
                    _onOpen?.Invoke();
                    handled = true;
                }
            }
            return IntPtr.Zero;
        }

        private void ShowContextMenu()
        {
            // Show a simple context menu with Open and Exit using WPF MessageBox as fallback
            // Use Dispatcher to ensure UI thread context
            Application.Current.Dispatcher.Invoke(() =>
            {
                var menu = new System.Windows.Controls.ContextMenu();
                var open = new System.Windows.Controls.MenuItem { Header = "Open" };
                open.Click += (_, _) => _onOpen?.Invoke();
                var exit = new System.Windows.Controls.MenuItem { Header = "Exit" };
                exit.Click += (_, _) => _onExit?.Invoke();
                menu.Items.Add(open);
                menu.Items.Add(new System.Windows.Controls.Separator());
                menu.Items.Add(exit);

                // Show at mouse position
                menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
                menu.IsOpen = true;
            });
        }

        public void Dispose()
        {
            try
            {
                var data = new NOTIFYICONDATA();
                data.cbSize = (uint)Marshal.SizeOf(typeof(NOTIFYICONDATA));
                data.hWnd = _source.Handle;
                data.uID = _id;
                Shell_NotifyIcon(NIM_DELETE, ref data);
            }
            catch { }
            try { _source.RemoveHook(WndProc); _source.Dispose(); } catch { }
        }

        #region Native
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct NOTIFYICONDATA
        {
            public uint cbSize;
            public IntPtr hWnd;
            public uint uID;
            public uint uFlags;
            public uint uCallbackMessage;
            public IntPtr hIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string szTip;
            // Rest omitted
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern bool Shell_NotifyIcon(int dwMessage, ref NOTIFYICONDATA lpData);
        #endregion
    }
}
