using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace BeltTensionTest.WPF.Services
{
    public static class GlobalHotKeyManager
    {
        private static IntPtr _hwnd = IntPtr.Zero;
        private static HwndSource? _source;
        private static int _nextId = 9000;
        private static readonly Dictionary<int, Action> _handlers = new();
        private static readonly Dictionary<string, int> _nameToId = new();

        private const int WM_HOTKEY = 0x0312;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        public static void Initialize(Window window)
        {
            if (window == null) throw new ArgumentNullException(nameof(window));
            window.SourceInitialized += (s, e) =>
            {
                var helper = new WindowInteropHelper(window);
                _hwnd = helper.Handle;
                _source = HwndSource.FromHwnd(_hwnd);
                if (_source != null)
                {
                    _source.AddHook(WndProc);
                }
            };

            // Ensure cleanup on exit
            Application.Current.Exit += (_, _) => UnregisterAll();
        }

        private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY)
            {
                int id = wParam.ToInt32();
                if (_handlers.TryGetValue(id, out var action))
                {
                    try
                    {
                        // marshal to UI thread
                        Application.Current?.Dispatcher?.BeginInvoke(action);
                    }
                    catch { }
                    handled = true;
                }
            }
            return IntPtr.Zero;
        }

        public static bool Register(string name, string gesture, Action callback)
        {
            if (string.IsNullOrWhiteSpace(gesture)) return false;
            if (callback == null) return false;

            // unregister existing
            if (_nameToId.ContainsKey(name))
            {
                Unregister(name);
            }

            if (!TryParseGesture(gesture, out uint mods, out uint vk)) return false;
            if (_hwnd == IntPtr.Zero) return false;

            int id = System.Threading.Interlocked.Increment(ref _nextId);
            if (!RegisterHotKey(_hwnd, id, mods, vk))
            {
                return false;
            }

            _handlers[id] = callback;
            _nameToId[name] = id;
            return true;
        }

        public static void Unregister(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            if (!_nameToId.TryGetValue(name, out var id)) return;
            try
            {
                if (_hwnd != IntPtr.Zero)
                    UnregisterHotKey(_hwnd, id);
            }
            catch { }
            _handlers.Remove(id);
            _nameToId.Remove(name);
        }

        public static void UnregisterAll()
        {
            foreach (var kv in _nameToId)
            {
                try { if (_hwnd != IntPtr.Zero) UnregisterHotKey(_hwnd, kv.Value); } catch { }
            }
            _handlers.Clear();
            _nameToId.Clear();
        }

        private static bool TryParseGesture(string s, out uint mods, out uint vk)
        {
            mods = 0; vk = 0;
            if (string.IsNullOrWhiteSpace(s)) return false;
            var parts = s.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 0) return false;
            uint mod = 0;
            string keyPart = parts[^1];
            for (int i = 0; i < parts.Length - 1; i++)
            {
                var p = parts[i].ToLowerInvariant();
                if (p == "ctrl" || p == "control") mod |= 0x0002; // MOD_CONTROL
                else if (p == "alt") mod |= 0x0001; // MOD_ALT
                else if (p == "shift") mod |= 0x0004; // MOD_SHIFT
                else if (p == "win" || p == "windows") mod |= 0x0008; // MOD_WIN
            }

            // parse keyPart to virtual key
            try
            {
                System.Windows.Input.Key k;
                if (Enum.TryParse<System.Windows.Input.Key>(keyPart, true, out k))
                {
                    int v = System.Windows.Input.KeyInterop.VirtualKeyFromKey(k);
                    vk = (uint)v;
                    mods = mod;
                    return true;
                }
                // handle single character like 'A'
                if (keyPart.Length == 1)
                {
                    char c = keyPart[0];
                    int v = (int)char.ToUpperInvariant(c);
                    vk = (uint)v;
                    mods = mod;
                    return true;
                }
            }
            catch { }
            return false;
        }
    }
}
