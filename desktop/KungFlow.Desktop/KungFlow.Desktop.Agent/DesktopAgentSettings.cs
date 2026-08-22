namespace KungFlow.Desktop.Agent;

public sealed class DesktopAgentSettings
{
    public bool IsDataCollectionEnabled { get; set; } = true;

    public FirewallSettings Firewall { get; } = new();
}

public sealed class FirewallSettings
{
    private readonly HashSet<string> mutedApplicationIds = new(StringComparer.OrdinalIgnoreCase);

    // Null means KungFlow should follow the server recommendation automatically.
    public bool? ManualNotificationOverride { get; set; }

    public bool UseGlobalNotificationFirewall { get; set; } = true;

    public IReadOnlyCollection<string> MutedApplicationIds => mutedApplicationIds;

    public void SetApplicationMuted(string applicationId, bool isMuted)
    {
        if (isMuted)
        {
            mutedApplicationIds.Add(applicationId);
            return;
        }

        mutedApplicationIds.Remove(applicationId);
    }

    public bool IsApplicationMuted(string applicationId)
    {
        return mutedApplicationIds.Contains(applicationId);
    }
}

public sealed record FirewallTarget(
    string Id,
    string DisplayName,
    string Description,
    IReadOnlyList<string> RegistryKeyHints);

public static class FirewallTargetCatalog
{
    public const string WhatsAppId = "whatsapp";
    public const string OutlookId = "outlook";

    public static IReadOnlyList<FirewallTarget> Defaults { get; } = new[]
    {
        new FirewallTarget(
            WhatsAppId,
            "WhatsApp",
            "Reduce messaging interruptions when the firewall is active.",
            ["WhatsApp", "WhatsAppDesktop", "5319275A.WhatsAppDesktop"]),
        new FirewallTarget(
            OutlookId,
            "Outlook",
            "Reduce email interruptions when the firewall is active.",
            ["Outlook", "Microsoft.Outlook", "Microsoft.Office.OUTLOOK", "MicrosoftCorporationII.OutlookForWindows"])
    };
}
