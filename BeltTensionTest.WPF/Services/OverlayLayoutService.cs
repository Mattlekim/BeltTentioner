using BeltTensionTest.WPF.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BeltTensionTest.WPF.Services
{
    /// <summary>
    /// Exports / imports the VR overlay layout: every Overlay* setting (panel
    /// positions, scales, opacity, show modes, bindings, width/offsets, curved,
    /// triple / triple + top screens, gaze...). Per-headset and per-person data
    /// (recenter origin, cursor calibration, driver tags) is left out.
    /// </summary>
    public static class OverlayLayoutService
    {
        private const string FormatName = "BST overlay layout";
        private const int FormatVersion = 1;

        private static readonly string[] ExcludedPrefixes = { "OverlayOrigin", "OverlayCursorCal" };
        private static readonly string[] ExcludedNames = { "OverlayDriverTags" };

        private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

        private static IEnumerable<PropertyInfo> LayoutProperties() =>
            typeof(AppSettings).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead && p.CanWrite
                    && p.Name.StartsWith("Overlay", StringComparison.Ordinal)
                    && !ExcludedNames.Contains(p.Name)
                    && !ExcludedPrefixes.Any(x => p.Name.StartsWith(x, StringComparison.Ordinal)));

        public static void Export(AppSettings settings, string path)
        {
            var values = new JsonObject();
            foreach (var p in LayoutProperties())
                values[p.Name] = JsonSerializer.SerializeToNode(p.GetValue(settings), p.PropertyType);

            var root = new JsonObject
            {
                ["format"] = FormatName,
                ["version"] = FormatVersion,
                ["settings"] = values
            };
            File.WriteAllText(path, root.ToJsonString(Options));
        }

        /// <summary>Applies a layout file onto <paramref name="settings"/>; returns how many values were applied.</summary>
        public static int Import(AppSettings settings, string path)
        {
            var root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
            if (root == null || (string?)root["format"] != FormatName || root["settings"] is not JsonObject values)
                throw new InvalidDataException("This is not a BST overlay layout file.");

            var props = LayoutProperties().ToDictionary(p => p.Name);
            // Parse everything first so a bad file changes nothing.
            var parsed = new List<(PropertyInfo Prop, object? Value)>();
            foreach (var kv in values)
            {
                if (!props.TryGetValue(kv.Key, out var p) || kv.Value == null) continue;
                parsed.Add((p, kv.Value.Deserialize(p.PropertyType)));
            }
            foreach (var (p, v) in parsed) p.SetValue(settings, v);
            return parsed.Count;
        }
    }
}
