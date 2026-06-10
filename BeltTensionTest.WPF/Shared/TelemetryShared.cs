using System;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;

namespace BeltTensionTest.WPF.Shared
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct TelemetrySharedData
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 20)]
        public string GameName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 20)]
        public string CarName;
        public float Braking;
        public float Cornering;
        public float Vertical;
        public float RotationPitch;
        public float RotationRoll;
        public float RotationYaw;
        [MarshalAs(UnmanagedType.I1)]
        public bool GameRunning;
        [MarshalAs(UnmanagedType.I1)]
        public bool SupportBraking;
        [MarshalAs(UnmanagedType.I1)]
        public bool SupportCornering;
        [MarshalAs(UnmanagedType.I1)]
        public bool SupportVertical;
        [MarshalAs(UnmanagedType.I1)]
        public bool Paused;
    }

    /// <summary>
    /// Reads the SimHub telemetry memory-mapped file written by the SimHub plugin.
    /// </summary>
    public class TelemetryMmfReader : IDisposable
    {
        private readonly MemoryMappedFile?         _mmf;
        private readonly MemoryMappedViewAccessor? _accessor;
        private readonly int                       _size;

        public bool Connected { get; private set; }

        public TelemetryMmfReader(string mapName = "SimHubTelemetry")
        {
            _size = Marshal.SizeOf<TelemetrySharedData>();
            try
            {
                _mmf = MemoryMappedFile.OpenExisting(mapName);
                _accessor = _mmf.CreateViewAccessor(0, _size, MemoryMappedFileAccess.Read);
                Connected = true;
            }
            catch { Connected = false; }
        }

        public TelemetrySharedData Read()
        {
            if (!Connected || _accessor == null) return default;
            var buf = new byte[_size];
            _accessor.ReadArray(0, buf, 0, _size);
            var ptr = Marshal.AllocHGlobal(_size);
            try
            {
                Marshal.Copy(buf, 0, ptr, _size);
                return Marshal.PtrToStructure<TelemetrySharedData>(ptr);
            }
            finally { Marshal.FreeHGlobal(ptr); }
        }

        public void Dispose()
        {
            _accessor?.Dispose();
            _mmf?.Dispose();
        }
    }
}
