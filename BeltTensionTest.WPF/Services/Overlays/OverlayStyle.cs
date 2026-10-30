using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace BeltTensionTest.WPF.Services.Overlays
{
    /// <summary>
    /// Look of the overlay windows that is shared by all of them, kept in
    /// <c>overlaystyle.json</c> next to the exe (copied there from the project folder on
    /// build, and committed to git). Every build reads it; only a Debug build can change
    /// it, in the Display panel in VR, and saving the layout writes it back to the
    /// project folder (so git sees it) and to the running copy in bin.
    /// </summary>
    public static class OverlayStyle
    {
        public const string FileName = "overlaystyle.json";
        public const int DefaultCornerRadius = 18;
        public const int MaxCornerRadius = 40;

        /// <summary>Corner radius of the overlay windows in pixels; 0 is square.</summary>
        public static int CornerRadius { get; set; } = DefaultCornerRadius;

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
        }

        private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
        private static int _saved = int.MinValue;

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
                _saved = CornerRadius;
            }
            catch (Exception) { /* keep the defaults */ }
        }

        /// <summary>Debug builds only: write the style to the project folder and next to the exe (does nothing in Release, or when nothing changed).</summary>
        public static void Save([CallerFilePath] string sourceFile = "")
        {
#if DEBUG
            if (CornerRadius == _saved) return;
            try
            {
                string json = JsonSerializer.Serialize(new Data { CornerRadius = CornerRadius }, Options);
                File.WriteAllText(Path.Combine(AppContext.BaseDirectory, FileName), json);
                // This file is in <project>\Services\Overlays: the project's copy is two folders up.
                string? dir = string.IsNullOrEmpty(sourceFile) ? null : Path.GetDirectoryName(sourceFile);
                string? project = dir == null ? null : Path.GetDirectoryName(Path.GetDirectoryName(dir));
                if (project != null && Directory.Exists(project))
                    File.WriteAllText(Path.Combine(project, FileName), json);
                _saved = CornerRadius;
            }
            catch (Exception) { /* the style just isn't saved this time */ }
#endif
        }
    }
}
