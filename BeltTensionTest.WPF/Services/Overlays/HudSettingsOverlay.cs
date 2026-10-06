using System;
using System.Collections.Generic;
using BeltTensionTest.WPF.Services.Data;
using Microsoft.Xna.Framework.Graphics;
using GameTime = Microsoft.Xna.Framework.GameTime;
using MonoXR.Client;
using XnaColor = Microsoft.Xna.Framework.Color;
using XnaRectangle = Microsoft.Xna.Framework.Rectangle;
using XnaVector2 = Microsoft.Xna.Framework.Vector2;

namespace BeltTensionTest.WPF.Services.Overlays
{
    /// <summary>
    /// In-VR settings for the HUD panels themselves: the colors a friend and
    /// a rival are painted in, how strongly that color fills their row,
    /// whether the relative boxes use it too, how many rows the standings
    /// board grows to, and what looking at a panel does to it. Built from the
    /// same MonoXR controls as <see cref="BeltSettingsOverlay"/>, so
    /// navigation input works the same.
    ///
    /// Unlike the Display panel this is not edit-mode only — colors are
    /// something you change while sitting in the pits, not while arranging
    /// panels. It starts collapsed so it is not in the way, and clicking its
    /// pill opens it (the edit-mode Collapse button is not reachable in
    /// normal use, so the panel handles that itself).
    ///
    /// Values are written straight into <see cref="DriverTags"/> and the
    /// standings board; the <c>saved</c> callback persists them.
    /// </summary>
    public sealed class HudSettingsOverlay : OverlayRenderTarget
    {
        private const int TitleBarHeight = 56;
        private const int PanelWidth = 760;
        private const int RowHeight = 48;
        private const int RowSpacing = 6;

        // App palette (Resources/Styles.xaml), same as the other panels.
        private static readonly XnaColor PanelBg = new XnaColor(0x12, 0x12, 0x1E, 235);
        private static readonly XnaColor TitleBg = new XnaColor(0x1C, 0x1C, 0x2E, 245);
        private static readonly XnaColor TitleText = new XnaColor(0xD0, 0xD0, 0xF0);
        private static readonly XnaColor Accent = new XnaColor(0x64, 0x96, 0xFF);
        private static readonly XnaColor Border = new XnaColor(0x46, 0x46, 0x6A);
        private static readonly XnaColor RowTextDim = new XnaColor(0xA0, 0xA0, 0xBE);

        private readonly SpriteBatch _sb;
        private readonly Texture2D _white;
        private readonly SpriteFont _font;     // title
        private readonly SpriteFont _fontBody; // menu rows
        private readonly SpriteFont _fontHead; // small labels

        private readonly MonoXRMenuControl _menu;
        private readonly OverlayColorRow _friendColor;
        private readonly OverlayColorRow _rivalColor;
        private readonly MonoXRSliderControl _tint;
        private readonly MonoXRCheckbox _inRelative;
        private readonly MonoXRSliderControl _rows;
        private readonly OverlayButtonRow _clearTags;
        private readonly Func<int> _taggedCount;
        private string _tagSummary = string.Empty;

        private readonly GazeFocus _gaze;
        private readonly OverlayChoiceRow _gazeEffect;
        private readonly OverlayChoiceRow _gazeSource;
        private readonly MonoXRSliderControl _gazeDim;
        private readonly MonoXRSliderControl _gazeGrow;
        private string _gazeStatus = string.Empty;

        // Option order matches the GazeEffect / GazeSource enums.
        private static readonly string[] GazeEffects = { "Nothing", "Brighten", "Enlarge", "Both" };
        private static readonly string[] GazeSources = { "Off", "Head", "Eye" };

        public override int CollapsedWidth => (int)_font.MeasureString(Name).X + 60;
        public override int CollapsedHeight => TitleBarHeight;

        /// <summary>Panel height for the fixed set of rows below.</summary>
        private static int HeightFor() =>
            TitleBarHeight + 14
            + 36 + RowSpacing            // "Driver tags" header
            + 56 + RowSpacing            // friend color
            + 56 + RowSpacing            // rival color
            + RowHeight + RowSpacing     // tint
            + RowHeight + RowSpacing     // show in relative boxes
            + RowHeight + RowSpacing     // clear all
            + 36 + RowSpacing            // "Standings" header
            + RowHeight + RowSpacing     // rows on the board
            + 36 + RowSpacing            // "Look at a panel" header
            + RowHeight + RowSpacing     // gaze effect
            + RowHeight + RowSpacing     // gaze source
            + RowHeight + RowSpacing     // dim others
            + RowHeight + RowSpacing     // enlarge by
            + 34;                        // footer line + padding

