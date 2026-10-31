using System;
using System.Collections.Generic;
using System.Drawing.Text;
using System.Linq;
using Microsoft.Xna.Framework.Graphics;

namespace BeltTensionTest.WPF.Services.Overlays
{
    /// <summary>
    /// The fonts of every overlay window: one family, size and weight for the text in them, and a
    /// family and size of their own for the titles. A window gets its fonts from <see cref="Body"/>
    /// and <see cref="Title"/> each time it draws, so a change shows at once; the standings boxes
    /// bake their own and rebake when <see cref="Version"/> changes. Chosen in the in-VR HUD
    /// Settings panel and saved with the overlay layout.
    /// </summary>
    public static class OverlayFonts
    {
        public const string DefaultFamily = "Segoe UI";
        public const int MinPercent = 70, MaxPercent = 130;
        public const int MinTitlePercent = 70, MaxTitlePercent = 150;

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
        private static string _titleFamily = DefaultFamily;
        private static int _percent = 100;
        private static int _titlePercent = 100;
        private static bool _bold = true;

        /// <summary>Bumped on every change; boxes compare it to know when to rebake.</summary>
        public static int Version { get; private set; }

        /// <summary>Raised after any of the font settings changes, so windows can redraw.</summary>
        public static event Action? Changed;

        private static void Bump()
        {
            Version++;
            Changed?.Invoke();
        }

        public static string Family
        {
            get => _family;
            set
            {
                string v = Available.FirstOrDefault(f => string.Equals(f, value, StringComparison.OrdinalIgnoreCase)) ?? Available[0];
                if (v == _family) return;
                _family = v;
                Bump();
            }
        }

        /// <summary>Font of the window titles.</summary>
        public static string TitleFamily
        {
            get => _titleFamily;
            set
            {
                string v = Available.FirstOrDefault(f => string.Equals(f, value, StringComparison.OrdinalIgnoreCase)) ?? Available[0];
                if (v == _titleFamily) return;
                _titleFamily = v;
                Bump();
            }
        }

        /// <summary>Title size as a percentage of the default, <see cref="MinTitlePercent"/>..<see cref="MaxTitlePercent"/>.</summary>
        public static int TitlePercent
        {
            get => _titlePercent;
            set
            {
                int v = Math.Clamp(value, MinTitlePercent, MaxTitlePercent);
                if (v == _titlePercent) return;
                _titlePercent = v;
                Bump();
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
                Bump();
            }
        }

        /// <summary>Whether the names and numbers in the boxes are bold (the default).</summary>
        public static bool Bold
        {
            get => _bold;
            set
            {
                if (value == _bold) return;
                _bold = value;
                Bump();
            }
        }

        /// <summary>Style for the body text of the boxes, from <see cref="Bold"/>.</summary>
        public static System.Drawing.FontStyle Style => _bold ? System.Drawing.FontStyle.Bold : System.Drawing.FontStyle.Regular;

        public static float Scaled(float basePx) => MathF.Round(basePx * _percent / 100f);

        public static float TitleScaled(float basePx) => MathF.Round(basePx * _titlePercent / 100f);

        // ----- Shared font cache ---------------------------------------------
        // A window asks for its font every time it draws; the baked font is kept until a setting
        // changes. Replaced fonts are not disposed at once (a frame under way may still hold one),
        // but a few seconds later.

        private static GraphicsDevice? _device;
        private static int _cacheVersion = -1;
        private static readonly Dictionary<(string Family, int Px, System.Drawing.FontStyle Style), SpriteFont> Cache = new();
        private static readonly List<(SpriteFont Font, DateTime Retired)> Retired = new();
        private static readonly TimeSpan RetireAfter = TimeSpan.FromSeconds(5);

        /// <summary>
        /// Font for the text in a window: the chosen family, size and weight. <paramref name="bold"/>
        /// keeps a font bold whatever the weight setting says (column headings, big readouts).
        /// </summary>
        public static SpriteFont Body(GraphicsDevice device, float basePx, bool bold = false) =>
            Get(device, _family, (int)Scaled(basePx), bold ? System.Drawing.FontStyle.Bold : Style);

        /// <summary>Font for a window's title: the chosen title family and size.</summary>
        public static SpriteFont Title(GraphicsDevice device, float basePx, bool bold = false) =>
            Get(device, _titleFamily, (int)TitleScaled(basePx), bold ? System.Drawing.FontStyle.Bold : Style);

        private static SpriteFont Get(GraphicsDevice device, string family, int px, System.Drawing.FontStyle style)
        {
            if (!ReferenceEquals(device, _device))
            {
                Cache.Clear(); // a new device: the old fonts went with it
                Retired.Clear();
                _device = device;
                _cacheVersion = Version;
            }
            else if (_cacheVersion != Version)
            {
                var now = DateTime.UtcNow;
                foreach (var font in Cache.Values) Retired.Add((font, now));
                Cache.Clear();
                _cacheVersion = Version;
            }

            if (Retired.Count > 0)
            {
                var cutoff = DateTime.UtcNow - RetireAfter;
                for (int i = Retired.Count - 1; i >= 0; i--)
                    if (Retired[i].Retired < cutoff)
                    {
                        Retired[i].Font.Texture.Dispose();
                        Retired.RemoveAt(i);
                    }
            }

            var key = (family, px, style);
            if (!Cache.TryGetValue(key, out var baked))
                Cache[key] = baked = RuntimeSpriteFont.Bake(device, family, Math.Max(8, px), style);
            return baked;
        }
    }
}
