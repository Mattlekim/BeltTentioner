using System;
using Microsoft.Xna.Framework.Graphics;
using GameTime = Microsoft.Xna.Framework.GameTime;
using MonoXR.Client;
using XnaColor = Microsoft.Xna.Framework.Color;
using XnaRectangle = Microsoft.Xna.Framework.Rectangle;
using XnaVector2 = Microsoft.Xna.Framework.Vector2;

namespace BeltTensionTest.WPF.Services.Overlays
{
    /// <summary>
    /// In-VR GPU monitor: load %, temperature and VRAM used / total, each with
    /// a bar colored green → amber → red as it climbs. Data comes from
    /// <see cref="GpuMonitorService"/> (NVML, polled once a second off the
    /// render thread); the panel only redraws when a new reading arrives.
    /// </summary>
    public sealed class GpuStatsOverlay : OverlayRenderTarget
    {
        private const int PanelWidth = 440;
        private const int PanelHeight = 250;
        private const int TitleBarHeight = 56;
        private const int RowHeight = 56;

        // App palette (Resources/Styles.xaml), same as the Belt Settings panel.
        private static readonly XnaColor PanelBg = new XnaColor(0x12, 0x12, 0x1E, 235);
        private static readonly XnaColor TitleBg = new XnaColor(0x1C, 0x1C, 0x2E, 245);
        private static readonly XnaColor TitleText = new XnaColor(0xD0, 0xD0, 0xF0);
        private static readonly XnaColor BodyText = new XnaColor(0xA0, 0xA0, 0xBE);
        private static readonly XnaColor Accent = new XnaColor(0x64, 0x96, 0xFF);
        private static readonly XnaColor Border = new XnaColor(0x46, 0x46, 0x6A);
        private static readonly XnaColor TrackEmpty = new XnaColor(0x37, 0x37, 0x4E);
        private static readonly XnaColor Good = new XnaColor(0x4C, 0xC3, 0x6A);
        private static readonly XnaColor Warn = new XnaColor(0xFF, 0xB0, 0x2E);
        private static readonly XnaColor Hot = new XnaColor(0xFF, 0x4D, 0x4D);

        private readonly SpriteBatch _sb;
        private readonly Texture2D _white;
        private readonly SpriteFont _fontTitle;
        private readonly SpriteFont _fontBody;
        private readonly SpriteFont _fontSmall;
        private readonly int _collapsedWidth;
        private GpuSnapshot _shown = GpuSnapshot.None;

        public override int CollapsedWidth => _collapsedWidth;
        public override int CollapsedHeight => TitleBarHeight;

        public GpuStatsOverlay(GraphicsDevice device, int x, int y)
            : base(device, PanelWidth, PanelHeight, x, y)
        {
            Name = "GPU";
            _sb = new SpriteBatch(device);
            _white = new Texture2D(device, 1, 1);
            _white.SetData(new[] { XnaColor.White });
            _fontTitle = RuntimeSpriteFont.Bake(device, "Segoe UI", 32f);
            _fontBody = RuntimeSpriteFont.Bake(device, "Segoe UI", 26f);
            _fontSmall = RuntimeSpriteFont.Bake(device, "Segoe UI", 20f);
            _collapsedWidth = (int)_fontTitle.MeasureString(Name).X + 60;
        }

        public override void Update(GameTime gameTime)
        {
            var latest = GpuMonitorService.Instance.Latest;
            if (!Equals(latest, _shown))
            {
                _shown = latest;
                Invalidate();
            }
        }

