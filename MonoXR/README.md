# MonoXR

Inject **MonoGame render targets into OpenXR as overlays**. Each `RenderTarget2D`
becomes an `XrCompositionLayerQuad` in the running VR app, with programmatic
position, orientation, and size — world-locked or head-locked.

This is the same architecture DesktopXR uses (an OpenXR **API layer** that adds
composition layers inside the game's own frame submission), but with an inlet so
*your* textures can be shown. It works across OpenXR runtimes — including SteamVR —
because the layer runs inside the game's OpenXR call chain rather than relying on
the (largely unsupported) `XR_EXTX_overlay` extension.

> **Graphics API:** D3D11 only in this version. The target VR app must use the
> `XR_KHR_D3D11_enable` graphics binding (most OpenXR titles do). D3D12/Vulkan
> hosts are future work.

---

## How it works

```
 MonoGame app (your process)                 VR game (its process)
 ┌─────────────────────────┐                 ┌────────────────────────────┐
 │ RenderTarget2D          │                 │  XR_APILAYER_NOVELTY_monoxr │
 │   .GetData(buf)         │   shared D3D11  │  (our DLL, in the game)     │
 │ Overlay.Update(buf) ────┼──► texture  ───►│  • opens the shared texture │
 │   Position/Rotation/    │  (named NT      │  • copies into XrSwapchain  │
 │   Size/Space            │   handle +      │  • appends XrComposition    │
 │ OverlayManager          │   keyed mutex)  │    LayerQuad with your pose │
 │   writes control block ─┼──► Local\MonoXR-Control (shared memory)       │
 └─────────────────────────┘                 │  hooks xrEndFrame           │
                                             └────────────────────────────┘
```

- **`Local\MonoXR-Control`** — a shared-memory control block (created by the
  client) with one slot per overlay: active/visible flags, pose, size, reference
  space, texture format/size, and the shared-texture name. ABI defined once in
  `shared/monoxr_abi.h` and mirrored in `shared/MonoXrAbi.cs`.
- **Shared textures** — one per overlay, created by the client as a D3D11 shared
  texture (named NT handle + keyed mutex). The client copies the render target
  into it; the layer copies it into an OpenXR swapchain image. A keyed mutex
  hands the texture back and forth without tearing; the client's acquire is
  non-blocking so the game loop never stalls if the layer is busy or absent.

## Layout

| Path | What |
|---|---|
| `shared/monoxr_abi.h` / `MonoXrAbi.cs` | The cross-process ABI (keep in sync) |
| `layer/` | Native C++ OpenXR API layer (CMake) → `XR_APILAYER_NOVELTY_monoxr.dll` + `MonoXR.json` |
| `client/` | `MonoXR.Client` — C# library (`OverlayManager`, `Overlay`), uses Vortice/D3D11 |
| `sample/` | `MonoXR.Sample` — MonoGame app publishing two overlays |

## Build

**Prerequisites:** Visual Studio 2022 (MSVC), CMake ≥ 3.20, .NET 8 SDK.

```powershell
# 1. Native layer  (produces layer/build/Release/XR_APILAYER_NOVELTY_monoxr.dll + MonoXR.json)
./build-layer.ps1

# 2. Client + sample
dotnet build MonoXR.sln -c Release
```

## Install the layer

Register it as a per-user implicit OpenXR layer (no admin; loads into every
OpenXR app). The generated manifest points at the absolute DLL path, so re-run
this after rebuilding if the path changes.

```powershell
./install-layer.ps1              # register
./install-layer.ps1 -Uninstall   # remove
```

Prefer to enable it only for one launch? Skip the install and set env vars in the
shell that starts the VR app:

```powershell
$env:XR_API_LAYER_PATH   = "$PWD\layer\build\Release"
$env:XR_ENABLE_API_LAYERS = "XR_APILAYER_NOVELTY_monoxr"
```

## Use it in your own MonoGame app

```csharp
using MonoXR.Client;
using MonoXR.Shared;

var mgr = new OverlayManager();                       // creates the control block + D3D device

var overlay = mgr.CreateOverlay(1024, 512);           // one per render target
overlay.Space    = MonoXrSpace.World;                 // or .Head
overlay.Position = new System.Numerics.Vector3(0, 0, -1.5f);  // metres
overlay.Size     = new System.Numerics.Vector2(1.2f, 0.6f);   // metres
overlay.Rotation = System.Numerics.Quaternion.Identity;

// each frame, after rendering into your RenderTarget2D:
renderTarget.GetData(colorBuffer);                    // public MonoGame API
overlay.Update(System.Runtime.InteropServices.MemoryMarshal.AsBytes<Color>(colorBuffer));
mgr.Heartbeat();
```

`Overlay.Update` takes raw RGBA8 bytes, so the client has **no MonoGame
dependency** and works with any backend (WindowsDX or DesktopGL). See
`sample/OverlayGame.cs` for a complete example.

## Test

1. `./build-layer.ps1` then `./install-layer.ps1`.
2. Run the sample: `dotnet run --project sample -c Release` (a window shows the two
   overlay textures; this already validates the client, shared textures, and
   control block even with no headset).
3. Start any D3D11 OpenXR app / game. The two overlays should appear — a
   world-locked panel that slowly rotates, and a head-locked square.
4. Layer logs: `%TEMP%\MonoXR\layer.log`.

## Limitations / notes

- **D3D11 host only.** No D3D12/Vulkan/OpenGL game support yet.
- **Same GPU adapter.** The client makes its own D3D11 device on the default
  adapter; on multi-GPU systems it must match the game's adapter for the shared
  texture to open. (Adapter LUID negotiation is a future addition.)
- **Format** is fixed to `R8G8B8A8_UNORM` (matches MonoGame `SurfaceFormat.Color`).
  The runtime must expose that swapchain format; if it only offers the sRGB
  variant, a typeless-copy path would be needed.
- **Readback cost.** `GetData` does a GPU→CPU→GPU round trip per overlay per
  frame — fine for HUD-sized overlays; a zero-copy shared-handle fast path is a
  natural next step.
- **Not hardware-tested end-to-end here.** The C++ layer compiles and exports the
  loader negotiation entry point, and the full client path runs; the in-headset
  quad rendering needs to be verified on your VR rig.
