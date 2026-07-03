using System.Numerics;
using System.Runtime.InteropServices;
using MonoXR.Shared;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace MonoXR.Client;

/// <summary>
/// One overlay = one shared texture published to the OpenXR layer as a quad.
/// Set <see cref="Position"/>/<see cref="Rotation"/>/<see cref="Size"/>/<see cref="Space"/>
/// freely, then call <see cref="Update"/> each frame with fresh RGBA pixels
/// (e.g. from <c>RenderTarget2D.GetData</c>).
/// </summary>
public sealed unsafe class Overlay : IDisposable
{
    // DXGI_FORMAT_R8G8B8A8_UNORM — matches MonoGame SurfaceFormat.Color byte order.
    private const uint DXGI_R8G8B8A8_UNORM = 28;

    private readonly OverlayManager _mgr;
    private readonly int _index;
    private readonly int _width;
    private readonly int _height;
    private readonly string _name;
    private readonly ID3D11Texture2D _tex;
    private readonly IDXGIResource1 _resource;
    private readonly IDXGIKeyedMutex? _mutex;
    private readonly IntPtr _mutexPtr;
    private readonly IntPtr _sharedHandle;
    private ulong _frame;
    private bool _disposed;

    // GPU-path state: the shared texture opened on an external (caller-owned)
    // D3D11 device, so frames can be copied texture-to-texture without ever
    // touching the CPU. Wrappers around caller pointers are borrowed (no AddRef)
    // and must never be disposed; the opened texture/mutex/context are ours.
    private IntPtr _extDevicePtr;
    private ID3D11DeviceContext? _extContext;
    private ID3D11Texture2D? _extTex;
    private IDXGIKeyedMutex? _extMutex;
    private IntPtr _extMutexPtr;
    private IntPtr _extSrcPtr;
    private ID3D11Resource? _extSrc;

    // Pose is applied every Update(). Defaults: 1m in front, ~1m wide.
    public Vector3 Position = new(0f, 0f, -1f);
    public Quaternion Rotation = Quaternion.Identity;
    public Vector2 Size = new(1.0f, 0.6f);
    public MonoXrSpace Space = MonoXrSpace.World;
    public bool Visible = true;
    public int ZOrder;

    public int Width => _width;
    public int Height => _height;

    internal Overlay(OverlayManager mgr, int index, int width, int height)
    {
        _mgr = mgr; _index = index; _width = width; _height = height;
        _name = $@"Local\MonoXR-tex-{Environment.ProcessId}-{index}-{Guid.NewGuid():N}";

        var desc = new Texture2DDescription(
            Format.R8G8B8A8_UNorm, (uint)width, (uint)height,
            arraySize: 1, mipLevels: 1,
            BindFlags.ShaderResource | BindFlags.RenderTarget,
            ResourceUsage.Default, CpuAccessFlags.None,
            sampleCount: 1, sampleQuality: 0,
            ResourceOptionFlags.SharedKeyedMutex | ResourceOptionFlags.SharedNTHandle);
        _tex = mgr.Device.CreateTexture2D(desc, default(Span<SubresourceData>));

        _resource = _tex.QueryInterface<IDXGIResource1>();
        _sharedHandle = _resource.CreateSharedHandle(null,
            Vortice.DXGI.SharedResourceFlags.Read | Vortice.DXGI.SharedResourceFlags.Write, _name);

        _mutex = _tex.QueryInterface<IDXGIKeyedMutex>();
        _mutexPtr = _mutex.NativePointer;

        // Publish the slot so the layer can find and open us.
        var slot = mgr.Slot(index);
        slot->TexWidth = (uint)width;
        slot->TexHeight = (uint)height;
        slot->Format = DXGI_R8G8B8A8_UNORM;
        WriteName(slot, _name);
        WriteMetadata(slot);
        slot->Active = 1;
    }

    /// <summary>
    /// Copy a frame of RGBA8 pixels into the shared texture and refresh the pose.
    /// <paramref name="rgba"/> must be at least width*height*4 bytes, row-major, no padding.
    /// </summary>
    public void Update(ReadOnlySpan<byte> rgba)
    {
        if (_disposed) return;
        int expected = _width * _height * 4;
        if (rgba.Length < expected)
            throw new ArgumentException($"Expected {expected} bytes, got {rgba.Length}.", nameof(rgba));

        var slot = _mgr.Slot(_index);
        WriteMetadata(slot);

        // Non-blocking producer handoff. AcquireSync returns WAIT_TIMEOUT (non-zero)
        // if the layer still holds the previous frame — then we keep the last good
        // frame rather than stall the game loop.
        if (_mutexPtr != IntPtr.Zero &&
            KeyedMutex.AcquireSync(_mutexPtr, MonoXrConstants.KeyProducer, 0) != 0)
            return;

        fixed (byte* src = rgba)
        {
            _mgr.Context.UpdateSubresource(_tex, 0u, null, (IntPtr)src, (uint)(_width * 4), 0u);
        }

        if (_mutexPtr != IntPtr.Zero)
            KeyedMutex.ReleaseSync(_mutexPtr, MonoXrConstants.KeyConsumer);

        _frame++;
        slot->FrameIndex = _frame;
    }

