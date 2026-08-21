using Microsoft.Win32;
using System.Diagnostics;
using System.Globalization;

namespace KungFlow.Desktop.Agent;

public sealed class LocalFocusModeController
{
    private const string RegistrySubKey =
        @"Software\Microsoft\Windows\CurrentVersion\PushNotifications";
    private const string RegistryValueName = "ToastEnabled";
    private const string LegacyPolicySubKey =
        @"Software\Policies\Microsoft\Windows\CurrentVersion\PushNotifications";
    private const string LegacyPolicyValueName = "NoToastApplicationNotification";

    private bool isEnabled = ReadCurrentState();

    public void SetEnabled(bool isEnabled)
    {
        ClearLegacyPolicyOverride();

        if (this.isEnabled == isEnabled)
        {
            return;
        }

        WriteToastEnabled(isEnabled ? 0 : 1);
        RefreshWindowsPushNotificationService();
        this.isEnabled = isEnabled;

        DesktopDiagnosticLogger.Log(
            "windows_notifications_toggle_applied",
            new Dictionary<string, string?>
            {
                ["notificationsState"] = isEnabled ? "off" : "on",
                ["toastEnabled"] = isEnabled ? "0" : "1"
            });
    }

    public bool IsEnabled()
    {
        return isEnabled;
    }

    public bool RefreshState()
    {
        isEnabled = ReadCurrentState();
        return isEnabled;
    }

    private static bool ReadCurrentState()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RegistrySubKey);
        object? value = key?.GetValue(RegistryValueName);

        if (value is null)
        {
            return false;
        }

        return Convert.ToInt32(value, CultureInfo.InvariantCulture) == 0;
    }

    private static void WriteToastEnabled(int value)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(RegistrySubKey)
            ?? throw new InvalidOperationException("Windows notification registry key could not be opened.");
        key.SetValue(RegistryValueName, value, RegistryValueKind.DWord);
    }

    private static void ClearLegacyPolicyOverride()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(LegacyPolicySubKey, writable: true);
            key?.DeleteValue(LegacyPolicyValueName, throwOnMissingValue: false);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            DesktopDiagnosticLogger.Log(
                "legacy_notification_policy_clear_failed",
                new Dictionary<string, string?>
                {
                    ["policyValue"] = LegacyPolicyValueName,
                    ["errorType"] = ex.GetType().FullName,
                    ["message"] = ex.Message
                });
        }
    }

    private static void RefreshWindowsPushNotificationService()
    {
        const string command =
            "$svc = Get-Service -Name 'WpnUserService*' | " +
            "Where-Object { $_.Status -eq 'Running' } | " +
            "Select-Object -First 1; " +
            "if ($null -eq $svc) { " +
            "Write-Error 'Running WpnUserService was not found.'; exit 1 " +
            "}; " +
            "Restart-Service -Name $svc.Name -Force";

        RunPowerShell(command);
    }

    private static string RunPowerShell(string command)
    {
        using Process process = new();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        process.StartInfo.ArgumentList.Add("-NoProfile");
        process.StartInfo.ArgumentList.Add("-NonInteractive");
        process.StartInfo.ArgumentList.Add("-Command");
        process.StartInfo.ArgumentList.Add(command);

        process.Start();
        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            string errorMessage = string.IsNullOrWhiteSpace(error)
                ? output.Trim()
                : error.Trim();

            throw new InvalidOperationException(
                $"Windows notification setting could not be changed: {errorMessage}");
        }

        return output;
    }
}
