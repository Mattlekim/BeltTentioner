using System;
using System.Collections.Generic;
using System.Numerics;
using Microsoft.Xna.Framework.Graphics;
using GameTime = Microsoft.Xna.Framework.GameTime;
using MonoXR.Client;
using MonoXR.Shared;
using XnaColor = Microsoft.Xna.Framework.Color;

namespace BeltTensionTest.WPF.Services.Overlays
{
    /// <summary>
    /// One MonoGame render target placed somewhere on the overlay canvas.
    /// Inherit from this and override <see cref="Update"/> (game logic) and
    /// <see cref="Render"/> (drawing, target already bound). Move it at runtime
    /// by changing <see cref="X"/>/<see cref="Y"/>.
    /// </summary>
    public abstract class OverlayRenderTarget : IDisposable
    {
        public RenderTarget2D Target { get; }

        /// <summary>Shared device for creating textures, SpriteBatches, fonts, etc.</summary>
        public GraphicsDevice GraphicsDevice { get; }

        public int Width => Target.Width;
        public int Height => Target.Height;

        private int _x, _y;
        private bool _visible = true;

        /// <summary>Pixel location of this target's top-left corner in the overlay canvas.</summary>
        public int X { get => _x; set { if (_x != value) { _x = value; Invalidate(); } } }
        public int Y { get => _y; set { if (_y != value) { _y = value; Invalidate(); } } }

        public bool Visible { get => _visible; set { if (_visible != value) { _visible = value; Invalidate(); } } }

        /// <summary>
        /// True when this target needs re-rendering and the canvas re-publishing.
        /// Starts true so the first frame always draws. The host clears it after
        /// rendering; call <see cref="Invalidate"/> whenever your content changes.
        /// </summary>
        public bool IsDirty { get; private set; } = true;

        /// <summary>Mark this target as changed so the next frame re-renders and publishes it.</summary>
        public void Invalidate() => IsDirty = true;

        internal void ClearDirty() => IsDirty = false;

        protected OverlayRenderTarget(GraphicsDevice device, int width, int height, int x, int y)
        {
            GraphicsDevice = device;
            Target = new RenderTarget2D(device, width, height);
            X = x;
            Y = y;
        }

        /// <summary>Called once per frame, before any target renders. Put game/animation logic here.</summary>
        public abstract void Update(GameTime gameTime);

        /// <summary>Called once per frame with this render target already bound — just draw.</summary>
        public abstract void Render(GameTime gameTime);

        public virtual void Dispose() => Target.Dispose();
    }

    /// <summary>
    /// Hosts a headless MonoGame GraphicsDevice inside the WPF app and publishes
    /// a single OpenXR overlay (via MonoXR) composed from any number of MonoGame
    /// render targets, each at its own pixel location on the overlay canvas.
    ///
    /// Usage:
    ///   class MyPanel : OverlayRenderTarget { ... override Update/Render ... }
    ///   var host = new MonoGameOverlayHost(1024, 1024);
    ///   var rt = host.AddRenderTarget(new MyPanel(host.GraphicsDevice, 512, 256, x: 32, y: 32));
    ///   ... call host.RenderFrame(totalSeconds) on a timer ...
    /// </summary>
    public sealed class MonoGameOverlayHost : IDisposable
    {
        private readonly System.Windows.Forms.Form _hiddenForm;
        private readonly OverlayManager _mgr;
        private Overlay _overlay;
        private readonly List<OverlayRenderTarget> _targets = new List<OverlayRenderTarget>();
        private RenderTarget2D _canvasTarget;
        private readonly SpriteBatch _compositor;
        private readonly Texture2D _whitePixel;
        private TimeSpan _lastTotal;
        private bool _canvasDirty = true;
        private bool _disposed;

        /// <summary>Shared device for creating textures, SpriteBatches, fonts, etc.</summary>
        public GraphicsDevice GraphicsDevice { get; }

        /// <summary>The published overlay quad — set Position/Rotation/Size/Space/Visible on it.</summary>
        public Overlay Overlay => _overlay;

        /// <summary>Pixel resolution of the overlay canvas (independent of the VR display size).</summary>
        public int CanvasWidth => _canvasTarget.Width;
        public int CanvasHeight => _canvasTarget.Height;

        /// <summary>
        /// Physical size of the overlay quad in VR, in meters. Independent of
        /// the canvas pixel resolution; the quad pose is its center, so
        /// changing the size keeps it centered on the current position.
        /// </summary>
        public Vector2 DisplaySize
        {
            get => _overlay.Size;
            set { _overlay.Size = value; _canvasDirty = true; }
        }

