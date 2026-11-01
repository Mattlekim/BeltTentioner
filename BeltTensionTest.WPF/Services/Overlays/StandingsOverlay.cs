using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BeltTensionTest.WPF.Services.Data;
using Microsoft.Xna.Framework.Graphics;
using MonoXR.Client;
using XnaColor = Microsoft.Xna.Framework.Color;
using XnaRectangle = Microsoft.Xna.Framework.Rectangle;
using XnaVector2 = Microsoft.Xna.Framework.Vector2;

namespace BeltTensionTest.WPF.Services.Overlays
{
    /// <summary>
    /// In-VR standings board: every driver in the session in running order,
    /// rather than the handful around you the relative boxes show. Shown in
    /// every session type.
    ///
    /// Each row is a driver at their current position with the best lap that
    /// earned it (purple when it is the class fastest), the lap they are on
    /// right now ticking live, that lap's sector times colored by merit
    /// (purple = session best sector, green = the driver's own best, yellow =
    /// slower, red = invalid; sectors they have not reached yet are blank),
    /// and the gap.
    ///
    /// The gap column is the point of the board. Outside a race it is the gap
    /// to pole: while a driver is on a lap it is the live delta of that lap
    /// against the session's fastest, which steps at every sector crossing
    /// (green = up on pole, yellow = down), and between laps it falls back to
    /// how far their best lap sits off pole's. In a race it is the gap to the
    /// leader, in laps once they are lapped.
    ///
    /// Clicking a row opens a small menu that marks that driver as a rival
    /// or a friend (or clears the mark). Tags are kept by iRacing customer id
    /// in <see cref="DriverTags"/>, so they survive into the next session, and
    /// every standings panel paints a tagged driver's row in their color.
    ///
    /// The panel is as tall as the user dragged it, <see cref="MaxRows"/>
    /// rows; a field bigger than that shows the leaders, a break, then the
    /// block around you.
    /// </summary>
    public sealed class StandingsOverlay : StandingsOverlayBase
    {
        private const int InfoBarH = 44;
        private const int FooterH = 0;
        /// <summary>Panel width in canvas pixels (wider than the relative boxes).</summary>
        public const int BoardWidth = 1500;

        /// <summary>Range the board can be dragged to.</summary>
        public const int MinRowSetting = MinPanelRows, MaxRowSetting = MaxPanelRows;

        private const int DefaultRows = 26;

        /// <summary>
        /// Rows the board holds before it starts leaving cars out, set by
        /// dragging its bottom edge in edit mode (which sets the panel's row
        /// capacity, so this just reads it back). Clamped to
        /// <see cref="MinRowSetting"/>..<see cref="MaxRowSetting"/>.
        /// </summary>
        public int MaxRows
        {
            get => RowCapacity;
            set => RowCapacity = Math.Clamp(value, MinRowSetting, MaxRowSetting);
        }

        /// <summary>Leaders always kept on the board when the field does not fit.</summary>
        private const int LeaderRows = 3;

        private static readonly Col[] Columns =
        {
            Col.Pos, Col.License, Col.IRating, Col.Driver, Col.Status,
            Col.BestLap, Col.CurLap, Col.LiveSectors, Col.Gap,
        };
        protected override Col[] DefaultColumns => Columns;

        protected override string EmptyMessage => "Shown in every session";

        // Driver-tag menu geometry and state. The menu is anchored where the
        // row was clicked and kept inside the panel.
        private const int MenuWidthBase = 440;
        private int MenuWidth => Scaled(440);
        private const int MenuItemBase = 40;
        private int MenuItemHeight => Scaled(40);
        private const int MenuTitleBase = 34;
        private int MenuTitleHeight => Scaled(34);
        private int MenuPad => Scaled(6);
        // "Menu size" from the HUD Settings panel: the pop-up menus grow with it, so they stay readable in VR.
        // The menu is drawn inside this panel, so it cannot be bigger than the panel: the size asked for is
        // held back to what fits, or the extra rows would be cut off and could not be clicked.
        private int BaseMenuHeight => MenuTitleBase + Math.Max(1, _menuItems.Count) * MenuItemBase + 12;
        private float MenuScale => Math.Max(0.5f, Math.Min(DriverTags.Instance.MenuScale,
            Math.Min((Width - Pad * 2) / (float)MenuWidthBase,
                     (Height - HeaderTop - Pad) / (float)BaseMenuHeight)));
        private int Scaled(int v) => (int)Math.Round(v * MenuScale);

