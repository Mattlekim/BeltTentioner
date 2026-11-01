using System;
using System.Collections.Generic;
using System.Linq;
using BeltTensionTest.WPF.Services.Data;
using Microsoft.Xna.Framework.Graphics;
using GameTime = Microsoft.Xna.Framework.GameTime;
using MonoXR.Client;
using MonoXR.Shared;
using XnaColor = Microsoft.Xna.Framework.Color;
using XnaRectangle = Microsoft.Xna.Framework.Rectangle;
using XnaVector2 = Microsoft.Xna.Framework.Vector2;

namespace BeltTensionTest.WPF.Services.Overlays
{
    /// <summary>The overlay-wide switches the HUD Settings panel can change, owned by the overlay window.</summary>
    public sealed class HudOptions
    {
        public bool EyeTracking => GetEyeTracking();
        public int ColorBlindIndex => GetColorBlindIndex();
        public Action<bool> SetEyeTracking { get; init; } = _ => { };
        public Action<int> SetColorBlindIndex { get; init; } = _ => { };
        public RecenterBinding Recenter { get; init; } = null!;
        public Func<bool> GetEyeTracking { get; init; } = () => true;
        public Func<int> GetColorBlindIndex { get; init; } = () => 0;

        // Car behind (the "delta behind" box): how far back it shows, when it counts as close,
        // and whether it flashes when close. Live in the app settings, so the box picks them up at once.
        public Func<double> GetCarBehindShow { get; init; } = () => CarBehindOverlay.DefaultShowGap;
        public Action<double> SetCarBehindShow { get; init; } = _ => { };
        public Func<double> GetCarBehindClose { get; init; } = () => CarBehindOverlay.DefaultCloseGap;
        public Action<double> SetCarBehindClose { get; init; } = _ => { };
        public Func<bool> GetCarBehindFlash { get; init; } = () => true;
        public Action<bool> SetCarBehindFlash { get; init; } = _ => { };
    }

    /// <summary>
    /// In-VR settings for the HUD panels themselves: the colors a friend and
    /// a rival are painted in, how strongly that color fills their row,
    /// whether the relative boxes use it too, the overlay-wide switches (eye
    /// tracking, colour-blind mode, the controller button that recenters) and
    /// what looking at a panel does to it. Built from the
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
        public override int TitleBarSourceHeight => TitleBarHeight;
        private const int PanelWidth = 760;
        private const int RowHeight = 48;
        private const int RowSpacing = 6;

        // App palette (Resources/Styles.xaml), same as the other panels.
        private static readonly XnaColor PanelBg = new XnaColor(0x12, 0x12, 0x1E, 235);
        private static readonly XnaColor TitleBg = new XnaColor(0x1C, 0x1C, 0x2E, 245);
        private static readonly XnaColor TitleText = new XnaColor(0xD0, 0xD0, 0xF0);
        private static XnaColor Accent => ColorBlind.Map(CbRole.Info, new XnaColor(0x64, 0x96, 0xFF));
        private static readonly XnaColor Border = new XnaColor(0x46, 0x46, 0x6A);
        private static readonly XnaColor RowTextDim = new XnaColor(0xA0, 0xA0, 0xBE);

        private readonly SpriteBatch _sb;
        private readonly Texture2D _white;
        private SpriteFont _font => OverlayFonts.Title(GraphicsDevice, 32f);     // title
        private SpriteFont _fontBody => OverlayFonts.Body(GraphicsDevice, 26f); // menu rows
        private SpriteFont _fontHead => OverlayFonts.Body(GraphicsDevice, 18f, bold: true); // small labels

        private readonly MonoXRMenuControl _menu;
        private readonly OverlayColorRow _friendColor;
        private readonly OverlayColorRow _rivalColor;
        private readonly MonoXRSliderControl _tint;
        private readonly MonoXRCheckbox _inRelative;
        private readonly MonoXRCheckbox _eyeTracking;
        private readonly OverlayChoiceRow _fontChoice;
        private readonly MonoXRSliderControl _fontSize;
        private readonly MonoXRCheckbox _fontBold;
        private readonly OverlayChoiceRow _titleFontChoice;
        private readonly MonoXRSliderControl _titleFontSize;
        private readonly OverlayChoiceRow _colorBlind;
        private readonly OverlayButtonRow _recenterBtn;
        private readonly HudOptions _options;
        private string _recenterText = string.Empty;
        private readonly OverlayButtonRow _clearTags;
        private readonly OverlayChoiceRow _pinTags;
        private readonly MonoXRSliderControl _behindShow;
        private readonly MonoXRSliderControl _behindClose;
        private readonly MonoXRCheckbox _behindFlash;
        private readonly Func<int> _taggedCount;
        private string _tagSummary = string.Empty;

