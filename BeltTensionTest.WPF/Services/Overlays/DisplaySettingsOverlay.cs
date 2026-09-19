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
    /// In-VR copy of the overlay window's edit panel: sliders for the VR
    /// display width/height and distance (meters) and the canvas DPI (pixels
    /// per meter), plus a read-only canvas resolution line. Built from the
    /// same MonoXR controls as <see cref="BeltSettingsOverlay"/>, and rows are
    /// plain <see cref="BeltSettingRow"/>s so the values keep living in
    /// OverlayWindow (which applies them to the host and saves them).
    ///
    /// <see cref="OverlayRenderTarget.EditModeOnly"/> is set, so the panel
    /// only exists while edit mode is on — it is what you use to size and
    /// place the overlay without taking the headset off.
    /// </summary>
    public sealed class DisplaySettingsOverlay : OverlayRenderTarget
    {
        private const int TitleBarHeight = 56;
        private const int PanelWidth = 700;
        private const int RowHeight = 48;
        private const int RowSpacing = 6;
        private const int ResolutionRowHeight = 36;

        // App palette (Resources/Styles.xaml), same as the belt panel.
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
        private readonly MonoXRLabel _resolution;
        private readonly Func<string> _resolutionText;
        private readonly int _collapsedWidth;

        public override int CollapsedWidth => _collapsedWidth;
        public override int CollapsedHeight => TitleBarHeight;

        /// <summary>Panel height for <paramref name="rowCount"/> sliders plus the resolution line.</summary>
        private static int HeightFor(int rowCount) =>
            TitleBarHeight + 14 + rowCount * (RowHeight + RowSpacing) + ResolutionRowHeight + 16;

        /// <param name="rows">Width / height / distance / DPI, in display order.</param>
        /// <param name="resolutionText">Current canvas resolution, polled every frame for the readout.</param>
        public DisplaySettingsOverlay(GraphicsDevice device, int x, int y,
                                      IReadOnlyList<BeltSettingRow> rows, Func<string> resolutionText)
            : base(device, PanelWidth, HeightFor(rows.Count), x, y)
        {
            Name = "Display";
            EditModeOnly = true;
            _resolutionText = resolutionText;
            _sb = new SpriteBatch(device);
            _white = new Texture2D(device, 1, 1);
            _white.SetData(new[] { XnaColor.White });
            _font = RuntimeSpriteFont.Bake(device, "Segoe UI", 32f);
            _fontBody = RuntimeSpriteFont.Bake(device, "Segoe UI", 26f);
            _collapsedWidth = (int)_font.MeasureString(Name).X + 60; // name + accent dot + padding

            _menu = new MonoXRMenuControl
            {
                Bounds = new XnaRectangle(16, TitleBarHeight + 14, Width - 32, Height - TitleBarHeight - 22),
                ItemHeight = RowHeight,
                ItemSpacing = RowSpacing,
            };

            foreach (var row in rows)
            {
                var slider = new MonoXRSliderControl(row.Name, row.Min, row.Max, row.Get(),
                                                     row.Step, row.Format)
                { FillColor = row.Fill };
                slider.ValueChanged += row.Set;
                slider.ValueChanged += _ => Invalidate();
                _menu.Add(slider);
                _bindings.Add((row, slider));
            }

            // Not selectable (MonoXRLabel is disabled), so navigation skips it.
            _resolution = new MonoXRLabel(resolutionText()) { PreferredHeight = ResolutionRowHeight };
            _menu.Add(_resolution);

            OverlayNavigation.Navigated += OnNavigated;
        }

        private void OnNavigated(OverlayNavAction action)
        {
            if (IsCollapsed) return;
            _menu.HandleNavigation(action);
            Invalidate(); // Up/Down move the highlight without changing a value
        }

        // ----- Mouse ---------------------------------------------------------
        // The panel only exists in edit mode, so only the edit-mode gestures
        // matter: pressing a slider bar sets/drags it, presses elsewhere fall
        // through so the panel itself still drags.

        public override bool OnEditPress(int x, int y)
        {
            if (IsCollapsed) return false;
            bool captured = _menu.OnPointerPress(x, y);
            Invalidate(); // the press may have moved the selection highlight
            return captured;
        }

        public override void OnEditDrag(int x, int y) => _menu.OnPointerDrag(x, y);

        public override void OnEditRelease(int x, int y)
        {
            _menu.OnPointerRelease(x, y);
            Invalidate();
        }

        public override void Update(GameTime gameTime)
        {
            // Pull changes made elsewhere (the overlay window's own sliders, a
            // depth-only distance change that rescales width/height/DPI) into
            // the controls.
            foreach (var (row, slider) in _bindings)
                slider.Value = row.Get();

            string resolution = _resolutionText();
            if (resolution != _resolution.Text)
            {
                _resolution.Text = resolution;
                Invalidate();
            }

            _menu.Update(gameTime);
        }

        public override void Render(GameTime gameTime)
        {
            const int Radius = 18;

            if (IsCollapsed)
            {
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

            GraphicsDevice.Clear(XnaColor.Transparent);

            _sb.Begin();

            var panel = new XnaRectangle(0, 0, Width, Height);
            MonoXRDraw.RoundedRect(_sb, panel, Radius, PanelBg);

            MonoXRDraw.RoundedRect(_sb, new XnaRectangle(0, 0, Width, TitleBarHeight), Radius,
                                   TitleBg, roundBottom: false);
            MonoXRDraw.VerticalFade(_sb, new XnaRectangle(0, 0, Width, TitleBarHeight / 2), XnaColor.White * 0.05f);
            _sb.DrawString(_font, "Display", new XnaVector2(20, 12), XnaColor.Black * 0.45f);
            _sb.DrawString(_font, "Display", new XnaVector2(20, 10), TitleText);
            _sb.Draw(_white, new XnaRectangle(0, TitleBarHeight - 3, Width, 3), Accent);
            MonoXRDraw.VerticalFade(_sb, new XnaRectangle(0, TitleBarHeight, Width, 14), Accent * 0.25f);

            _menu.Draw(_sb, _fontBody, _white);

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
