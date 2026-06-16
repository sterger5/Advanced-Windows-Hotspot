using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Data;

namespace AdvancedWindowsHotspot
{
    public partial class App : Application
    {
        private static readonly string CrashLogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AdvancedWindowsHotspot", "crash.log");

        private static readonly object CrashLogLock = new();

        private static void AppendCrashLog(string content)
        {
            lock (CrashLogLock)
            {
                try
                {
                    var dir = Path.GetDirectoryName(CrashLogPath);
                    if (dir != null && !Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }

                    // 限制日志大小，超过 256KB 则截断保留末尾部分
                    if (File.Exists(CrashLogPath))
                    {
                        var info = new FileInfo(CrashLogPath);
                        if (info.Length > 256 * 1024)
                        {
                            var lines = File.ReadAllLines(CrashLogPath);
                            var kept = lines[^100..];
                            File.WriteAllLines(CrashLogPath, kept, Encoding.UTF8);
                        }
                    }

                    File.AppendAllText(CrashLogPath, content, Encoding.UTF8);
                }
                catch
                {
                    // 日志写入失败时不抛异常
                }
            }
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                var ex = args.ExceptionObject as Exception;
                AppendCrashLog($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Unhandled: {ex}\n\n");
                MessageBox.Show($"发生未处理异常:\n{ex?.Message}\n\n{ex?.StackTrace}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            };
            DispatcherUnhandledException += (s, args) =>
            {
                AppendCrashLog($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Dispatcher: {args.Exception}\n\n");
                MessageBox.Show($"发生UI异常:\n{args.Exception.Message}\n\n{args.Exception.StackTrace}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                args.Handled = true;
            };
        }
    }
}
