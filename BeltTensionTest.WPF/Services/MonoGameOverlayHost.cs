using System;
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using Microsoft.Xna.Framework.Graphics;
using MonoXR.Client;
using MonoXR.Shared;
using XnaColor = Microsoft.Xna.Framework.Color;
using XnaVector2 = Microsoft.Xna.Framework.Vector2;

namespace BeltTensionTest.WPF.Services
{
    /// <summary>
    /// One MonoGame render target placed somewhere on the overlay canvas.
    /// Set <see cref="Render"/> to draw into it each frame; move it at runtime
    /// by changing <see cref="X"/>/<see cref="Y"/>.
    /// </summary>
    public sealed class OverlayRenderTarget : IDisposable
    {
        public RenderTarget2D Target { get; }

        /// <summary>Pixel location of this target's top-left corner in the overlay canvas.</summary>
        public int X { get; set; }
        public int Y { get; set; }

        public bool Visible { get; set; } = true;

        /// <summary>
        /// Called once per frame with the render target already bound, so just
        /// draw. Arguments: the shared GraphicsDevice, this target, total seconds.
        /// </summary>
        public Action<GraphicsDevice, RenderTarget2D, float>? Render { get; set; }

        internal OverlayRenderTarget(GraphicsDevice device, int width, int height, int x, int y)
        {
            Target = new RenderTarget2D(device, width, height);
            X = x;
            Y = y;
        }

        public void Dispose() => Target.Dispose();
    }

    /// <summary>
    /// Hosts a headless MonoGame GraphicsDevice inside the WPF app and publishes
    /// a single OpenXR overlay (via MonoXR) composed from any number of MonoGame
    /// render targets, each at its own pixel location on the overlay canvas.
    ///
    /// The composite is done on the GPU (targets are sprite-drawn onto a canvas
    /// render target) and published zero-copy: MonoXR opens its shared texture on
    /// MonoGame's D3D11 device and the canvas is CopyResource'd into it, so pixels
    /// never touch the CPU. If MonoGame's native handles can't be reached (internal
    /// API change), a CPU fallback does one GetData on the canvas per frame.
    ///
    /// Usage:
    ///   var host = new MonoGameOverlayHost(1024, 1024);
    ///   var rt = host.AddRenderTarget(512, 256, x: 32, y: 32);
    ///   rt.Render = (gd, target, t) => { /* MonoGame drawing */ };
    ///   ... call host.RenderFrame(totalSeconds) on a timer ...
    /// </summary>
    public sealed class MonoGameOverlayHost : IDisposable
    {
        private readonly System.Windows.Forms.Form _hiddenForm;
        private readonly OverlayManager _mgr;
        private readonly Overlay _overlay;
        private readonly List<OverlayRenderTarget> _targets = new List<OverlayRenderTarget>();
        private readonly RenderTarget2D _canvasTarget;
        private readonly SpriteBatch _compositor;
        private bool _disposed;

        // Zero-copy publish: cached reflection into MonoGame's WindowsDX internals
        // (SharpDX device + texture); if anything is missing we fall back to a
        // per-frame GetData readback of the composed canvas.
        private readonly IntPtr _nativeDevicePtr;
        private readonly MethodInfo? _getTextureMethod;
        private bool _gpuPublishBroken;
        private byte[]? _cpuCanvas;

        /// <summary>Shared device for creating textures, SpriteBatches, fonts, etc.</summary>
        public GraphicsDevice GraphicsDevice { get; }

        /// <summary>The published overlay quad — set Position/Rotation/Size/Space/Visible on it.</summary>
        public Overlay Overlay => _overlay;

        /// <summary>True once an OpenXR app with the MonoXR layer is running.</summary>
        public bool LayerAttached => _mgr.LayerAttached;

        public ulong FramesPublished { get; private set; }

        /// <summary>Canvas fill drawn under the render targets each frame (RGBA).</summary>
        public XnaColor BackgroundColor { get; set; } = new XnaColor(20, 24, 48, 255);

        public MonoGameOverlayHost(int canvasWidth, int canvasHeight)
        {
            // MonoGame needs a real HWND for its swap chain; a hidden WinForms
            // window (never shown) satisfies it. All output goes to render
            // targets, nothing is ever presented to this window.
            _hiddenForm = new System.Windows.Forms.Form { ShowInTaskbar = false };
            var pp = new PresentationParameters
            {
                DeviceWindowHandle = _hiddenForm.Handle,
                BackBufferWidth = Math.Max(1, canvasWidth),
                BackBufferHeight = Math.Max(1, canvasHeight),
                IsFullScreen = false,
            };
            GraphicsDevice = new GraphicsDevice(GraphicsAdapter.DefaultAdapter, GraphicsProfile.HiDef, pp);

            _canvasTarget = new RenderTarget2D(GraphicsDevice, canvasWidth, canvasHeight);
            _compositor = new SpriteBatch(GraphicsDevice);

            _nativeDevicePtr = ReflectNativeDevicePtr(GraphicsDevice);
            _getTextureMethod = typeof(Texture).GetMethod("GetTexture",
                BindingFlags.NonPublic | BindingFlags.Instance);
            _gpuPublishBroken = _nativeDevicePtr == IntPtr.Zero || _getTextureMethod == null;

            _mgr = new OverlayManager();
            _overlay = _mgr.CreateOverlay(canvasWidth, canvasHeight);
            _overlay.Space = MonoXrSpace.World;
            _overlay.Position = new Vector3(0f, 0f, -3f);
            _overlay.Size = new Vector2(2.5f, 2.5f);
            _overlay.Visible = true;
        }

