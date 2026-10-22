using System;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace LocalDesktopNote
{
    internal static class StartupVerification
    {
        private static void Check(bool condition, string name, StringBuilder report)
        {
            if (!condition) throw new InvalidOperationException("FAIL: " + name);
            report.AppendLine("PASS: " + name);
        }

        private static bool Rejects(string path)
        {
            try { StartupManager.BuildCommand(path); return false; }
            catch (ArgumentException) { return true; }
        }

        private static int Main()
        {
            string keyPath = @"Software\YourselfCopilot-QA-" + Guid.NewGuid().ToString("N");
            var report = new StringBuilder();
            int exitCode = 0;
            try
            {
                string executable = @"D:\工作 & 测试\Yourself Copilot.exe";
                string command = StartupManager.BuildCommand(executable);
                Check(command == "\"D:\\工作 & 测试\\Yourself Copilot.exe\" --startup", "spaces, Chinese characters and ampersand are quoted as one executable", report);
                Check(StartupManager.BuildCommand(@"D:\one\..\two\Yourself Copilot.exe") == "\"D:\\two\\Yourself Copilot.exe\" --startup", "command stores a normalized absolute executable path", report);
                Check(Rejects(null) && Rejects("") && Rejects("  "), "blank executable paths are rejected", report);
                Check(Rejects(@"Yourself Copilot.exe") && Rejects(@"D:Yourself Copilot.exe") && Rejects(@"\Yourself Copilot.exe"), "relative, drive-relative and root-relative paths are rejected", report);
                Check(StartupManager.BuildCommand(@"\\server\share\Yourself Copilot.exe") == "\"\\\\server\\share\\Yourself Copilot.exe\" --startup", "fully qualified UNC paths are supported", report);
                Check(Rejects("D:\\bad\"path\\tool.exe") && Rejects("D:\\bad\npath\\tool.exe") && Rejects("D:\\bad\0path\\tool.exe"), "quotes and control characters are rejected", report);
                Check(StartupManager.ExecutablePath == Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Yourself Copilot.exe"), "startup points at the formal executable rather than the QA entry point", report);
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(keyPath))
                {
                    Check(!StartupManager.IsEnabled(key, executable), "missing isolated startup value is disabled", report);
                    key.SetValue("OtherApplication", "unrelated startup command", RegistryValueKind.String);
                    StartupManager.SetEnabled(key, executable, true);
                    Check(StartupManager.IsEnabled(key, executable), "enabling persists a matching startup command", report);
                    Check((string)key.GetValue(StartupManager.ValueName) == command && key.GetValueKind(StartupManager.ValueName) == RegistryValueKind.String, "startup value is stored literally as REG_SZ", report);
                    Check(!StartupManager.IsEnabled(key, @"E:\Moved\Yourself Copilot.exe"), "a command from the old installation location is not reported enabled", report);
                    key.SetValue(StartupManager.ValueName, command.ToUpperInvariant());
                    Check(StartupManager.IsEnabled(key, executable), "Windows path matching is case insensitive", report);
                    StartupManager.SetEnabled(key, executable, false);
                    Check(!StartupManager.IsEnabled(key, executable) && key.GetValue(StartupManager.ValueName) == null, "disabling removes only the matching application value", report);
                    Check((string)key.GetValue("OtherApplication") == "unrelated startup command", "other applications' startup entries are preserved", report);
                    StartupManager.SetEnabled(key, executable, false);
                    Check(key.GetValue(StartupManager.ValueName) == null, "disabling an absent value is idempotent", report);
                    key.SetValue(StartupManager.ValueName, "foreign command");
                    bool foreignRejected = false;
                    try { StartupManager.SetEnabled(key, executable, false); }
                    catch (InvalidOperationException error) { foreignRejected = error.Message.Contains("已保留"); }
                    Check(foreignRejected && (string)key.GetValue(StartupManager.ValueName) == "foreign command", "a foreign same-name command is preserved with a readable explanation", report);
                    key.SetValue(StartupManager.ValueName, 7, RegistryValueKind.DWord);
                    Check(!StartupManager.IsEnabled(key, executable), "invalid registry value kinds are not mistaken for enabled", report);
                }
                using (RegistryKey readOnly = Registry.CurrentUser.OpenSubKey(keyPath, false))
                {
                    bool failed = false;
                    try { StartupManager.SetEnabled(readOnly, executable, true); }
                    catch (InvalidOperationException error) { failed = error.Message.Contains("无法修改 Windows 登录启动设置") && error.InnerException != null; }
                    Check(failed, "registry write failure reaches the UI as a readable error with its cause", report);
                }
                RegistryKey disposed = Registry.CurrentUser.OpenSubKey(keyPath, false);
                disposed.Dispose();
                bool readFailed = false;
                try { StartupManager.IsEnabled(disposed, executable); }
                catch (InvalidOperationException error) { readFailed = error.Message.Contains("无法读取 Windows 登录启动设置") && error.InnerException != null; }
                Check(readFailed, "registry read failure is not silently interpreted as disabled", report);
                report.AppendLine("QA uses only isolated HKCU key: " + keyPath);
            }
            catch (Exception error)
            {
                report.AppendLine(error.ToString()); exitCode = 1;
            }
            finally
            {
                try { Registry.CurrentUser.DeleteSubKeyTree(keyPath, false); }
                catch (Exception error) { report.AppendLine("FAIL: isolated QA key cleanup: " + error.Message); exitCode = 1; }
                File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "verification-startup.txt"), report.ToString(), Encoding.UTF8);
            }
            return exitCode;
        }
    }
}
