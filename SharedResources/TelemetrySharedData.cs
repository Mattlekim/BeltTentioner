using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace SharedResources
{
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct TelemetrySharedData
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 20)]
        public string GameName;   // 20 chars, padded/truncated

        public float Braking;
        public float Cornering;
        public float Vertical;
        [MarshalAs(UnmanagedType.I1)]
        public bool GameRunning;
    }

}