        /// <summary>
        /// Create a render target of the given size, placed at pixel (x, y) in
        /// the overlay canvas. Assign its <see cref="OverlayRenderTarget.Render"/>
        /// callback to draw into it.
        /// </summary>
        public OverlayRenderTarget AddRenderTarget(int width, int height, int x, int y)
        {
            var rt = new OverlayRenderTarget(GraphicsDevice, width, height, x, y);
            _targets.Add(rt);
            return rt;
        }

        /// <summary>
        /// Run every registered target's Render callback, composite all targets
        /// into the overlay canvas at their locations, and publish the frame.
        /// </summary>
        public void RenderFrame(float totalSeconds)
        {
            if (_disposed) return;

            foreach (var rt in _targets)
            {
                if (rt.Render == null) continue;
                GraphicsDevice.SetRenderTarget(rt.Target);
                rt.Render(GraphicsDevice, rt.Target, totalSeconds);
            }

            // Compose on the GPU: background clear, then each visible target
            // sprite-drawn at (X, Y). Opaque blend = a straight pixel copy,
            // matching the old CPU blit (alpha included).
            GraphicsDevice.SetRenderTarget(_canvasTarget);
            GraphicsDevice.Clear(BackgroundColor);
            _compositor.Begin(SpriteSortMode.Deferred, BlendState.Opaque, SamplerState.PointClamp);
            foreach (var rt in _targets)
            {
                if (!rt.Visible) continue;
                _compositor.Draw(rt.Target, new XnaVector2(rt.X, rt.Y), XnaColor.White);
            }
            _compositor.End();
            GraphicsDevice.SetRenderTarget(null);

            Publish();
            _mgr.Heartbeat();
            FramesPublished++;
        }

        // Hand the composed canvas to MonoXR — GPU texture-to-texture when the
        // native handles are reachable, otherwise one CPU readback of the canvas.
        private void Publish()
        {
            if (!_gpuPublishBroken)
            {
                IntPtr texPtr = ReflectNativeTexturePtr(_canvasTarget);
                if (texPtr != IntPtr.Zero)
                {
                    _overlay.UpdateFromTexture(_nativeDevicePtr, texPtr);
                    return;
                }
                _gpuPublishBroken = true;
            }

            _cpuCanvas ??= new byte[_canvasTarget.Width * _canvasTarget.Height * 4];
            _canvasTarget.GetData(_cpuCanvas);
            _overlay.Update(_cpuCanvas);
        }

        // MonoGame WindowsDX wraps D3D11 via SharpDX; the device lives in
        // GraphicsDevice._d3dDevice and every texture exposes GetTexture().
        // Both are internal, hence reflection. SharpDX objects expose the raw
        // COM pointer via the public NativePointer property.
        private static IntPtr ReflectNativeDevicePtr(GraphicsDevice device)
        {
            var field = typeof(GraphicsDevice).GetField("_d3dDevice",
                BindingFlags.NonPublic | BindingFlags.Instance);
            object? d3d = field?.GetValue(device)
                ?? typeof(GraphicsDevice).GetProperty("D3DDevice",
                    BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(device);
            return NativePointerOf(d3d);
        }

        private IntPtr ReflectNativeTexturePtr(Texture texture)
            => NativePointerOf(_getTextureMethod?.Invoke(texture, null));

        private static PropertyInfo? _nativePointerCache;

        private static IntPtr NativePointerOf(object? sharpDxObject)
        {
            if (sharpDxObject == null) return IntPtr.Zero;
            // NativePointer is declared on SharpDX.CppObject, the base of every
            // SharpDX wrapper, so one cached PropertyInfo serves all of them.
            _nativePointerCache ??= sharpDxObject.GetType().GetProperty("NativePointer");
            return _nativePointerCache?.GetValue(sharpDxObject) is IntPtr p ? p : IntPtr.Zero;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var rt in _targets) rt.Dispose();
            _targets.Clear();
            _overlay.Dispose();
            _mgr.Dispose();
            _compositor.Dispose();
            _canvasTarget.Dispose();
            GraphicsDevice.Dispose();
            _hiddenForm.Dispose();
        }
    }
}