        private readonly GazeFocus _gaze;
        private readonly OverlayChoiceRow _gazeEffect;
        private readonly MonoXRSliderControl _gazeDim;
        private readonly MonoXRSliderControl _gazeGrow;
        private string _gazeStatus = string.Empty;

        // Option order matches the GazeEffect / GazeSource enums.
        private static readonly string[] GazeEffects = { "Nothing", "Brighten", "Enlarge", "Both" };

        // Option order matches the DriverPin enum.
        private static readonly string[] PinChoices = { "Off", "Friends", "Rivals", "Friends + rivals" };

        // Why "Eye" is running on head direction instead, by what the layer reports.
        private static string EyeFallbackStatus(MonoXrGazeState state) => state switch
        {
            MonoXrGazeState.Unsupported => "No eye tracking on this headset/game - using head",
            MonoXrGazeState.Idle or MonoXrGazeState.NoSignal => "Waiting for eye tracking - using head",
            _ => "Eye tracking needs a game restart - using head",
        };

        public override int CollapsedWidth => (int)_font.MeasureString(Name).X + 60;
        public override int CollapsedHeight => TitleBarHeight;

        /// <summary>
        /// Panel height for the fixed set of rows below: five section headers, the two colour
        /// rows, and 18 ordinary rows (tint, tags in relative boxes, always show, clear all;
        /// show within, close distance, flash; eye tracking, colour blind, recenter; font, size,
        /// bold, title font, title size; gaze effect, faded opacity, enlarge). Add a row to the
        /// menu and this count has to grow with it, or the last rows are cut off.
        /// </summary>
        private static int HeightFor() =>
            TitleBarHeight + 14
            + 5 * (36 + RowSpacing)      // section headers
            + 2 * (56 + RowSpacing)      // friend colour, rival colour
            + 18 * (RowHeight + RowSpacing)
            + 34;                        // footer line + padding

