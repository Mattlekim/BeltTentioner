using System;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace BeltTensionTest.WPF.Services
{
    /// <summary>
    /// Registers the MonoXR OpenXR API layer that ships next to the exe
    /// (MonoXR.json + XR_APILAYER_NOVELTY_monoxr.dll) as a per-user implicit
    /// layer, so an unzipped copy of the app works without any install step.
    /// Safe to call on every startup: it re-points the registration if the
    /// app folder moved and removes stale MonoXR registrations (e.g. a dev
    /// registration from install-layer.ps1 or a previous unzip location) so
    /// the loader never sees the layer twice.
    /// </summary>
    public static class MonoXRLayerInstaller
    {
        private const string ImplicitLayersKey = @"Software\Khronos\OpenXR\1\ApiLayers\Implicit";
        private const string ManifestFileName = "MonoXR.json";

        public static void EnsureRegistered()
        {
            try
            {
                string manifest = Path.Combine(AppContext.BaseDirectory, ManifestFileName);
                if (!File.Exists(manifest))
                    return; // dev build without packaged layer; leave any existing registration alone

                using var key = Registry.CurrentUser.CreateSubKey(ImplicitLayersKey, writable: true);
                if (key == null)
                    return;

                // Registry value names are the manifest paths. Drop every other
                // MonoXR manifest registration so only the copy next to this exe
                // is loaded.
                foreach (var name in key.GetValueNames()
                             .Where(n => n.EndsWith("\\" + ManifestFileName, StringComparison.OrdinalIgnoreCase)
                                         && !n.Equals(manifest, StringComparison.OrdinalIgnoreCase)))
                {
                    key.DeleteValue(name, throwOnMissingValue: false);
                }

                // DWORD 0 = enabled (nonzero = disabled) per the OpenXR loader spec.
                if (!(key.GetValue(manifest) is int existing && existing == 0))
                    key.SetValue(manifest, 0, RegistryValueKind.DWord);
            }
            catch
            {
                // Registration is best-effort; the app itself works without it,
                // only the in-VR overlay would be missing.
            }
        }
    }
}
