using System;
using System.Collections.Generic;
using System.IO;
using GameRegionGuard.Models;

namespace GameRegionGuard.Services
{
    public static class Logger
    {
        private static readonly object LockObject = new object();
        private static string _logFilePath;
        private static readonly List<LogEntry> LogEntries = new List<LogEntry>();

        public static event EventHandler<string> LogEntryAdded;

        public static void Initialize()
        {
            try
            {
                var appDirectory = AppDomain.CurrentDomain.BaseDirectory;
                _logFilePath = Path.Combine(appDirectory, "GameRegionGuard.log");

                // Keep logs per run instead of appending forever:
                // - Current run:  GameRegionGuard.log
                // - Previous run: GameRegionGuard.previous.log (overwritten each start)
                var previousLogPath = Path.Combine(appDirectory, "GameRegionGuard.previous.log");

                try
                {
                    if (File.Exists(_logFilePath))
                    {
                        // Overwrite the previous log snapshot each start.
                        File.Move(_logFilePath, previousLogPath, overwrite: true);
                    }
                }
                catch (Exception rotateEx)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to rotate log file: {rotateEx.Message}");
                }

                // Write initial log entry
                Info($"Logger initialized. Log file: {_logFilePath}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to initialize logger: {ex.Message}");
            }
        }

        public static void Debug(string message)
        {
            Log(LogLevel.Debug, message);
        }

        public static void Info(string message)
        {
            Log(LogLevel.Info, message);
        }

        public static void Warning(string message)
        {
            Log(LogLevel.Warning, message);
        }

        public static void Error(string message)
        {
            Log(LogLevel.Error, message);
        }

        private static void Log(LogLevel level, string message)
        {
            lock (LockObject)
            {
                try
                {
                    var entry = new LogEntry
                    {
                        Timestamp = DateTime.Now,
                        Level = level,
                        Message = message
                    };

                    LogEntries.Add(entry);

                    var logMessage = entry.ToString();

                    // Write to file
                    if (!string.IsNullOrEmpty(_logFilePath))
                    {
                        File.AppendAllText(_logFilePath, logMessage + Environment.NewLine);
                    }

                    // Write to debug output
                    System.Diagnostics.Debug.WriteLine(logMessage);

                    // Notify UI
                    LogEntryAdded?.Invoke(null, logMessage);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Logging error: {ex.Message}");
                }
            }
        }

        public static void Shutdown()
        {
            Info("Logger shutting down");
        }
    }
}