        /// <summary>
        /// Distance in meters from the headset default position (world origin)
        /// to the overlay. The quad stays centered straight ahead (x = y = 0).
        /// </summary>
        public float Distance
        {
            get => -_overlay.Position.Z;
            set { _overlay.Position = new Vector3(0f, 0f, -Math.Max(0.05f, value)); _canvasDirty = true; }
        }

        /// <summary>True while an OpenXR app with the MonoXR layer is running (and its process is alive).</summary>
        public bool LayerAttached => _mgr.LayerAttached;

        /// <summary>
        /// Raised when the OpenXR game attaches (true) or closes/crashes (false).
        /// Fires from the thread that calls <see cref="RenderFrame"/>; marshal to
        /// the UI thread with Dispatcher before touching WPF state.
        /// </summary>
        public event Action<bool>? LayerAttachedChanged
        {
            add => _mgr.LayerAttachedChanged += value;
            remove => _mgr.LayerAttachedChanged -= value;
        }

        public ulong FramesPublished { get; private set; }

        /// <summary>
        /// Maximum update/render rate in frames per second. Calls to
        /// <see cref="RenderFrame"/> that arrive sooner than 1/rate after the
        /// last published frame are skipped entirely (no Update, no Render, no
        /// publish), keeping GPU cost away from the game. 0 disables the cap.
        /// </summary>
        public float MaxFrameRate { get; set; } = 30f;

        private bool _editMode;

        /// <summary>When true, a red border is drawn around the overlay canvas edge.</summary>
        public bool EditMode
        {
            get => _editMode;
            set { if (_editMode != value) { _editMode = value; _canvasDirty = true; } }
        }

        /// <summary>Pixel thickness of the edit-mode border.</summary>
        public int EditBorderThickness { get; set; } = 6;

        /// <summary>
        /// Force the next frame to composite and publish even if no target is
        /// dirty — needed after changing the overlay pose directly through
        /// <see cref="Overlay"/> (the pose is only pushed to VR on publish).
        /// </summary>
        public void Invalidate() => _canvasDirty = true;

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

            // GPU-side canvas the targets are composited into; published to the
            // overlay with a device-local CopyResource (both are R8G8B8A8_UNORM),
            // so no frame ever crosses to the CPU.
            _canvasTarget = new RenderTarget2D(GraphicsDevice, canvasWidth, canvasHeight);
            _compositor = new SpriteBatch(GraphicsDevice);
            _whitePixel = new Texture2D(GraphicsDevice, 1, 1);
            _whitePixel.SetData(new[] { XnaColor.White });

            _mgr = new OverlayManager(MonoGameInterop.GetDevicePointer(GraphicsDevice));

            // With dirty-flag rendering the client goes idle after the last
            // change, but a freshly attached layer (game start/restart) has no
            // content yet and shows nothing until the next publish. Republish
            // on every attach so the overlay appears immediately.
            _mgr.LayerAttachedChanged += attached => { if (attached) _canvasDirty = true; };

            _overlay = _mgr.CreateOverlay(canvasWidth, canvasHeight);
            _overlay.Space = MonoXrSpace.World;
            _overlay.Position = new Vector3(0f, 0f, -3f);
            _overlay.Size = new Vector2(2.5f, 2.5f);
            _overlay.Visible = true;
        }

        /// <summary>
        /// Register an <see cref="OverlayRenderTarget"/> subclass instance so its
        /// Update/Render run each frame and it is composited onto the canvas.
        /// Returns the same instance for convenience.
        /// </summary>
        public T AddRenderTarget<T>(T renderTarget) where T : OverlayRenderTarget
        {
            _targets.Add(renderTarget);
            return renderTarget;
        }

        /// <summary>
        /// Change the pixel resolution of the overlay canvas without touching
        /// its VR pose or size. Recreates the canvas render target and the
        /// shared overlay texture; the layer picks up the new swapchain
        /// automatically. Render targets keep their pixel positions/sizes.
        /// </summary>
        public void SetCanvasResolution(int width, int height)
        {
            if (_disposed) return;
            width = Math.Max(16, width);
            height = Math.Max(16, height);
            if (width == CanvasWidth && height == CanvasHeight) return;

            var old = _overlay;
            var newOverlay = _mgr.CreateOverlay(width, height);
            newOverlay.Space = old.Space;
            newOverlay.Position = old.Position;
            newOverlay.Rotation = old.Rotation;
            newOverlay.Size = old.Size;
            newOverlay.ZOrder = old.ZOrder;
            newOverlay.Visible = old.Visible;
            old.Dispose();
            _overlay = newOverlay;

            _canvasTarget.Dispose();
            _canvasTarget = new RenderTarget2D(GraphicsDevice, width, height);
            foreach (var rt in _targets) rt.Invalidate();
            _canvasDirty = true;
        }

