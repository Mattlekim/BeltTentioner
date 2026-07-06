using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using BeltTensionTest.WPF.Services;
using BeltTensionTest.WPF.Services.Overlays;
using Microsoft.Xna.Framework.Graphics;
using GameTime = Microsoft.Xna.Framework.GameTime;
using XnaColor = Microsoft.Xna.Framework.Color;
using XnaRectangle = Microsoft.Xna.Framework.Rectangle;

namespace BeltTensionTest.WPF.Views
{
    /// <summary>
    /// Hosts the OpenXR overlay. The window itself only shows the layer/log
    /// status; all visual content comes from MonoGame render targets that are
    /// composited onto the overlay canvas (see the render section below).
    /// </summary>
    public partial class OverlayWindow : Window
    {
        private const int CanvasSize = 1024; // pixel size of the overlay canvas

        private MonoGameOverlayHost? _host;
        private SpriteBatch? _sb;
        private Texture2D? _white;
        private SpriteFont? _font;

        private readonly DispatcherTimer _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        private float _time;
        private bool _lastAttached;

        public OverlayWindow()
        {
            InitializeComponent();

            try
            {
                Log("Creating MonoGame overlay host...");
                _host = new MonoGameOverlayHost(CanvasSize, CanvasSize);
                Log("Host ready: MonoGame device up, overlay published (World, 3m ahead, 2.5m).");

                SetupRenderTargets();
            }
            catch (Exception ex)
            {
                Log("INIT FAILED: " + ex);
                StatusLabel.Text = "Init failed — see log.";
            }

            _timer.Tick += OnTick;
            _timer.Start();
            Closed += OnClosed;
        }

        // =====================================================================
        //  MONOGAME RENDER SECTION
        //
        //  Subclass OverlayRenderTarget, override Update (game logic) and
        //  Render (drawing, target already bound), then register it with
        //      _host.AddRenderTarget(new MyTarget(_host.GraphicsDevice, ...));
        //  (x, y) is the pixel location of the target inside the 1024x1024
        //  overlay canvas, changeable at runtime via rt.X / rt.Y.
        // =====================================================================
        private void SetupRenderTargets()
        {
            if (_host == null) return;

            _sb = new SpriteBatch(_host.GraphicsDevice);
            _white = new Texture2D(_host.GraphicsDevice, 1, 1);
            _white.SetData(new[] { XnaColor.White });

            // Runtime-baked sprite font (no content pipeline needed).
            _font = RuntimeSpriteFont.Bake(_host.GraphicsDevice, "Segoe UI", 64f);

            // Text panel across the top of the overlay canvas.
            _host.AddRenderTarget(new TextPanelTarget(_host.GraphicsDevice, _sb, _font, 768, 96, x: 128, y: 8));

            // Example target A: wide panel, top-left area of the overlay.
            _host.AddRenderTarget(new BouncingBarTarget(_host.GraphicsDevice, _sb, _white, 768, 256, x: 128, y: 96));

            // Example target B: square panel, lower-right area of the overlay.
            _host.AddRenderTarget(new PulsingSquareTarget(_host.GraphicsDevice, _sb, _white, 320, 320, x: 576, y: 576));
        }

        /// <summary>Centered "hello" text on a dark panel.</summary>
        private sealed class TextPanelTarget : OverlayRenderTarget
        {
            private readonly SpriteBatch _sb;
            private readonly SpriteFont _font;

            public TextPanelTarget(GraphicsDevice device, SpriteBatch sb, SpriteFont font,
                                   int width, int height, int x, int y)
                : base(device, width, height, x, y)
            {
                _sb = sb;
                _font = font;
            }

            public override void Update(GameTime gameTime) { }

            public override void Render(GameTime gameTime)
            {
                GraphicsDevice.Clear(new XnaColor(10, 10, 20));
                _sb.Begin();
                var size = _font.MeasureString("hello");
                _sb.DrawString(_font, "hello",
                    new Microsoft.Xna.Framework.Vector2(
                        (Width - size.X) / 2f, (Height - size.Y) / 2f),
                    XnaColor.White);
                _sb.End();
            }
        }

