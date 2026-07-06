using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using BeltTensionTest.WPF.Services;
using Microsoft.Xna.Framework.Graphics;
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
        //  Add as many render targets as you like with
        //      var rt = _host.AddRenderTarget(width, height, x, y);
        //  (x, y) is the pixel location of the target inside the 1024x1024
        //  overlay canvas, changeable at runtime via rt.X / rt.Y.
        //
        //  Each target gets a Render callback that runs once per frame with the
        //  target already bound — just draw with normal MonoGame code.
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
            OverlayRenderTarget rtText = _host.AddRenderTarget(768, 96, x: 128, y: 8);
            rtText.Render = (gd, target, t) =>
            {
                gd.Clear(new XnaColor(10, 10, 20));
                _sb!.Begin();
                var size = _font!.MeasureString("hello");
                _sb.DrawString(_font, "hello",
                    new Microsoft.Xna.Framework.Vector2(
                        (target.Width - size.X) / 2f, (target.Height - size.Y) / 2f),
                    XnaColor.White);
                _sb.End();
            };

            // Example target A: wide panel, top-left area of the overlay.
            OverlayRenderTarget rtA = _host.AddRenderTarget(768, 256, x: 128, y: 96);
            rtA.Render = (gd, target, t) =>
            {
                
                gd.Clear(new XnaColor(20, 40, 80));
                _sb!.Begin();
                int bw = target.Width / 6;
                int bx = (int)((MathF.Sin(t * 1.5f) * 0.5f + 0.5f) * (target.Width - bw));
                _sb.Draw(_white, new XnaRectangle(bx, target.Height / 2 - bw / 2, bw, bw), XnaColor.Gold);
                _sb.End();
            };

            // Example target B: square panel, lower-right area of the overlay.
            OverlayRenderTarget rtB = _host.AddRenderTarget(320, 320, x: 576, y: 576);
            rtB.Render = (gd, target, t) =>
            {
                gd.Clear(new XnaColor(80, 20, 40));
                _sb!.Begin();
                int s = (int)(target.Width * 0.5f * (0.75f + 0.25f * MathF.Sin(t * 2f)));
                _sb.Draw(_white, new XnaRectangle((target.Width - s) / 2, (target.Height - s) / 2, s, s),
                         new XnaColor(120, 90, 230));
                _sb.End();
            };
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
