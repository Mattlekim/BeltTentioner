using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace belttentiontest
{
    [StructLayout(LayoutKind.Sequential, Pack = 1, CharSet = CharSet.Ansi)]
    public struct MemoryMapFileFormat
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string CarName;
        public float LongStrengh;
        public int MaxPower;
        public double CurveAmount;
        public float CorneringStrength;
        public float VerticalStrength;
        public float AbsStrength;
        public byte AbsEnabled; // 0 = false, 1 = true
        public byte InvertCornering; // 0 = false, 1 = true
        public double ConeringCurveAmount;

        public float GForce, LateralG, VerticalG;

        public bool ConnectedToSim;
        public bool ConnectedToBelt;
    }
}
