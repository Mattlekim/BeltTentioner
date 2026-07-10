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
    /// In-VR car-alongside spotter, ported from IrachingHud's NearbyCarAlert
    /// ("Car Side Warning"). Driven by iRacing's CarLeftRight telemetry: a
    /// wide, mostly-empty box with two indicator slots on each edge — your car
    /// sits in the gap in the middle. One car alongside lights the inner slot
    /// on that side, two cars light both slots, cars on both sides light both
    /// inner slots. The blocks flash at the original's cadence and the whole
    /// thing is invisible while clear. Suppressed in Lone Qualify sessions,
    /// like the original.
    /// </summary>
    public sealed class NearbyCarsOverlay : OverlayRenderTarget
    {
        private const int MinBoxWidth = 500;  // slider range, from the original
        private const int MaxBoxWidth = 2000;
        private const int BoxHeight = 50;
        private const int Block = 50;      // indicator square size, from the original
        private const int BlockGap = 10;   // between the two slots on one side
        private const int TrackLen = 240;  // edit-mode width slider track length

        private static readonly XnaColor AlertRed = new XnaColor(0xE0, 0x30, 0x30, 245);
        private static readonly XnaColor AlertRedDark = new XnaColor(0x80, 0x18, 0x18);
        private static readonly XnaColor GhostGray = new XnaColor(0x40, 0x40, 0x55, 200);
        private static readonly XnaColor TitleText = new XnaColor(0xD0, 0xD0, 0xF0);

        private readonly SpriteBatch _sb;
        private readonly SpriteFont _fontHeader;
        private readonly int _collapsedWidth;

        private bool _innerLeft, _outerLeft, _innerRight, _outerRight;
        private bool _wasShowing;
        private float _flasher; // original cadence: dt*15, cycle 2

        // Edit-mode width slider. The track geometry is frozen at press so
        // dragging stays stable while the box (and thus the live track
        // position) resizes under the pointer.
        private bool _sliderDrag;
        private XnaRectangle _dragTrack;

        public override int CollapsedWidth => _collapsedWidth;
        public override int CollapsedHeight => BoxHeight;

        /// <summary>Raised when the user finishes dragging the edit-mode width slider.</summary>
        public event Action? BoxWidthChanged;

        /// <summary>
        /// Overall strip width (the indicator blocks stay put: left ones on
        /// the left edge, right ones anchored to the right edge). Clamped to
        /// the original's 500–2000 range.
        /// </summary>
        public int BoxWidth
        {
            get => Width;
            set => Resize(Math.Clamp(value, MinBoxWidth, MaxBoxWidth), BoxHeight);
        }

        public NearbyCarsOverlay(GraphicsDevice device, int x, int y)
            : base(device, MinBoxWidth, BoxHeight, x, y)
        {
            Name = "Nearby Cars";
            _sb = new SpriteBatch(device);
            _fontHeader = RuntimeSpriteFont.Bake(device, "Segoe UI", 26f, System.Drawing.FontStyle.Bold);
            _collapsedWidth = (int)_fontHeader.MeasureString(Name).X + 60;
        }

        public override void Update(GameTime gameTime)
        {
            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
            if (dt <= 0) return;

            _flasher += dt * 15f;
            if (_flasher > 2f) _flasher -= 2f;

            _innerLeft = _outerLeft = _innerRight = _outerRight = false;

            var svc = IracingService.Instance;
            bool loneQual = svc.SessionType.IndexOf("Lone Qual", StringComparison.OrdinalIgnoreCase) >= 0;
            if (svc.IsConnected && !loneQual)
            {
                switch (svc.CarsAlongside)
                {
                    case CarLeftRight.CarLeft: _innerLeft = true; break;
                    case CarLeftRight.TwoCarsLeft: _innerLeft = _outerLeft = true; break;
                    case CarLeftRight.CarRight: _innerRight = true; break;
                    case CarLeftRight.TwoCarsRight: _innerRight = _outerRight = true; break;
                    case CarLeftRight.CarLeftRight: _innerLeft = _innerRight = true; break;
                }
            }

            // While alerting the blocks flash every frame; when it clears one
            // more redraw wipes the box.
            bool showing = _innerLeft || _outerLeft || _innerRight || _outerRight;
            if (showing || _wasShowing)
                Invalidate();
            _wasShowing = showing;
        }

        // ----- Edit-mode width slider (mouse-driven) -------------------------

        private XnaRectangle TrackRect() =>
            new XnaRectangle(Width / 2 - TrackLen / 2, Height / 2 - 4, TrackLen, 8);

        public override bool OnEditPress(int x, int y)
        {
            if (IsCollapsed) return false;
            var hit = TrackRect();
            hit.Inflate(14, 18); // generous target: the knob + around the track
            if (!hit.Contains(x, y)) return false;
            _sliderDrag = true;
            _dragTrack = TrackRect();
            ApplySlider(x);
            return true;
        }

        public override void OnEditDrag(int x, int y)
        {
            if (_sliderDrag) ApplySlider(x);
        }

        public override void OnEditRelease(int x, int y)
        {
            if (!_sliderDrag) return;
            _sliderDrag = false;
            Invalidate();
            BoxWidthChanged?.Invoke();
        }

        private void ApplySlider(int x)
        {
            float frac = Math.Clamp((x - _dragTrack.X) / (float)_dragTrack.Width, 0f, 1f);
            BoxWidth = MinBoxWidth + (int)(frac * (MaxBoxWidth - MinBoxWidth));
        }

        private void DrawSlider()
        {
            // While dragging, draw against the frozen track so the knob stays
            // under the pointer as the box resizes; otherwise centered live.
            var track = _sliderDrag ? _dragTrack : TrackRect();
            float frac = (Width - MinBoxWidth) / (float)(MaxBoxWidth - MinBoxWidth);

            MonoXRDraw.RoundedRect(_sb, track, 4, new XnaColor(0x30, 0x30, 0x44, 235));
            var filled = new XnaRectangle(track.X, track.Y, Math.Max(8, (int)(track.Width * frac)), track.Height);
            MonoXRDraw.RoundedRect(_sb, filled, 4, new XnaColor(0x64, 0x96, 0xFF));

            int knobX = track.X + (int)(track.Width * frac);
            MonoXRDraw.RoundedRect(_sb, new XnaRectangle(knobX - 8, track.Y - 8, 16, track.Height + 16), 6, XnaColor.White);

            // Width readout beside the track (only when it has room before
            // the right-side blocks).
            string readout = $"{Width}px";
            var size = _fontHeader.MeasureString(readout);
            int rx = track.Right + 16;
            if (rx + size.X < Width - Block * 2 - BlockGap - 8)
                _sb.DrawString(_fontHeader, readout,
                    new XnaVector2(rx, (Height - _fontHeader.LineSpacing) / 2f), TitleText);
        }

        public override void Render(GameTime gameTime)
        {
            GraphicsDevice.Clear(XnaColor.Transparent);

            if (IsCollapsed)
            {
                _sb.Begin();
                var pill = new XnaRectangle(0, 0, CollapsedWidth, CollapsedHeight);
                MonoXRDraw.RoundedRect(_sb, pill, CollapsedHeight / 2, AlertRed);
                MonoXRDraw.RoundedRectOutline(_sb, pill, CollapsedHeight / 2, 2, AlertRedDark);
                int dotR = 6;
                _sb.Draw(MonoXRDraw.Circle(GraphicsDevice, dotR),
                    new XnaRectangle(20 - dotR, CollapsedHeight / 2 - dotR, dotR * 2, dotR * 2), XnaColor.White);
                _sb.DrawString(_fontHeader, Name,
                    new XnaVector2(34, (CollapsedHeight - _fontHeader.LineSpacing) / 2f), XnaColor.White);
                _sb.End();
                return;
            }

            // Invisible while clear; edit mode draws all four slots dimmed as
            // a placeholder so the box can be found and dragged (MoveMode in
            // the original).
            bool showing = _innerLeft || _outerLeft || _innerRight || _outerRight;
            bool placeholder = !showing && EditMode;
            if (!showing && !placeholder) return;

            _sb.Begin();

            if (placeholder)
            {
                foreach (var r in SlotRects(true, true, true, true))
                    MonoXRDraw.RoundedRect(_sb, r, 8, GhostGray);
            }
            else if (_flasher > 1f)
            {
                foreach (var r in SlotRects(_outerLeft, _innerLeft, _innerRight, _outerRight))
                {
                    MonoXRDraw.RoundedRect(_sb, r, 8, AlertRed);
                    MonoXRDraw.RoundedRectOutline(_sb, r, 8, 3, AlertRedDark);
                }
            }

            if (EditMode)
                DrawSlider();

            _sb.End();
        }

        /// <summary>Rectangles for the requested slots: outer/inner on the left edge, inner/outer on the right.</summary>
        private XnaRectangle[] SlotRects(bool outerLeft, bool innerLeft, bool innerRight, bool outerRight)
        {
            var rects = new System.Collections.Generic.List<XnaRectangle>(4);
            if (outerLeft) rects.Add(new XnaRectangle(0, 0, Block, Block));
            if (innerLeft) rects.Add(new XnaRectangle(Block + BlockGap, 0, Block, Block));
            if (innerRight) rects.Add(new XnaRectangle(Width - Block * 2 - BlockGap, 0, Block, Block));
            if (outerRight) rects.Add(new XnaRectangle(Width - Block, 0, Block, Block));
            return rects.ToArray();
        }

        public override void Dispose()
        {
            _fontHeader.Texture.Dispose();
            _sb.Dispose();
            base.Dispose();
        }
    }
}
