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
    /// The panel grows to the size of the field up to <see cref="MaxRows"/>
    /// rows; a field bigger than that shows the leaders, a break, then the
    /// block around you.
    /// </summary>
    public sealed class StandingsOverlay : StandingsOverlayBase
    {
        private const int InfoBarH = 44;
        private const int FooterH = 0;
        /// <summary>Panel width in canvas pixels (wider than the relative boxes).</summary>
        public const int BoardWidth = 1500;

        /// <summary>Range the rows-on-the-board setting can be set to.</summary>
        public const int MinRowSetting = 6, MaxRowSetting = 40;

        private int _maxRows = 26;

        /// <summary>
        /// Rows the board grows to before it starts leaving cars out, set
        /// from the HUD settings panel. Clamped to
        /// <see cref="MinRowSetting"/>..<see cref="MaxRowSetting"/>.
        /// </summary>
        public int MaxRows
        {
            get => _maxRows;
            set => _maxRows = Math.Clamp(value, MinRowSetting, MaxRowSetting);
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
        private const int MenuWidth = 330;
        private const int MenuItemHeight = 40;
        private const int MenuTitleHeight = 34;
        private const int MenuPad = 6;
        private static readonly XnaColor MenuBg = new XnaColor(0x22, 0x22, 0x34, 250);

        private string _fastestText = string.Empty;
        private string _fieldText = string.Empty;
        private string _temps = string.Empty;

        private int _hoverCar = -1;      // CarIdx under the pointer, or -1
        private int _menuCar = -1;       // CarIdx the open menu is about, or -1
        private string _menuKey = string.Empty;
        private string _menuTitle = string.Empty;
        private readonly List<string> _menuItems = new();
        private int _menuHover = -1;
        private int _menuX, _menuY;

        private bool MenuOpen => _menuCar >= 0;
        private int MenuHeight => MenuTitleHeight + _menuItems.Count * MenuItemHeight + MenuPad * 2;

        public StandingsOverlay(GraphicsDevice device, int x, int y, int carsToDisplay = 12)
            : base(device, "Standings", x, y, carsToDisplay, InfoBarH, FooterH, BoardWidth)
        {
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
            SetRowCapacity(window.Count);

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
                ? $"{fastestDriver}  {FormatLapTime(classFastest)}" : string.Empty;
            _fieldText = ordered.Count == 1 ? "1 driver" : $"{ordered.Count} drivers";
            _temps = float.IsNaN(svc.TrackTempC) ? string.Empty
                : $"Track {svc.TrackTempC:0}C  Air {svc.AirTempC:0}C";
        }

        /// <summary>Position shown in the badge: class position in multiclass, otherwise overall.</summary>
        private int PositionOf(Car car, bool racing)
        {
            int pos = Multiclass ? car.ClassPosition : car.Position;
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
        /// </summary>
        private List<Car?> Window(List<Car> ordered, Car player)
        {
            var rows = new List<Car?>(Math.Min(ordered.Count, MaxRows));
            if (ordered.Count <= MaxRows)
            {
                rows.AddRange(ordered);
                return rows;
            }

            int blockRows = MaxRows - LeaderRows - 1; // the break marker takes one
            int playerIdx = ordered.FindIndex(c => c.CarIdx == player.CarIdx);
            if (playerIdx < 0) playerIdx = LeaderRows;
            int start = Math.Clamp(playerIdx - blockRows / 2, LeaderRows, ordered.Count - blockRows);

            for (int i = 0; i < LeaderRows; i++) rows.Add(ordered[i]);
            // Only mark a break when one was actually made; when the player
            // sits just behind the leaders the rows run on unbroken.
            if (start > LeaderRows) rows.Add(null);
            else start = LeaderRows;
            for (int i = start; i < ordered.Count && rows.Count < MaxRows; i++) rows.Add(ordered[i]);
            return rows;
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
        private static (string Text, int Cmp) RaceGap(Car car, Car leader)
        {
            if (car.CarIdx == leader.CarIdx) return ("LEADER", 0);
            float leaderDist = leader.Lap + Math.Clamp(leader.LapDistPct, 0f, 1f);
            float dist = car.Lap + Math.Clamp(car.LapDistPct, 0f, 1f);
            int laps = (int)Math.Floor(leaderDist - dist);
            if (laps >= 1) return ($"+{laps}L", 0);
            if (car.F2Time <= 0 && leader.F2Time <= 0) return ("--", 0);
            return ((car.F2Time - leader.F2Time).ToString("+0.0;-0.0;0.0"), 0);
        }

        // ----- Driver tagging ------------------------------------------------
        // Outside edit mode the rows are live: hovering one outlines it, and
        // clicking one (either button) opens the rival / friend menu. In edit
        // mode the panel just drags, like every other box.

        public override void OnPointerMove(int x, int y)
        {
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
            if (_hoverCar < 0 && _menuHover < 0) return;
            _hoverCar = -1;
            _menuHover = -1;
            Invalidate();
        }

        public override bool OnPointerPress(int x, int y)
        {
            if (!IsActive || IsCollapsed) return false;

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
            if (MenuOpen) { CloseMenu(); return true; }
            return OnPointerPress(x, y);
        }

        private void OpenMenu(Car car, int x, int y)
        {
            _menuCar = car.CarIdx;
            _menuKey = DriverTags.KeyFor(car);
            _menuTitle = string.IsNullOrEmpty(car.DriverName) ? "Driver" : car.DriverName;
            _menuHover = -1;

            // Only the moves that would change something are offered.
            var tag = DriverTags.Instance.TagOf(_menuKey);
            _menuItems.Clear();
            if (tag != DriverTag.Rival) _menuItems.Add(RivalItem);
            if (tag != DriverTag.Friend) _menuItems.Add(FriendItem);
            if (tag != DriverTag.None) _menuItems.Add(ClearItem);
            _menuItems.Add(CancelItem);

            _menuX = Math.Clamp(x, Pad, Math.Max(Pad, Width - MenuWidth - Pad));
            _menuY = Math.Clamp(y, HeaderTop, Math.Max(HeaderTop, Height - MenuHeight - Pad));
            Invalidate();
        }

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
            _menuItems.Clear();
            Invalidate();
        }

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
            DriverTag? tag = _menuItems[item] switch
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
            if (!MenuOpen || !IsActive) return;

            var tags = DriverTags.Instance;
            var rect = new XnaRectangle(_menuX, _menuY, MenuWidth, MenuHeight);
            MonoXRDraw.RoundedRect(Batch, rect, 10, MenuBg);
            MonoXRDraw.RoundedRectOutline(Batch, rect, 10, 2, Accent);

            // Whose menu this is.
            int titleY = rect.Y + MenuPad;
            Batch.DrawString(FontHead, Fit(_menuTitle, MenuWidth - 40),
                new XnaVector2(rect.X + MenuPad + 12, titleY + (MenuTitleHeight - FontHead.LineSpacing) / 2f),
                RowTextDim);
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
                int textX = item.X + 14;
                int chip = label == RivalItem ? tags.RivalColor
                         : label == FriendItem ? tags.FriendColor : -1;
                if (chip >= 0)
                {
                    MonoXRDraw.RoundedRect(Batch,
                        new XnaRectangle(item.X + 12, item.Y + (MenuItemHeight - 16) / 2, 16, 16), 4, FromRgb(chip));
                    textX = item.X + 38;
                }

                Batch.DrawString(FontBody, label,
                    new XnaVector2(textX, item.Y + (MenuItemHeight - FontBody.LineSpacing) / 2f),
                    label == CancelItem ? RowTextDim : RowText);
            }
        }

        /// <summary>Cut text down to a pixel width, ending in ".." when it had to.</summary>
        private string Fit(string text, int maxWidth)
        {
            if (string.IsNullOrEmpty(text) || FontHead.MeasureString(text).X <= maxWidth) return text;
            for (int n = text.Length - 1; n > 0; n--)
            {
                string cut = text.Substring(0, n) + "..";
                if (FontHead.MeasureString(cut).X <= maxWidth) return cut;
            }
            return string.Empty;
        }

        protected override void AppendSnapshot(StringBuilder sb)
        {
            sb.Append('#').Append(_fastestText).Append(_fieldText).Append(_temps);
        }

        protected override void DrawInfoBar(XnaRectangle area)
        {
            float y = area.Y + (area.Height - FontBody.LineSpacing) / 2f;
            if (!string.IsNullOrEmpty(_fastestText))
            {
                var chip = new XnaRectangle(area.X, area.Y + 9, 36, area.Height - 18);
                MonoXRDraw.RoundedRect(Batch, chip, 5, SecPurple);
                var fl = FontHead.MeasureString("FL");
                Batch.DrawString(FontHead, "FL",
                    new XnaVector2(chip.X + (chip.Width - fl.X) / 2f, area.Y + (area.Height - FontHead.LineSpacing) / 2f),
                    XnaColor.White);
                Batch.DrawString(FontBody, _fastestText, new XnaVector2(chip.Right + 10, y), RowText);
            }
            else
            {
                Batch.DrawString(FontBody, "No lap times yet", new XnaVector2(area.X, y), RowTextDim);
            }

            var size = FontBody.MeasureString(_fieldText);
            Batch.DrawString(FontBody, _fieldText,
                new XnaVector2(area.X + (area.Width - size.X) / 2f, y), RowTextDim);
            DrawRight(FontBody, _temps, area.Right, y, RowTextDim);
            MonoXRDraw.HorizontalFade(Batch, new XnaRectangle(area.X - 4, area.Bottom - 2, area.Width + 8, 2), Border);
        }
    }
}
