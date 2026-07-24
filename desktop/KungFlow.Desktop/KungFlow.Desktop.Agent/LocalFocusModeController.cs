namespace KungFlow.Desktop.Agent;

public sealed class LocalFocusModeController
{
    private const string RegistryPath =
        @"HKCU:\Software\Policies\Microsoft\Windows\CurrentVersion\PushNotifications";
    private const string RegistryValueName = "NoToastApplicationNotification";

    private bool isEnabled = ReadCurrentState();

    public void SetEnabled(bool isEnabled)
    {
        if (this.isEnabled == isEnabled)
        {
            return;
        }

        string value = isEnabled ? "1" : "0";
        string command =
            $"$path='{RegistryPath}'; " +
            "New-Item -Path $path -Force | Out-Null; " +
            $"New-ItemProperty -Path $path -Name '{RegistryValueName}' " +
            $"-PropertyType DWord -Value {value} -Force | Out-Null";

        RunPowerShell(command);
        this.isEnabled = isEnabled;
    }

    public bool IsEnabled()
    {
        return isEnabled;
    }

    private static bool ReadCurrentState()
    {
        string command =
            $"$value=(Get-ItemProperty -Path '{RegistryPath}' -Name '{RegistryValueName}' " +
            $"-ErrorAction SilentlyContinue).{RegistryValueName}; " +
            "if ($null -eq $value) { '0' } else { $value }";

        string output = RunPowerShell(command);
        return output.Trim() == "1";
    }

    private static string RunPowerShell(string command)
    {
        using System.Diagnostics.Process process = new();
        process.StartInfo = new System.Diagnostics.ProcessStartInfo
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
            throw new InvalidOperationException(
                $"Windows notification setting could not be changed: {error.Trim()}");
        }

        return output;
    }
}
