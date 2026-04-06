using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace belttentiontest
{
    internal static class MyLogger
    {
        private static string? _logDirectory;
        private static readonly object _fileLock = new();

        public static void Initialize()
        {
            try
            {
               
                // Use application execution directory and store logs in a "logs" subfolder
                _logDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
                Directory.CreateDirectory(_logDirectory);

                Application.ThreadException += (s, e) => HandleException(e.Exception, "UI Thread Exception");
              
                AppDomain.CurrentDomain.UnhandledException += (s, e) =>
                {
                    var ex = e.ExceptionObject as Exception ?? new Exception("Non-Exception object thrown");
                    HandleException(ex, "Unhandled Domain Exception");
                    // terminate after logging - app may be in unstable state
                    try { Environment.Exit(1); } catch { }
                };
             
                TaskScheduler.UnobservedTaskException += (s, e) =>
                {
                    HandleException(e.Exception, "Unobserved Task Exception");
                    e.SetObserved();
                };
            }
            catch
            {
                // Swallow any exceptions during initialization - logging must not crash app
            }
        }

        public static string? LastLogFilePath { get; private set; }

        public static void LogException(Exception ex, string? context = null)
        {
            HandleException(ex, context ?? "Manual Log");
        }

        /// <summary>
        /// Simple message logger. Appends the provided message (with optional context) to a daily log file.
        /// Thread-safe via an internal lock.
        /// </summary>
        public static void Log(string message, string? context = null)
        {
            try
            {
                if (message == null) return;

                var sb = new StringBuilder();
                sb.AppendLine("----- Log -----");
                sb.AppendLine($"Timestamp: {DateTime.UtcNow:O}");
                if (!string.IsNullOrEmpty(context)) sb.AppendLine($"Context: {context}");
                sb.AppendLine(message);
                sb.AppendLine();

                var fileName = $"log_{DateTime.UtcNow:yyyyMMdd}.log";
                var filePath = _logDirectory != null ? Path.Combine(_logDirectory, fileName) : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName);

                lock (_fileLock)
                {
                    File.AppendAllText(filePath, sb.ToString());
                }

                LastLogFilePath = filePath;
            }
            catch
            {
                // Swallow any exceptions from logging
            }
        }

        private static void HandleException(Exception ex, string context)
        {
            try
            {
                if (ex == null) return;

                var sb = new StringBuilder();
                sb.AppendLine("----- Crash Log -----");
                sb.AppendLine($"Timestamp: {DateTime.UtcNow:O}");
                sb.AppendLine($"Context: {context}");
                sb.AppendLine($"Message: {ex.Message}");
                sb.AppendLine($"Type: {ex.GetType().FullName}");
                sb.AppendLine("StackTrace:");
                sb.AppendLine(ex.StackTrace ?? "(no stack trace)");

                if (ex.InnerException != null)
                {
                    sb.AppendLine();
                    sb.AppendLine("--- Inner Exception ---");
                    sb.AppendLine(ex.InnerException.ToString());
                }

                sb.AppendLine();
                sb.AppendLine("--- Environment ---");
                sb.AppendLine($"Machine: {Environment.MachineName}");
                sb.AppendLine($"User: {Environment.UserName}");
                sb.AppendLine($"OS: {Environment.OSVersion}");
                sb.AppendLine($"Process: {Process.GetCurrentProcess().ProcessName} (PID {Process.GetCurrentProcess().Id})");
                sb.AppendLine($"Assembly: {Assembly.GetEntryAssembly()?.GetName().Name} {Assembly.GetEntryAssembly()?.GetName().Version}");
                sb.AppendLine($"CLR Version: {Environment.Version}");

                var fileName = $"crash_{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}.log";
                var filePath = _logDirectory != null ? Path.Combine(_logDirectory, fileName) : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName);

                File.WriteAllText(filePath, sb.ToString());
                LastLogFilePath = filePath;

                try
                {
                    // Inform user in UI thread if possible
                    if (Application.OpenForms.Count > 0)
                    {
                        MessageBox.Show($"The application has encountered an error and a crash log was written to:\n{filePath}\n\nPlease send this file to the developer for troubleshooting.", "Application Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
                catch
                {
                    // ignore
                }
            }
            catch
            {
                // If logging itself fails, don't let it crash the app
            }
        }
    }
}