        /// <param name="standingsRows">Rows-on-the-board setting of the standings board.</param>
        /// <param name="saved">Called after any change, to persist it.</param>
        /// <param name="gaze">Gaze-focus service the gaze rows drive.</param>
        public HudSettingsOverlay(GraphicsDevice device, int x, int y,
                                  BeltSettingRow standingsRows, GazeFocus gaze, Action saved)
            : base(device, PanelWidth, HeightFor(), x, y)
        {
            Name = "HUD Settings";
            _sb = new SpriteBatch(device);
            _white = new Texture2D(device, 1, 1);
            _white.SetData(new[] { XnaColor.White });
            _font = RuntimeSpriteFont.Bake(device, "Segoe UI", 32f);
            _fontBody = RuntimeSpriteFont.Bake(device, "Segoe UI", 26f);
            _fontHead = RuntimeSpriteFont.Bake(device, "Segoe UI", 18f, System.Drawing.FontStyle.Bold);

            var tags = DriverTags.Instance;
            _taggedCount = () => tags.Count;

            _menu = new MonoXRMenuControl
            {
                Bounds = new XnaRectangle(16, TitleBarHeight + 14, Width - 32, Height - TitleBarHeight - 48),
                ItemHeight = RowHeight,
                ItemSpacing = RowSpacing,
            };

            _menu.Add(new MonoXRLabel("Driver tags"));

            _friendColor = new OverlayColorRow("Friend colour", DriverTags.Palette, tags.FriendColor);
            _friendColor.ValueChanged += c => { tags.FriendColor = c; Invalidate(); };
            _menu.Add(_friendColor);

            _rivalColor = new OverlayColorRow("Rival colour", DriverTags.Palette, tags.RivalColor);
            _rivalColor.ValueChanged += c => { tags.RivalColor = c; Invalidate(); };
            _menu.Add(_rivalColor);

            // Stored 0..0.8, shown as a percentage because that is what it
            // reads as on the rows.
            _tint = new MonoXRSliderControl("Row tint", 0f, 80f, tags.TintStrength * 100f, 5f, "0");
            _tint.ValueChanged += v => { tags.TintStrength = v / 100f; Invalidate(); };
            _menu.Add(_tint);

            _inRelative = new MonoXRCheckbox("Tag colours in Race / Qualifying", tags.ShowInRelativeBoxes);
            _inRelative.Checked += () => { tags.ShowInRelativeBoxes = true; Invalidate(); };
            _inRelative.Unchecked += () => { tags.ShowInRelativeBoxes = false; Invalidate(); };
            _menu.Add(_inRelative);

            _clearTags = new OverlayButtonRow("Clear all tags");
            _clearTags.Click += () => { tags.ClearAll(); Invalidate(); };
            _menu.Add(_clearTags);

            _menu.Add(new MonoXRLabel("Standings"));

            _rows = new MonoXRSliderControl(standingsRows.Name, standingsRows.Min, standingsRows.Max,
                                            standingsRows.Get(), standingsRows.Step, standingsRows.Format);
            _rows.ValueChanged += standingsRows.Set;
            _rows.ValueChanged += _ => Invalidate();
            _menu.Add(_rows);

            _gaze = gaze;
            _menu.Add(new MonoXRLabel("Look at a panel"));

            _gazeEffect = new OverlayChoiceRow("Looking at a panel", GazeEffects, (int)gaze.Effect);
            _gazeEffect.SelectedIndexChanged += i => { gaze.Effect = (GazeEffect)i; saved(); Invalidate(); };
            _menu.Add(_gazeEffect);

            _gazeSource = new OverlayChoiceRow("Tracked with", GazeSources, (int)gaze.Source);
            _gazeSource.SelectedIndexChanged += i => { gaze.Source = (GazeSource)i; saved(); Invalidate(); };
            _menu.Add(_gazeSource);

            _gazeDim = new MonoXRSliderControl("Dim the others", 0f, 70f, gaze.DimAmount * 100f, 5f, "0");
            _gazeDim.ValueChanged += v => { gaze.DimAmount = v / 100f; saved(); Invalidate(); };
            _menu.Add(_gazeDim);

            _gazeGrow = new MonoXRSliderControl("Enlarge by", 100f, 160f, gaze.GrowFactor * 100f, 5f, "0");
            _gazeGrow.ValueChanged += v => { gaze.GrowFactor = v / 100f; saved(); Invalidate(); };
            _menu.Add(_gazeGrow);

            // One save path for every control on the panel: the tag settings
            // all report through DriverTags.Changed, the rows slider directly.
            _tagsChanged = saved;
            tags.Changed += _tagsChanged;
            _rows.ValueChanged += _ => saved();

            OverlayNavigation.Navigated += OnNavigated;
        }

        private readonly Action _tagsChanged;

        private void OnNavigated(OverlayNavAction action)
        {
            if (IsCollapsed) return;
            _menu.HandleNavigation(action);
            Invalidate();
        }

        // ----- Mouse ---------------------------------------------------------
        // The same gestures in and out of edit mode; a press that no control
        // claims falls through so the panel still drags in edit mode.

        public override bool OnPointerPress(int x, int y)
        {
            // Collapsed: the pill is the only thing on screen, and clicking it
            // is the only way back into the panel outside edit mode.
            if (IsCollapsed)
            {
                State = OverlayTargetState.Visible;
                Invalidate();
                return true;
            }
            // The title bar doubles as the collapse button, so the panel can
            // be put away again without opening edit mode.
            if (y < TitleBarHeight)
            {
                State = OverlayTargetState.Collapsed;
                Invalidate();
                return true;
            }
            bool captured = _menu.OnPointerPress(x, y);
            Invalidate(); // the press may have moved the selection highlight
            return captured;
        }

