namespace KungFlow.Desktop;

internal sealed class MockNotificationSilencer : INotificationSilencer
{
    private const string EnabledValue = "on";
    private const string DisabledValue = "off";

    private static readonly string StateDirectoryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "KungFlow");

    private static readonly string StateFilePath = Path.Combine(StateDirectoryPath, "desktop-focus-state.txt");

    public void Enable()
    {
        WriteState(EnabledValue);
    }

    public void Disable()
    {
        WriteState(DisabledValue);
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

    private static void WriteState(string state)
    {
        Directory.CreateDirectory(StateDirectoryPath);
        File.WriteAllText(StateFilePath, state);
    }
}
