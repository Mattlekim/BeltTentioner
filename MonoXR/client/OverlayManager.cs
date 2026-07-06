using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
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

    /// <summary>
    /// True when this manager wraps the game's own D3D11 device, which is what
    /// makes the GPU-to-GPU <see cref="Overlay.Update(IntPtr)"/> path valid.
    /// </summary>
    public bool UsesExternalDevice { get; }

    private MonoXrControlHeader* Header => (MonoXrControlHeader*)_base;
    internal MonoXrOverlaySlot* Slot(int i) =>
        (MonoXrOverlaySlot*)(_base + MonoXrConstants.HeaderSize + i * MonoXrConstants.SlotSize);

    public OverlayManager() : this(IntPtr.Zero) { }

    /// <summary>
    /// Create a manager on an existing D3D11 device (e.g. MonoGame's, obtained
    /// via <see cref="MonoGameInterop.GetDevicePointer"/>). Overlay textures are
    /// then created on that device, which enables the zero-copy
    /// <see cref="Overlay.Update(IntPtr)"/> path: a GPU-side CopyResource from a
    /// render target straight into the shared texture, with no CPU readback.
    /// Pass <see cref="IntPtr.Zero"/> to create a private device instead (CPU
    /// upload path only).
    /// </summary>
    public OverlayManager(IntPtr d3d11Device)
    {
        if (d3d11Device != IntPtr.Zero)
        {
            // Wrap the caller's device. AddRef so our Dispose doesn't pull the
            // rug out from under the game; ImmediateContext also AddRefs.
            Marshal.AddRef(d3d11Device);
            Device = new ID3D11Device(d3d11Device);
            Context = Device.ImmediateContext;
            UsesExternalDevice = true;
        }
        else
        {
            // Own D3D11 device on the default adapter. Shared keyed-mutex textures
            // are visible to the game's device as long as it is the same adapter.
            FeatureLevel[] levels = { FeatureLevel.Level_11_1, FeatureLevel.Level_11_0 };
            D3D11.D3D11CreateDevice(
                null, DriverType.Hardware, DeviceCreationFlags.BgraSupport, levels,
                out ID3D11Device device, out ID3D11DeviceContext context).CheckError();
            Device = device;
            Context = context;
        }

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

    /// <summary>
    /// True while an OpenXR session with the MonoXR layer is running AND the
    /// game process is still alive. The layer clears its flag on clean session
    /// shutdown; if the game crashes or is killed, we notice the published PID
    /// is dead (checked at most every 500 ms) and report detached anyway.
    /// </summary>
    public bool LayerAttached
    {
        get
        {
            if (Header->LayerActive == 0) return false;
            uint pid = Header->LayerPid;
            if (pid == 0) return true; // layer build that predates LayerPid

            int now = Environment.TickCount;
            if (pid != _watchedLayerPid || now - _nextLivenessCheck >= 0)
            {
                _watchedLayerPid = pid;
                _nextLivenessCheck = now + 500;
                _layerProcessAlive = IsProcessAlive(pid);
                if (!_layerProcessAlive)
                {
                    // Game died without cleanup — clear the stale flags so a
                    // future game start is detected as a fresh attach.
                    Header->LayerActive = 0;
                    Header->LayerPid = 0;
                }
            }
            return _layerProcessAlive;
        }
    }

    /// <summary>
    /// Raised (from the thread that calls <see cref="Heartbeat"/>) when the
    /// OpenXR game attaches or goes away — true = attached, false = closed.
    /// </summary>
    public event Action<bool>? LayerAttachedChanged;

    private uint _watchedLayerPid;
    private int _nextLivenessCheck;
    private bool _layerProcessAlive;
    private bool _lastAttached;

    /// <summary>
    /// Call once per frame: lets the layer see the client is alive, and fires
    /// <see cref="LayerAttachedChanged"/> on attach/detach transitions.
    /// </summary>
    public void Heartbeat()
    {
        Header->ClientHeartbeat++;
        bool attached = LayerAttached;
        if (attached != _lastAttached)
        {
            _lastAttached = attached;
            LayerAttachedChanged?.Invoke(attached);
        }
    }

    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    private const uint STILL_ACTIVE = 259;

    private const int ERROR_INVALID_PARAMETER = 87; // OpenProcess: no such pid

    private static bool IsProcessAlive(uint pid)
    {
        IntPtr h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == IntPtr.Zero)
        {
            // Anti-cheat-protected games (e.g. iRacing) deny even limited query
            // access. Access-denied means the process exists — only a missing
            // pid means it is gone. Fail open on anything ambiguous.
            return Marshal.GetLastWin32Error() != ERROR_INVALID_PARAMETER;
        }
        try
        {
            return GetExitCodeProcess(h, out uint code) && code == STILL_ACTIVE;
        }
        finally { CloseProcessHandle(h); }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetExitCodeProcess(IntPtr process, out uint exitCode);

    [DllImport("kernel32.dll", EntryPoint = "CloseHandle", SetLastError = true)]
    private static extern bool CloseProcessHandle(IntPtr handle);

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