        private const int MaxBlameIncidents = 8;
        private static readonly XnaColor MenuBg = new XnaColor(0x22, 0x22, 0x34, 250);

        /// <summary>Which step of the driver menu is up: the main list, or the two steps of blaming the driver.</summary>
        private enum MenuStep { Main, Incident, Reason, Praise }

        private string _fastestText = string.Empty;
        private string _brakeBias = "BB --";
        private string _fieldText = string.Empty;
        private string _temps = string.Empty;

        private int _hoverCar = -1;      // CarIdx under the pointer, or -1
        private int _menuCar = -1;       // CarIdx the open menu is about, or -1
        private string _menuKey = string.Empty;
        private string _menuTitle = string.Empty;
        private readonly List<string> _menuItems = new();
        private MenuStep _menuStep;
        private Incident[] _menuIncidents = Array.Empty<Incident>(); // the incidents on offer in the Incident step
        private Incident? _menuIncident;                             // the one picked, while its reason is chosen
        private int _menuHover = -1;
        private int _menuX, _menuY;

        // Scrolling. By default the board follows you: the leaders, then the block of cars around you. Turning
        // the wheel or dragging the bar switches to a plain window of the running order starting at
        // _scrollTop; that lasts ManualHoldMs after the last scroll, then the board follows you again.
        private const int ManualHoldMs = 10000;
        private const int BarWidth = 8;
        private const int WheelRowsPerNotch = 3;
        private int _fieldCount;         // cars in the running order
        private int _windowStart;        // index of the first car of the scrolling part of the window (for the thumb)
        private int _playerIdx;          // the viewed driver's place in the running order
        private int _scrollTop;          // first car shown while scrolling by hand
        private long _manualUntil;       // Environment.TickCount64 until which the manual window holds
        private bool _barDrag;
        private int _barGrab;

        private bool Manual => Environment.TickCount64 < _manualUntil;
        private bool HasScroll => _fieldCount > MaxRows;
        private int MaxTop => Math.Max(0, _fieldCount - MaxRows);

        private XnaRectangle TrackRect => new XnaRectangle(
            Width - Pad - 10 - BarWidth, RowsTop, BarWidth, MaxRows * (RowHeight + RowSpacing) - RowSpacing);

        private XnaRectangle ThumbRect
        {
            get
            {
                var track = TrackRect;
                int h = Math.Max(28, track.Height * MaxRows / Math.Max(_fieldCount, MaxRows));
                int range = Math.Max(1, track.Height - h);
                int top = Math.Clamp(_windowStart, 0, MaxTop);
                int y = track.Y + (MaxTop == 0 ? 0 : range * top / MaxTop);
                return new XnaRectangle(track.X, y, track.Width, h);
            }
        }

        /// <summary>Hand scrolling on: start from where the board is now if it was following you.</summary>
        private void BeginManual()
        {
            if (!Manual) _scrollTop = Math.Clamp(_playerIdx - MaxRows / 2, 0, MaxTop);
            _manualUntil = Environment.TickCount64 + ManualHoldMs;
        }

        private void ScrollTo(int top)
        {
            BeginManual();
            top = Math.Clamp(top, 0, MaxTop);
            _manualUntil = Environment.TickCount64 + ManualHoldMs;
            if (top == _scrollTop) return;
            _scrollTop = top;
            CloseMenu();
            Invalidate();
        }

        private void ScrollToThumbTop(int thumbTop)
        {
            var track = TrackRect;
            int range = Math.Max(1, track.Height - ThumbRect.Height);
            float f = Math.Clamp((thumbTop - track.Y) / (float)range, 0f, 1f);
            ScrollTo((int)Math.Round(f * MaxTop));
        }

        public override bool WantsPointerWheel(int x, int y) =>
            !IsCollapsed && IsActive && HasScroll && x >= 0 && x < Width && y >= RowsTop - HeaderH && y < Height;

        public override bool OnPointerWheel(int x, int y, int delta)
        {
            if (IsCollapsed || !IsActive || !HasScroll) return false;
            int rows = (int)Math.Round(delta / 120.0 * WheelRowsPerNotch);
            if (rows == 0) rows = delta > 0 ? 1 : -1;
            BeginManual();
            ScrollTo(_scrollTop - rows); // wheel up = towards the leaders
            return true;
        }

        public override void OnPointerDrag(int x, int y)
        {
            if (_barDrag) ScrollToThumbTop(y - _barGrab);
        }

        public override void OnPointerRelease(int x, int y)
        {
            if (!_barDrag) return;
            _barDrag = false;
            Invalidate();
        }

