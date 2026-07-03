using System.Numerics;
using System.Runtime.InteropServices;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MonoXR.Client;
using MonoXR.Shared;
using XnaColor = Microsoft.Xna.Framework.Color;
using NVector2 = System.Numerics.Vector2;
using NVector3 = System.Numerics.Vector3;
using NQuaternion = System.Numerics.Quaternion;

namespace MonoXR.Sample;

/// <summary>
/// Renders two MonoGame render targets and publishes each as an OpenXR overlay
/// via the MonoXR layer. Overlay A is world-locked and spins; overlay B is
/// head-locked. The window also mirrors both targets so you can see them on the
/// desktop even without a headset.
/// </summary>
public sealed class OverlayGame : Game
{
    private readonly GraphicsDeviceManager _graphics;
    private SpriteBatch _sb = null!;
    private Texture2D _white = null!;
    private SpriteFont? _font;

    private RenderTarget2D _rtA = null!, _rtB = null!;
    private XnaColor[] _bufA = null!, _bufB = null!;

    private OverlayManager _mgr = null!;
    private Overlay _ovA = null!, _ovB = null!;

    private float _time;

    public OverlayGame()
    {
        _graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = 900,
            PreferredBackBufferHeight = 600,
        };
        IsMouseVisible = true;
        Window.Title = "MonoXR Sample — publishing 2 overlays";
    }

    protected override void Initialize()
    {
        _mgr = new OverlayManager();
        base.Initialize();
    }

    protected override void LoadContent()
    {
        _sb = new SpriteBatch(GraphicsDevice);
        _white = new Texture2D(GraphicsDevice, 1, 1);
        _white.SetData(new[] { XnaColor.White });

        _rtA = new RenderTarget2D(GraphicsDevice, 1024, 512);
        _rtB = new RenderTarget2D(GraphicsDevice, 512, 512);
        _bufA = new XnaColor[_rtA.Width * _rtA.Height];
        _bufB = new XnaColor[_rtB.Width * _rtB.Height];

        // World-locked wide panel, 1.5 m in front, ~1.2 m wide.
        _ovA = _mgr.CreateOverlay(_rtA.Width, _rtA.Height);
        _ovA.Space = MonoXrSpace.World;
        _ovA.Position = new NVector3(0f, 0f, -1.5f);
        _ovA.Size = new NVector2(1.2f, 0.6f);

        // Head-locked square, down-right in the field of view.
        _ovB = _mgr.CreateOverlay(_rtB.Width, _rtB.Height);
        _ovB.Space = MonoXrSpace.Head;
        _ovB.Position = new NVector3(0.4f, -0.25f, -1.0f);
        _ovB.Size = new NVector2(0.3f, 0.3f);
    }

    protected override void Update(GameTime gameTime)
    {
        if (Keyboard.GetState().IsKeyDown(Keys.Escape)) Exit();
        _time += (float)gameTime.ElapsedGameTime.TotalSeconds;

        // Spin the world panel gently around Y so you can see pose control working.
        _ovA.Rotation = NQuaternion.CreateFromAxisAngle(NVector3.UnitY, MathF.Sin(_time) * 0.6f);
        base.Update(gameTime);
    }

    private void RenderOverlay(RenderTarget2D rt, XnaColor[] buffer, Overlay overlay, XnaColor bg)
    {
        GraphicsDevice.SetRenderTarget(rt);
        GraphicsDevice.Clear(bg);
        _sb.Begin();
        // A box that slides left/right, plus a border, so motion is obvious.
        int bw = rt.Width / 6;
        int x = (int)((MathF.Sin(_time * 1.5f) * 0.5f + 0.5f) * (rt.Width - bw));
        _sb.Draw(_white, new Rectangle(x, rt.Height / 2 - bw / 2, bw, bw), XnaColor.Gold);
        DrawBorder(rt.Width, rt.Height, 6, XnaColor.White);
        _sb.End();
        GraphicsDevice.SetRenderTarget(null);

        // Public MonoGame API only: read back the pixels and hand them to MonoXR.
        rt.GetData(buffer);
        overlay.Update(MemoryMarshal.AsBytes<XnaColor>(buffer));
    }

    private void DrawBorder(int w, int h, int t, XnaColor c)
    {
        _sb.Draw(_white, new Rectangle(0, 0, w, t), c);
        _sb.Draw(_white, new Rectangle(0, h - t, w, t), c);
        _sb.Draw(_white, new Rectangle(0, 0, t, h), c);
        _sb.Draw(_white, new Rectangle(w - t, 0, t, h), c);
    }

    protected override void Draw(GameTime gameTime)
    {
        RenderOverlay(_rtA, _bufA, _ovA, new XnaColor(20, 40, 80));
        RenderOverlay(_rtB, _bufB, _ovB, new XnaColor(80, 20, 40));
        _mgr.Heartbeat();

        // Mirror both targets to the window.
        GraphicsDevice.Clear(new XnaColor(12, 12, 16));
        _sb.Begin();
        _sb.Draw(_rtA, new Rectangle(20, 20, 512, 256), XnaColor.White);
        _sb.Draw(_rtB, new Rectangle(20, 300, 256, 256), XnaColor.White);
        _sb.End();

        base.Draw(gameTime);
    }

    protected override void UnloadContent()
    {
        _ovA.Dispose();
        _ovB.Dispose();
        _mgr.Dispose();
        _rtA.Dispose();
        _rtB.Dispose();
        _white.Dispose();
        _sb.Dispose();
    }
}