        public override void Render(GameTime gameTime)
        {
            const int Radius = 18;
            GraphicsDevice.Clear(XnaColor.Transparent);
            _sb.Begin();

            if (IsCollapsed)
            {
                var pill = new XnaRectangle(0, 0, CollapsedWidth, CollapsedHeight);
                MonoXRDraw.RoundedRect(_sb, pill, CollapsedHeight / 2, TitleBg);
                MonoXRDraw.RoundedRectOutline(_sb, pill, CollapsedHeight / 2, 2, Border);
                int dotR = 6;
                _sb.Draw(MonoXRDraw.Circle(GraphicsDevice, dotR),
                    new XnaRectangle(20 - dotR, CollapsedHeight / 2 - dotR, dotR * 2, dotR * 2), Accent);
                _sb.DrawString(_fontTitle, Name, new XnaVector2(34, (CollapsedHeight - _fontTitle.LineSpacing) / 2f), TitleText);
                _sb.End();
                return;
            }

            var g = _shown;
            var panel = new XnaRectangle(0, 0, Width, Height);
            MonoXRDraw.RoundedRect(_sb, panel, Radius, PanelBg);

            // Title bar: "GPU" plus the card's name, right-aligned and dimmer.
            MonoXRDraw.RoundedRect(_sb, new XnaRectangle(0, 0, Width, TitleBarHeight), Radius, TitleBg, roundBottom: false);
            MonoXRDraw.VerticalFade(_sb, new XnaRectangle(0, 0, Width, TitleBarHeight / 2), XnaColor.White * 0.05f);
            _sb.DrawString(_fontTitle, "GPU", new XnaVector2(20, 12), XnaColor.Black * 0.45f);
            _sb.DrawString(_fontTitle, "GPU", new XnaVector2(20, 10), TitleText);
            string name = g.Name;
            var nameSize = _fontSmall.MeasureString(name);
            _sb.DrawString(_fontSmall, name,
                new XnaVector2(Width - 20 - nameSize.X, (TitleBarHeight - _fontSmall.LineSpacing) / 2f), BodyText);
            _sb.Draw(_white, new XnaRectangle(0, TitleBarHeight - 3, Width, 3), Accent);
            MonoXRDraw.VerticalFade(_sb, new XnaRectangle(0, TitleBarHeight, Width, 14), Accent * 0.25f);

            int y = TitleBarHeight + 14;
            if (!g.Available)
            {
                _sb.DrawString(_fontBody, "No NVIDIA GPU detected", new XnaVector2(20, y + 10), BodyText);
            }
            else
            {
                DrawRow(y, "Usage", $"{g.UsagePercent}%", g.UsagePercent / 100f,
                        LevelColor(g.UsagePercent, 80, 95), degree: false);
                // Temperature bar spans 30..95 °C.
                DrawRow(y + RowHeight, "Temp", $"{g.TemperatureC} C", (g.TemperatureC - 30) / 65f,
                        LevelColor(g.TemperatureC, 75, 83), degree: true);
                double usedGb = g.MemoryUsedBytes / 1073741824.0, totalGb = g.MemoryTotalBytes / 1073741824.0;
                float memFrac = g.MemoryTotalBytes > 0 ? (float)(usedGb / totalGb) : 0f;
                DrawRow(y + RowHeight * 2, "VRAM", $"{usedGb:0.0} / {totalGb:0.0} GB", memFrac,
                        LevelColor(memFrac * 100f, 85, 95), degree: false);
            }

            MonoXRDraw.RoundedRectOutline(_sb, panel, Radius, 2, Border);
            _sb.End();
        }

        /// <summary>Label on the left, readout on the right, bar underneath.</summary>
        private void DrawRow(int y, string label, string readout, float fraction, XnaColor color, bool degree)
        {
            const int pad = 20;
            _sb.DrawString(_fontBody, label, new XnaVector2(pad, y), BodyText);

            var size = _fontBody.MeasureString(readout);
            float rx = Width - pad - size.X;
            _sb.DrawString(_fontBody, readout, new XnaVector2(rx, y), TitleText);
            if (degree)
            {
                // The baked font is ASCII-only: draw the ° as a small ring
                // just before the trailing "C".
                float cw = _fontBody.MeasureString("C").X;
                int r = 4;
                int dx = (int)(rx + size.X - cw - r - 3), dy = y + 9;
                _sb.Draw(MonoXRDraw.Circle(GraphicsDevice, r), new XnaRectangle(dx - r, dy - r, r * 2, r * 2), TitleText);
                _sb.Draw(MonoXRDraw.Circle(GraphicsDevice, r - 2), new XnaRectangle(dx - r + 2, dy - r + 2, (r - 2) * 2, (r - 2) * 2), PanelBg);
            }

            const int barH = 8;
            var track = new XnaRectangle(pad, y + _fontBody.LineSpacing + 2, Width - pad * 2, barH);
            MonoXRDraw.RoundedRect(_sb, track, barH / 2, TrackEmpty);
            int fillW = (int)(track.Width * Math.Clamp(fraction, 0f, 1f));
            if (fillW >= barH)
                MonoXRDraw.RoundedRect(_sb, new XnaRectangle(track.X, track.Y, fillW, barH), barH / 2, color);
        }

        private static XnaColor LevelColor(float value, float warnAt, float hotAt) =>
            value >= hotAt ? Hot : value >= warnAt ? Warn : Good;

        public override void Dispose()
        {
            _fontSmall.Texture.Dispose();
            _fontBody.Texture.Dispose();
            _fontTitle.Texture.Dispose();
            _white.Dispose();
            _sb.Dispose();
            base.Dispose();
        }
    }
}
