using System;
using System.IO;

namespace FastImageGallery
{
    public static class Logger
    {
        private static readonly string LogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FastImageGallery", "log.txt");

        static Logger()
        {
            try
            {
                var directory = Path.GetDirectoryName(LogPath);
                if (directory != null)
                {
                    Directory.CreateDirectory(directory);
                    // Write initial log entry to test file creation
                    File.WriteAllText(LogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Logger initialized{Environment.NewLine}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to initialize logger: {ex.Message}");
            }
        }

        public static void Log(string message)
        {
            try
            {
                var logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}";
                File.AppendAllText(LogPath, logEntry);
                Console.WriteLine(logEntry);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to write log: {ex.Message}");
            }
        }

        public static void LogError(string message, Exception ex)
        {
            Log($"ERROR: {message}");
            Log($"Exception: {ex.GetType().Name}: {ex.Message}");
            Log($"Stack Trace: {ex.StackTrace}");
        }
    }
} 