        private bool MenuOpen => _menuCar >= 0;
        private int MenuHeight => MenuTitleHeight + _menuItems.Count * MenuItemHeight + MenuPad * 2;

        public StandingsOverlay(GraphicsDevice device, int x, int y, int carsToDisplay = 12)
            : base(device, "Standings", x, y, carsToDisplay, InfoBarH, FooterH, BoardWidth)
        {
            SetRowCapacity(DefaultRows);
        }

        // The board is useful in practice, qualifying and the race alike.
        protected override bool IsSessionActive(string sessionType) => true;

        // Tagging happens here, so the board always shows the result whatever
        // the relative boxes are set to.
        protected override bool ShowsDriverTags => true;

        protected override void BuildRows(IReadOnlyList<Car> all, Car player)
        {
            var svc = IracingService.Instance;
            bool race = CurrentSessionType.IndexOf("Race", StringComparison.OrdinalIgnoreCase) >= 0;
            bool racing = race && svc.SessionState >= IracingService.SessionStateRacing;

            var (classFastest, fastestDriver) = ClassFastestLap(all, player);
            var poleByClass = PoleLapByClass(all);

            var ordered = RunningOrder(all, player, racing);
            var window = Window(ordered, player);
            SetRowCapacity(MaxRows); // the box is as tall as the user dragged it

            foreach (var car in window)
            {
                if (car == null) { Rows.Add(new Row { Divider = true, CarIdx = -1 }); continue; }

                // rel = 0: the board is not a relative, so the on-track gap
                // column it would fill has no place here.
                var row = BaseRow(car, player, 0f, classFastest);
                row.Rel = string.Empty;
                row.Pos = PositionOf(car, racing);
                row.Highlight = car.CarIdx == _hoverCar || car.CarIdx == _menuCar;
                row.LiveSectors = SectorCellsFor(car.CarIdx);

                var lap = LapProgressFor(car.CarIdx);
                row.CurLap = lap.Running ? FormatLapTime((float)lap.Elapsed) : string.Empty;

                poleByClass.TryGetValue(car.CarClassId, out float pole);
                (row.Gap, row.GapCmp) = racing
                    ? RaceGap(car, ordered.FirstOrDefault(c => c.CarClassId == car.CarClassId) ?? car)
                    : PoleGap(car, pole, lap);

                Rows.Add(row);
            }

            // A driver who left the session takes their menu with them.
            if (MenuOpen && !ordered.Any(c => c.CarIdx == _menuCar)) CloseMenu();

            _fastestText = classFastest < float.PositiveInfinity
                ? $"{FormatName(fastestDriver)}  {FormatLapTime(classFastest)}" : string.Empty;
            _fieldText = ordered.Count == 1 ? "1 driver" : $"{ordered.Count} drivers";
            if (svc.IsViewingOther) _fieldText += $"  |  Viewing {FormatName(player.DriverName)}";
            _brakeBias = BrakeBiasText(svc);
            _temps = float.IsNaN(svc.TrackTempC) ? string.Empty
                : $"Track {svc.TrackTempC:0}C  Air {svc.AirTempC:0}C";
        }

        /// <summary>Position shown in the badge: class position in multiclass, otherwise overall.</summary>
        private int PositionOf(Car car, bool racing)
        {
            int pos = Multiclass ? car.ClassPosition : car.Position;
            // In a race the order comes from how far round the track each car is (a finisher's is
            // final), not from iRacing's position, which only changes once a lap.
            if (racing)
            {
                var place = RacePositionOf(car);
                pos = Multiclass ? place.Class : place.Overall;
            }
            // Outside the race iRacing only places a car once it has a time,
            // so an unplaced row keeps a blank badge rather than a fake "0".
            return pos > 0 && (racing || car.BestLapTime > 0) ? pos : 0;
        }

        /// <summary>
        /// The field in running order: classes kept together with the
        /// player's first, then iRacing's position within the class. Cars it
        /// has not placed yet (no time set) fall in behind by best lap.
        /// </summary>
        private List<Car> RunningOrder(IReadOnlyList<Car> all, Car player, bool racing)
        {
            return all
                .OrderBy(c => player.CarClassId >= 0 && c.CarClassId == player.CarClassId ? 0 : 1)
                .ThenBy(c => c.CarClassId)
                .ThenBy(c =>
                {
                    int pos = PositionOf(c, racing);
                    return pos > 0 ? pos : int.MaxValue;
                })
                .ThenBy(c => c.BestLapTime > 0 ? c.BestLapTime : float.MaxValue)
                .ThenBy(c => c.CarIdx)
                .ToList();
        }

