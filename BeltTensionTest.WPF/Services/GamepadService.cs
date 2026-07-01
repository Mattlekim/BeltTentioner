using System;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace BeltTensionTest.WPF.Services
{
    /// <summary>
    /// Polls game controllers and raises <see cref="ButtonPressed"/> on the UI thread whenever a
    /// button (or trigger / POV hat) transitions from released to pressed. Two backends are used:
    ///   * XInput  – Xbox-style controllers ("A", "LeftShoulder", "DPadUp", "LeftTrigger", …)
    ///   * winmm   – generic DirectInput devices such as racing wheels ("Button1", "PovUp", …)
    /// Button names are stable identifiers; the binding system stores them prefixed with "Pad:" so
    /// they can live alongside keyboard gestures.
    /// </summary>
    public sealed class GamepadService
    {
        public static GamepadService Instance { get; } = new GamepadService();

        /// <summary>Raised (on the UI thread) with the button name when a control is newly pressed.</summary>
        public event Action<string>? ButtonPressed;

        private const int MaxControllers = 4;   // XInput
        private const int MaxJoysticks = 16;     // winmm
        private const int TriggerThreshold = 60; // out of 255

        private readonly DispatcherTimer _timer;

        // XInput remembered state
        private readonly ushort[] _prevButtons = new ushort[MaxControllers];
        private readonly bool[] _prevConnected = new bool[MaxControllers];
        private readonly bool[] _prevLeftTrigger = new bool[MaxControllers];
        private readonly bool[] _prevRightTrigger = new bool[MaxControllers];

        // winmm (legacy joystick / wheel) remembered state
        private readonly uint[] _prevJoyButtons = new uint[MaxJoysticks];
        private readonly bool[] _prevJoyConnected = new bool[MaxJoysticks];
        private readonly string[] _prevJoyPov = new string[MaxJoysticks];

        private bool _started;

        private static readonly (ushort flag, string name)[] ButtonMap =
        {
            (0x0001, "DPadUp"),
            (0x0002, "DPadDown"),
            (0x0004, "DPadLeft"),
            (0x0008, "DPadRight"),
            (0x0010, "Start"),
            (0x0020, "Back"),
            (0x0040, "LeftThumb"),
            (0x0080, "RightThumb"),
            (0x0100, "LeftShoulder"),
            (0x0200, "RightShoulder"),
            (0x1000, "A"),
            (0x2000, "B"),
            (0x4000, "X"),
            (0x8000, "Y"),
        };

        private GamepadService()
        {
            for (int i = 0; i < MaxJoysticks; i++) _prevJoyPov[i] = string.Empty;

            _timer = new DispatcherTimer(DispatcherPriority.Input)
            {
                Interval = TimeSpan.FromMilliseconds(60)
            };
            _timer.Tick += (_, _) => Poll();
        }

        /// <summary>Begin polling. Safe to call multiple times.</summary>
        public void Start()
        {
            if (_started) return;
            _started = true;
            _timer.Start();
        }

        public void Stop()
        {
            _timer.Stop();
            _started = false;
        }

        private void Poll()
        {
            PollXInput();
            PollJoysticks();
        }

        // ---- XInput (Xbox controllers) --------------------------------------------------------
        private void PollXInput()
        {
            for (uint i = 0; i < MaxControllers; i++)
            {
                if (XInputGetState(i, out var state) != 0)
                {
                    _prevConnected[i] = false;
                    _prevButtons[i] = 0;
                    _prevLeftTrigger[i] = false;
                    _prevRightTrigger[i] = false;
                    continue;
                }

                ushort buttons = state.Gamepad.wButtons;
                ushort prev = _prevConnected[i] ? _prevButtons[i] : (ushort)0;
                int newlyPressed = buttons & ~prev;

                if (newlyPressed != 0)
                {
                    foreach (var (flag, name) in ButtonMap)
                    {
                        if ((newlyPressed & flag) != 0)
                            Raise(name);
                    }
                }

                bool lt = state.Gamepad.bLeftTrigger > TriggerThreshold;
                bool rt = state.Gamepad.bRightTrigger > TriggerThreshold;
                if (lt && !(_prevConnected[i] && _prevLeftTrigger[i])) Raise("LeftTrigger");
                if (rt && !(_prevConnected[i] && _prevRightTrigger[i])) Raise("RightTrigger");

                _prevButtons[i] = buttons;
                _prevLeftTrigger[i] = lt;
                _prevRightTrigger[i] = rt;
                _prevConnected[i] = true;
            }
        }

        // ---- winmm (DirectInput wheels / generic joysticks) -----------------------------------
        private void PollJoysticks()
        {
            uint count = 0;
            try { count = joyGetNumDevs(); }
            catch { return; }
            if (count == 0) return;
            if (count > MaxJoysticks) count = MaxJoysticks;

            for (uint id = 0; id < count; id++)
            {
                var info = new JOYINFOEX
                {
                    dwSize = (uint)Marshal.SizeOf<JOYINFOEX>(),
                    dwFlags = JOY_RETURNBUTTONS | JOY_RETURNPOV
                };

                uint result;
                try { result = joyGetPosEx(id, ref info); }
                catch { return; } // winmm not available

                if (result != 0) // JOYERR_NOERROR == 0; otherwise unplugged / no driver
                {
                    _prevJoyConnected[id] = false;
                    _prevJoyButtons[id] = 0;
                    _prevJoyPov[id] = string.Empty;
                    continue;
                }

                uint buttons = info.dwButtons;
                uint prev = _prevJoyConnected[id] ? _prevJoyButtons[id] : 0;
                uint newlyPressed = buttons & ~prev;
                if (newlyPressed != 0)
                {
                    for (int b = 0; b < 32; b++)
                    {
                        if ((newlyPressed & (1u << b)) != 0)
                            Raise("Button" + (b + 1));
                    }
                }

                // POV hat: report the four cardinal directions on entry.
                string pov = ClassifyPov(info.dwPOV);
                string prevPov = _prevJoyConnected[id] ? _prevJoyPov[id] : string.Empty;
                if (pov.Length != 0 && pov != prevPov)
                    Raise(pov);

                _prevJoyButtons[id] = buttons;
                _prevJoyPov[id] = pov;
                _prevJoyConnected[id] = true;
            }
        }

        private static string ClassifyPov(uint pov)
        {
            // Centered is 0xFFFF (or 0xFFFFFFFF); otherwise hundredths of a degree, 0 = up.
            if ((pov & 0xFFFF) == 0xFFFF) return string.Empty;
            uint deg = pov;
            if (deg > 31500 || deg <= 4500) return "PovUp";
            if (deg <= 13500) return "PovRight";
            if (deg <= 22500) return "PovDown";
            return "PovLeft";
        }

        private void Raise(string name)
        {
            try { ButtonPressed?.Invoke(name); } catch { }
        }

        // ---- XInput P/Invoke ------------------------------------------------------------------
        [StructLayout(LayoutKind.Sequential)]
        private struct XINPUT_GAMEPAD
        {
            public ushort wButtons;
            public byte bLeftTrigger;
            public byte bRightTrigger;
            public short sThumbLX;
            public short sThumbLY;
            public short sThumbRX;
            public short sThumbRY;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct XINPUT_STATE
        {
            public uint dwPacketNumber;
            public XINPUT_GAMEPAD Gamepad;
        }

        [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
        private static extern uint XInputGetState_14(uint dwUserIndex, out XINPUT_STATE pState);

        [DllImport("xinput9_1_0.dll", EntryPoint = "XInputGetState")]
        private static extern uint XInputGetState_91(uint dwUserIndex, out XINPUT_STATE pState);

        private static bool _useLegacyXInput;

        private static uint XInputGetState(uint index, out XINPUT_STATE state)
        {
            if (!_useLegacyXInput)
            {
                try { return XInputGetState_14(index, out state); }
                catch (DllNotFoundException) { _useLegacyXInput = true; }
            }
            try { return XInputGetState_91(index, out state); }
            catch (DllNotFoundException) { state = default; return 1167; /* ERROR_DEVICE_NOT_CONNECTED */ }
        }

        // ---- winmm P/Invoke -------------------------------------------------------------------
        private const uint JOY_RETURNBUTTONS = 0x00000080;
        private const uint JOY_RETURNPOV = 0x00000040;

        [StructLayout(LayoutKind.Sequential)]
        private struct JOYINFOEX
        {
            public uint dwSize;
            public uint dwFlags;
            public uint dwXpos;
            public uint dwYpos;
            public uint dwZpos;
            public uint dwRpos;
            public uint dwUpos;
            public uint dwVpos;
            public uint dwButtons;
            public uint dwButtonNumber;
            public uint dwPOV;
            public uint dwReserved1;
            public uint dwReserved2;
        }

        [DllImport("winmm.dll")]
        private static extern uint joyGetNumDevs();

        [DllImport("winmm.dll")]
        private static extern uint joyGetPosEx(uint uJoyID, ref JOYINFOEX pji);
    }
}
