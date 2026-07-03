using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using Microsoft.Xna.Framework.Graphics;
using MonoXR.Client;
using MonoXR.Shared;
using XnaColor = Microsoft.Xna.Framework.Color;

namespace BeltTensionTest.WPF.Services
{
    /// <summary>
    /// One MonoGame render target placed somewhere on the overlay canvas.
    /// Set <see cref="Render"/> to draw into it each frame; move it at runtime
    /// by changing <see cref="X"/>/<see cref="Y"/>.
    /// </summary>
    public sealed class OverlayRenderTarget : IDisposable
    {
        internal readonly XnaColor[] Pixels;

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
            Pixels = new XnaColor[width * height];
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
        private readonly byte[] _canvas;
        private readonly int _canvasWidth;
        private readonly int _canvasHeight;
        private bool _disposed;

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
            _canvasWidth = canvasWidth;
            _canvasHeight = canvasHeight;
            _canvas = new byte[canvasWidth * canvasHeight * 4];

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
            GraphicsDevice.SetRenderTarget(null);

            // Compose: background fill, then each visible target blitted at (X, Y).
            uint bg = BackgroundColor.PackedValue; // ABGR packed = RGBA byte order in memory
            MemoryMarshal.Cast<byte, uint>(_canvas.AsSpan()).Fill(bg);
            foreach (var rt in _targets)
            {
                if (!rt.Visible) continue;
                rt.Target.GetData(rt.Pixels);
                Blit(rt);
            }

            _overlay.Update(_canvas);
            _mgr.Heartbeat();
            FramesPublished++;
        }

        // Copy a target's pixels into the canvas at its location, clipped to the canvas.
        private void Blit(OverlayRenderTarget rt)
        {
            int w = rt.Target.Width, h = rt.Target.Height;
            int srcX = Math.Max(0, -rt.X), srcY = Math.Max(0, -rt.Y);
            int dstX = Math.Max(0, rt.X), dstY = Math.Max(0, rt.Y);
            int copyW = Math.Min(w - srcX, _canvasWidth - dstX);
            int copyH = Math.Min(h - srcY, _canvasHeight - dstY);
            if (copyW <= 0 || copyH <= 0) return;

            ReadOnlySpan<byte> src = MemoryMarshal.AsBytes<XnaColor>(rt.Pixels);
            for (int row = 0; row < copyH; row++)
            {
                int srcOffset = ((srcY + row) * w + srcX) * 4;
                int dstOffset = ((dstY + row) * _canvasWidth + dstX) * 4;
                src.Slice(srcOffset, copyW * 4).CopyTo(_canvas.AsSpan(dstOffset, copyW * 4));
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var rt in _targets) rt.Dispose();
            _targets.Clear();
            _overlay.Dispose();
            _mgr.Dispose();
            GraphicsDevice.Dispose();
            _hiddenForm.Dispose();
        }
    }
}