    /// <summary>
    /// Zero-copy update: copy a GPU texture straight into the shared texture,
    /// entirely on the GPU. <paramref name="devicePtr"/> is the caller's native
    /// ID3D11Device* and <paramref name="texturePtr"/> a native ID3D11Resource*
    /// created on that device with the same size and format as this overlay
    /// (R8G8B8A8_UNORM, single-sample). The shared texture is opened on the
    /// caller's device on first use; pixels never round-trip through the CPU.
    /// </summary>
    public void UpdateFromTexture(IntPtr devicePtr, IntPtr texturePtr)
    {
        if (_disposed) return;
        if (devicePtr == IntPtr.Zero || texturePtr == IntPtr.Zero)
            throw new ArgumentException("Native device/texture pointer is null.");

        var slot = _mgr.Slot(_index);
        WriteMetadata(slot);

        if (_extDevicePtr != devicePtr)
            OpenOnExternalDevice(devicePtr);

        if (_extSrcPtr != texturePtr)
        {
            _extSrc = new ID3D11Resource(texturePtr); // borrowed — never disposed
            _extSrcPtr = texturePtr;
        }

        // Same non-blocking producer handoff as Update(): skip the frame if the
        // layer still holds the texture rather than stall the caller.
        if (KeyedMutex.AcquireSync(_extMutexPtr, MonoXrConstants.KeyProducer, 0) != 0)
            return;

        _extContext!.CopyResource(_extTex!, _extSrc!);
        KeyedMutex.ReleaseSync(_extMutexPtr, MonoXrConstants.KeyConsumer);
        _extContext.Flush();

        _frame++;
        slot->FrameIndex = _frame;
    }

    private void OpenOnExternalDevice(IntPtr devicePtr)
    {
        ReleaseExternal();

        var borrowed = new ID3D11Device(devicePtr); // borrowed — never disposed
        using var dev1 = borrowed.QueryInterface<ID3D11Device1>();
        _extContext = borrowed.ImmediateContext;
        _extTex = dev1.OpenSharedResource1<ID3D11Texture2D>(_sharedHandle);
        _extMutex = _extTex.QueryInterface<IDXGIKeyedMutex>();
        _extMutexPtr = _extMutex.NativePointer;
        _extDevicePtr = devicePtr;
    }

    private void ReleaseExternal()
    {
        _extMutex?.Dispose();
        _extTex?.Dispose();
        _extContext?.Dispose();
        _extMutex = null; _extTex = null; _extContext = null;
        _extMutexPtr = IntPtr.Zero;
        _extDevicePtr = IntPtr.Zero;
        _extSrc = null; _extSrcPtr = IntPtr.Zero;
    }

    private void WriteMetadata(MonoXrOverlaySlot* slot)
    {
        slot->Visible = Visible ? 1u : 0u;
        slot->Space = (uint)Space;
        slot->ZOrder = ZOrder;
        slot->PosX = Position.X; slot->PosY = Position.Y; slot->PosZ = Position.Z;
        slot->QuatX = Rotation.X; slot->QuatY = Rotation.Y; slot->QuatZ = Rotation.Z; slot->QuatW = Rotation.W;
        slot->SizeX = Size.X; slot->SizeY = Size.Y;
    }

    private static void WriteName(MonoXrOverlaySlot* slot, string name)
    {
        int n = Math.Min(name.Length, MonoXrConstants.NameLen - 1);
        for (int i = 0; i < n; i++) slot->SharedName[i] = name[i];
        slot->SharedName[n] = '\0';
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        var slot = _mgr.Slot(_index);
        slot->Active = 0;
        slot->Visible = 0;
        ReleaseExternal();
        _mutex?.Dispose();
        _resource.Dispose();
        if (_sharedHandle != IntPtr.Zero) CloseHandle(_sharedHandle);
        _tex.Dispose();
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);
}

/// <summary>
/// Vortice's IDXGIKeyedMutex.AcquireSync returns void (throws only on failed
/// HRESULTs), so it cannot report WAIT_TIMEOUT. We call the COM vtable directly
/// to get the raw HRESULT and implement a non-blocking try-acquire.
/// vtable layout: IUnknown(0-2) IDXGIObject(3-6) IDXGIDeviceSubObject(7)
///                IDXGIKeyedMutex.AcquireSync(8) .ReleaseSync(9)
/// </summary>
internal static unsafe class KeyedMutex
{
    public static int AcquireSync(IntPtr p, ulong key, uint ms)
    {
        var fn = (delegate* unmanaged[Stdcall]<IntPtr, ulong, uint, int>)(*(void***)p)[8];
        return fn(p, key, ms);
    }

    public static int ReleaseSync(IntPtr p, ulong key)
    {
        var fn = (delegate* unmanaged[Stdcall]<IntPtr, ulong, int>)(*(void***)p)[9];
        return fn(p, key);
    }
}