        /// <summary>
        /// The rows to draw. Everyone, unless the field is longer than the
        /// board: then the leaders, a break marker (a null entry) and the
        /// block around the player, so both ends of the story stay visible.
        /// With "always show" on (see <see cref="DriverTags.PinMode"/>), the tagged
        /// drivers it names are kept too, each group of rows set off by a break.
        /// </summary>
        private List<Car?> Window(List<Car> ordered, Car player)
        {
            int capacity = MaxRows;
            _fieldCount = ordered.Count;
            _playerIdx = ordered.FindIndex(c => c.CarIdx == player.CarIdx);

            // Scrolled by hand: a plain window of the running order, pinned drivers and all.
            if (ordered.Count > capacity && Manual)
            {
                _scrollTop = Math.Clamp(_scrollTop, 0, MaxTop);
                _windowStart = _scrollTop;
                var manual = new List<Car?>(capacity);
                for (int i = _scrollTop; i < ordered.Count && manual.Count < capacity; i++) manual.Add(ordered[i]);
                return manual;
            }

            // Following the player: the thumb sits where the block around them begins.
            int blockRows = capacity - LeaderRows - 1;
            _windowStart = ordered.Count <= capacity ? 0
                : Math.Clamp((_playerIdx < 0 ? LeaderRows : _playerIdx) - blockRows / 2, LeaderRows, ordered.Count - blockRows);

            var tags = DriverTags.Instance;
            if (ordered.Count <= capacity || tags.PinMode == DriverPin.Off)
                return DefaultWindow(ordered, player, capacity);

            // The drivers to keep, in running order (never the player, who is always shown).
            var pinned = new List<int>();
            for (int i = 0; i < ordered.Count; i++)
                if (ordered[i].CarIdx != player.CarIdx && tags.IsPinned(tags.TagOf(ordered[i])))
                    pinned.Add(i);
            if (pinned.Count == 0) return DefaultWindow(ordered, player, capacity);

            // Shrink the leaders-and-player window until the rows it needs, plus a row for each
            // pinned driver it leaves out and each break between groups, fit the board. If they
            // still do not fit, show fewer leaders (down to the winner alone) before leaving
            // a pinned driver out.
            for (int leaders = LeaderRows; leaders >= 1; leaders--)
            {
                int floorRows = leaders + 3; // the leaders, a break and two rows around the player
                for (int budget = capacity; budget >= floorRows; budget--)
                {
                    var merged = MergePinned(ordered, DefaultWindow(ordered, player, budget, leaders), pinned);
                    if (merged.Count <= capacity) return merged;
                }
            }

            // A board too short to hold even the winner, a break and two rows around you: no room to pin anyone.
            if (capacity < 4) return DefaultWindow(ordered, player, capacity);

            // Too many to fit at all: keep the pinned drivers nearest the player, as many as the rows allow.
            int playerIdx = Math.Max(0, ordered.FindIndex(c => c.CarIdx == player.CarIdx));
            var byDistance = pinned.OrderBy(i => Math.Abs(i - playerIdx)).ToList();
            var floorWindow = DefaultWindow(ordered, player, 4, 1);
            var kept = new List<int>();
            foreach (int i in byDistance)
            {
                kept.Add(i);
                if (MergePinned(ordered, floorWindow, kept).Count > capacity) { kept.RemoveAt(kept.Count - 1); break; }
            }
            return MergePinned(ordered, floorWindow, kept);
        }

        /// <summary>The window without any pinned drivers: everyone, or the leaders, a break and the block around the player.</summary>
        private static List<Car?> DefaultWindow(List<Car> ordered, Car player, int capacity, int leaders = LeaderRows)
        {
            var rows = new List<Car?>(Math.Min(ordered.Count, capacity));
            if (ordered.Count <= capacity)
            {
                rows.AddRange(ordered);
                return rows;
            }

            int blockRows = capacity - leaders - 1; // the break marker takes one
            int playerIdx = ordered.FindIndex(c => c.CarIdx == player.CarIdx);
            if (playerIdx < 0) playerIdx = leaders;
            int start = Math.Clamp(playerIdx - blockRows / 2, leaders, ordered.Count - blockRows);

            for (int i = 0; i < leaders; i++) rows.Add(ordered[i]);
            // Only mark a break when one was actually made; when the player
            // sits just behind the leaders the rows run on unbroken.
            if (start > leaders) rows.Add(null);
            else start = leaders;
            for (int i = start; i < ordered.Count && rows.Count < capacity; i++) rows.Add(ordered[i]);
            return rows;
        }

