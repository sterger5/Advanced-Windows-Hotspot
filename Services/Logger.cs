using System;
using System.IO;
using System.Text;

namespace AdvancedWindowsHotspot.Services
{
    public static class Logger
    {
        private static readonly object _lock = new();
        private static readonly string _logDirectory;
        private static readonly long _maxLogSize = 512 * 1024; // 512KB
        private static readonly int _maxLogFiles = 5;
        private static string? _currentLogFile;

        static Logger()
        {
            _logDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AdvancedWindowsHotspot", "logs");
            Directory.CreateDirectory(_logDirectory);
        }

        public static void Info(string message)
        {
            WriteLog("INFO", message);
        }

        public static void Warning(string message)
        {
            WriteLog("WARN", message);
        }

        public static void Error(string message)
        {
            WriteLog("ERROR", message);
        }

        public static void Error(string message, Exception ex)
        {
            WriteLog("ERROR", $"{message}: {ex}");
        }

        public static void Debug(string message)
        {
#if DEBUG
            WriteLog("DEBUG", message);
#endif
        }

        private static void WriteLog(string level, string message)
        {
            lock (_lock)
            {
                try
                {
                    EnsureLogFile();
                    var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                    var line = $"[{timestamp}] [{level}] {message}{Environment.NewLine}";
                    File.AppendAllText(_currentLogFile!, line, Encoding.UTF8);
                    RotateIfNeeded();
                }
                catch
                {
                    // 日志写入失败时不抛异常，避免影响主功能
                }
            }
        }

        private static void EnsureLogFile()
        {
            if (_currentLogFile != null && File.Exists(_currentLogFile))
            {
                return;
            }

            var dateStr = DateTime.Now.ToString("yyyyMMdd");
            _currentLogFile = Path.Combine(_logDirectory, $"hotspot_{dateStr}.log");
        }

        private static void RotateIfNeeded()
        {
            if (_currentLogFile == null || !File.Exists(_currentLogFile))
            {
                return;
            }

            var info = new FileInfo(_currentLogFile);
            if (info.Length < _maxLogSize)
            {
                return;
            }

            // 重命名当前日志文件
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var rotatedFile = Path.Combine(_logDirectory, $"hotspot_{timestamp}.log");
            try
            {
                File.Move(_currentLogFile, rotatedFile);
            }
            catch
            {
                return;
            }

            // 清理旧日志，保留最近N个
            var logFiles = Directory.GetFiles(_logDirectory, "hotspot_*.log");
            if (logFiles.Length <= _maxLogFiles)
            {
                return;
            }

            Array.Sort(logFiles);
            var filesToDelete = logFiles[..^(Math.Min(_maxLogFiles, logFiles.Length))];
            foreach (var file in filesToDelete)
            {
                try
                {
                    File.Delete(file);
                }
                catch
                {
                    // 忽略删除失败
                }
            }
        }
    }
}
