using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace BeltTensionTest.WPF.Services.Overlays
{
    /// <summary>
    /// Look of the overlay windows that is shared by all of them, kept in
    /// <c>overlaystyle.json</c> next to the exe (copied there from the project folder on
    /// build, and committed to git). Every build reads it; only a Debug build can change
    /// it, in the Display panel in VR (corner radius) and by dragging column edges in the
    /// standings / relative boxes (column widths), and saving the layout writes it back to
    /// the project folder (so git sees it) and to the running copy in bin.
    /// </summary>
    public static class OverlayStyle
    {
        public const string FileName = "overlaystyle.json";
        public const int DefaultCornerRadius = 18;
        public const int MaxCornerRadius = 40;

        /// <summary>Corner radius of the overlay windows in pixels; 0 is square.</summary>
        public static int CornerRadius { get; set; } = DefaultCornerRadius;

        /// <summary>
        /// Top of a window's title text so that it sits in the middle of the bar (above the 3 px accent
        /// strip at its bottom). A font's line box has room above the capitals for accents, so
        /// centering the line box left the text low; this centers the letters instead.
        /// </summary>
        public static int TitleY(Microsoft.Xna.Framework.Graphics.SpriteFont font, int barHeight) =>
            Math.Max(0, (int)Math.Round((barHeight - 3) / 2f - font.LineSpacing * 0.548f));

        /// <summary>True in a Debug build, where the style can be edited and saved.</summary>
        public static bool Editable =>
#if DEBUG
            true;
#else
            false;
#endif

        private sealed class Data
        {
            public int CornerRadius { get; set; } = DefaultCornerRadius;

            /// <summary>Column width overrides in pixels: box name ("Standings"), then column name ("Driver").</summary>
            public Dictionary<string, Dictionary<string, int>> ColumnWidths { get; set; } = new();
        }

        private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
        private static Dictionary<string, Dictionary<string, int>> _columnWidths = new();
        private static string? _savedJson;

        private static string ToJson() =>
            JsonSerializer.Serialize(new Data { CornerRadius = CornerRadius, ColumnWidths = _columnWidths }, Options);

        /// <summary>Width the style gives a column of a box, or 0 for "use the box's own default".</summary>
        public static int ColumnWidth(string box, string column) =>
            _columnWidths.TryGetValue(box, out var cols) && cols.TryGetValue(column, out int w) ? w : 0;

        /// <summary>Set a column's width; written to the file by the next <see cref="Save"/> (Debug builds).</summary>
        public static void SetColumnWidth(string box, string column, int width)
        {
            if (!_columnWidths.TryGetValue(box, out var cols)) _columnWidths[box] = cols = new Dictionary<string, int>();
            cols[column] = width;
        }

        /// <summary>Read the file next to the exe; a missing or broken file leaves the defaults.</summary>
        public static void Load()
        {
            try
            {
                string path = Path.Combine(AppContext.BaseDirectory, FileName);
                if (!File.Exists(path)) return;
                var data = JsonSerializer.Deserialize<Data>(File.ReadAllText(path));
                if (data == null) return;
                CornerRadius = Math.Clamp(data.CornerRadius, 0, MaxCornerRadius);
                _columnWidths = data.ColumnWidths ?? new();
                _savedJson = ToJson();
            }
            catch (Exception) { /* keep the defaults */ }
        }

        /// <summary>Debug builds only: write the style to the project folder and next to the exe (does nothing in Release, or when nothing changed).</summary>
        public static void Save([CallerFilePath] string sourceFile = "")
        {
#if DEBUG
            string json = ToJson();
            if (json == _savedJson) return;
            try
            {
                File.WriteAllText(Path.Combine(AppContext.BaseDirectory, FileName), json);
                // This file is in <project>\Services\Overlays: the project's copy is two folders up.
                string? dir = string.IsNullOrEmpty(sourceFile) ? null : Path.GetDirectoryName(sourceFile);
                string? project = dir == null ? null : Path.GetDirectoryName(Path.GetDirectoryName(dir));
                if (project != null && Directory.Exists(project))
                    File.WriteAllText(Path.Combine(project, FileName), json);
                _savedJson = json;
            }
            catch (Exception) { /* the style just isn't saved this time */ }
#endif
        }
    }
}