        /// <summary>
        /// The window plus the pinned drivers it does not already hold, in running
        /// order, with a break marker wherever the shown drivers are not neighbours.
        /// </summary>
        private static List<Car?> MergePinned(List<Car> ordered, List<Car?> window, List<int> pinned)
        {
            var shown = new SortedSet<int>(pinned);
            foreach (var car in window)
                if (car != null) shown.Add(ordered.FindIndex(c => c.CarIdx == car.CarIdx));

            var rows = new List<Car?>(shown.Count + 2);
            int previous = -1;
            foreach (int i in shown)
            {
                if (previous >= 0 && i > previous + 1) rows.Add(null);
                rows.Add(ordered[i]);
                previous = i;
            }
            return rows;
        }

        // ----- Edit-mode right-click menu ----------------------------------------

        private static readonly string[] PinNames = { "Off", "Friends", "Rivals", "Friends + rivals" };

        /// <summary>Panel options for the edit-mode right-click menu: which tagged drivers are always shown.</summary>
        public override IEnumerable<(string Label, Action Run)> EditMenuItems()
        {
            foreach (var item in base.EditMenuItems()) yield return item;
            var tags = DriverTags.Instance;
            yield return ($"Always show: {PinNames[(int)tags.PinMode]} (click to change)", () =>
            {
                tags.PinMode = (DriverPin)(((int)tags.PinMode + 1) % PinNames.Length);
                Invalidate();
            });
        }

        /// <summary>Fastest best lap set in each car class so far (pole), keyed by class id.</summary>
        private static Dictionary<int, float> PoleLapByClass(IReadOnlyList<Car> all)
        {
            var pole = new Dictionary<int, float>();
            foreach (var c in all)
            {
                if (c.BestLapTime <= 0) continue;
                if (!pole.TryGetValue(c.CarClassId, out float best) || c.BestLapTime < best)
                    pole[c.CarClassId] = c.BestLapTime;
            }
            return pole;
        }

        /// <summary>
        /// Gap to pole. On a lap it is that lap's live delta to the session's
        /// fastest over the sectors completed so far, so it moves once per
        /// sector; otherwise it is how far this driver's best lap sits off
        /// pole's. (The live delta is against the session's outright fastest
        /// lap, which in a multiclass session may belong to another class.)
        /// </summary>
        private static (string Text, int Cmp) PoleGap(
            Car car, float pole, (double Elapsed, bool Running, double Delta, bool HasDelta) lap)
        {
            if (lap.HasDelta)
                return (lap.Delta.ToString("+0.000;-0.000;0.000"), lap.Delta <= 0 ? -1 : 1);
            if (car.BestLapTime <= 0 || pole <= 0) return ("--", 0);
            float off = car.BestLapTime - pole;
            return (off <= 0.0005f ? "POLE" : off.ToString("+0.000"), 0);
        }

        /// <summary>Race gap to the class leader, in laps once a car is lapped.</summary>
        private (string Text, int Cmp) RaceGap(Car car, Car leader)
        {
            if (car.CarIdx == leader.CarIdx) return ("LEADER", 0);
            int laps = (int)Math.Floor(DistanceOf(leader) - DistanceOf(car));
            if (laps >= 1) return ($"+{laps}L", 0);
            // From the distance between the two cars, so it moves all lap (iRacing's F2Time only changes at the line).
            float seconds = GapSeconds(car, leader);
            return (float.IsNaN(seconds) ? "--" : seconds.ToString("+0.0;-0.0;0.0"), 0);
        }

        // ----- Driver tagging ------------------------------------------------
        // Outside edit mode the rows are live: hovering one outlines it, and
        // clicking one (either button) opens the rival / friend menu. In edit
        // mode the panel just drags, like every other box.

        public override void OnPointerMove(int x, int y)
        {
            if (HeaderMenuPointerMove(x, y)) return;
            if (MenuOpen)
            {
                int item = MenuItemAt(x, y);
                if (item == _menuHover) return;
                _menuHover = item;
                Invalidate();
                return;
            }

            int car = -1;
            if (IsActive && !IsCollapsed)
            {
                int row = RowAt(x, y);
                if (row >= 0 && !Rows[row].Divider) car = Rows[row].CarIdx;
            }
            if (car == _hoverCar) return;
            _hoverCar = car;
            Invalidate();
        }

