using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace BeltTentionerLib
{
    public enum BeltMessageType
    {
        GForce,
        GCurve,
        CForce,
        CCurve,
        VForce
    }

    public class BeltMessage
    {
        public BeltMessageType Type { get; set; }
        public float Value { get; set; }
        public BeltMessage(BeltMessageType type, float value)
        {
            Type = type;
            Value = value;
        }
        public override string ToString() => $"{Type}:{Value}";
        public static bool TryParse(string msg, out BeltMessage? result)
        {
            result = null;
            var parts = msg.Split(':');
            if (parts.Length == 2 && Enum.TryParse(parts[0], out BeltMessageType type) && float.TryParse(parts[1], out float value))
            {
                result = new BeltMessage(type, value);
                return true;
            }
            return false;
        }
    }

    public class WindowsMessageBridge
    {
        // Win32 API declarations
        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        public const int WM_COPYDATA = 0x004A;

        // For sending string data
        [StructLayout(LayoutKind.Sequential)]
        public struct COPYDATASTRUCT
        {
            public IntPtr dwData;
            public int cbData;
            public IntPtr lpData;
        }

        // Send a BeltMessage to another window by title
        public static bool SendBeltMessage(string targetWindowTitle, BeltMessageType type, float value)
        {
            return SendStringMessage(targetWindowTitle, new BeltMessage(type, value).ToString());
        }

        // Send a string message to another window by title
        public static bool SendStringMessage(string targetWindowTitle, string message)
        {
            IntPtr hWnd = FindWindow(null, targetWindowTitle);
            if (hWnd == IntPtr.Zero)
                return false;

            byte[] sarr = Encoding.UTF8.GetBytes(message);
            IntPtr ptr = Marshal.AllocHGlobal(sarr.Length);
            Marshal.Copy(sarr, 0, ptr, sarr.Length);

            COPYDATASTRUCT cds = new COPYDATASTRUCT
            {
                dwData = IntPtr.Zero,
                cbData = sarr.Length,
                lpData = ptr
            };

            IntPtr cdsPtr = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(COPYDATASTRUCT)));
            Marshal.StructureToPtr(cds, cdsPtr, false);

            SendMessage(hWnd, WM_COPYDATA, IntPtr.Zero, cdsPtr);

            Marshal.FreeHGlobal(ptr);
            Marshal.FreeHGlobal(cdsPtr);
            return true;
        }

        // For receiving messages in a Form
        public static event Action<BeltMessage>? BeltMessageReceived;
        public static event Action<string>? MessageReceived;

        // Call this from your Form's WndProc
        public static void HandleWndProc(ref Message m)
        {
            if (m.Msg == WM_COPYDATA)
            {
                COPYDATASTRUCT cds = Marshal.PtrToStructure<COPYDATASTRUCT>(m.LParam);
                byte[] data = new byte[cds.cbData];
                Marshal.Copy(cds.lpData, data, 0, cds.cbData);
                string msg = Encoding.UTF8.GetString(data);
                MessageReceived?.Invoke(msg);
                if (BeltMessage.TryParse(msg, out var beltMsg) && beltMsg != null)
                {
                    BeltMessageReceived?.Invoke(beltMsg);
                }
                m.Result = IntPtr.Zero;
            }
        }
    }
}
