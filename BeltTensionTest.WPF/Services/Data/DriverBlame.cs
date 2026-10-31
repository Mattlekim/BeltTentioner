using System;
using System.Collections.Generic;
using System.Linq;

namespace BeltTensionTest.WPF.Services.Data
{
    /// <summary>
    /// How often the driver has blamed each other driver, and for what: "Dive bomb" x3,
    /// "Reckless" x1. Added to when blame is given on the incident list and taken off again
    /// if that blame is changed or cleared. Keyed like <see cref="DriverTags"/> (the iRacing
    /// customer id, a name for AI), so the counts carry on from one race to the next and the
    /// car-behind box can show them.
    ///
    /// The dictionary is the one held in AppSettings (kept by reference), so a change only
    /// needs saving: <see cref="Changed"/> is the cue.
    /// </summary>
    public sealed class DriverBlame
    {
        private static readonly Lazy<DriverBlame> Lazy = new(() => new DriverBlame());
        public static DriverBlame Instance => Lazy.Value;

        /// <summary>What a driver can be praised for.</summary>
        public static readonly string[] PraiseReasons = { "Good drive", "Good awareness", "Good side-by-side battling" };

        /// <summary>One line on the car-ahead / car-behind boxes: a blame or praise count, "Dive bomb x3".</summary>
        public readonly record struct NoteLine(string Text, bool Good);

        private readonly object _lock = new();
        private Dictionary<string, Dictionary<string, int>> _counts = new();
        private Dictionary<string, Dictionary<string, int>> _praise = new();

        /// <summary>Adopt the saved praise counts (kept by reference, like the blame counts).</summary>
        public void LoadPraise(Dictionary<string, Dictionary<string, int>> praise)
        {
            lock (_lock) _praise = praise;
        }

        /// <summary>One more time <paramref name="reason"/> was held in this driver's favour.</summary>
        public void AddPraise(string key, string reason)
        {
            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(reason)) return;
            lock (_lock)
            {
                if (!_praise.TryGetValue(key, out var reasons)) _praise[key] = reasons = new Dictionary<string, int>();
                reasons[reason] = reasons.GetValueOrDefault(reason) + 1;
            }
            Changed?.Invoke();
        }

        /// <summary>
        /// The lines to show for a driver: what they have been blamed for, then what they have been
        /// praised for, at most <paramref name="maxLines"/> (the last becomes "+n more" if there are extra).
        /// </summary>
        public NoteLine[] LinesFor(Car car, int maxLines)
        {
            var all = new List<NoteLine>();
            foreach (var (reason, count) in For(car)) all.Add(new NoteLine($"{reason} x{count}", false));
            string key = DriverTags.KeyFor(car);
            if (!string.IsNullOrEmpty(key))
                lock (_lock)
                    if (_praise.TryGetValue(key, out var praise))
                        foreach (var p in praise.OrderByDescending(r => r.Value).ThenBy(r => r.Key, StringComparer.Ordinal))
                            all.Add(new NoteLine($"{p.Key} x{p.Value}", true));

            if (all.Count <= maxLines) return all.ToArray();
            var shown = all.Take(maxLines).ToArray();
            shown[^1] = new NoteLine($"+{all.Count - maxLines + 1} more", false);
            return shown;
        }

        /// <summary>Raised after a count changes, so the owner can save.</summary>
        public event Action? Changed;

        /// <summary>Adopt the saved counts (kept by reference).</summary>
        public void Load(Dictionary<string, Dictionary<string, int>> counts)
        {
            lock (_lock) _counts = counts;
        }

        /// <summary>One more time <paramref name="reason"/> was held against this driver.</summary>
        public void Add(string key, string reason)
        {
            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(reason)) return;
            lock (_lock)
            {
                if (!_counts.TryGetValue(key, out var reasons)) _counts[key] = reasons = new Dictionary<string, int>();
                reasons[reason] = reasons.GetValueOrDefault(reason) + 1;
            }
            Changed?.Invoke();
        }

        /// <summary>Take one back (the blame was changed or cleared).</summary>
        public void Remove(string key, string reason)
        {
            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(reason)) return;
            lock (_lock)
            {
                if (!_counts.TryGetValue(key, out var reasons) || !reasons.TryGetValue(reason, out int n)) return;
                if (n <= 1) reasons.Remove(reason); else reasons[reason] = n - 1;
                if (reasons.Count == 0) _counts.Remove(key);
            }
            Changed?.Invoke();
        }

        /// <summary>This driver's reasons with how many times each, most often first; empty when never blamed.</summary>
        public IReadOnlyList<(string Reason, int Count)> For(Car car)
        {
            string key = DriverTags.KeyFor(car);
            if (string.IsNullOrEmpty(key)) return Array.Empty<(string, int)>();
            lock (_lock)
            {
                if (!_counts.TryGetValue(key, out var reasons)) return Array.Empty<(string, int)>();
                return reasons.OrderByDescending(r => r.Value).ThenBy(r => r.Key, StringComparer.Ordinal)
                              .Select(r => (r.Key, r.Value)).ToList();
            }
        }
    }
}
