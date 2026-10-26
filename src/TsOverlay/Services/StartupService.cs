using Microsoft.Win32;

namespace TsOverlay.Services;

/// <summary>Автозапуск приложения вместе с Windows через HKCU\...\Run.</summary>
public static class StartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "TsOverlay";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(ValueName) != null;
        }
        catch (Exception ex)
        {
            Log.Error("Автозапуск: не удалось прочитать реестр", ex);
            return false;
        }
    }

    public static void SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (enabled)
            {
                var exe = Environment.ProcessPath ?? "";
                key.SetValue(ValueName, "\"" + exe + "\"");
                Log.Info("Автозапуск включён: " + exe);
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
                Log.Info("Автозапуск выключен");
            }
        }
        catch (Exception ex)
        {
            Log.Error("Автозапуск: не удалось изменить реестр", ex);
        }
    }
}
