using System.Diagnostics;

namespace KungFlow.Desktop.Agent;

internal static class WindowsNotificationServiceRefresher
{
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
