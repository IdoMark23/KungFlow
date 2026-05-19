using System;
using System.IO;

namespace KungFlow.Desktop.UI;

internal sealed class LocalFocusModeController
{
    private const string EnabledValue = "on";
    private const string DisabledValue = "off";

    private static readonly string StateDirectoryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "KungFlow");

    private static readonly string StateFilePath = Path.Combine(StateDirectoryPath, "desktop-focus-state.txt");

    public void SetEnabled(bool isEnabled)
    {
        Directory.CreateDirectory(StateDirectoryPath);
        File.WriteAllText(StateFilePath, isEnabled ? EnabledValue : DisabledValue);
    }

    public bool IsEnabled()
    {
        if (!File.Exists(StateFilePath))
        {
            return false;
        }

        string state = File.ReadAllText(StateFilePath).Trim();
        return string.Equals(state, EnabledValue, StringComparison.OrdinalIgnoreCase);
    }
}
