using System;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;

namespace BeltTensionTest.WPF.Services
{
    // Re-use the same struct layout from the WinForms project
    [StructLayout(LayoutKind.Sequential, Pack = 1, CharSet = CharSet.Ansi)]
    public struct MmfPayload
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string CarName;
        public float LongStrengh;
        public int MaxPower;
        public double CurveAmount;
        public float CorneringStrength;
        public float VerticalStrength;
        public float AbsStrength;
        public byte AbsEnabled;
        public byte InvertCornering;
        public double ConeringCurveAmount;
        public float GForce, LateralG, VerticalG;
        public bool ConnectedToSim;
        public bool ConnectedToBelt;
        public float MotorRange;
        public float MotorSwayValue;
        public float MotorSurgeValue;
        public float MotorHeaveValue;
    }

    /// <summary>
    /// Writes current state to the shared BeltTensionerSettings memory-mapped file
    /// so SimHub plugins can read it.
    /// </summary>
    public class MmfWriterService : IDisposable
    {
        private readonly MemoryMappedFile _mmf;
        private readonly MemoryMappedViewAccessor _accessor;
        private readonly int _structSize;
        private readonly byte[] _buffer;
        private readonly IntPtr _ptr;
        private bool _disposed;

        public MmfWriterService()
        {
            _structSize = Marshal.SizeOf<MmfPayload>();
            _buffer = new byte[_structSize];
            _ptr = Marshal.AllocHGlobal(_structSize);
            _mmf = MemoryMappedFile.CreateOrOpen("BeltTensionerSettings", 64 * 1024);
            _accessor = _mmf.CreateViewAccessor(0, 64 * 1024);
        }

        public void Write(MmfPayload payload)
        {
            if (_disposed) return;
            Marshal.StructureToPtr(payload, _ptr, false);
            Marshal.Copy(_ptr, _buffer, 0, _structSize);
            _accessor.WriteArray(0, _buffer, 0, _structSize);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Marshal.FreeHGlobal(_ptr);
            _accessor.Dispose();
            _mmf.Dispose();
        }
    }
}
