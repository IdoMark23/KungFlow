using System.Text.Json;

namespace KungFlow.Desktop.Agent;

public static class DesktopAgentSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static string SettingsFilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "KungFlow",
        "desktop-settings.json");

    public static DesktopAgentSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsFilePath))
            {
                return new DesktopAgentSettings();
            }

            string json = File.ReadAllText(SettingsFilePath);
            DesktopAgentSettingsDto? dto = JsonSerializer.Deserialize<DesktopAgentSettingsDto>(json, JsonOptions);

            if (dto is null)
            {
                return new DesktopAgentSettings();
            }

            DesktopAgentSettings settings = new()
            {
                IsDataCollectionEnabled = dto.IsDataCollectionEnabled
            };
            settings.Firewall.UseGlobalNotificationFirewall =
                dto.Firewall?.UseGlobalNotificationFirewall
                ?? dto.Firewall?.UseDefaultDoNotDisturb
                ?? true;
            settings.Firewall.ManualNotificationOverride = dto.Firewall?.ManualNotificationOverride;

            foreach (string applicationId in dto.Firewall?.MutedApplicationIds ?? [])
            {
                if (!string.IsNullOrWhiteSpace(applicationId))
                {
                    settings.Firewall.SetApplicationMuted(applicationId, true);
                }
            }

            DesktopDiagnosticLogger.Log(
                "desktop_settings_loaded",
                new Dictionary<string, string?>
                {
                    ["path"] = SettingsFilePath,
                    ["dataCollectionEnabled"] = settings.IsDataCollectionEnabled ? "true" : "false"
                });

            return settings;
        }
        catch (Exception ex)
        {
            DesktopDiagnosticLogger.Log(
                "desktop_settings_load_failed",
                new Dictionary<string, string?>
                {
                    ["path"] = SettingsFilePath,
                    ["errorType"] = ex.GetType().FullName,
                    ["message"] = ex.Message
                });

            return new DesktopAgentSettings();
        }
    }

    public static void Save(DesktopAgentSettings settings)
    {
        try
        {
            string? directory = Path.GetDirectoryName(SettingsFilePath);

            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            DesktopAgentSettingsDto dto = new()
            {
                IsDataCollectionEnabled = settings.IsDataCollectionEnabled,
                Firewall = new FirewallSettingsDto
                {
                    ManualNotificationOverride = settings.Firewall.ManualNotificationOverride,
                    UseDefaultDoNotDisturb = settings.Firewall.UseGlobalNotificationFirewall,
                    UseGlobalNotificationFirewall = settings.Firewall.UseGlobalNotificationFirewall,
                    MutedApplicationIds = settings.Firewall.MutedApplicationIds.ToArray()
                }
            };

            string json = JsonSerializer.Serialize(dto, JsonOptions);
            File.WriteAllText(SettingsFilePath, json);

            DesktopDiagnosticLogger.Log(
                "desktop_settings_saved",
                new Dictionary<string, string?>
                {
                    ["path"] = SettingsFilePath,
                    ["dataCollectionEnabled"] = settings.IsDataCollectionEnabled ? "true" : "false"
                });
        }
        catch (Exception ex)
        {
            DesktopDiagnosticLogger.Log(
                "desktop_settings_save_failed",
                new Dictionary<string, string?>
                {
                    ["path"] = SettingsFilePath,
                    ["errorType"] = ex.GetType().FullName,
                    ["message"] = ex.Message
                });
        }
    }

    private sealed class DesktopAgentSettingsDto
    {
        public bool IsDataCollectionEnabled { get; set; } = true;

        public FirewallSettingsDto? Firewall { get; set; } = new();
    }

    private sealed class FirewallSettingsDto
    {
        public bool? ManualNotificationOverride { get; set; }

        public bool? UseDefaultDoNotDisturb { get; set; }

        public bool? UseGlobalNotificationFirewall { get; set; }

        public string[] MutedApplicationIds { get; set; } = [];
    }
}
