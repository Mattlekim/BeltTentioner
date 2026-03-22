using System;
using System.Collections.Generic;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace SharedResources
{
    public class TelemetryMmfWriter : IDisposable
    {
        private readonly MemoryMappedFile _mmf;
        private readonly MemoryMappedViewAccessor _accessor;
        private readonly int _size;

        public TelemetryMmfWriter(string mapName = "SimHubTelemetry")
        {
            _size = Marshal.SizeOf<TelemetrySharedData>();
            _mmf = MemoryMappedFile.CreateOrOpen(mapName, _size);
            _accessor = _mmf.CreateViewAccessor(0, _size, MemoryMappedFileAccess.ReadWrite);
        }

        public void Write(TelemetrySharedData data)
        {
            byte[] buffer = new byte[_size];
            IntPtr ptr = Marshal.AllocHGlobal(_size);
            try
            {
                Marshal.StructureToPtr(data, ptr, false);
                Marshal.Copy(ptr, buffer, 0, _size);
                _accessor.WriteArray(0, buffer, 0, _size);
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }

        public void Dispose()
        {
            _accessor?.Dispose();
            _mmf?.Dispose();
        }
    }

}
