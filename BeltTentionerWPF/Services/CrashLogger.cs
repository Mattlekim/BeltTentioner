using System.IO;

namespace BeltTentionerWPF.Services
{
    /// <summary>
    /// Simple file-based logger.
    /// </summary>
    public static class CrashLogger
    {
        private static string _logPath = string.Empty;

        public static void Initialize()
        {
            _logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash_log.txt");
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                try { File.AppendAllText(_logPath, $"[{DateTime.Now}] CRASH: {e.ExceptionObject}\n"); } catch { }
            };
        }

        public static void Log(string msg)
        {
            try { File.AppendAllText(_logPath, $"[{DateTime.Now:HH:mm:ss}] {msg}\n"); } catch { }
        }
    }
}
