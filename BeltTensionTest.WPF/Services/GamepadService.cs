using System;
using System.Collections.Generic;
using System.Linq;
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

        /// <summary>
        /// Raised (on the UI thread) with the button name when a control is
        /// newly pressed. <see cref="IsHeld"/> already reflects the same poll,
        /// so a handler can check what else is being held (button combos).
        /// </summary>
        public event Action<string>? ButtonPressed;

        /// <summary>Raised (on the UI thread) after every poll, once presses have been raised.</summary>
        public event Action? Polled;

        // Every control down as of the last poll, across all devices.
        private HashSet<string> _held = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _pressedThisPoll = new();

        /// <summary>True while the named control is held down (as of the last poll).</summary>
        public bool IsHeld(string name) => _held.Contains(name);

        // When each control was last seen held (Environment.TickCount64), so a combo of quick taps still counts.
        private readonly Dictionary<string, long> _lastHeld = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>True while the control is held, and for <paramref name="milliseconds"/> after it was let go.</summary>
        public bool HeldWithin(string name, int milliseconds) =>
            _held.Contains(name) || (_lastHeld.TryGetValue(name, out long at) && Environment.TickCount64 - at <= milliseconds);

        /// <summary>Snapshot of every control currently held down.</summary>
        public IReadOnlyCollection<string> HeldButtons => _held.ToArray();

        // Every analog axis as of the last poll, 0..1 (sticks and wheel / pedal axes are 0 at one end of their
        // travel and 1 at the other, a centred stick 0.5). The first device uses the plain names ("AxisZ",
        // "LeftStickX", "LTrigger"); the others get a prefix ("Joy2AxisZ", "Pad2LeftStickX"). They are not part
        // of the held set: an idle pedal or an unused axis would otherwise look like a button held down.
        private Dictionary<string, double> _axes = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>The named axis' position, 0..1, or false when no connected device has it.</summary>
        public bool TryGetAxis(string name, out double value) => _axes.TryGetValue(name, out value);

        /// <summary>Snapshot of every axis' position, 0..1.</summary>
        public IReadOnlyDictionary<string, double> Axes => new Dictionary<string, double>(_axes, StringComparer.OrdinalIgnoreCase);

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
        private readonly JoyCaps?[] _joyCaps = new JoyCaps?[MaxJoysticks];

        // What a winmm device really has. Windows lists "ghost" joysticks that answer every poll with success and
        // all zeros; read as a hat, that zero is "up", so a button such as PovUp would look held down for ever.
        private sealed record JoyCaps(bool HasPov, double[] Min, double[] Max, bool[] HasAxis);

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
                Interval = TimeSpan.FromMilliseconds(20)
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

        // Poll every device first, then raise the presses: that way a handler
        // for one press sees the full held state, whichever device the other
        // buttons of a combo live on.
        private void Poll()
        {
            var held = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var axes = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            _pressedThisPoll.Clear();
            PollXInput(held, axes);
            PollJoysticks(held, axes);
            _held = held;
            _axes = axes;
            long now = Environment.TickCount64;
            foreach (var name in held) _lastHeld[name] = now;
            foreach (var name in _pressedThisPoll)
            {
                try { ButtonPressed?.Invoke(name); } catch { }
            }
            try { Polled?.Invoke(); } catch { }
        }

        // ---- XInput (Xbox controllers) --------------------------------------------------------
        private void PollXInput(HashSet<string> held, Dictionary<string, double> axes)
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

                foreach (var (flag, name) in ButtonMap)
                {
                    if ((buttons & flag) != 0) held.Add(name);
                    if ((newlyPressed & flag) != 0) Raise(name);
                }

                string pad = i == 0 ? string.Empty : "Pad" + (i + 1);
                axes[pad + "LeftStickX"] = (state.Gamepad.sThumbLX + 32768) / 65535.0;
                axes[pad + "LeftStickY"] = (state.Gamepad.sThumbLY + 32768) / 65535.0;
                axes[pad + "RightStickX"] = (state.Gamepad.sThumbRX + 32768) / 65535.0;
                axes[pad + "RightStickY"] = (state.Gamepad.sThumbRY + 32768) / 65535.0;
                axes[pad + "LTrigger"] = state.Gamepad.bLeftTrigger / 255.0;
                axes[pad + "RTrigger"] = state.Gamepad.bRightTrigger / 255.0;

                bool lt = state.Gamepad.bLeftTrigger > TriggerThreshold;
                bool rt = state.Gamepad.bRightTrigger > TriggerThreshold;
                if (lt) held.Add("LeftTrigger");
                if (rt) held.Add("RightTrigger");
                if (lt && !(_prevConnected[i] && _prevLeftTrigger[i])) Raise("LeftTrigger");
                if (rt && !(_prevConnected[i] && _prevRightTrigger[i])) Raise("RightTrigger");

                _prevButtons[i] = buttons;
                _prevLeftTrigger[i] = lt;
                _prevRightTrigger[i] = rt;
                _prevConnected[i] = true;
            }
        }

        // ---- winmm (DirectInput wheels / generic joysticks) -----------------------------------
        private void PollJoysticks(HashSet<string> held, Dictionary<string, double> axes)
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
                    dwFlags = JOY_RETURNBUTTONS | JOY_RETURNPOV | JOY_RETURNAXES
                };

                uint result;
                try { result = joyGetPosEx(id, ref info); }
                catch { return; } // winmm not available

                if (result != 0) // JOYERR_NOERROR == 0; otherwise unplugged / no driver
                {
                    _prevJoyConnected[id] = false;
                    _prevJoyButtons[id] = 0;
                    _prevJoyPov[id] = string.Empty;
                    _joyCaps[id] = null;
                    continue;
                }

                if (!_prevJoyConnected[id] || _joyCaps[id] == null) _joyCaps[id] = ReadCaps(id);
                var caps = _joyCaps[id];
                if (caps == null) // no buttons, axes or hat: not a real device
                {
                    _prevJoyConnected[id] = false;
                    continue;
                }

                string joy = id == 0 ? string.Empty : "Joy" + (id + 1);
                uint[] raw = { info.dwXpos, info.dwYpos, info.dwZpos, info.dwRpos, info.dwUpos, info.dwVpos };
                string[] axisNames = { "AxisX", "AxisY", "AxisZ", "AxisR", "AxisU", "AxisV" };
                for (int a = 0; a < 6; a++)
                {
                    if (!caps.HasAxis[a]) continue;
                    double span = Math.Max(1.0, caps.Max[a] - caps.Min[a]);
                    axes[joy + axisNames[a]] = Math.Clamp((raw[a] - caps.Min[a]) / span, 0.0, 1.0);
                }

                uint buttons = info.dwButtons;
                uint prev = _prevJoyConnected[id] ? _prevJoyButtons[id] : 0;
                uint newlyPressed = buttons & ~prev;
                for (int b = 0; b < 32; b++)
                {
                    if ((buttons & (1u << b)) != 0) held.Add("Button" + (b + 1));
                    if ((newlyPressed & (1u << b)) != 0) Raise("Button" + (b + 1));
                }

                // POV hat: report the four cardinal directions on entry.
                string pov = caps.HasPov ? ClassifyPov(info.dwPOV) : string.Empty;
                if (pov.Length != 0) held.Add(pov);
                string prevPov = _prevJoyConnected[id] ? _prevJoyPov[id] : string.Empty;
                if (pov.Length != 0 && pov != prevPov)
                    Raise(pov);

                _prevJoyButtons[id] = buttons;
                _prevJoyPov[id] = pov;
                _prevJoyConnected[id] = true;
            }
        }

        private static JoyCaps? ReadCaps(uint id)
        {
            try
            {
                var c = new JOYCAPS();
                if (joyGetDevCapsW((UIntPtr)id, ref c, (uint)Marshal.SizeOf<JOYCAPS>()) != 0) return null;
                bool hasPov = (c.wCaps & JOYCAPS_HASPOV) != 0;
                if (c.wNumButtons == 0 && c.wNumAxes == 0 && !hasPov) return null;
                return new JoyCaps(hasPov,
                    new double[] { c.wXmin, c.wYmin, c.wZmin, c.wRmin, c.wUmin, c.wVmin },
                    new double[] { c.wXmax, c.wYmax, c.wZmax, c.wRmax, c.wUmax, c.wVmax },
                    new[] { true, true, (c.wCaps & 0x01) != 0, (c.wCaps & 0x02) != 0, (c.wCaps & 0x04) != 0, (c.wCaps & 0x08) != 0 });
            }
            catch { return null; }
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

        // Queued; Poll raises them once every device's held state is known.
        private void Raise(string name) => _pressedThisPoll.Add(name);

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
        private const uint JOY_RETURNAXES = 0x0000003F; // X, Y, Z, R, U and V

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

        private const uint JOYCAPS_HASPOV = 0x0010;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct JOYCAPS
        {
            public ushort wMid, wPid;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szPname;
            public uint wXmin, wXmax, wYmin, wYmax, wZmin, wZmax, wNumButtons, wPeriodMin, wPeriodMax;
            public uint wRmin, wRmax, wUmin, wUmax, wVmin, wVmax, wCaps, wMaxAxes, wNumAxes, wMaxButtons;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szRegKey;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szOEMVxD;
        }

        [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
        private static extern uint joyGetDevCapsW(UIntPtr uJoyID, ref JOYCAPS pjc, uint cbjc);

        [DllImport("winmm.dll")]
        private static extern uint joyGetNumDevs();

        [DllImport("winmm.dll")]
        private static extern uint joyGetPosEx(uint uJoyID, ref JOYINFOEX pji);
    }
}
