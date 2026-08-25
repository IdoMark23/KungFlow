using System.Diagnostics;
using System.Runtime.InteropServices;

namespace KungFlow.Desktop.Agent;

internal static class WindowsNotificationServiceRefresher
{
    private const int HwndBroadcast = 0xffff;
    private const uint WmSettingChange = 0x001A;
    private const uint SmtoAbortIfHung = 0x0002;
    private const uint BroadcastTimeoutMilliseconds = 1000;

    public static void RefreshPushNotificationService()
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
        BroadcastNotificationSettingsChanged();
    }

    public static void RefreshApplicationNotificationSettings()
    {
        BroadcastNotificationSettingsChanged();
    }

    private static void BroadcastNotificationSettingsChanged()
    {
        string[] settingAreas =
        [
            @"Software\Microsoft\Windows\CurrentVersion\PushNotifications",
            @"Software\Microsoft\Windows\CurrentVersion\Notifications\Settings"
        ];

        foreach (string settingArea in settingAreas)
        {
            _ = SendMessageTimeout(
                new IntPtr(HwndBroadcast),
                WmSettingChange,
                UIntPtr.Zero,
                settingArea,
                SmtoAbortIfHung,
                BroadcastTimeoutMilliseconds,
                out _);
        }
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

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr hWnd,
        uint msg,
        UIntPtr wParam,
        string lParam,
        uint flags,
        uint timeout,
        out UIntPtr result);
}