        public override void OnPointerDrag(int x, int y) => _menu.OnPointerDrag(x, y);

        public override void OnPointerRelease(int x, int y)
        {
            _menu.OnPointerRelease(x, y);
            Invalidate();
        }

        // In edit mode the title bar must stay a drag handle, so only the
        // controls themselves claim a press.
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

        public override void Update(GameTime gameTime)
        {
            // Pull anything changed elsewhere (a reload, another panel) back
            // into the controls.
            var tags = DriverTags.Instance;
            _friendColor.Value = tags.FriendColor;
            _rivalColor.Value = tags.RivalColor;
            _tint.Value = tags.TintStrength * 100f;
            _inRelative.IsChecked = tags.ShowInRelativeBoxes;

            // Gaze rows follow the service, and the status line says why
            // nothing is happening when that is the case.
            _gazeEffect.SelectedIndex = (int)_gaze.Effect;
            _gazeSource.SelectedIndex = (int)_gaze.Source;
            _gazeDim.Value = _gaze.DimAmount * 100f;
            _gazeGrow.Value = _gaze.GrowFactor * 100f;
            _gazeDim.IsEnabled = _gaze.Effect is GazeEffect.Brighten or GazeEffect.Both;
            _gazeGrow.IsEnabled = _gaze.Effect is GazeEffect.Enlarge or GazeEffect.Both;

            string gazeStatus =
                _gaze.Source == GazeSource.Off || _gaze.Effect == GazeEffect.None ? "Gaze off"
                : !_gaze.HasSignal ? "No head pose - start the sim in VR"
                : _gaze.Source == GazeSource.Eye ? "Eye gaze unavailable - using head"
                : string.IsNullOrEmpty(_gaze.FocusedName) ? "Looking away from the panels"
                : "Looking at " + _gaze.FocusedName;
            if (gazeStatus != _gazeStatus)
            {
                _gazeStatus = gazeStatus;
                Invalidate();
            }

            int count = _taggedCount();
            string summary = count == 0 ? "No drivers tagged"
                : count == 1 ? "1 driver tagged" : $"{count} drivers tagged";
            if (summary != _tagSummary)
            {
                _tagSummary = summary;
                Invalidate();
            }
            _clearTags.IsEnabled = count > 0;

            _menu.Update(gameTime);
        }

        public override void Render(GameTime gameTime)
        {
            const int Radius = 18;

            GraphicsDevice.Clear(XnaColor.Transparent);

            if (IsCollapsed)
            {
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

            _sb.Begin();

            var panel = new XnaRectangle(0, 0, Width, Height);
            MonoXRDraw.RoundedRect(_sb, panel, Radius, PanelBg);

            MonoXRDraw.RoundedRect(_sb, new XnaRectangle(0, 0, Width, TitleBarHeight), Radius,
                                   TitleBg, roundBottom: false);
            MonoXRDraw.VerticalFade(_sb, new XnaRectangle(0, 0, Width, TitleBarHeight / 2), XnaColor.White * 0.05f);
            _sb.DrawString(_font, Name, new XnaVector2(20, 12), XnaColor.Black * 0.45f);
            _sb.DrawString(_font, Name, new XnaVector2(20, 10), TitleText);

            // Hint that the title bar puts the panel away again.
            const string Close = "click title to close";
            var closeSize = _fontHead.MeasureString(Close);
            _sb.DrawString(_fontHead, Close,
                new XnaVector2(Width - 20 - closeSize.X, (TitleBarHeight - _fontHead.LineSpacing) / 2f), RowTextDim);

            _sb.Draw(_white, new XnaRectangle(0, TitleBarHeight - 3, Width, 3), Accent);
            MonoXRDraw.VerticalFade(_sb, new XnaRectangle(0, TitleBarHeight, Width, 14), Accent * 0.25f);

            _menu.Draw(_sb, _fontBody, _white);

            // Footer: tag count on the left, what the gaze is doing on the right.
            _sb.DrawString(_fontHead, _tagSummary + "  -  click a row on the standings board to tag a driver",
                new XnaVector2(28, Height - 30), RowTextDim);
            var gazeSize = _fontHead.MeasureString(_gazeStatus);
            _sb.DrawString(_fontHead, _gazeStatus,
                new XnaVector2(Width - 28 - gazeSize.X, Height - 30), RowTextDim);

            MonoXRDraw.RoundedRectOutline(_sb, panel, Radius, 2, Border);

            _sb.End();
        }

        public override void Dispose()
        {
            OverlayNavigation.Navigated -= OnNavigated;
            DriverTags.Instance.Changed -= _tagsChanged;
            _fontHead.Texture.Dispose();
            _fontBody.Texture.Dispose();
            _font.Texture.Dispose();
            _white.Dispose();
            _sb.Dispose();
            base.Dispose();
        }
    }
}