        /// <param name="options">Overlay-wide switches the Overlay rows drive.</param>
        /// <param name="saved">Called after any change, to persist it.</param>
        /// <param name="gaze">Gaze-focus service the gaze rows drive.</param>
        public HudSettingsOverlay(GraphicsDevice device, int x, int y,
                                  HudOptions options, GazeFocus gaze, Action saved)
            : base(device, PanelWidth, HeightFor(), x, y)
        {
            Name = "HUD Settings";
            _sb = new SpriteBatch(device);
            _white = new Texture2D(device, 1, 1);
            _white.SetData(new[] { XnaColor.White });

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

            // Keep tagged drivers on the standings board even when the field is longer than the board.
            _pinTags = new OverlayChoiceRow("Always show on standings", PinChoices, (int)tags.PinMode);
            _pinTags.SelectedIndexChanged += i => { tags.PinMode = (DriverPin)i; Invalidate(); };
            _menu.Add(_pinTags);

            // The delta-behind box: these write straight into the app settings it reads every frame.
            _options = options;
            _menu.Add(new MonoXRLabel("Car behind (delta box)"));

            _behindShow = new MonoXRSliderControl("Show within (s)", 0.1f, 5f, (float)options.GetCarBehindShow(), 0.1f, "0.0");
            _behindShow.ValueChanged += v => { options.SetCarBehindShow(Math.Round(v, 2)); saved(); Invalidate(); };
            _menu.Add(_behindShow);

            _behindClose = new MonoXRSliderControl("Close distance (s)", 0.05f, 0.5f, (float)options.GetCarBehindClose(), 0.05f, "0.00");
            _behindClose.ValueChanged += v => { options.SetCarBehindClose(Math.Round(v, 2)); saved(); Invalidate(); };
            _menu.Add(_behindClose);

            _behindFlash = new MonoXRCheckbox("Flash when close", options.GetCarBehindFlash());
            _behindFlash.Checked += () => { options.SetCarBehindFlash(true); saved(); Invalidate(); };
            _behindFlash.Unchecked += () => { options.SetCarBehindFlash(false); saved(); Invalidate(); };
            _menu.Add(_behindFlash);

            _menu.Add(new MonoXRLabel("Overlay"));

            _eyeTracking = new MonoXRCheckbox("Enable eye tracking", options.EyeTracking);
            _eyeTracking.Checked += () => { options.SetEyeTracking(true); saved(); Invalidate(); };
            _eyeTracking.Unchecked += () => { options.SetEyeTracking(false); saved(); Invalidate(); };
            _menu.Add(_eyeTracking);

            _colorBlind = new OverlayChoiceRow("Colour blind mode", ColorBlindModes, options.ColorBlindIndex);
            _colorBlind.SelectedIndexChanged += i => { options.SetColorBlindIndex(i); saved(); Invalidate(); };
            _menu.Add(_colorBlind);

            // Click to bind: the next controller button pressed recenters the
            // overlay from then on. Clicking again while it waits clears it.
            _recenterBtn = new OverlayButtonRow(RecenterText());
            _recenterBtn.Click += () =>
            {
                if (options.Recenter.Capturing || options.Recenter.Binding.Length > 0) options.Recenter.Clear();
                else options.Recenter.BeginCapture();
                Invalidate();
            };
            _menu.Add(_recenterBtn);

            // Font and size of the relative box and the standings board.
            _menu.Add(new MonoXRLabel("Text (all windows)"));
            var fonts = OverlayFonts.Available.ToArray();
            _fontChoice = new OverlayChoiceRow("Font", fonts, Math.Max(0, Array.IndexOf(fonts, OverlayFonts.Family)));
            _fontChoice.SelectedIndexChanged += i => { OverlayFonts.Family = fonts[i]; saved(); Invalidate(); };
            _menu.Add(_fontChoice);
            _fontSize = new MonoXRSliderControl("Text size (%)", OverlayFonts.MinPercent, OverlayFonts.MaxPercent,
                                                OverlayFonts.Percent, 5f, "0");
            _fontSize.ValueChanged += v => { OverlayFonts.Percent = (int)v; saved(); Invalidate(); };
            _menu.Add(_fontSize);
            _fontBold = new MonoXRCheckbox("Bold text", OverlayFonts.Bold);
            _fontBold.Checked += () => { OverlayFonts.Bold = true; saved(); Invalidate(); };
            _fontBold.Unchecked += () => { OverlayFonts.Bold = false; saved(); Invalidate(); };
            _menu.Add(_fontBold);

            _titleFontChoice = new OverlayChoiceRow("Title font", fonts, Math.Max(0, Array.IndexOf(fonts, OverlayFonts.TitleFamily)));
            _titleFontChoice.SelectedIndexChanged += i => { OverlayFonts.TitleFamily = fonts[i]; saved(); Invalidate(); };
            _menu.Add(_titleFontChoice);
            _titleFontSize = new MonoXRSliderControl("Title size (%)", OverlayFonts.MinTitlePercent, OverlayFonts.MaxTitlePercent,
                                                     OverlayFonts.TitlePercent, 5f, "0");
            _titleFontSize.ValueChanged += v => { OverlayFonts.TitlePercent = (int)v; saved(); Invalidate(); };
            _menu.Add(_titleFontSize);

            _gaze = gaze;
            _menu.Add(new MonoXRLabel("Look at a panel"));

            _gazeEffect = new OverlayChoiceRow("Looking at a panel", GazeEffects, (int)gaze.Effect);
            _gazeEffect.SelectedIndexChanged += i => { gaze.Effect = (GazeEffect)i; saved(); Invalidate(); };
            _menu.Add(_gazeEffect);

            _gazeDim = new MonoXRSliderControl("Faded opacity", 5f, 100f, gaze.FadedOpacity * 100f, 5f, "0");
            _gazeDim.ValueChanged += v => { gaze.FadedOpacity = v / 100f; saved(); Invalidate(); };
            _menu.Add(_gazeDim);

            _gazeGrow = new MonoXRSliderControl("Enlarge by", 100f, 160f, gaze.GrowFactor * 100f, 5f, "0");
            _gazeGrow.ValueChanged += v => { gaze.GrowFactor = v / 100f; saved(); Invalidate(); };
            _menu.Add(_gazeGrow);

            // One save path for every control on the panel: the tag settings
            // all report through DriverTags.Changed, the rows slider directly.
            _tagsChanged = saved;
            tags.Changed += _tagsChanged;
            options.Recenter.Changed += saved;

            OverlayNavigation.Navigated += OnNavigated;
        }

