using System;
using System.IO;

namespace UserModule
{
    public static class Logger
    {
        // Store logs in AppData\Local instead of Program Files to avoid permission issues
        private static readonly string appDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), 
            "Railax", "Logs");
        private static readonly string logFilePath = Path.Combine(appDataFolder, $"Log_{DateTime.Now:yyyy-MM-dd}.txt");

        static Logger()
        {
            try
            {
                if (!Directory.Exists(appDataFolder))
                    Directory.CreateDirectory(appDataFolder);
            }
            catch
            {
                // If we can't create log directory, just continue without logging
            }
        }

        public static void Log(string message)
        {
            // Commented out - will use later
            //try
            //{
            //    using (StreamWriter writer = new StreamWriter(logFilePath, true))
            //    {
            //        writer.WriteLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}");
            //    }
            //}
            //catch (Exception )
            //{
            //    //Console.WriteLine("Error writing log: " + ex.Message);
            //}
        }

        public static void LogError(Exception ex)
        {
            // Commented out - will use later
            //Log($"ERROR: {ex.Message}\nSTACK TRACE: {ex.StackTrace}");
        }
    }
}
