using System;
using System.IO;
using Microsoft.Win32;

namespace LocalDesktopNote
{
    internal static class StartupManager
    {
        internal const string ValueName = "YourselfCopilot";
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

        internal static string ExecutablePath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Yourself Copilot.exe"); }
        }

        internal static string BuildCommand(string executablePath)
        {
            if (String.IsNullOrWhiteSpace(executablePath))
                throw new ArgumentException("启动程序路径不能为空。", "executablePath");
            foreach (char value in executablePath)
                if (value == '"' || Char.IsControl(value))
                    throw new ArgumentException("启动程序路径不能包含引号或控制字符。", "executablePath");

            string root = Path.GetPathRoot(executablePath);
            bool driveRooted = !String.IsNullOrEmpty(root) && root.Length == 3 && Char.IsLetter(root[0]) && root[1] == ':' &&
                (root[2] == Path.DirectorySeparatorChar || root[2] == Path.AltDirectorySeparatorChar);
            bool uncRooted = !String.IsNullOrEmpty(root) && root.StartsWith(@"\\", StringComparison.Ordinal) &&
                root.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries).Length >= 2;
            if (!driveRooted && !uncRooted)
                throw new ArgumentException("启动程序必须使用绝对路径。", "executablePath");
            string fullPath = Path.GetFullPath(executablePath);
            return "\"" + fullPath + "\" --startup";
        }

        internal static bool IsEnabled()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false))
                    return key != null && IsEnabled(key, ExecutablePath);
            }
            catch (Exception error)
            {
                throw ReadError(error);
            }
        }

        internal static void SetEnabled(bool enabled)
        {
            try
            {
                using (RegistryKey key = enabled
                    ? Registry.CurrentUser.CreateSubKey(RunKeyPath)
                    : Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
                {
                    if (key == null)
                    {
                        if (enabled) throw new InvalidOperationException("无法打开当前用户的启动设置。");
                        return;
                    }
                    SetEnabled(key, ExecutablePath, enabled);
                }
            }
            catch (Exception error)
            {
                throw WriteError(error);
            }
        }

        // Tests pass an isolated HKCU key here. Never redirect production's Run path.
        internal static bool IsEnabled(RegistryKey key, string executablePath)
        {
            if (key == null) throw new ArgumentNullException("key", "启动设置键不能为空。");
            string expected = BuildCommand(executablePath);
            try
            {
                object value = key.GetValue(ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                return value is string && String.Equals((string)value, expected, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception error)
            {
                throw ReadError(error);
            }
        }

        internal static void SetEnabled(RegistryKey key, string executablePath, bool enabled)
        {
            if (key == null) throw new ArgumentNullException("key", "启动设置键不能为空。");
            string expected = BuildCommand(executablePath);
            try
            {
                if (enabled)
                    key.SetValue(ValueName, expected, RegistryValueKind.String);
                else
                {
                    object value = key.GetValue(ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                    // A different command under this name is not ours to remove.
                    if (value != null && (!(value is string) || !String.Equals((string)value, expected, StringComparison.OrdinalIgnoreCase)))
                        throw new InvalidOperationException("同名启动项指向其他位置，已保留。请先检查 Windows 启动设置。");
                    key.DeleteValue(ValueName, false);
                }
                key.Flush();
            }
            catch (Exception error)
            {
                throw WriteError(error);
            }
        }

        private static InvalidOperationException ReadError(Exception error)
        {
            if (error is InvalidOperationException && error.Message.StartsWith("无法读取 Windows 登录启动设置：", StringComparison.Ordinal))
                return (InvalidOperationException)error;
            return new InvalidOperationException("无法读取 Windows 登录启动设置：" + error.Message, error);
        }

        private static InvalidOperationException WriteError(Exception error)
        {
            if (error is InvalidOperationException && error.Message.StartsWith("无法修改 Windows 登录启动设置：", StringComparison.Ordinal))
                return (InvalidOperationException)error;
            return new InvalidOperationException("无法修改 Windows 登录启动设置：" + error.Message, error);
        }
    }
}
