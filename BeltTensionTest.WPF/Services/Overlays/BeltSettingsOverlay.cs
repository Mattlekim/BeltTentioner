using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework.Graphics;
using GameTime = Microsoft.Xna.Framework.GameTime;
using XnaColor = Microsoft.Xna.Framework.Color;
using XnaRectangle = Microsoft.Xna.Framework.Rectangle;
using XnaVector2 = Microsoft.Xna.Framework.Vector2;

namespace BeltTensionTest.WPF.Services.Overlays
{
    /// <summary>
    /// One adjustable setting shown in the overlay. Get/Set delegates keep the
    /// overlay decoupled from where the value actually lives (view model,
    /// CarSettings, ...) so changes flow through the normal save path.
    /// </summary>
    public sealed class BeltSettingRow
    {
        public BeltSettingRow(string name, Func<float> get, Action<float> set,
                              float min, float max, float step, string format = "0")
        {
            Name = name; Get = get; Set = set;
            Min = min; Max = max; Step = step; Format = format;
        }

        public string Name { get; }
        public Func<float> Get { get; }
        public Action<float> Set { get; }
        public float Min { get; }
        public float Max { get; }
        public float Step { get; }
        public string Format { get; }
    }

    /// <summary>
    /// In-VR belt settings panel built from MonoXR controls: a
    /// <see cref="MonoXRMenuControl"/> of <see cref="MonoXRSliderControl"/>s,
    /// one per <see cref="BeltSettingRow"/>. Navigation keybindings move the
    /// selection (Up/Down) and adjust the selected slider (Increase/Decrease).
    /// Slider changes write through the row's Set delegate; Update pulls
    /// row.Get() back into the sliders so external changes stay in sync.
    /// </summary>
    public sealed class BeltSettingsOverlay : OverlayRenderTarget
    {
        private const int TitleBarHeight = 56;

        private readonly SpriteBatch _sb;
        private readonly Texture2D _white;
        private readonly SpriteFont _font;

        private readonly MonoXRMenuControl _menu;
        private readonly List<(BeltSettingRow Row, MonoXRSliderControl Slider)> _bindings = new();

        public BeltSettingsOverlay(GraphicsDevice device, int width, int height, int x, int y,
                                   IReadOnlyList<BeltSettingRow> rows)
            : base(device, width, height, x, y)
        {
            _sb = new SpriteBatch(device);
            _white = new Texture2D(device, 1, 1);
            _white.SetData(new[] { XnaColor.White });
            _font = RuntimeSpriteFont.Bake(device, "Segoe UI", 32f);

            _menu = new MonoXRMenuControl
            {
                Bounds = new XnaRectangle(8, TitleBarHeight + 14, width - 16, height - TitleBarHeight - 22),
            };
            foreach (var row in rows)
            {
                var slider = new MonoXRSliderControl(row.Name, row.Min, row.Max, row.Get(),
                                                     row.Step, row.Format);
                slider.ValueChanged += row.Set;
                // Any value change (from VR input or synced from the WPF UI)
                // means the panel must be redrawn and republished.
                slider.ValueChanged += _ => Invalidate();
                _menu.Add(slider);
                _bindings.Add((row, slider));
            }

            OverlayNavigation.Navigated += OnNavigated;
        }

        private void OnNavigated(OverlayNavAction action)
        {
            _menu.HandleNavigation(action);
            // Up/Down move the highlight without touching any value, so the
            // ValueChanged hook alone would miss it — redraw on every nav input.
            Invalidate();
        }

        public override void Update(GameTime gameTime)
        {
            // Pull external value changes (WPF UI, loaded car settings) into the sliders.
            foreach (var (row, slider) in _bindings)
                slider.Value = row.Get();

            _menu.Update(gameTime);
        }

        public override void Render(GameTime gameTime)
        {
            // Semi-transparent panel; the canvas itself is transparent, so
            // anything not drawn here is see-through in VR.
            GraphicsDevice.Clear(new XnaColor(12, 14, 28, 220));

            _sb.Begin();

            // Title bar.
            _sb.Draw(_white, new XnaRectangle(0, 0, Width, TitleBarHeight), new XnaColor(24, 28, 56, 240));
            _sb.DrawString(_font, "Belt Settings", new XnaVector2(16, 10), XnaColor.White);

            _menu.Draw(_sb, _font, _white);

            // Thin panel outline.
            var edge = new XnaColor(70, 70, 106, 255);
            _sb.Draw(_white, new XnaRectangle(0, 0, Width, 2), edge);
            _sb.Draw(_white, new XnaRectangle(0, Height - 2, Width, 2), edge);
            _sb.Draw(_white, new XnaRectangle(0, 0, 2, Height), edge);
            _sb.Draw(_white, new XnaRectangle(Width - 2, 0, 2, Height), edge);

            _sb.End();
        }

        public override void Dispose()
        {
            OverlayNavigation.Navigated -= OnNavigated;
            _font.Texture.Dispose();
            _white.Dispose();
            _sb.Dispose();
            base.Dispose();
        }
    }
}
