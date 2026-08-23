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
    public const string TeamsId = "teams";
    public const string SlackId = "slack";
    public const string DiscordId = "discord";
    public const string ChromeId = "chrome";
    public const string EdgeId = "edge";

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
            ["Outlook", "Microsoft.Outlook", "Microsoft.Office.OUTLOOK", "MicrosoftCorporationII.OutlookForWindows"]),
        new FirewallTarget(
            TeamsId,
            "Microsoft Teams",
            "Reduce meeting and chat interruptions when the firewall is active.",
            ["Teams", "MSTeams", "MicrosoftTeams", "Microsoft.Teams"]),
        new FirewallTarget(
            SlackId,
            "Slack",
            "Reduce workspace chat interruptions when the firewall is active.",
            ["Slack"]),
        new FirewallTarget(
            DiscordId,
            "Discord",
            "Reduce community and voice chat interruptions when the firewall is active.",
            ["Discord"]),
        new FirewallTarget(
            ChromeId,
            "Google Chrome",
            "Reduce browser and web app notifications when the firewall is active.",
            ["Chrome", "Google.Chrome"]),
        new FirewallTarget(
            EdgeId,
            "Microsoft Edge",
            "Reduce browser and web app notifications when the firewall is active.",
            ["Edge", "MSEdge", "MicrosoftEdge", "Microsoft.MicrosoftEdge"])
    };
}
