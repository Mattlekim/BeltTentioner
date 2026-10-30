using System;
using BeltTensionTest.WPF.Models;
using Microsoft.Xna.Framework.Graphics;
using GameTime = Microsoft.Xna.Framework.GameTime;
using MonoXR.Client;
using XnaColor = Microsoft.Xna.Framework.Color;
using XnaRectangle = Microsoft.Xna.Framework.Rectangle;
using XnaVector2 = Microsoft.Xna.Framework.Vector2;

namespace BeltTensionTest.WPF.Services.Overlays
{
    /// <summary>
    /// In-VR twin of Preferences &gt; Head Tracking (EXPERIMENTAL): smooth the head
    /// pose the VR game receives, ignore small movements, or lock the head
    /// position and keep only rotation. Values are written straight into
    /// <see cref="AppSettings"/>; the <c>changed</c> callback applies them to the
    /// layer and saves. Edit-mode only, docked on the left under HUD Settings.
    /// </summary>
    public sealed class HeadTrackingOverlay : OverlayRenderTarget
    {
        private const int TitleBarHeight = 56;
        private const int PanelWidth = 700;
        private const int RowHeight = 48;
        private const int RowSpacing = 6;

        private static readonly XnaColor PanelBg = new XnaColor(0x12, 0x12, 0x1E, 235);
        private static readonly XnaColor TitleBg = new XnaColor(0x1C, 0x1C, 0x2E, 245);
        private static readonly XnaColor TitleText = new XnaColor(0xD0, 0xD0, 0xF0);
        private static XnaColor Accent => ColorBlind.Map(CbRole.Info, new XnaColor(0x64, 0x96, 0xFF));
        private static readonly XnaColor Border = new XnaColor(0x46, 0x46, 0x6A);
        private static readonly XnaColor RowTextDim = new XnaColor(0xA0, 0xA0, 0xBE);

        private readonly SpriteBatch _sb;
        private readonly Texture2D _white;
        private readonly SpriteFont _font;
        private readonly SpriteFont _fontBody;
        private readonly SpriteFont _fontHead;
        private readonly MonoXRMenuControl _menu;

        private readonly MonoXRSliderControl _lockX, _lockY, _lockZ;
        private readonly OverlayButtonRow _lockHere;
        private string _status = string.Empty;

        // Header, 3 groups of toggle + sliders, lock button, status line.
        private static int HeightFor() =>
            TitleBarHeight + 14
            + 36 + RowSpacing                              // "Smoothing" header
            + 2 * (RowHeight + RowSpacing)                 // smooth on, amount
            + 36 + RowSpacing                              // "Small movements" header
            + 3 * (RowHeight + RowSpacing)                 // on, position, rotation
            + 36 + RowSpacing                              // "Lock position" header
            + 5 * (RowHeight + RowSpacing)                 // on, use current, X, Y, Z
            + 34;

