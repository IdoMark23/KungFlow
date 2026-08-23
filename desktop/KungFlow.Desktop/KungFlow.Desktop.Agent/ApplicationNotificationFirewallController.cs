using Microsoft.Win32;

namespace KungFlow.Desktop.Agent;

public sealed class ApplicationNotificationFirewallController
{
    private const string NotificationSettingsSubKey =
        @"Software\Microsoft\Windows\CurrentVersion\Notifications\Settings";
    private const string EnabledValueName = "Enabled";

    public IReadOnlyList<FirewallTarget> GetAvailableTargets(IReadOnlyList<FirewallTarget> targets)
    {
        using RegistryKey? settingsKey = Registry.CurrentUser.OpenSubKey(NotificationSettingsSubKey, writable: false);

        if (settingsKey is null)
        {
            return [];
        }

        return targets
            .Where(target => FindMatchingSubKeyNames(settingsKey, target).Count > 0)
            .ToList();
    }

    public ApplicationFirewallApplyResult SetApplicationsSilenced(
        IReadOnlyList<FirewallTarget> availableTargets,
        IReadOnlyCollection<string> selectedApplicationIds,
        bool shouldSilence)
    {
        HashSet<string> selectedIds = new(selectedApplicationIds, StringComparer.OrdinalIgnoreCase);
        List<FirewallTarget> selectedTargets = availableTargets
            .Where(target => selectedIds.Contains(target.Id))
            .ToList();

        if (selectedTargets.Count == 0)
        {
            return new ApplicationFirewallApplyResult(
                shouldSilence,
                [],
                [],
                "No firewall app targets are selected.");
        }

        using RegistryKey? settingsKey = Registry.CurrentUser.OpenSubKey(NotificationSettingsSubKey, writable: true);

        if (settingsKey is null)
        {
            return new ApplicationFirewallApplyResult(
                shouldSilence,
                [],
                selectedTargets.Select(target => target.DisplayName).ToArray(),
                "Windows per-app notification settings were not found for this user yet.");
        }

        List<string> updatedRegistryKeys = [];
        List<string> missingApplications = [];

        foreach (FirewallTarget target in selectedTargets)
        {
            List<string> matchingSubKeyNames = FindMatchingSubKeyNames(settingsKey, target);

            if (matchingSubKeyNames.Count == 0)
            {
                missingApplications.Add(target.DisplayName);
                continue;
            }

            foreach (string subKeyName in matchingSubKeyNames)
            {
                using RegistryKey? applicationKey = settingsKey.OpenSubKey(subKeyName, writable: true);

                if (applicationKey is null)
                {
                    continue;
                }

                if (shouldSilence)
                {
                    applicationKey.SetValue(
                        EnabledValueName,
                        0,
                        RegistryValueKind.DWord);
                }
                else
                {
                    applicationKey.SetValue(
                        EnabledValueName,
                        1,
                        RegistryValueKind.DWord);
                }

                updatedRegistryKeys.Add(subKeyName);
            }
        }

        if (updatedRegistryKeys.Count > 0)
        {
            WindowsNotificationServiceRefresher.RefreshPushNotificationService();
        }

        DesktopDiagnosticLogger.Log(
            "windows_app_notification_firewall_applied",
            new Dictionary<string, string?>
            {
                ["requestedState"] = shouldSilence ? "off" : "on",
                ["updatedKeys"] = string.Join(",", updatedRegistryKeys),
            ["missingApplications"] = string.Join(",", missingApplications)
            });

        return new ApplicationFirewallApplyResult(
            shouldSilence,
            updatedRegistryKeys,
            missingApplications,
            BuildSummary(shouldSilence, updatedRegistryKeys, missingApplications));
    }

    private static List<string> FindMatchingSubKeyNames(RegistryKey settingsKey, FirewallTarget target)
    {
        return settingsKey
            .GetSubKeyNames()
            .Where(subKeyName => IsMatchingTargetSubKey(subKeyName, target))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool IsMatchingTargetSubKey(string subKeyName, FirewallTarget target)
    {
        return target.RegistryKeyHints.Any(hint =>
            subKeyName.Contains(hint, StringComparison.OrdinalIgnoreCase));
    }

    private static string BuildSummary(
        bool shouldSilence,
        IReadOnlyCollection<string> updatedRegistryKeys,
        IReadOnlyCollection<string> missingApplications)
    {
        if (updatedRegistryKeys.Count == 0)
        {
            return shouldSilence
                ? "No matching Windows app notification entries were found."
                : "No app notification entries needed to be restored.";
        }

        string action = shouldSilence ? "silenced" : "restored";
        string summary = $"{updatedRegistryKeys.Count} Windows app notification entries {action}.";

        if (missingApplications.Count > 0)
        {
            summary += $" Missing: {string.Join(", ", missingApplications)}.";
        }

        return summary;
    }
}

public sealed record ApplicationFirewallApplyResult(
    bool RequestedSilenced,
    IReadOnlyList<string> UpdatedRegistryKeys,
    IReadOnlyList<string> MissingApplications,
    string Summary)
{
    public bool HasUpdates => UpdatedRegistryKeys.Count > 0;
}