        public override void OnPointerExit()
        {
            HeaderMenuPointerExit();
            if (_hoverCar < 0 && _menuHover < 0) return;
            _hoverCar = -1;
            _menuHover = -1;
            Invalidate();
        }

        public override bool OnPointerPress(int x, int y)
        {
            if (HeaderMenuPress(x, y)) return true;
            if (!IsActive || IsCollapsed) return false;

            // The scroll bar: grab the thumb, or click the track to bring the thumb there and drag from it.
            if (HasScroll && !MenuOpen)
            {
                var track = TrackRect;
                if (x >= track.X - 6 && x < track.Right + 6 && y >= track.Y && y < track.Bottom)
                {
                    var thumb = ThumbRect;
                    _barGrab = y >= thumb.Y && y < thumb.Bottom ? y - thumb.Y : thumb.Height / 2;
                    _barDrag = true;
                    ScrollToThumbTop(y - _barGrab);
                    return true;
                }
            }

            // With the menu up it takes the click wherever it lands, so a
            // press beside it dismisses instead of opening another one.
            if (MenuOpen)
            {
                int item = MenuItemAt(x, y);
                if (item >= 0) ActivateMenuItem(item); else CloseMenu();
                return true;
            }

            int row = RowAt(x, y);
            if (row < 0 || Rows[row].Divider) return false;
            var car = IracingService.Instance.Cars.FirstOrDefault(c => c.CarIdx == Rows[row].CarIdx);
            if (car == null) return false;
            OpenMenu(car, x, y);
            return true;
        }

        // Right-click does the same, so whichever button the wheel or the
        // pointer sends reaches the menu.
        public override bool OnPointerRightPress(int x, int y)
        {
            if (HeaderMenuRightPress(x, y)) return true;
            if (MenuOpen) { CloseMenu(); return true; }
            return OnPointerPress(x, y);
        }

        private void OpenMenu(Car car, int x, int y)
        {
            _menuCar = car.CarIdx;
            _menuKey = DriverTags.KeyFor(car);
            _menuTitle = string.IsNullOrEmpty(car.DriverName) ? "Driver" : car.DriverName;
            _menuHover = -1;

            _menuStep = MenuStep.Main;
            _menuIncident = null;

            // Only the moves that would change something are offered.
            var svc = IracingService.Instance;
            bool isYou = car.CarIdx == svc.PlayerCar.CarIdx;
            var tag = DriverTags.Instance.TagOf(_menuKey);
            _menuItems.Clear();
            if (!isYou && car.CarIdx != svc.ViewCar.CarIdx) _menuItems.Add(ViewItem);
            if (svc.IsViewingOther) _menuItems.Add(ClearViewItem);
            if (!isYou) _menuItems.Add(BlameItem);
            if (!isYou) _menuItems.Add(PraiseItem);
            if (tag != DriverTag.Rival) _menuItems.Add(RivalItem);
            if (tag != DriverTag.Friend) _menuItems.Add(FriendItem);
            if (tag != DriverTag.None) _menuItems.Add(ClearItem);
            _menuItems.Add(CancelItem);

            PlaceMenu(x, y);
        }

        // Anchor the menu where it was clicked, kept inside the panel.
        private void PlaceMenu(int x, int y)
        {
            _menuX = Math.Clamp(x, Pad, Math.Max(Pad, Width - MenuWidth - Pad));
            _menuY = Math.Clamp(y, HeaderTop, Math.Max(HeaderTop, Height - MenuHeight - Pad));
            Invalidate();
        }

        private const string ViewItem = "View from here";
        private const string ClearViewItem = "Clear view";
        private const string BlameItem = "Blame...";
        private const string PraiseItem = "Praise...";
        private const string RivalItem = "Mark as rival";
        private const string FriendItem = "Mark as friend";
        private const string ClearItem = "Clear tag";
        private const string CancelItem = "Cancel";

        private void CloseMenu()
        {
            if (!MenuOpen) return;
            _menuCar = -1;
            _menuKey = string.Empty;
            _menuTitle = string.Empty;
            _menuHover = -1;
            _menuStep = MenuStep.Main;
            _menuIncident = null;
            _menuIncidents = Array.Empty<Incident>();
            _menuItems.Clear();
            Invalidate();
        }

