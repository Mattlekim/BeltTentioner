using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace BeltTensionTest.WPF.Services
{
    /// <summary>One GPU reading. <see cref="Available"/> is false when no NVIDIA GPU / driver was found.</summary>
    public sealed record GpuSnapshot(
        bool Available,
        string Name,
        int UsagePercent,
        int TemperatureC,
        ulong MemoryUsedBytes,
        ulong MemoryTotalBytes)
    {
        public static readonly GpuSnapshot None = new(false, "No NVIDIA GPU", 0, 0, 0, 0);
    }

    /// <summary>
    /// Polls GPU load, temperature and VRAM through NVIDIA's NVML (nvml.dll,
    /// installed with the driver) once a second on a background timer, so the
    /// render thread only ever reads <see cref="Latest"/>. GPU 0 is reported.
    /// Without an NVIDIA driver <see cref="Latest"/> stays <see cref="GpuSnapshot.None"/>.
    /// </summary>
    public sealed class GpuMonitorService
    {
        private static readonly Lazy<GpuMonitorService> _instance = new(() => new GpuMonitorService());
        public static GpuMonitorService Instance => _instance.Value;

        private readonly Timer _timer;
        private IntPtr _device;
        private bool _nvmlReady;
        private bool _nvmlFailed;
        private string _name = "";
        private volatile GpuSnapshot _latest = GpuSnapshot.None;
        private int _polling; // re-entrancy guard for the timer callback

        /// <summary>Most recent reading (replaced as a whole, safe to read from any thread).</summary>
        public GpuSnapshot Latest => _latest;

        private GpuMonitorService()
        {
            _timer = new Timer(_ => Poll(), null, 0, 1000);
        }

        private void Poll()
        {
            if (Interlocked.Exchange(ref _polling, 1) == 1) return;
            try
            {
                if (!EnsureNvml()) return;

                if (Nvml.nvmlDeviceGetUtilizationRates(_device, out var util) != 0 ||
                    Nvml.nvmlDeviceGetTemperature(_device, Nvml.TemperatureGpu, out uint temp) != 0 ||
                    Nvml.nvmlDeviceGetMemoryInfo(_device, out var mem) != 0)
                    return; // keep the last good reading

                _latest = new GpuSnapshot(true, _name, (int)util.Gpu, (int)temp, mem.Used, mem.Total);
            }
            catch (Exception)
            {
                // DllNotFound / EntryPointNotFound: not an NVIDIA system.
                _nvmlFailed = true;
                _timer.Change(Timeout.Infinite, Timeout.Infinite);
            }
            finally
            {
                Volatile.Write(ref _polling, 0);
            }
        }

        private bool EnsureNvml()
        {
            if (_nvmlReady) return true;
            if (_nvmlFailed) return false;

            if (Nvml.nvmlInit_v2() != 0 || Nvml.nvmlDeviceGetHandleByIndex_v2(0, out _device) != 0)
            {
                _nvmlFailed = true;
                _timer.Change(Timeout.Infinite, Timeout.Infinite);
                return false;
            }

            var buf = new byte[96];
            _name = Nvml.nvmlDeviceGetName(_device, buf, (uint)buf.Length) == 0
                ? Encoding.ASCII.GetString(buf, 0, Math.Max(0, Array.IndexOf(buf, (byte)0))).Trim()
                : "NVIDIA GPU";
            if (_name.StartsWith("NVIDIA ", StringComparison.Ordinal)) _name = _name.Substring(7);
            _nvmlReady = true;
            return true;
        }

        private static class Nvml
        {
            private const string Dll = "nvml.dll";
            public const int TemperatureGpu = 0; // NVML_TEMPERATURE_GPU

            [StructLayout(LayoutKind.Sequential)]
            public struct Utilization { public uint Gpu; public uint Memory; }

            [StructLayout(LayoutKind.Sequential)]
            public struct Memory { public ulong Total; public ulong Free; public ulong Used; }

            [DllImport(Dll)] public static extern int nvmlInit_v2();
            [DllImport(Dll)] public static extern int nvmlDeviceGetHandleByIndex_v2(uint index, out IntPtr device);
            [DllImport(Dll)] public static extern int nvmlDeviceGetName(IntPtr device, byte[] name, uint length);
            [DllImport(Dll)] public static extern int nvmlDeviceGetUtilizationRates(IntPtr device, out Utilization utilization);
            [DllImport(Dll)] public static extern int nvmlDeviceGetTemperature(IntPtr device, int sensor, out uint temp);
            [DllImport(Dll)] public static extern int nvmlDeviceGetMemoryInfo(IntPtr device, out Memory memory);
        }
    }
}
