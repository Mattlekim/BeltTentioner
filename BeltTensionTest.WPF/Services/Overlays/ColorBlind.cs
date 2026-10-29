using System;
using XnaColor = Microsoft.Xna.Framework.Color;

namespace BeltTensionTest.WPF.Services.Overlays
{
    /// <summary>Which kind of colour vision the overlay colours are adjusted for.</summary>
    public enum ColorBlindMode
    {
        Off,
        /// <summary>Red-green (no or weak green cones): red/green/orange status colours look alike.</summary>
        Deuteranopia,
        /// <summary>Blue-yellow (weak blue cones): blue/green and yellow/pink are confused.</summary>
        Tritanomaly,
    }

    /// <summary>What a colour means on the overlay; each gets its own colour per mode.</summary>
    public enum CbRole
    {
        /// <summary>The blue accent (selection, slider fill, "behind" lap tint).</summary>
        Info,
        /// <summary>Good / positive: personal best, player row, gaining.</summary>
        Good,
        /// <summary>Bad / danger: invalid, contact, car closing, losing.</summary>
        Bad,
        /// <summary>Caution: yellow flag, slower sector.</summary>
        Warn,
        /// <summary>Between caution and danger: orange, slow car, solo spin.</summary>
        Amber,
        /// <summary>Best of all: session-best purple.</summary>
        Best,
    }

    /// <summary>
    /// Colour-blind mode for the VR overlay. Panels pass their normal colour and
    /// its role through <see cref="Map"/>: with the mode off that colour comes back
    /// untouched, otherwise the role's colour from a palette picked so that, in
    /// a simulation of that kind of colour vision, all the roles stay clearly apart
    /// (the earlier red/green/orange/yellow set collapses into two or three
    /// lookalikes for a red-green or blue-yellow deficiency). Every panel reads
    /// its colours through here each time it draws, so a change shows at once
    /// once the panels are invalidated.
    /// </summary>
    public static class ColorBlind
    {
        /// <summary>The mode in force. Set by the overlay window from the app settings.</summary>
        public static ColorBlindMode Mode { get; set; }

        public static ColorBlindMode Parse(string? name) =>
            Enum.TryParse(name, true, out ColorBlindMode mode) ? mode : ColorBlindMode.Off;

        // Roles in CbRole order: Info, Good, Bad, Warn, Amber, Best. Bright enough
        // to read on the dark panels; the Deuteranopia set keeps the blue accent
        // (blue is untouched by that deficiency) and moves green and red to
        // aqua and a brown-orange, the Tritanomaly set keeps green, red and
        // yellow apart from one another and moves the blue accent to teal.
        private static readonly XnaColor[] Deuteranopia =
        {
            new(100, 150, 255), new(60, 230, 225), new(190, 112, 77),
            new(234, 231, 53),  new(215, 112, 6),  new(172, 87, 180),
        };

        private static readonly XnaColor[] Tritanomaly =
        {
            new(42, 141, 161),  new(18, 249, 42),  new(243, 103, 50),
            new(221, 228, 39),  new(196, 140, 39), new(148, 88, 245),
        };

        /// <summary>
        /// <paramref name="normal"/> when the mode is off, else the role's colour
        /// for the mode, darkened to <paramref name="shade"/> (0..1) for
        /// backgrounds and badges, with <paramref name="normal"/>'s alpha.
        /// </summary>
        public static XnaColor Map(CbRole role, XnaColor normal, float shade = 1f)
        {
            var palette = Mode switch
            {
                ColorBlindMode.Deuteranopia => Deuteranopia,
                ColorBlindMode.Tritanomaly => Tritanomaly,
                _ => null,
            };
            if (palette == null) return normal;

            var c = palette[(int)role];
            return new XnaColor((int)(c.R * shade), (int)(c.G * shade), (int)(c.B * shade), (int)normal.A);
        }
    }
}