        // Blame step 1: the latest incidents, newest first, to pick the one this driver is blamed for.
        // A "*" marks the ones this driver was in or near.
        private void OpenIncidentStep()
        {
            _menuStep = MenuStep.Incident;
            _menuHover = -1;
            _menuItems.Clear();

            var all = IncidentTracker.Instance.Snapshot();
            int count = Math.Min(all.Length, MaxBlameIncidents);
            _menuIncidents = new Incident[count];
            var cars = IracingService.Instance.Cars;
            for (int i = 0; i < count; i++)
            {
                var inc = all[all.Length - 1 - i];
                _menuIncidents[i] = inc;
                bool involved = Array.IndexOf(inc.Cars, _menuCar) >= 0
                             || Array.IndexOf(inc.InvolvedCars, _menuCar) >= 0
                             || Array.IndexOf(inc.NearbyCars, _menuCar) >= 0;
                string who = string.Join(" ", inc.Cars.Select(idx => "#" + (cars.FirstOrDefault(c => c.CarIdx == idx)?.CarNumber ?? idx.ToString())));
                string lap = inc.Lap > 0 ? $"Lap {inc.Lap}" : "Lap -";
                _menuItems.Add($"{lap}  {IncidentLabel(inc.Type)} {inc.Points}x  {who}{(involved ? " *" : string.Empty)}");
            }
            if (count == 0) _menuItems.Add(NoIncidentsItem);
            _menuItems.Add(CancelItem);
            PlaceMenu(_menuX, _menuY);
        }

        // Blame step 2: why.
        private void OpenReasonStep()
        {
            _menuStep = MenuStep.Reason;
            _menuHover = -1;
            _menuItems.Clear();
            _menuItems.AddRange(IncidentTracker.BlameReasons);
            _menuItems.Add(CancelItem);
            PlaceMenu(_menuX, _menuY);
        }

        // Praise: one step, no incident to pick.
        private void OpenPraiseStep()
        {
            _menuStep = MenuStep.Praise;
            _menuHover = -1;
            _menuItems.Clear();
            _menuItems.AddRange(DriverBlame.PraiseReasons);
            _menuItems.Add(CancelItem);
            PlaceMenu(_menuX, _menuY);
        }

        private const string NoIncidentsItem = "No incidents yet";

        private static string IncidentLabel(IncidentType type) => type switch
        {
            IncidentType.OffTrack => "Off Track",
            IncidentType.CarContact => "Contact",
            _ => "Solo",
        };

        /// <summary>The menu item at a panel-local point, or -1.</summary>
        private int MenuItemAt(int x, int y)
        {
            if (!MenuOpen || x < _menuX || x >= _menuX + MenuWidth) return -1;
            int rel = y - _menuY - MenuPad - MenuTitleHeight;
            if (rel < 0) return -1;
            int item = rel / MenuItemHeight;
            return item < _menuItems.Count ? item : -1;
        }

        private void ActivateMenuItem(int item)
        {
            string label = _menuItems[item];

            // The blame steps: pick the incident, then the reason.
            if (_menuStep == MenuStep.Incident)
            {
                if (label == CancelItem || label == NoIncidentsItem || item >= _menuIncidents.Length) { CloseMenu(); return; }
                _menuIncident = _menuIncidents[item];
                OpenReasonStep();
                return;
            }
            if (_menuStep == MenuStep.Reason)
            {
                if (label != CancelItem && _menuIncident != null)
                    IncidentTracker.Instance.SetBlame(_menuIncident, mine: false, carIdx: _menuCar, reason: label);
                CloseMenu();
                return;
            }

            if (_menuStep == MenuStep.Praise)
            {
                if (label != CancelItem) DriverBlame.Instance.AddPraise(_menuKey, label);
                CloseMenu();
                return;
            }

            switch (label)
            {
                case BlameItem:
                    OpenIncidentStep();
                    return;
                case PraiseItem:
                    OpenPraiseStep();
                    return;
                case ViewItem:
                    IracingService.Instance.SetViewCar(_menuCar);
                    CloseMenu();
                    return;
                case ClearViewItem:
                    IracingService.Instance.SetViewCar(-1);
                    CloseMenu();
                    return;
            }

            DriverTag? tag = label switch
            {
                RivalItem => DriverTag.Rival,
                FriendItem => DriverTag.Friend,
                ClearItem => DriverTag.None,
                _ => null, // Cancel
            };
            if (tag is DriverTag t) DriverTags.Instance.SetTag(_menuKey, t);
            CloseMenu();
        }

