using System;
using System.IO;

namespace BeltTensionTest.WPF.Services
{
    /// <summary>Where the app keeps the user's own files.</summary>
    public static class AppPaths
    {
        /// <summary>
        /// Documents\BBT — app settings and car profiles live here, where the user
        /// can find, back up and copy them. Created on first use.
        /// </summary>
        public static string UserDataDir
        {
            get
            {
                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "BBT");
                try { Directory.CreateDirectory(dir); } catch { }
                return dir;
            }
        }

        /// <summary>Where settings used to be kept (%APPDATA%\BeltTensioner), for the one-time move.</summary>
        public static string LegacyAppDataDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BeltTensioner");
    }
}
