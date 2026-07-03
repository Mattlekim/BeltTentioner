using System.IO;
using System.IO.MemoryMappedFiles;
using MonoXR.Shared;
using SharpGen.Runtime;
using Vortice.Direct3D;
using Vortice.Direct3D11;

namespace MonoXR.Client;

/// <summary>
/// Owns the shared control block and the D3D11 device used to publish overlay
/// textures. Create one per process, call <see cref="CreateOverlay"/> for each
/// MonoGame render target you want to show in VR, and <see cref="Heartbeat"/>
/// once per frame.
/// </summary>
public sealed unsafe class OverlayManager : IDisposable
{
    private readonly MemoryMappedFile _mmf;
    private readonly MemoryMappedViewAccessor _view;
    private readonly byte* _base;
    private readonly List<Overlay> _overlays = new();
    private bool _disposed;

    internal ID3D11Device Device { get; }
    internal ID3D11DeviceContext Context { get; }

    /// <summary>
    /// True if this manager attached to a control block that already existed
    /// (e.g. the VR game's layer created/held it) rather than creating a new one.
    /// </summary>
    public bool AttachedToExisting { get; }

    private MonoXrControlHeader* Header => (MonoXrControlHeader*)_base;
    internal MonoXrOverlaySlot* Slot(int i) =>
        (MonoXrOverlaySlot*)(_base + MonoXrConstants.HeaderSize + i * MonoXrConstants.SlotSize);

    public OverlayManager()
    {
        // Own D3D11 device on the default adapter. Shared keyed-mutex textures
        // are visible to the game's device as long as it is the same adapter.
        FeatureLevel[] levels = { FeatureLevel.Level_11_1, FeatureLevel.Level_11_0 };
        D3D11.D3D11CreateDevice(
            null, DriverType.Hardware, DeviceCreationFlags.BgraSupport, levels,
            out ID3D11Device device, out ID3D11DeviceContext context).CheckError();
        Device = device;
        Context = context;

        // Attach to the existing control block if one is already present,
        // otherwise create it. The block can outlive the client that created it:
        // the native layer (inside the VR game) holds a handle open for the whole
        // session, so after this app is closed and relaunched — while the game is
        // still running — the named block still exists. In that case we must reuse
        // it, not fail.
        try
        {
            _mmf = MemoryMappedFile.OpenExisting(MonoXrConstants.ControlName, MemoryMappedFileRights.ReadWrite);
            AttachedToExisting = true;
        }
        catch (FileNotFoundException)
        {
            _mmf = MemoryMappedFile.CreateNew(MonoXrConstants.ControlName, MonoXrConstants.ControlSize);
            AttachedToExisting = false;
        }

        _view = _mmf.CreateViewAccessor(0, MonoXrConstants.ControlSize, MemoryMappedFileAccess.ReadWrite);
        byte* p = null;
        _view.SafeMemoryMappedViewHandle.AcquirePointer(ref p);
        _base = p;

        // Only (re)initialize the header when we created the block, or when an
        // existing block is stale/incompatible. When attaching to a live, valid
        // block we leave its contents — especially LayerActive, which the layer
        // owns — intact, and just take over as the publishing client. New
        // overlays overwrite slots starting at index 0, so any orphaned slots
        // from a previous client are reclaimed as we recreate them.
        bool needsInit = !AttachedToExisting
                         || Header->Magic != MonoXrConstants.Magic
                         || Header->Version != MonoXrConstants.Version;
        if (needsInit)
        {
            new Span<byte>(_base, MonoXrConstants.ControlSize).Clear();
            Header->Magic = MonoXrConstants.Magic;
            Header->Version = MonoXrConstants.Version;
            Header->MaxOverlays = MonoXrConstants.MaxOverlays;
            Header->LayerActive = 0;
        }
        Header->ClientPid = (uint)Environment.ProcessId;
    }

    /// <summary>True once an OpenXR session with the MonoXR layer is running.</summary>
    public bool LayerAttached => Header->LayerActive != 0;

    /// <summary>Call once per frame so the layer can tell the client is alive.</summary>
    public void Heartbeat() => Header->ClientHeartbeat++;

    /// <summary>
    /// Allocate an overlay backed by a shared texture of the given size.
    /// Feed it each frame with <see cref="Overlay.Update"/>.
    /// </summary>
    public Overlay CreateOverlay(int width, int height)
    {
        if (_overlays.Count >= MonoXrConstants.MaxOverlays)
            throw new InvalidOperationException($"At most {MonoXrConstants.MaxOverlays} overlays are supported.");
        int index = _overlays.Count;
        var overlay = new Overlay(this, index, width, height);
        _overlays.Add(overlay);
        return overlay;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var o in _overlays) o.Dispose();
        _overlays.Clear();
        if (_base != null) _view.SafeMemoryMappedViewHandle.ReleasePointer();
        _view.Dispose();
        _mmf.Dispose();
        Context.Dispose();
        Device.Dispose();
    }
}
