using System;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using belttentiontest;

namespace belttentiontest
{
    public class MemoryMapFileWriter : IDisposable
    {
        private readonly MemoryMappedFile _mmf;
        private readonly MemoryMappedViewAccessor _accessor;
        private readonly int _structSize;
        private readonly byte[] _buffer;
        private readonly IntPtr _ptr;
        private bool _disposed;

        public MemoryMapFileWriter(string mapName, int size)
        {
            _mmf = MemoryMappedFile.CreateOrOpen(mapName, size);
            _accessor = _mmf.CreateViewAccessor(0, size);
            _structSize = Marshal.SizeOf<MemoryMapFileFormat>();
            _buffer = new byte[_structSize];
            _ptr = Marshal.AllocHGlobal(_structSize);
        }

        public void WriteSettings(MemoryMapFileFormat settings)
        {
            Marshal.StructureToPtr(settings, _ptr, false);
            Marshal.Copy(_ptr, _buffer, 0, _structSize);
            _accessor.WriteArray(0, _buffer, 0, _structSize);
        }

        public MemoryMapFileFormat ReadSettings()
        {
            _accessor.ReadArray(0, _buffer, 0, _structSize);
            Marshal.Copy(_buffer, 0, _ptr, _structSize);
            return Marshal.PtrToStructure<MemoryMapFileFormat>(_ptr);
        }

        public void Dispose()
        {
            if (_disposed) return;
            Marshal.FreeHGlobal(_ptr);
            _accessor.Dispose();
            _mmf.Dispose();
            _disposed = true;
        }
    }
}
