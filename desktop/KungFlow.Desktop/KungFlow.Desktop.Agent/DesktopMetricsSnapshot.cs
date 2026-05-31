namespace KungFlow.Desktop.Agent;

public sealed record DesktopMetricsSnapshot(
    DateTimeOffset Timestamp,
    bool HasUserActivity,
    int OpenWindowsCount,
    int WindowSwitchCount,
    int DeleteKeyCount,
    int KeyPressCount,
    double TypingSpeed,
    double MouseSpeed);