        public HeadTrackingOverlay(GraphicsDevice device, int x, int y, AppSettings settings, Action changed)
            : base(device, PanelWidth, HeightFor(), x, y)
        {
            Name = "Head Tracking";
            _sb = new SpriteBatch(device);
            _white = new Texture2D(device, 1, 1);
            _white.SetData(new[] { XnaColor.White });
            _font = RuntimeSpriteFont.Bake(device, "Segoe UI", 32f);
            _fontBody = RuntimeSpriteFont.Bake(device, "Segoe UI", 26f);
            _fontHead = RuntimeSpriteFont.Bake(device, "Segoe UI", 18f, System.Drawing.FontStyle.Bold);

            _menu = new MonoXRMenuControl
            {
                Bounds = new XnaRectangle(16, TitleBarHeight + 14, Width - 32, Height - TitleBarHeight - 48),
                ItemHeight = RowHeight,
                ItemSpacing = RowSpacing,
            };

            void Toggle(string text, bool value, Action<bool> set)
            {
                var box = new MonoXRCheckbox(text, value);
                box.Checked += () => { set(true); changed(); Invalidate(); };
                box.Unchecked += () => { set(false); changed(); Invalidate(); };
                _menu.Add(box);
            }
            MonoXRSliderControl Slider(string text, float min, float max, float value, float step, string format, Action<float> set)
            {
                var slider = new MonoXRSliderControl(text, min, max, value, step, format);
                slider.ValueChanged += v => { set(v); changed(); Invalidate(); };
                _menu.Add(slider);
                return slider;
            }

            _menu.Add(new MonoXRLabel("Smoothing"));
            Toggle("Smooth head tracking", settings.HeadSmoothEnabled, v => settings.HeadSmoothEnabled = v);
            Slider("Smoothing (%)", 0f, 100f, (float)settings.HeadSmoothAmount * 100f, 5f, "0",
                   v => settings.HeadSmoothAmount = v / 100.0);

            _menu.Add(new MonoXRLabel("Small movements"));
            Toggle("Ignore small movements", settings.HeadMinMoveEnabled, v => settings.HeadMinMoveEnabled = v);
            Slider("Position (mm)", 0f, 20f, (float)settings.HeadMinMovePositionMm, 0.5f, "0.0",
                   v => settings.HeadMinMovePositionMm = v);
            Slider("Rotation (deg)", 0f, 2f, (float)settings.HeadMinMoveRotationDeg, 0.05f, "0.00",
                   v => settings.HeadMinMoveRotationDeg = v);

            _menu.Add(new MonoXRLabel("Lock position"));
            Toggle("Lock position, rotation only", settings.HeadLockEnabled, v => settings.HeadLockEnabled = v);
            _lockHere = new OverlayButtonRow("Use current head position");
            _lockHere.Click += () =>
            {
                if (!Views.OverlayWindow.TryGetHeadPosition(out var p))
                {
                    _status = "No head position yet - start the VR game with the headset on";
                    Invalidate();
                    return;
                }
                _status = string.Empty;
                // Setting a slider's Value does not raise ValueChanged, so write the settings too.
                settings.HeadLockX = Math.Clamp(p.X, -2f, 2f);
                settings.HeadLockY = Math.Clamp(p.Y, -2f, 2f);
                settings.HeadLockZ = Math.Clamp(p.Z, -2f, 2f);
                _lockX!.Value = (float)settings.HeadLockX;
                _lockY!.Value = (float)settings.HeadLockY;
                _lockZ!.Value = (float)settings.HeadLockZ;
                changed();
                Invalidate();
            };
            _menu.Add(_lockHere);
            _lockX = Slider("Lock X (m)", -2f, 2f, (float)settings.HeadLockX, 0.01f, "0.00", v => settings.HeadLockX = v);
            _lockY = Slider("Lock Y (m)", -2f, 2f, (float)settings.HeadLockY, 0.01f, "0.00", v => settings.HeadLockY = v);
            _lockZ = Slider("Lock Z (m)", -2f, 2f, (float)settings.HeadLockZ, 0.01f, "0.00", v => settings.HeadLockZ = v);

            OverlayNavigation.Navigated += OnNavigated;
        }

        private void OnNavigated(OverlayNavAction action)
        {
            if (IsCollapsed) return;
            _menu.HandleNavigation(action);
            Invalidate();
        }

        // Edit-mode only, so only the edit gestures are used; the title bar stays a drag handle.
        public override bool OnEditPress(int x, int y)
        {
            if (IsCollapsed || y < TitleBarHeight) return false;
            bool captured = _menu.OnPointerPress(x, y);
            Invalidate();
            return captured;
        }

        public override void OnEditDrag(int x, int y) => _menu.OnPointerDrag(x, y);

        public override void OnEditRelease(int x, int y)
        {
            _menu.OnPointerRelease(x, y);
            Invalidate();
        }

        public override void Update(GameTime gameTime) => _menu.Update(gameTime);

        public override void Render(GameTime gameTime)
        {
            const int Radius = 18;
            GraphicsDevice.Clear(XnaColor.Transparent);
            _sb.Begin();

            var panel = new XnaRectangle(0, 0, Width, Height);
            MonoXRDraw.RoundedRect(_sb, panel, Radius, PanelBg);
            MonoXRDraw.RoundedRect(_sb, new XnaRectangle(0, 0, Width, TitleBarHeight), Radius, TitleBg, roundBottom: false);
            MonoXRDraw.VerticalFade(_sb, new XnaRectangle(0, 0, Width, TitleBarHeight / 2), XnaColor.White * 0.05f);
            _sb.DrawString(_font, Name + " (experimental)", new XnaVector2(20, 12), XnaColor.Black * 0.45f);
            _sb.DrawString(_font, Name + " (experimental)", new XnaVector2(20, 10), TitleText);
            _sb.Draw(_white, new XnaRectangle(0, TitleBarHeight - 3, Width, 3), Accent);
            MonoXRDraw.VerticalFade(_sb, new XnaRectangle(0, TitleBarHeight, Width, 14), Accent * 0.25f);

            _menu.Draw(_sb, _fontBody, _white);

            if (_status.Length > 0)
                _sb.DrawString(_fontHead, _status, new XnaVector2(28, Height - 30), RowTextDim);

            MonoXRDraw.RoundedRectOutline(_sb, panel, Radius, 2, Border);
            _sb.End();
        }

        public override void Dispose()
        {
            OverlayNavigation.Navigated -= OnNavigated;
            _fontHead.Texture.Dispose();
            _fontBody.Texture.Dispose();
            _font.Texture.Dispose();
            _white.Dispose();
            _sb.Dispose();
            base.Dispose();
        }
    }
}
