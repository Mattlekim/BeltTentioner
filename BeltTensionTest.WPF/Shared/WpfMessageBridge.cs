using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;

namespace BeltTensionTest.WPF.Shared
{
    public enum BeltMessageType
    {
        GForce, GCurve, CForce, CCurve, VForce,
        MaxOutput, InvertConeringForces, ABSEnabled, ABSStrength
    }

    public class BeltMessage
    {
        public BeltMessageType Type  { get; set; }
        public float           Value { get; set; }

        public BeltMessage(BeltMessageType type, float value) { Type = type; Value = value; }

        public override string ToString() => $"{Type}:{Value}";

        public static bool TryParse(string msg, out BeltMessage? result)
        {
            result = null;
            var parts = msg.Split(':');
            if (parts.Length != 2) return false;
            if (Enum.TryParse(parts[0], out BeltMessageType t) && float.TryParse(parts[1], out float v))
            {
                result = new BeltMessage(t, v);
                return true;
            }
            return false;
        }
    }

    /// <summary>
    /// WPF version of WindowsMessageBridge.
    /// Call Attach(window) once after the main window is loaded, then subscribe to BeltMessageReceived.
    /// </summary>
    public static class WpfMessageBridge
    {
        public const int WM_COPYDATA = 0x004A;

        public static bool IsEnabled { get; set; } = false;
        public static event Action<BeltMessage>? BeltMessageReceived;

        private static HwndSource? _hwndSource;

        public static void Attach(Window window)
        {
            if (!IsEnabled) return;
            var helper = new WindowInteropHelper(window);
            helper.EnsureHandle();
            _hwndSource = HwndSource.FromHwnd(helper.Handle);
            _hwndSource?.AddHook(WndProc);
        }

        public static void Detach()
        {
            _hwndSource?.RemoveHook(WndProc);
            _hwndSource = null;
        }

        private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_COPYDATA)
            {
                try
                {
                    var cds = Marshal.PtrToStructure<COPYDATASTRUCT>(lParam);
                    var bytes = new byte[cds.cbData];
                    Marshal.Copy(cds.lpData, bytes, 0, cds.cbData);
                    var text = Encoding.UTF8.GetString(bytes);
                    if (BeltMessage.TryParse(text, out var bm) && bm != null)
                        BeltMessageReceived?.Invoke(bm);
                    handled = true;
                }
                catch { }
            }
            return IntPtr.Zero;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct COPYDATASTRUCT
        {
            public IntPtr dwData;
            public int    cbData;
            public IntPtr lpData;
        }
    }
}
