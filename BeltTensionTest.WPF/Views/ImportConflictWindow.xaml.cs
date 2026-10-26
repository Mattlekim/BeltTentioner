using BeltAPI;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;

namespace BeltTensionTest.WPF.Views
{
    /// <summary>
    /// Shown when an import contains profiles that already exist: lists each setting that
    /// differs and lets the user choose which existing profiles to replace.
    /// </summary>
    public partial class ImportConflictWindow : Window
    {
        public sealed record DiffRow(string Setting, string Current, string Imported);

        public sealed class ConflictItem
        {
            public string Name { get; init; } = "";
            public IReadOnlyList<DiffRow> Differences { get; init; } = Array.Empty<DiffRow>();
            public bool Replace { get; set; }

            public bool HasDifferences => Differences.Count > 0;
            public string Title => $"Replace \"{Name}\"";
            public string Summary => HasDifferences
                ? $"{Differences.Count} setting{(Differences.Count == 1 ? "" : "s")} differ"
                : "Identical to your current profile — nothing to replace.";
            public Visibility DiffVisibility => HasDifferences ? Visibility.Visible : Visibility.Collapsed;
        }

        private readonly List<ConflictItem> _items;

        /// <summary>Existing profiles the user chose to replace.</summary>
        public IReadOnlyList<string> ReplaceNames { get; private set; } = Array.Empty<string>();

        /// <param name="conflicts">Profile name → (current settings, imported settings).</param>
        public ImportConflictWindow(IEnumerable<(string Name, CarSettings Current, CarSettings Imported)> conflicts)
        {
            InitializeComponent();

            _items = conflicts
                .Select(c =>
                {
                    var diffs = Compare(c.Current, c.Imported);
                    return new ConflictItem { Name = c.Name, Differences = diffs, Replace = diffs.Count > 0 };
                })
                .ToList();

            int differing = _items.Count(i => i.HasDifferences);
            txt_Header.Text = _items.Count == 1
                ? "This profile already exists. Tick it to replace yours with the imported one."
                : $"{_items.Count} profiles already exist ({differing} differ). Tick the ones to replace with the imported version; unticked ones keep your settings.";
            lst_Conflicts.ItemsSource = _items;
        }

        private static List<DiffRow> Compare(CarSettings current, CarSettings imported)
        {
            var rows = new List<DiffRow>();
            foreach (var p in typeof(CarSettings).GetProperties().Where(p => p.CanRead && p.CanWrite))
            {
                var a = p.GetValue(current);
                var b = p.GetValue(imported);
                if (!Equals(a, b))
                    rows.Add(new DiffRow(Humanize(p.Name), Format(a), Format(b)));
            }
            return rows;
        }

        private static string Humanize(string name) => Regex.Replace(name, "(?<=[a-z])(?=[A-Z])", " ");

        private static string Format(object? v) => v switch
        {
            bool b => b ? "On" : "Off",
            float f => f.ToString("0.##", CultureInfo.CurrentCulture),
            double d => d.ToString("0.##", CultureInfo.CurrentCulture),
            null => "",
            _ => Convert.ToString(v, CultureInfo.CurrentCulture) ?? ""
        };

        private void Import_Click(object sender, RoutedEventArgs e)
        {
            ReplaceNames = _items.Where(i => i.Replace && i.HasDifferences).Select(i => i.Name).ToList();
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                try { DragMove(); } catch { }
            }
        }
    }
}
