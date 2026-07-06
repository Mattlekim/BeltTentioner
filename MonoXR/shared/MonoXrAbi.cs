// MonoXR shared ABI — C# mirror of shared/monoxr_abi.h.
// Blittable, Pack = 4, byte-for-byte identical to the C++ structs:
//   MonoXrOverlaySlot  = 204 bytes
//   control header     = 32 bytes  (slots[] start at offset 32)
//   full control block = 32 + 16*204 = 3296 bytes
using System.Runtime.InteropServices;

namespace MonoXR.Shared;

public static class MonoXrConstants
{
    public const string ControlName = @"Local\MonoXR-Control";
    public const uint Magic = 0x4D584F52u; // 'MXOR'
    public const uint Version = 1u;
    public const int MaxOverlays = 16;
    public const int NameLen = 64; // wchar_t count incl. null

    public const int SlotSize = 204;
    public const int HeaderSize = 32;
    public const int ControlSize = HeaderSize + MaxOverlays * SlotSize; // 3296

    // Keyed-mutex keys (see header).
    public const ulong KeyProducer = 0ul;
    public const ulong KeyConsumer = 1ul;
}

public enum MonoXrSpace : uint
{
    World = 0, // stationary in the play space (LOCAL)
    Head = 1,  // locked to the headset (VIEW)
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
public unsafe struct MonoXrOverlaySlot
{
    public uint Active;
    public uint Visible;
    public uint Space;      // MonoXrSpace
    public uint TexWidth;
    public uint TexHeight;
    public uint Format;     // DXGI_FORMAT

    public float PosX, PosY, PosZ;
    public float QuatX, QuatY, QuatZ, QuatW;
    public float SizeX, SizeY;

    public int ZOrder;
    public uint Pad0;

    public ulong FrameIndex;

    public fixed char SharedName[MonoXrConstants.NameLen]; // 64 * 2 = 128 bytes
}

[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct MonoXrControlHeader
{
    public uint Magic;
    public uint Version;
    public uint MaxOverlays;
    public uint ClientPid;
    public ulong ClientHeartbeat;
    public uint LayerActive;
    public uint LayerPid; // game's pid while LayerActive; 0 otherwise
    // slots[MaxOverlays] follow at offset HeaderSize.
}