        private readonly Action _tagsChanged;

        private static readonly string[] ColorBlindModes = { "Off", "Deuteranopia", "Tritanomaly" };

        private string RecenterText() =>
            _options.Recenter.Capturing ? "Press a controller button... (click to cancel)"
            : _options.Recenter.Binding.Length > 0
                ? $"Recenter button: {RecenterBinding.Short(_options.Recenter.Binding)} (click to clear)"
                : "Bind a controller button to recenter";

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
            _pinTags.SelectedIndex = (int)tags.PinMode;
            _behindShow.Value = (float)_options.GetCarBehindShow();
            _behindClose.Value = (float)_options.GetCarBehindClose();
            _behindFlash.IsChecked = _options.GetCarBehindFlash();
            _eyeTracking.IsChecked = _options.EyeTracking;
            _colorBlind.SelectedIndex = _options.ColorBlindIndex;
            string recenterText = RecenterText();
            if (recenterText != _recenterText)
            {
                _recenterText = recenterText;
                _recenterBtn.Text = recenterText;
                Invalidate();
            }

            // Gaze rows follow the service, and the status line says why
            // nothing is happening when that is the case.
            _gazeEffect.SelectedIndex = (int)_gaze.Effect;
            _gazeDim.Value = _gaze.FadedOpacity * 100f;
            _gazeGrow.Value = _gaze.GrowFactor * 100f;
            _gazeEffect.IsEnabled = _gaze.Enabled;
            _gazeDim.IsEnabled = _gaze.Enabled && _gaze.Effect is GazeEffect.Brighten or GazeEffect.Both;
            _gazeGrow.IsEnabled = _gaze.Enabled && _gaze.Effect is GazeEffect.Enlarge or GazeEffect.Both;

            string gazeStatus =
                !_gaze.Enabled ? "Eye tracking is off in Preferences"
                : !_gaze.IsActive ? "Gaze off - set a panel to eye or head in edit mode"
                : !_gaze.HasSignal ? "No head pose - start the sim in VR"
                : string.IsNullOrEmpty(_gaze.FocusedName) ? "Looking away from the panels"
                : "Looking at " + _gaze.FocusedName + (_gaze.UsingEyes ? " (eyes, experimental)" : "");
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
            int Radius = OverlayStyle.CornerRadius;

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
            _sb.DrawString(_font, Name, new XnaVector2(20, OverlayStyle.TitleY(_font, TitleBarHeight) + 2), XnaColor.Black * 0.45f);
            _sb.DrawString(_font, Name, new XnaVector2(20, OverlayStyle.TitleY(_font, TitleBarHeight)), TitleText);

            // Hint that the title bar puts the panel away again.
            const string Close = "click title to close";
            var closeSize = _fontHead.MeasureString(Close);
            _sb.DrawString(_fontHead, Close,
                new XnaVector2(Width - 20 - closeSize.X, (TitleBarHeight - _fontHead.LineSpacing) / 2f), RowTextDim);

            _sb.Draw(_white, new XnaRectangle(0, TitleBarHeight - 3, Width, 3), Accent);
            MonoXRDraw.VerticalFade(_sb, new XnaRectangle(0, TitleBarHeight, Width, 14), Accent * 0.25f);

            _menu.Draw(_sb, _fontBody, _white);

            // Footer: tag count on the left, what the gaze is doing on the right.
            // The hint is dropped when it would run into the gaze status on the right.
            var gazeSize = _fontHead.MeasureString(_gazeStatus);
            string footerLeft = _tagSummary + "  -  click a row on the standings board to tag a driver";
            if (28 + _fontHead.MeasureString(footerLeft).X + 24 > Width - 28 - gazeSize.X)
                footerLeft = _tagSummary;
            _sb.DrawString(_fontHead, footerLeft, new XnaVector2(28, Height - 30), RowTextDim);
            _sb.DrawString(_fontHead, _gazeStatus,
                new XnaVector2(Width - 28 - gazeSize.X, Height - 30), RowTextDim);

            MonoXRDraw.RoundedRectOutline(_sb, panel, Radius, 2, Border);

            _sb.End();
        }

        public override void Dispose()
        {
            OverlayNavigation.Navigated -= OnNavigated;
            DriverTags.Instance.Changed -= _tagsChanged;
            _options.Recenter.Changed -= _tagsChanged;
            _white.Dispose();
            _sb.Dispose();
            base.Dispose();
        }
    }
}