        /// <summary>
        /// Run every registered target's Update then Render, composite all
        /// targets into the overlay canvas at their locations, and publish the
        /// frame.
        /// </summary>
        public void RenderFrame(float totalSeconds)
        {
            if (_disposed) return;

            var total = TimeSpan.FromSeconds(totalSeconds);

            // Frame-rate cap: skip the whole frame if it is too soon. The
            // skipped time is not lost — the next processed frame's delta
            // covers it, because _lastTotal only advances on processed frames.
            if (MaxFrameRate > 0 && FramesPublished > 0 &&
                total - _lastTotal < TimeSpan.FromSeconds(1.0 / MaxFrameRate))
                return;

            var gameTime = new GameTime(total, total - _lastTotal);
            _lastTotal = total;

            foreach (var rt in _targets)
                rt.Update(gameTime);

            // Dirty-flag rendering: when no target changed and nothing at the
            // canvas level changed (pose, edit mode, resolution), skip all GPU
            // work AND the publish — the layer then keeps re-showing the last
            // frame for free. Heartbeat still runs so attach/detach tracking works.
            bool anyDirty = _canvasDirty;
            foreach (var rt in _targets)
                anyDirty |= rt.IsDirty;
            if (!anyDirty)
            {
                _mgr.Heartbeat();
                return;
            }

            // Only re-render targets that actually changed; clean targets keep
            // their existing texture and are just re-composited.
            foreach (var rt in _targets)
            {
                if (!rt.IsDirty) continue;
                GraphicsDevice.SetRenderTarget(rt.Target);
                rt.Render(gameTime);
                rt.ClearDirty();
            }

            // Compose on the GPU: transparent canvas, then each visible target
            // drawn at (X, Y). Opaque blend writes the target's own alpha, so
            // anywhere no target covers stays fully transparent in VR (the
            // layer submits with BLEND_TEXTURE_SOURCE_ALPHA).
            GraphicsDevice.SetRenderTarget(_canvasTarget);
            GraphicsDevice.Clear(XnaColor.Transparent);
            _compositor.Begin(SpriteSortMode.Deferred, BlendState.Opaque, SamplerState.PointClamp);
            foreach (var rt in _targets)
            {
                if (!rt.Visible) continue;
                _compositor.Draw(rt.Target, new Microsoft.Xna.Framework.Vector2(rt.X, rt.Y), XnaColor.White);
            }
            if (EditMode)
            {
                int t = Math.Max(1, EditBorderThickness);
                int w = _canvasTarget.Width, h = _canvasTarget.Height;
                _compositor.Draw(_whitePixel, new Microsoft.Xna.Framework.Rectangle(0, 0, w, t), XnaColor.Red);
                _compositor.Draw(_whitePixel, new Microsoft.Xna.Framework.Rectangle(0, h - t, w, t), XnaColor.Red);
                _compositor.Draw(_whitePixel, new Microsoft.Xna.Framework.Rectangle(0, t, t, h - 2 * t), XnaColor.Red);
                _compositor.Draw(_whitePixel, new Microsoft.Xna.Framework.Rectangle(w - t, t, t, h - 2 * t), XnaColor.Red);
            }
            _compositor.End();
            GraphicsDevice.SetRenderTarget(null);

            // Publish with a device-local GPU copy — no readback, no upload.
            // If the layer held the shared texture this instant (publish
            // skipped), stay dirty so the next frame retries — otherwise the
            // last change would never reach VR.
            bool published = _overlay.Update(MonoGameInterop.GetTexturePointer(_canvasTarget));
            _mgr.Heartbeat();
            if (published)
            {
                FramesPublished++;
                _canvasDirty = false;
            }
            else
            {
                _canvasDirty = true;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var rt in _targets) rt.Dispose();
            _targets.Clear();
            _whitePixel.Dispose();
            _compositor.Dispose();
            _canvasTarget.Dispose();
            _overlay.Dispose();
            _mgr.Dispose();
            GraphicsDevice.Dispose();
            _hiddenForm.Dispose();
        }
    }
}
