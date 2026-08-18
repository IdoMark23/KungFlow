namespace KungFlow.Desktop.Agent;

public static class DesktopDiagnosticLogger
{
    private static readonly object SyncRoot = new();

    public static string LogFilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "KungFlow",
        "desktop-diagnostics.log");

    public static void Log(string eventName, IReadOnlyDictionary<string, string?> details)
    {
        try
        {
            string? directory = Path.GetDirectoryName(LogFilePath);

            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string fields = string.Join(
                " ",
                details.Select(detail => $"{detail.Key}={Escape(detail.Value)}"));
            string line = $"{DateTimeOffset.UtcNow:O} event={Escape(eventName)} {fields}";

            lock (SyncRoot)
            {
                File.AppendAllText(LogFilePath, line + Environment.NewLine);
            }
        }
        catch
        {
            // Diagnostics must never break the prototype flow.
        }
    }

    private static string Escape(string? value)
    {
        return string.IsNullOrEmpty(value)
            ? "\"\""
            : $"\"{value.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";
    }
}
