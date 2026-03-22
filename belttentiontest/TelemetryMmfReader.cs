using SharedResources;
using System;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;


namespace SharedResources
{
    public class TelemetryMmfReader : IDisposable
    {
        private readonly MemoryMappedFile _mmf;
        private readonly MemoryMappedViewAccessor _accessor;
        private readonly int _size;

        public bool Connected = false;
        public TelemetryMmfReader(string mapName = "SimHubTelemetry")
        {
            _size = Marshal.SizeOf<TelemetrySharedData>();
            try
            {
                Connected = true;
                _mmf = MemoryMappedFile.OpenExisting(mapName);
            }
            catch
            {
                Connected = false;
                return;
            }
            _accessor = _mmf.CreateViewAccessor(0, _size, MemoryMappedFileAccess.Read);
        }

        public TelemetrySharedData Read()
        {
            if (!Connected)
                return default;

            byte[] buffer = new byte[_size];
            _accessor.ReadArray(0, buffer, 0, _size);

            IntPtr ptr = Marshal.AllocHGlobal(_size);
            try
            {
                Marshal.Copy(buffer, 0, ptr, _size);
                return Marshal.PtrToStructure<TelemetrySharedData>(ptr);
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