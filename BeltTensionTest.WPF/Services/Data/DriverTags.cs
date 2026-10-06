using System;
using System.Collections.Generic;

namespace BeltTensionTest.WPF.Services.Data
{
    /// <summary>How the driver has marked another driver.</summary>
    public enum DriverTag
    {
        None,
        /// <summary>Someone you race with — drawn in the friend color.</summary>
        Friend,
        /// <summary>Someone you are racing against — drawn in the rival color.</summary>
        Rival,
    }

    /// <summary>
    /// Drivers marked as a friend or a rival, so the HUD can pick them out of
    /// a full field at a glance. Tags are set from the standings board (click
    /// a row) and picked up by every standings panel as it builds its rows.
    ///
    /// Tags are keyed on the iRacing customer id, which is the same number for
    /// a driver in every session, so a rival marked in one race is still a
    /// rival next week. AI drivers have no customer id and are keyed on their
    /// name instead — good enough, since AI rosters are per-session anyway.
    ///
    /// Everything here is touched only from the overlay's render thread (row
    /// building, pointer input and the settings panel all run there), so there
    /// is no locking; <see cref="Changed"/> is the cue for the owner to save.
    /// </summary>
    public sealed class DriverTags
    {
        private static readonly Lazy<DriverTags> Lazy = new(() => new DriverTags());
        public static DriverTags Instance => Lazy.Value;

        public const int DefaultFriendColor = 0x2E8CD8; // blue
        public const int DefaultRivalColor = 0xD8462E;  // red

        /// <summary>Colors the in-VR picker offers for the friend / rival roles.</summary>
        public static readonly int[] Palette =
        {
            0xD8462E, // red
            0xE07B28, // orange
            0xE0C020, // yellow
            0x50C878, // green
            0x2E8CD8, // blue
            0x8A2BE2, // purple
            0xE060B0, // pink
            0x30C8C8, // cyan
            0xB0B0C0, // silver
        };

        // Customer id (or "name:<driver>") -> DriverTag name. This is the very
        // dictionary held in AppSettings, so writes land straight in the
        // settings object and only need saving.
        private Dictionary<string, string> _tags = new();

        /// <summary>Raised after a tag or a tag setting changes, so the owner can save.</summary>
        public event Action? Changed;

        private int _friendColor = DefaultFriendColor;
        private int _rivalColor = DefaultRivalColor;
        private float _tint = 0.35f;
        private bool _showInRelativeBoxes = true;

        /// <summary>Row color for a friend, 0xRRGGBB.</summary>
        public int FriendColor
        {
            get => _friendColor;
            set { if (SetColor(ref _friendColor, value)) Changed?.Invoke(); }
        }

        /// <summary>Row color for a rival, 0xRRGGBB.</summary>
        public int RivalColor
        {
            get => _rivalColor;
            set { if (SetColor(ref _rivalColor, value)) Changed?.Invoke(); }
        }

        /// <summary>
        /// How strongly a tagged driver's color replaces the normal row shade,
        /// 0 (a left-edge stripe only) to 0.8 (almost solid). Text stays
        /// readable across the whole range.
        /// </summary>
        public float TintStrength
        {
            get => _tint;
            set
            {
                float v = Math.Clamp(value, 0f, 0.8f);
                if (Math.Abs(v - _tint) < 0.001f) return;
                _tint = v;
                Changed?.Invoke();
            }
        }

        /// <summary>
        /// Whether the Race and Qualifying relative boxes paint tag colors
        /// too, or only the standings board does.
        /// </summary>
        public bool ShowInRelativeBoxes
        {
            get => _showInRelativeBoxes;
            set { if (_showInRelativeBoxes == value) return; _showInRelativeBoxes = value; Changed?.Invoke(); }
        }

        /// <summary>Drivers currently tagged.</summary>
        public int Count => _tags.Count;

        /// <summary>
        /// Adopt the saved state. The dictionary is kept by reference, so
        /// later tag changes are already in the settings object by the time
        /// <see cref="Changed"/> fires.
        /// </summary>
        public void Load(Dictionary<string, string> tags, int friendColor, int rivalColor,
                         double tint, bool showInRelativeBoxes)
        {
            _tags = tags;
            SetColor(ref _friendColor, friendColor);
            SetColor(ref _rivalColor, rivalColor);
            _tint = (float)Math.Clamp(tint, 0.0, 0.8);
            _showInRelativeBoxes = showInRelativeBoxes;
        }

        /// <summary>The key a car's tag is stored under; empty when it has no identity yet.</summary>
        public static string KeyFor(Car car) =>
            car.UserId > 0 ? car.UserId.ToString()
            : string.IsNullOrEmpty(car.DriverName) ? string.Empty
            : "name:" + car.DriverName;

        public DriverTag TagOf(Car car) => TagOf(KeyFor(car));

        public DriverTag TagOf(string key) =>
            !string.IsNullOrEmpty(key) && _tags.TryGetValue(key, out string? name) &&
            Enum.TryParse(name, out DriverTag tag) ? tag : DriverTag.None;

        public void SetTag(string key, DriverTag tag)
        {
            if (string.IsNullOrEmpty(key)) return;
            bool changed = tag == DriverTag.None
                ? _tags.Remove(key)
                : !_tags.TryGetValue(key, out string? existing) || existing != tag.ToString();
            if (tag != DriverTag.None) _tags[key] = tag.ToString();
            if (changed) Changed?.Invoke();
        }

        /// <summary>Forget every tagged driver.</summary>
        public void ClearAll()
        {
            if (_tags.Count == 0) return;
            _tags.Clear();
            Changed?.Invoke();
        }

        /// <summary>Row color for a tag, or -1 when the driver is untagged.</summary>
        public int ColorOf(DriverTag tag) => tag switch
        {
            DriverTag.Friend => _friendColor,
            DriverTag.Rival => _rivalColor,
            _ => -1,
        };

        // A color outside 0xRRGGBB means a corrupt or unset saved value; keep
        // whatever we already have rather than painting rows black.
        private static bool SetColor(ref int field, int value)
        {
            if (value < 0 || value > 0xFFFFFF || value == field) return false;
            field = value;
            return true;
        }
    }
}