        /// <summary>Gold square sweeping left/right across a blue panel.</summary>
        private sealed class BouncingBarTarget : OverlayRenderTarget
        {
            private readonly SpriteBatch _sb;
            private readonly Texture2D _white;
            private int _barX;

            public BouncingBarTarget(GraphicsDevice device, SpriteBatch sb, Texture2D white,
                                     int width, int height, int x, int y)
                : base(device, width, height, x, y)
            {
                _sb = sb;
                _white = white;
            }

            private int BarSize => Width / 6;

            public override void Update(GameTime gameTime)
            {
                float t = (float)gameTime.TotalGameTime.TotalSeconds;
                _barX = (int)((MathF.Sin(t * 1.5f) * 0.5f + 0.5f) * (Width - BarSize));
            }

            public override void Render(GameTime gameTime)
            {
                GraphicsDevice.Clear(new XnaColor(20, 40, 80));
                _sb.Begin();
                _sb.Draw(_white, new XnaRectangle(_barX, Height / 2 - BarSize / 2, BarSize, BarSize), XnaColor.Gold);
                _sb.End();
            }
        }

        /// <summary>Purple square pulsing in size on a red panel.</summary>
        private sealed class PulsingSquareTarget : OverlayRenderTarget
        {
            private readonly SpriteBatch _sb;
            private readonly Texture2D _white;
            private int _size;

            public PulsingSquareTarget(GraphicsDevice device, SpriteBatch sb, Texture2D white,
                                       int width, int height, int x, int y)
                : base(device, width, height, x, y)
            {
                _sb = sb;
                _white = white;
            }

            public override void Update(GameTime gameTime)
            {
                float t = (float)gameTime.TotalGameTime.TotalSeconds;
                _size = (int)(Width * 0.5f * (0.75f + 0.25f * MathF.Sin(t * 2f)));
            }

            public override void Render(GameTime gameTime)
            {
                GraphicsDevice.Clear(new XnaColor(80, 20, 40));
                _sb.Begin();
                _sb.Draw(_white, new XnaRectangle((Width - _size) / 2, (Height - _size) / 2, _size, _size),
                         new XnaColor(120, 90, 230));
                _sb.End();
            }
        }
        // ===================== END MONOGAME RENDER SECTION ===================

        private void OnTick(object? sender, EventArgs e)
        {
            if (_host == null) return;
            _time += (float)_timer.Interval.TotalSeconds;

            
            try
            {
                _host.RenderFrame(_time);
            }
            catch (Exception ex)
            {
                _timer.Stop();
                Log("RENDER FAILED: " + ex);
                StatusLabel.Text = "Render failed — see log.";
                return;
            }

            bool attached = _host.LayerAttached;
            if (attached != _lastAttached || _host.FramesPublished == 1)
            {
                _lastAttached = attached;
                Log(attached ? "Layer attached — overlay is live in VR."
                             : "Waiting for an OpenXR app (layer not attached yet).");
            }
            StatusLabel.Text = (attached ? "Layer attached — live in VR." : "Waiting for OpenXR app…")
                               + $"   Frames: {_host.FramesPublished}";
        }

        private void OnClosed(object? sender, EventArgs e)
        {
            _timer.Stop();
            _font?.Texture.Dispose();
            _white?.Dispose();
            _sb?.Dispose();
            _host?.Dispose();
        }

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                try { DragMove(); } catch { }
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

        // Log to the window and to %TEMP%\MonoXR\client.log (the native layer
        // logs separately to %TEMP%\MonoXR\layer.log).
        private void Log(string message)
        {
            string stamped = $"{DateTime.Now:HH:mm:ss.fff} {message}";
            LogBox.AppendText(stamped + Environment.NewLine);
            LogBox.ScrollToEnd();
            try
            {
                string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "MonoXR");
                System.IO.Directory.CreateDirectory(dir);
                using var fs = new System.IO.FileStream(
                    System.IO.Path.Combine(dir, "client.log"),
                    System.IO.FileMode.Append, System.IO.FileAccess.Write, System.IO.FileShare.ReadWrite);
                using var sw = new System.IO.StreamWriter(fs);
                sw.Write($"{DateTime.Now:HH:mm:ss.fff} [pid {Environment.ProcessId}] {message}{Environment.NewLine}");
            }
            catch { /* logging must never throw */ }
        }
    }
}
