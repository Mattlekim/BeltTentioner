using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework.Graphics;
using GameTime = Microsoft.Xna.Framework.GameTime;
using MonoXR.Client;
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
                              float min, float max, float step, string format = "0",
                              XnaColor? fill = null)
        {
            Name = name; Get = get; Set = set;
            Min = min; Max = max; Step = step; Format = format; Fill = fill;
        }

        public string Name { get; }
        public Func<float> Get { get; }
        public Action<float> Set { get; }
        public float Min { get; }
        public float Max { get; }
        public float Step { get; }
        public string Format { get; }

        /// <summary>Slider fill color, matching the row's FillBrush in MainWindow.xaml.</summary>
        public XnaColor? Fill { get; }
    }

    /// <summary>
    /// One boolean setting shown in the overlay as a checkbox, same
    /// get/set decoupling as <see cref="BeltSettingRow"/>.
    /// </summary>
    public sealed class BeltToggleRow
    {
        public BeltToggleRow(string name, Func<bool> get, Action<bool> set)
        {
            Name = name; Get = get; Set = set;
        }

        public string Name { get; }
        public Func<bool> Get { get; }
        public Action<bool> Set { get; }
    }

    /// <summary>
    /// A titled section of the overlay panel: a header label followed by its
    /// sliders and toggles, mirroring how MainWindow groups Surge/Sway/Heave.
    /// </summary>
    public sealed class BeltSettingGroup
    {
        public BeltSettingGroup(string name, IReadOnlyList<BeltSettingRow>? rows = null,
                                IReadOnlyList<BeltToggleRow>? toggles = null)
        {
            Name = name;
            Rows = rows ?? Array.Empty<BeltSettingRow>();
            Toggles = toggles ?? Array.Empty<BeltToggleRow>();
        }

        public string Name { get; }
        public IReadOnlyList<BeltSettingRow> Rows { get; }
        public IReadOnlyList<BeltToggleRow> Toggles { get; }
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

        // App palette (Resources/Styles.xaml).
        private static readonly XnaColor PanelBg = new XnaColor(0x12, 0x12, 0x1E, 235);   // BgBrush
        private static readonly XnaColor TitleBg = new XnaColor(0x1C, 0x1C, 0x2E, 245);   // BgLightBrush
        private static readonly XnaColor TitleText = new XnaColor(0xD0, 0xD0, 0xF0);      // TextBrightBrush
        private static readonly XnaColor Accent = new XnaColor(0x64, 0x96, 0xFF);         // AccentBlueBrush
        private static readonly XnaColor Border = new XnaColor(0x46, 0x46, 0x6A);         // BorderBrush

        private readonly SpriteBatch _sb;
        private readonly Texture2D _white;
        private readonly SpriteFont _font;     // title
        private readonly SpriteFont _fontBody; // menu rows

        private readonly MonoXRMenuControl _menu;
        private readonly List<(BeltSettingRow Row, MonoXRSliderControl Slider)> _bindings = new();
        private readonly List<(BeltToggleRow Row, MonoXRCheckbox Box)> _toggleBindings = new();
        private readonly int _collapsedWidth;

        // Collapsed bar sized to fit the name (measured in the ctor).
        public override int CollapsedWidth => _collapsedWidth;
        public override int CollapsedHeight => TitleBarHeight;

        public BeltSettingsOverlay(GraphicsDevice device, int width, int height, int x, int y,
                                   IReadOnlyList<BeltSettingGroup> groups)
            : base(device, width, height, x, y)
        {
            Name = "Belt Settings";
            _sb = new SpriteBatch(device);
            _white = new Texture2D(device, 1, 1);
            _white.SetData(new[] { XnaColor.White });
            _font = RuntimeSpriteFont.Bake(device, "Segoe UI", 32f);
            _fontBody = RuntimeSpriteFont.Bake(device, "Segoe UI", 26f);
            _collapsedWidth = (int)_font.MeasureString(Name).X + 60; // name + accent dot + padding

            _menu = new MonoXRMenuControl
            {
                Bounds = new XnaRectangle(16, TitleBarHeight + 14, width - 32, height - TitleBarHeight - 22),
                ItemHeight = 48,
                ItemSpacing = 6,
            };

            foreach (var group in groups)
            {
                _menu.Add(new MonoXRLabel(group.Name));

                foreach (var row in group.Rows)
                {
                    var slider = new MonoXRSliderControl(row.Name, row.Min, row.Max, row.Get(),
                                                         row.Step, row.Format)
                    { FillColor = row.Fill };
                    slider.ValueChanged += row.Set;
                    // Any value change (from VR input or synced from the WPF UI)
                    // means the panel must be redrawn and republished.
                    slider.ValueChanged += _ => Invalidate();
                    _menu.Add(slider);
                    _bindings.Add((row, slider));
                }

                foreach (var row in group.Toggles)
                {
                    var box = new MonoXRCheckbox(row.Name, row.Get());
                    box.Checked += () => { row.Set(true); Invalidate(); };
                    box.Unchecked += () => { row.Set(false); Invalidate(); };
                    _menu.Add(box);
                    _toggleBindings.Add((row, box));
                }
            }

            OverlayNavigation.Navigated += OnNavigated;
        }

        private void OnNavigated(OverlayNavAction action)
        {
            // While collapsed the menu is not on screen — don't move the
            // selection or change values invisibly.
            if (IsCollapsed) return;
            _menu.HandleNavigation(action);
            // Up/Down move the highlight without touching any value, so the
            // ValueChanged hook alone would miss it — redraw on every nav input.
            Invalidate();
        }

        public override void Update(GameTime gameTime)
        {
            // Pull external value changes (WPF UI, loaded car settings) into the controls.
            foreach (var (row, slider) in _bindings)
                slider.Value = row.Get();
            foreach (var (row, box) in _toggleBindings)
                box.IsChecked = row.Get();

            _menu.Update(gameTime);
        }

        public override void Render(GameTime gameTime)
        {
            const int Radius = 18;

            if (IsCollapsed)
            {
                // Only the top-left CollapsedWidth×CollapsedHeight region is
                // composited: a rounded pill with an accent dot and the name.
                GraphicsDevice.Clear(XnaColor.Transparent);
                _sb.Begin();
                var pill = new XnaRectangle(0, 0, CollapsedWidth, CollapsedHeight);
                MonoXRDraw.RoundedRect(_sb, pill, CollapsedHeight / 2, TitleBg);
                MonoXRDraw.RoundedRectOutline(_sb, pill, CollapsedHeight / 2, 2, Border);
                int dotR = 6;
                _sb.Draw(MonoXRDraw.Circle(GraphicsDevice, dotR),
                    new XnaRectangle(20 - dotR, CollapsedHeight / 2 - dotR, dotR * 2, dotR * 2), Accent);
                _sb.DrawString(_font, Name, new XnaVector2(34, (CollapsedHeight - _font.LineSpacing) / 2f), TitleText);
                _sb.End();
                return;
            }

            // Rounded panel over a transparent canvas, so the corners are
            // see-through in VR.
            GraphicsDevice.Clear(XnaColor.Transparent);

            _sb.Begin();

            var panel = new XnaRectangle(0, 0, Width, Height);
            MonoXRDraw.RoundedRect(_sb, panel, Radius, PanelBg);

            // Title bar (rounded only at the top) with a subtle sheen, the
            // title text drop-shadowed, and a glowing accent strip underneath.
            MonoXRDraw.RoundedRect(_sb, new XnaRectangle(0, 0, Width, TitleBarHeight), Radius,
                                   TitleBg, roundBottom: false);
            MonoXRDraw.VerticalFade(_sb, new XnaRectangle(0, 0, Width, TitleBarHeight / 2), XnaColor.White * 0.05f);
            _sb.DrawString(_font, "Belt Settings", new XnaVector2(20, 12), XnaColor.Black * 0.45f);
            _sb.DrawString(_font, "Belt Settings", new XnaVector2(20, 10), TitleText);
            _sb.Draw(_white, new XnaRectangle(0, TitleBarHeight - 3, Width, 3), Accent);
            MonoXRDraw.VerticalFade(_sb, new XnaRectangle(0, TitleBarHeight, Width, 14), Accent * 0.25f);

            _menu.Draw(_sb, _fontBody, _white);

            // Rounded panel outline.
            MonoXRDraw.RoundedRectOutline(_sb, panel, Radius, 2, Border);

            _sb.End();
        }

        public override void Dispose()
        {
            OverlayNavigation.Navigated -= OnNavigated;
            _fontBody.Texture.Dispose();
            _font.Texture.Dispose();
            _white.Dispose();
            _sb.Dispose();
            base.Dispose();
        }
    }
}
