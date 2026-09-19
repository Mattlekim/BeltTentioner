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
    /// In-VR incident list, ported from IrachingHud's IncidentBox: every
    /// incident the session has scored, with its point value, the lap, what it
    /// looked like (off-track / solo / contact) and how many cars were in it.
    /// A multi-car incident lists the cars underneath it, like the original.
    ///
    /// Left-clicking a row jumps the replay to a few seconds before that
    /// incident and puts the camera on the car that scored it. Right-clicking
    /// one opens the blame menu (the original's right-click menu, rebuilt for
    /// the overlay): take the blame yourself, or pick from the cars that were
    /// near the incident — choosing one swings the iRacing camera onto that
    /// driver so you can watch what they did before you judge it, then you
    /// pick the verdict (Careless, Reckless, Deliberate...). The verdict shows
    /// in the BLAME column.
    ///
    /// The panel only shows while the sim is in its replay (and in edit mode,
    /// to place it); the incidents themselves are collected live by
    /// <see cref="Data.IncidentTracker"/>, whether the panel is up or not.
    /// </summary>
    public sealed class IncidentsOverlay : OverlayRenderTarget, IPanelShowDefault
    {
        /// <summary>Replay-only out of the box (the original's ShowSetting.Replay), changeable per panel.</summary>
        public PanelShowMode DefaultShowMode => PanelShowMode.Replay;

        // The original box is 330x200 with 20 px rows; scaled up so the rows
        // stay readable in VR, with room for the blame column it didn't have.
        private const int PanelWidth = 720;
        private const int TitleBarHeight = 48;
        private const int HeaderRowHeight = 30;
        private const int RowHeight = 30;
        private const int RowSpacing = 2;
        private const int MaxRows = 10;
        private const int Pad = 8;

        // Column x positions, following the original's proportions.
        private const int ColPoints = 14;
        private const int ColLap = 96;
        private const int ColType = 176;
        private const int ColCars = 340;
        private const int ColBlame = 400;
        private const int ColCarLine = 200; // indented car lines under an incident

        /// <summary>Replay frames to rewind before an incident (~3 s at 60 fps), as in the original.</summary>
        private const int PreRollFrames = 200;

        // Context menu geometry.
        private const int MenuWidth = 320;
        private const int MenuItemHeight = 34;
        private const int MenuPad = 5;

        /// <summary>What a driver can be held to have done (the original's incident tags).</summary>
        private static readonly string[] Reasons =
        {
            "Racing incident", "Mistake", "Careless", "Reckless",
            "Deliberate", "Divebomb", "Unsafe rejoin",
        };

        // App palette (Resources/Styles.xaml), matching the other panels.
        private static readonly XnaColor PanelBg = new XnaColor(0x12, 0x12, 0x1E, 235);
        private static readonly XnaColor TitleBg = new XnaColor(0x1C, 0x1C, 0x2E, 245);
        private static readonly XnaColor TitleText = new XnaColor(0xD0, 0xD0, 0xF0);
        private static readonly XnaColor Accent = new XnaColor(0x64, 0x96, 0xFF);
        private static readonly XnaColor Border = new XnaColor(0x46, 0x46, 0x6A);
        private static readonly XnaColor RowText = new XnaColor(0xE6, 0xE6, 0xF5);
        private static readonly XnaColor RowTextDim = new XnaColor(0xA0, 0xA0, 0xBE);
        private static readonly XnaColor RowBg = new XnaColor(0x1A, 0x1A, 0x28, 235);
        private static readonly XnaColor MenuBg = new XnaColor(0x22, 0x22, 0x34, 250);
        private static readonly XnaColor TypeOffTrack = new XnaColor(0xFF, 0xD5, 0x2E);
        private static readonly XnaColor TypeSolo = new XnaColor(0xFF, 0x9E, 0x2E);
        private static readonly XnaColor TypeContact = new XnaColor(0xE0, 0x50, 0x50);

        private readonly SpriteBatch _sb;
        private readonly Texture2D _white;
        private readonly SpriteFont _font;     // title
        private readonly SpriteFont _fontBody; // rows
        private readonly SpriteFont _fontHead; // column headers

        private readonly int _collapsedWidth;

        /// <summary>One drawn line: an incident, or one car underneath it.</summary>
        private struct Entry
        {
            public Incident Incident; // null for a car line
            public string Points;
            public string Lap;
            public string Type;
            public XnaColor TypeColor;
            public string Cars;
            public string Blame;
            public XnaColor BlameColor;
            public string CarLine;    // "#7 - Some Driver" for a car line
        }

        private readonly List<Entry> _entries = new();
        private int _version = -1;      // tracker version the entries were built from
        private int _hoverRow = -1;     // index into _entries, or -1
        private int _total;             // incidents recorded this session
        private int _hidden;            // older ones that did not fit
        private bool _active;           // the sim is in its replay

        /// <summary>Which step of the right-click blame flow is open.</summary>
        private enum MenuMode { None, Blame, Driver, Reason }

        private MenuMode _mode;
        private Incident? _menuIncident;
        private int _menuCar = -1;      // driver picked in the Driver step
        private int _menuX, _menuY;     // popup top-left, panel pixels
        private readonly List<string> _menuLabels = new();
        private readonly List<int> _menuValues = new(); // CarIdx in the Driver step, else -1
        private int _menuHover = -1;

        public override int CollapsedWidth => _collapsedWidth;
        public override int CollapsedHeight => TitleBarHeight;

        public IncidentsOverlay(GraphicsDevice device, int x, int y)
            : base(device, PanelWidth,
                   TitleBarHeight + Pad + HeaderRowHeight + MaxRows * (RowHeight + RowSpacing) + Pad,
                   x, y)
        {
            Name = "Incidents";
            _sb = new SpriteBatch(device);
            _white = new Texture2D(device, 1, 1);
            _white.SetData(new[] { XnaColor.White });
            _font = RuntimeSpriteFont.Bake(device, "Segoe UI", 30f);
            _fontBody = RuntimeSpriteFont.Bake(device, "Segoe UI", 22f);
            _fontHead = RuntimeSpriteFont.Bake(device, "Segoe UI", 18f, System.Drawing.FontStyle.Bold);
            _collapsedWidth = (int)_font.MeasureString(Name).X + 60;
        }

        /// <summary>Top of the first row, in panel pixels.</summary>
        private static int RowsTop => TitleBarHeight + Pad + HeaderRowHeight;

        /// <summary>The entry at a panel-local point, or -1.</summary>
        private int RowAt(int x, int y)
        {
            if (x < 0 || x >= Width) return -1;
            int row = (y - RowsTop) / (RowHeight + RowSpacing);
            return row >= 0 && row < _entries.Count ? row : -1;
        }

        public override void Update(GameTime gameTime)
        {
            var svc = IracingService.Instance;
            bool active = svc.IsConnected && svc.InReplay;
            var tracker = IncidentTracker.Instance;

            if (tracker.Version != _version)
            {
                _version = tracker.Version;
                BuildEntries(tracker.Snapshot());
                Invalidate();
            }

            if (active != _active)
            {
                _active = active;
                _hoverRow = -1;
                if (!active) CloseMenu();
                Invalidate();
            }
        }

        /// <summary>
        /// Flatten the incidents into drawn lines, newest ones first to fit:
        /// with no way to scroll in VR, the panel shows the last page of the
        /// list (an incident and its cars always stay together).
        /// </summary>
        private void BuildEntries(Incident[] incidents)
        {
            _entries.Clear();
            _total = incidents.Length;
            _hoverRow = -1;

            // Walk back from the newest until the panel is full.
            int rows = 0, first = incidents.Length;
            while (first > 0)
            {
                var inc = incidents[first - 1];
                int need = 1 + (inc.Cars.Length > 1 ? inc.Cars.Length : 0);
                if (rows + need > MaxRows) break;
                rows += need;
                first--;
            }
            _hidden = first;

            var cars = IracingService.Instance.Cars;
            for (int i = first; i < incidents.Length; i++)
            {
                var inc = incidents[i];
                _entries.Add(new Entry
                {
                    Incident = inc,
                    Points = $"{inc.Points}x",
                    Lap = inc.Lap > 0 ? inc.Lap.ToString() : "-",
                    Type = TypeLabel(inc.Type),
                    TypeColor = TypeColor(inc.Type),
                    Cars = inc.Cars.Length.ToString(),
                    Blame = BlameLabel(cars, inc),
                    BlameColor = inc.BlameIsMine ? RowTextDim : TypeContact,
                });

                if (inc.Cars.Length <= 1) continue;
                foreach (int carIdx in inc.Cars)
                    _entries.Add(new Entry { CarLine = Describe(cars, carIdx) });
            }
        }

        private static string TypeLabel(IncidentType type) => type switch
        {
            IncidentType.OffTrack => "Off Track",
            IncidentType.CarContact => "Contact",
            _ => "Solo",
        };

        private static XnaColor TypeColor(IncidentType type) => type switch
        {
            IncidentType.OffTrack => TypeOffTrack,
            IncidentType.CarContact => TypeContact,
            _ => TypeSolo,
        };

        private static string BlameLabel(IReadOnlyList<Car> cars, Incident inc)
        {
            if (inc.BlameIsMine)
                return inc.BlameReason.Length > 0 ? "Mine - " + inc.BlameReason : "Mine";
            if (inc.BlameCarIdx < 0) return string.Empty;

            string who = "#" + NumberOf(cars, inc.BlameCarIdx);
            return inc.BlameReason.Length > 0 ? who + " " + inc.BlameReason : who;
        }

        private static string NumberOf(IReadOnlyList<Car> cars, int carIdx)
        {
            foreach (var car in cars)
                if (car.CarIdx == carIdx)
                    return car.CarNumber;
            return carIdx.ToString();
        }

        private static string Describe(IReadOnlyList<Car> cars, int carIdx)
        {
            foreach (var car in cars)
                if (car.CarIdx == carIdx)
                    return $"#{car.CarNumber} - {car.DriverName}";
            return $"car {carIdx}";
        }

        /// <summary>Cut text down to a pixel width, ending in ".." when it had to.</summary>
        private static string Fit(SpriteFont font, string text, int maxWidth)
        {
            if (string.IsNullOrEmpty(text) || font.MeasureString(text).X <= maxWidth) return text;
            for (int n = text.Length - 1; n > 0; n--)
            {
                string cut = text.Substring(0, n) + "..";
                if (font.MeasureString(cut).X <= maxWidth) return cut;
            }
            return string.Empty;
        }

        // ----- Context menu --------------------------------------------------

        private int MenuHeight => _menuLabels.Count * MenuItemHeight + MenuPad * 2;

        private void CloseMenu()
        {
            if (_mode == MenuMode.None) return;
            _mode = MenuMode.None;
            _menuIncident = null;
            _menuCar = -1;
            _menuHover = -1;
            _menuLabels.Clear();
            _menuValues.Clear();
            Invalidate();
        }

        /// <summary>Open a step of the blame flow, anchored at a panel-local point.</summary>
        private void OpenMenu(MenuMode mode, Incident incident, int x, int y)
        {
            _mode = mode;
            _menuIncident = incident;
            _menuHover = -1;
            _menuLabels.Clear();
            _menuValues.Clear();

            switch (mode)
            {
                case MenuMode.Blame:
                    _menuLabels.Add("Accept blame");
                    _menuLabels.Add("Blame other driver");
                    if (incident.HasBlame) _menuLabels.Add("Clear blame");
                    _menuLabels.Add("Cancel");
                    for (int i = 0; i < _menuLabels.Count; i++) _menuValues.Add(-1);
                    break;

                case MenuMode.Driver:
                    var cars = IracingService.Instance.Cars;
                    foreach (int carIdx in incident.NearbyCars)
                    {
                        _menuLabels.Add(Describe(cars, carIdx));
                        _menuValues.Add(carIdx);
                    }
                    if (_menuLabels.Count == 0)
                    {
                        _menuLabels.Add("No cars were nearby");
                        _menuValues.Add(-1);
                    }
                    _menuLabels.Add("Cancel");
                    _menuValues.Add(-1);
                    break;

                case MenuMode.Reason:
                    foreach (string reason in Reasons)
                    {
                        _menuLabels.Add(reason);
                        _menuValues.Add(-1);
                    }
                    _menuLabels.Add("Cancel");
                    _menuValues.Add(-1);
                    break;
            }

            // Keep the popup inside the panel.
            _menuX = Math.Clamp(x, Pad, Math.Max(Pad, Width - MenuWidth - Pad));
            _menuY = Math.Clamp(y, TitleBarHeight, Math.Max(TitleBarHeight, Height - MenuHeight - Pad));
            Invalidate();
        }

        /// <summary>The menu item at a panel-local point, or -1.</summary>
        private int MenuItemAt(int x, int y)
        {
            if (_mode == MenuMode.None) return -1;
            if (x < _menuX || x >= _menuX + MenuWidth) return -1;
            int item = (y - _menuY - MenuPad) / MenuItemHeight;
            return item >= 0 && item < _menuLabels.Count ? item : -1;
        }

        private void ActivateMenuItem(int item)
        {
            if (_menuIncident is not { } inc) { CloseMenu(); return; }
            string label = _menuLabels[item];
            if (label == "Cancel") { CloseMenu(); return; }

            var tracker = IncidentTracker.Instance;
            int x = _menuX, y = _menuY;

            switch (_mode)
            {
                case MenuMode.Blame:
                    if (label == "Accept blame")
                    {
                        tracker.SetBlame(inc, mine: true, carIdx: -1, reason: string.Empty);
                        CloseMenu();
                    }
                    else if (label == "Clear blame")
                    {
                        tracker.ClearBlame(inc);
                        CloseMenu();
                    }
                    else
                    {
                        OpenMenu(MenuMode.Driver, inc, x, y);
                    }
                    break;

                case MenuMode.Driver:
                    int carIdx = _menuValues[item];
                    if (carIdx < 0) { CloseMenu(); return; } // the "no cars" line
                    _menuCar = carIdx;

                    // Replay the incident from this driver's camera, so the
                    // verdict is based on what they actually did.
                    ShowIncident(inc, carIdx);
                    OpenMenu(MenuMode.Reason, inc, x, y);
                    break;

                case MenuMode.Reason:
                    tracker.SetBlame(inc, mine: false, carIdx: _menuCar, reason: label);
                    CloseMenu();
                    break;
            }
        }

        /// <summary>
        /// Wind the replay back to just before an incident and put the camera
        /// on one of the cars in it. A frame of 0 means the SDK never gave us
        /// a replay position for this incident (the var is missing) — then
        /// nothing moves, rather than jumping to the start of the replay.
        /// </summary>
        private static void ShowIncident(Incident incident, int carIdx)
        {
            if (incident.Frame <= 0) return;

            var svc = IracingService.Instance;
            svc.SeekReplayToFrame(incident.Frame - PreRollFrames);
            foreach (var car in svc.Cars)
                if (car.CarIdx == carIdx)
                {
                    svc.FocusCameraOnCar(car.CarNumber);
                    break;
                }
        }

        // ----- Mouse ---------------------------------------------------------
        // Outside edit mode the rows are live: hovering one highlights it,
        // left-clicking jumps the replay, right-clicking opens the blame menu.
        // In edit mode the panel just drags.

        public override void OnPointerMove(int x, int y)
        {
            if (_mode != MenuMode.None)
            {
                int item = MenuItemAt(x, y);
                if (item == _menuHover) return;
                _menuHover = item;
                Invalidate();
                return;
            }

            int row = _active && !IsCollapsed ? RowAt(x, y) : -1;
            if (row >= 0 && _entries[row].Incident == null) row = -1; // car lines aren't targets
            if (row == _hoverRow) return;
            _hoverRow = row;
            Invalidate();
        }

        public override void OnPointerExit()
        {
            if (_hoverRow < 0 && _menuHover < 0) return;
            _hoverRow = -1;
            _menuHover = -1;
            Invalidate();
        }

        public override bool OnPointerPress(int x, int y)
        {
            if (!_active || IsCollapsed) return false;

            // A menu is up: it takes the click, whether on an item or not.
            if (_mode != MenuMode.None)
            {
                int item = MenuItemAt(x, y);
                if (item >= 0) ActivateMenuItem(item);
                else CloseMenu();
                return true;
            }

            int row = RowAt(x, y);
            if (row < 0 || _entries[row].Incident is not { } inc) return false;

            ShowIncident(inc, inc.Cars[0]);
            return true; // consume it so the press doesn't fall through
        }

        public override bool OnPointerRightPress(int x, int y)
        {
            if (!_active || IsCollapsed) return false;

            // Right-clicking again anywhere closes an open menu.
            if (_mode != MenuMode.None) { CloseMenu(); return true; }

            int row = RowAt(x, y);
            if (row < 0 || _entries[row].Incident is not { } inc) return false;
            OpenMenu(MenuMode.Blame, inc, x, y);
            return true;
        }

        public override void Render(GameTime gameTime)
        {
            const int Radius = 16;
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
            _sb.DrawString(_font, Name, new XnaVector2(Pad + 12, 10), XnaColor.Black * 0.45f);
            _sb.DrawString(_font, Name, new XnaVector2(Pad + 12, 8), TitleText);

            string count = _hidden > 0 ? $"{_total} total, {_hidden} earlier" : $"{_total} total";
            var countSize = _fontHead.MeasureString(count);
            _sb.DrawString(_fontHead, count,
                new XnaVector2(Width - Pad - 12 - countSize.X, (TitleBarHeight - _fontHead.LineSpacing) / 2f),
                RowTextDim);

            _sb.Draw(_white, new XnaRectangle(0, TitleBarHeight - 3, Width, 3), Accent);
            MonoXRDraw.VerticalFade(_sb, new XnaRectangle(0, TitleBarHeight, Width, 12), Accent * 0.25f);

            // Column headers, the original's "Inc  Lap  Inc Type  Cars Involved".
            int headY = TitleBarHeight + Pad + (HeaderRowHeight - _fontHead.LineSpacing) / 2;
            _sb.DrawString(_fontHead, "INC", new XnaVector2(ColPoints, headY), RowTextDim);
            _sb.DrawString(_fontHead, "LAP", new XnaVector2(ColLap, headY), RowTextDim);
            _sb.DrawString(_fontHead, "INC TYPE", new XnaVector2(ColType, headY), RowTextDim);
            _sb.DrawString(_fontHead, "CARS", new XnaVector2(ColCars, headY), RowTextDim);
            _sb.DrawString(_fontHead, "BLAME", new XnaVector2(ColBlame, headY), RowTextDim);
            _sb.Draw(_white, new XnaRectangle(Pad + 4, TitleBarHeight + Pad + HeaderRowHeight - 4,
                                              Width - (Pad + 4) * 2, 2), Border);

            if (_entries.Count == 0)
            {
                _sb.DrawString(_fontBody, _active ? "No incidents recorded" : "Nothing recorded yet",
                               new XnaVector2(ColPoints, RowsTop + 10), RowTextDim);
                MonoXRDraw.RoundedRectOutline(_sb, panel, Radius, 2, Border);
                _sb.End();
                return;
            }

            int y = RowsTop;
            for (int i = 0; i < _entries.Count; i++)
            {
                var e = _entries[i];
                float textY = y + (RowHeight - _fontBody.LineSpacing) / 2f;

                if (e.Incident == null)
                {
                    // Car involved in the row above.
                    _sb.DrawString(_fontBody, Fit(_fontBody, e.CarLine, ColBlame - ColCarLine - 12),
                                   new XnaVector2(ColCarLine, textY), RowTextDim);
                    y += RowHeight + RowSpacing;
                    continue;
                }

                var row = new XnaRectangle(Pad, y, Width - Pad * 2, RowHeight);
                MonoXRDraw.RoundedRect(_sb, row, 5, RowBg);
                bool menuRow = _menuIncident != null && ReferenceEquals(_menuIncident, e.Incident);
                if (i == _hoverRow || menuRow)
                {
                    // The original's light-blue hover wash, in the app's accent.
                    MonoXRDraw.RoundedRect(_sb, row, 5, Accent * (menuRow ? 0.22f : 0.3f));
                    MonoXRDraw.RoundedRect(_sb, new XnaRectangle(row.X, y + 4, 4, RowHeight - 8), 2, Accent);
                }

                _sb.DrawString(_fontBody, e.Points, new XnaVector2(ColPoints, textY), RowText);
                _sb.DrawString(_fontBody, e.Lap, new XnaVector2(ColLap, textY), RowText);
                _sb.DrawString(_fontBody, e.Type, new XnaVector2(ColType, textY), e.TypeColor);
                _sb.DrawString(_fontBody, e.Cars, new XnaVector2(ColCars, textY), RowText);
                if (e.Blame.Length > 0)
                    _sb.DrawString(_fontBody, Fit(_fontBody, e.Blame, Width - Pad - 12 - ColBlame),
                                   new XnaVector2(ColBlame, textY), e.BlameColor);

                y += RowHeight + RowSpacing;
            }

            DrawMenu();

            MonoXRDraw.RoundedRectOutline(_sb, panel, Radius, 2, Border);
            _sb.End();
        }

        /// <summary>The blame popup, over the rows. The compositor is inside Begin/End.</summary>
        private void DrawMenu()
        {
            if (_mode == MenuMode.None) return;

            var menu = new XnaRectangle(_menuX, _menuY, MenuWidth, MenuHeight);
            // Drop shadow first, so the popup reads as being above the rows.
            MonoXRDraw.RoundedRect(_sb, new XnaRectangle(menu.X + 3, menu.Y + 4, menu.Width, menu.Height),
                                   10, XnaColor.Black * 0.45f);
            MonoXRDraw.RoundedRect(_sb, menu, 10, MenuBg);
            MonoXRDraw.RoundedRectOutline(_sb, menu, 10, 2, Accent);

            for (int i = 0; i < _menuLabels.Count; i++)
            {
                var item = new XnaRectangle(menu.X + MenuPad, menu.Y + MenuPad + i * MenuItemHeight,
                                            MenuWidth - MenuPad * 2, MenuItemHeight);
                if (i == _menuHover)
                    MonoXRDraw.RoundedRect(_sb, item, 5, Accent * 0.35f);

                string label = _menuLabels[i];
                var color = label == "Cancel" ? RowTextDim : RowText;
                _sb.DrawString(_fontBody, Fit(_fontBody, label, item.Width - 20),
                               new XnaVector2(item.X + 10, item.Y + (MenuItemHeight - _fontBody.LineSpacing) / 2f),
                               color);
            }
        }

        public override void Dispose()
        {
            _fontHead.Texture.Dispose();
            _fontBody.Texture.Dispose();
            _font.Texture.Dispose();
            _white.Dispose();
            _sb.Dispose();
            base.Dispose();
        }
    }
}
