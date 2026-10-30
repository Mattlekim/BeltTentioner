using System;
using System.Collections.Generic;
using System.Drawing.Text;
using System.Linq;

namespace BeltTensionTest.WPF.Services.Overlays
{
    /// <summary>
    /// The font and text size of the standings boxes (the relative box and the
    /// standings board). A box bakes its fonts from here and rebakes when
    /// <see cref="Version"/> changes. Chosen in the in-VR HUD Settings panel and
    /// saved with the overlay layout.
    /// </summary>
    public static class OverlayFonts
    {
        public const string DefaultFamily = "Segoe UI";
        public const int MinPercent = 70, MaxPercent = 130;

        // Offered when installed; a font the user does not have is not listed.
        private static readonly string[] Candidates =
        {
            "Segoe UI", "Arial", "Calibri", "Consolas", "Verdana", "Tahoma", "Trebuchet MS",
            "Bahnschrift", "Cambria", "Courier New", "Lucida Console", "Franklin Gothic Medium",
        };

        private static string[]? _available;

        /// <summary>The fonts that can be picked, in a stable order.</summary>
        public static IReadOnlyList<string> Available => _available ??= Detect();

        private static string[] Detect()
        {
            try
            {
                using var installed = new InstalledFontCollection();
                var names = new HashSet<string>(installed.Families.Select(f => f.Name), StringComparer.OrdinalIgnoreCase);
                var list = Candidates.Where(names.Contains).ToArray();
                if (list.Length > 0) return list;
            }
            catch { }
            return new[] { DefaultFamily };
        }

        private static string _family = DefaultFamily;
        private static int _percent = 100;

        /// <summary>Bumped on every change; boxes compare it to know when to rebake.</summary>
        public static int Version { get; private set; }

        public static string Family
        {
            get => _family;
            set
            {
                string v = Available.FirstOrDefault(f => string.Equals(f, value, StringComparison.OrdinalIgnoreCase)) ?? Available[0];
                if (v == _family) return;
                _family = v;
                Version++;
            }
        }

        /// <summary>Text size as a percentage of the default, <see cref="MinPercent"/>..<see cref="MaxPercent"/>.</summary>
        public static int Percent
        {
            get => _percent;
            set
            {
                int v = Math.Clamp(value, MinPercent, MaxPercent);
                if (v == _percent) return;
                _percent = v;
                Version++;
            }
        }

        public static float Scaled(float basePx) => MathF.Round(basePx * _percent / 100f);
    }
}
