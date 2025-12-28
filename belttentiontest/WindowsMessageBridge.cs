using System;
using System.Diagnostics;
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
        VForce,
        MaxOutput,
        InvertConeringForces,
        ABSEnabled,
        ABSStrength
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
            if (parts.Length == 2)
            {
                if (Enum.TryParse(parts[0], out BeltMessageType type) && float.TryParse(parts[1], out float value))
                {
                    result = new BeltMessage(type, value);
                    return true;
                }
            }
            return false;
        }
    }

    public class WindowsMessageBridge
    {
        public const int WM_COPYDATA = 0x004A;

        // For receiving messages in a Form
        public static event Action<BeltMessage>? BeltMessageReceived;
        public static event Action<string>? MessageReceived;

        // Log received BeltMessages for debugging
        static WindowsMessageBridge()
        {
            BeltMessageReceived += (msg) =>
            {
                try
                {
                    Debug.WriteLine($"[App] Received BeltMessage: Type={msg.Type}, Value={msg.Value}");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[App] Exception in BeltMessageReceived: {ex}");
                }
            };
        }

        // Call this from your Form's WndProc
        public static void HandleWndProc(ref Message m)
        {
            if (m.Msg == WM_COPYDATA)
            {
                try
                {
                    COPYDATASTRUCT cds = Marshal.PtrToStructure<COPYDATASTRUCT>(m.LParam);
                    byte[] data = new byte[cds.cbData];
                    Marshal.Copy(cds.lpData, data, 0, cds.cbData);
                    string msg = Encoding.UTF8.GetString(data);
                    Debug.WriteLine($"[HandleWndProc] Received WM_COPYDATA message: '{msg}'");
                    MessageReceived?.Invoke(msg);
                    if (BeltMessage.TryParse(msg, out var beltMsg) && beltMsg != null)
                    {
                        Debug.WriteLine($"[HandleWndProc] Parsed BeltMessage: Type={beltMsg.Type}, Value={beltMsg.Value}");
                        BeltMessageReceived?.Invoke(beltMsg);
                    }
                    m.Result = IntPtr.Zero;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[HandleWndProc] Exception: {ex}");
                }
            }
        }

        // Win32 API declarations
        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        // For sending string da
        [StructLayout(LayoutKind.Sequential)]
        public struct COPYDATASTRUCT
        {
            public IntPtr dwData;
            public int cbData;
            public IntPtr lpData;
        }

        // Send a BeltMessage to another window by title
        public static bool SendBeltMessage(BeltMessageType type, float value)
        {
            return SendStringMessage("Belt Tensioner", new BeltMessage(type, value).ToString());
        }
        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        public static void ListAllWindows()
        {
            EnumWindows((hWnd, lParam) =>
            {
                if (IsWindowVisible(hWnd))
                {
                    StringBuilder sb = new StringBuilder(256);
                    GetWindowText(hWnd, sb, sb.Capacity);
                    string title = sb.ToString();
                    if (!string.IsNullOrWhiteSpace(title))
                        Console.WriteLine(title);
                }
                return true;
            }, IntPtr.Zero);
        }
        
       
        // Send a string message to another window by title
        public static bool SendStringMessage(string targetWindowTitle, string message)
        {
            ListAllWindows();

            IntPtr hPointer = FindWindow(null, targetWindowTitle);
                if (hPointer == IntPtr.Zero)
                {
                    Debug.WriteLine($"[SendStringMessage] Window '{targetWindowTitle}' not found.");
                    return false;
                }
            

            Debug.WriteLine($"[SendStringMessage] Sending to window '{targetWindowTitle}' (hWnd: 0x{hPointer.ToInt64():X}) message: '{message}'");

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

            SendMessage(hPointer, WM_COPYDATA, IntPtr.Zero, cdsPtr);

            Marshal.FreeHGlobal(ptr);
            Marshal.FreeHGlobal(cdsPtr);
            return true;
        }

        // Decodes and handles Windows messages
        public static bool DecodeWndProc(ref Message m)
        {
            // Only handle custom WM_COPYDATA messages, ignore all others
            if (m.Msg == WM_COPYDATA)
            {
                HandleWndProc(ref m);
                return true;
            }
            // Do not handle any other Windows messages here
            return false;
        }
    }
}
