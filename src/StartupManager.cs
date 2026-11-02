using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace XiaoJingDesktopPet
{
    public static class StartupManager
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "XiaoJingDesktopPet";
        public static bool IsEnabled() { return IsEnabled(CurrentExecutable()); }
        public static bool IsEnabled(string executablePath)
        {
            string expected = BuildCommand(executablePath);
            using (var key = Registry.CurrentUser.OpenSubKey(RunKey, false)) {
                return key != null && String.Equals(key.GetValue(ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string, expected, StringComparison.OrdinalIgnoreCase);
            }
        }
        // Called only from the explicit user checkbox action. No constructor,
        // startup code or status query enables this option by default.
        public static void SetEnabled(bool enabled, string executablePath)
        {
            string executable = Normalize(executablePath);
            if (!String.Equals(executable, Normalize(CurrentExecutable()), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("开机启动仅能指向当前桌宠程序。");
            string expected = BuildCommand(executable);
            if (enabled) {
                if (!File.Exists(executable)) throw new FileNotFoundException("当前桌宠程序不存在。", executable);
                using (var key = Registry.CurrentUser.CreateSubKey(RunKey)) key.SetValue(ValueName, expected, RegistryValueKind.String);
            } else {
                using (var key = Registry.CurrentUser.OpenSubKey(RunKey, true)) {
                    if (key == null) return;
                    // Never remove another install's registration or any other app.
                    if (String.Equals(key.GetValue(ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string, expected, StringComparison.OrdinalIgnoreCase))
                        key.DeleteValue(ValueName, false);
                }
            }
        }
        public static string BuildCommand(string executablePath) { return "\"" + Normalize(executablePath) + "\""; }
        private static string Normalize(string executablePath)
        {
            if (String.IsNullOrWhiteSpace(executablePath) || !Path.IsPathRooted(executablePath)
                || executablePath.IndexOf('"') >= 0 || executablePath.IndexOf('\r') >= 0 || executablePath.IndexOf('\n') >= 0)
                throw new ArgumentException("必须使用当前程序的完整可执行文件路径。", "executablePath");
            string full = Path.GetFullPath(executablePath);
            if (!String.Equals(Path.GetExtension(full), ".exe", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("开机启动目标必须是可执行文件。", "executablePath");
            return full;
        }
        private static string CurrentExecutable()
        {
            using (var process = Process.GetCurrentProcess()) return process.MainModule.FileName;
        }
    }
}