        protected override void DrawPopups()
        {
            if (HasScroll && IsActive)
            {
                var track = TrackRect;
                MonoXRDraw.RoundedRect(Batch, track, BarWidth / 2, XnaColor.White * 0.08f);
                MonoXRDraw.RoundedRect(Batch, ThumbRect, BarWidth / 2, Accent * (_barDrag ? 1f : Manual ? 0.85f : 0.55f));
            }

            if (!MenuOpen || !IsActive) return;

            var tags = DriverTags.Instance;
            var rect = new XnaRectangle(_menuX, _menuY, MenuWidth, MenuHeight);
            MonoXRDraw.RoundedRect(Batch, rect, 10, MenuBg);
            MonoXRDraw.RoundedRectOutline(Batch, rect, 10, 2, Accent);

            // Whose menu this is.
            int titleY = rect.Y + MenuPad;
            string title = _menuStep switch
            {
                MenuStep.Incident => $"Blame {_menuTitle}: which incident?",
                MenuStep.Reason => $"Blame {_menuTitle}: why?",
                MenuStep.Praise => $"Praise {_menuTitle}:",
                _ => _menuTitle,
            };
            Batch.DrawString(FontHead, Fit(FontHead, title, (MenuWidth - Scaled(40)) / MenuScale),
                new XnaVector2(rect.X + MenuPad + Scaled(12), titleY + (MenuTitleHeight - FontHead.LineSpacing * MenuScale) / 2f),
                RowTextDim, 0f, XnaVector2.Zero, MenuScale, SpriteEffects.None, 0f);
            MonoXRDraw.HorizontalFade(Batch,
                new XnaRectangle(rect.X + MenuPad + 8, titleY + MenuTitleHeight - 2,
                                 MenuWidth - (MenuPad + 8) * 2, 2), Border);

            for (int i = 0; i < _menuItems.Count; i++)
            {
                string label = _menuItems[i];
                var item = new XnaRectangle(rect.X + MenuPad, titleY + MenuTitleHeight + i * MenuItemHeight,
                                            MenuWidth - MenuPad * 2, MenuItemHeight);
                if (i == _menuHover)
                    MonoXRDraw.RoundedRect(Batch, item, 7, Accent * 0.3f);

                // The two tagging entries carry a chip of the color they
                // would paint the row, so the menu doubles as the legend.
                int textX = item.X + Scaled(14);
                int chip = label == RivalItem ? tags.RivalColor
                         : label == FriendItem ? tags.FriendColor : -1;
                if (chip >= 0)
                {
                    MonoXRDraw.RoundedRect(Batch,
                        new XnaRectangle(item.X + Scaled(12), item.Y + (MenuItemHeight - Scaled(16)) / 2, Scaled(16), Scaled(16)), 4, FromRgb(chip));
                    textX = item.X + Scaled(38);
                }

                Batch.DrawString(FontBody, Fit(FontBody, label, (item.Right - textX - Scaled(10)) / MenuScale),
                    new XnaVector2(textX, item.Y + (MenuItemHeight - FontBody.LineSpacing * MenuScale) / 2f),
                    label == CancelItem || label == NoIncidentsItem ? RowTextDim : RowText,
                    0f, XnaVector2.Zero, MenuScale, SpriteEffects.None, 0f);
            }
        }

        /// <summary>Cut text down to a pixel width, ending in ".." when it had to.</summary>
        private static string Fit(SpriteFont font, string text, float maxWidth)
        {
            if (string.IsNullOrEmpty(text) || font.MeasureString(text).X <= maxWidth) return text;
            for (int n = text.Length - 1; n > 0; n--)
            {
                string cut = text.Substring(0, n) + "..";
                if (font.MeasureString(cut).X <= maxWidth) return cut;
            }
            return string.Empty;
        }

        protected override void AppendSnapshot(StringBuilder sb)
        {
            sb.Append('#').Append(_fastestText).Append(_fieldText).Append(_temps).Append(_brakeBias)
              .Append('S').Append(_windowStart).Append(Manual ? 'm' : 'a').Append(_barDrag ? 'd' : 'u');
        }

        protected override void DrawInfoBar(XnaRectangle area)
        {
            float y = area.Y + (area.Height - FontBody.LineSpacing) / 2f;
            DrawFastestLapInfo(area, _fastestText);

            var size = FontBody.MeasureString(_fieldText);
            Batch.DrawString(FontBody, _fieldText,
                new XnaVector2(area.X + (area.Width - size.X) / 2f, y), RowTextDim);
            DrawRight(FontBody, _temps, area.Right, y, RowTextDim);
            DrawBrakeBias(_brakeBias, area, area.Right - FontBody.MeasureString(_temps).X - 30);
            MonoXRDraw.HorizontalFade(Batch, new XnaRectangle(area.X - 4, area.Bottom - 2, area.Width + 8, 2), Border);
        }
    }
}
