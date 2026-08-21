using KungFlow.Desktop.Agent;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Runtime.InteropServices;
using Forms = System.Windows.Forms;
using MediaColor = System.Windows.Media.Color;

namespace KungFlow.Desktop.UI;

public partial class MainWindow : Window
{
    private readonly KungFlowApiClient apiClient = new();
    private readonly DesktopAgentSettings agentSettings = DesktopAgentSettingsStore.Load();
    private readonly LocalFocusModeController focusModeController = new();
    private readonly DesktopMetricsCollector metricsCollector = new();
    private readonly DispatcherTimer statusRefreshTimer = new();
    private readonly DispatcherTimer metricsSendTimer = new();
    private readonly DispatcherTimer metricsPollingTimer = new();
    private readonly Forms.NotifyIcon trayIcon = new();
    private readonly TimeSpan metricsCollectionWindow = TimeSpan.FromMinutes(1);
    private DesktopSession? session;
    private bool isRefreshingStatus;
    private bool isRefreshingFirewallHistory;
    private bool isSendingMetrics;
    private bool isExitRequested;
    private bool isLoadingSettings = true;
    private bool? manualNotificationOverride;

    public MainWindow()
    {
        InitializeComponent();

        statusRefreshTimer.Interval = TimeSpan.FromSeconds(10);
        statusRefreshTimer.Tick += StatusRefreshTimer_Tick;
        metricsSendTimer.Interval = metricsCollectionWindow;
        metricsSendTimer.Tick += MetricsSendTimer_Tick;
        metricsPollingTimer.Interval = TimeSpan.FromMilliseconds(250);
        metricsPollingTimer.Tick += MetricsPollingTimer_Tick;

        ApplySavedLoginCredentials();
        ApplySettingsToControls();
        manualNotificationOverride = agentSettings.Firewall.ManualNotificationOverride;
        isLoadingSettings = false;

        ConfigureTrayIcon();
        UpdateManualNotificationControls();
        ShowDashboardPage(DashboardPage.Status);
    }

    private async void LoginButton_Click(object sender, RoutedEventArgs e)
    {
        string email = EmailTextBox.Text.Trim();
        string password = PasswordBox.Password;

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            SetMessage("Email and password are required.", true);
            return;
        }

        SetMessage("Logging in...");
        SetAuthButtonsEnabled(false);

        try
        {
            LoginResponse response = await apiClient.LoginAsync(email, password, CancellationToken.None);
            DesktopLoginCredentialStore.Save(email, password);
            await StartSessionAsync(response);
        }
        catch (Exception ex)
        {
            SetMessage(ex.Message, true);
        }
        finally
        {
            SetAuthButtonsEnabled(true);
        }
    }

    private async void RegisterButton_Click(object sender, RoutedEventArgs e)
    {
        string email = RegisterEmailTextBox.Text.Trim();
        string username = RegisterUsernameTextBox.Text.Trim();
        string password = RegisterPasswordBox.Password;

        List<string> registrationErrors = [];

        if (string.IsNullOrWhiteSpace(email))
        {
            registrationErrors.Add("Email cannot be empty.");
        }
        else if (!System.Text.RegularExpressions.Regex.IsMatch(email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
        {
            registrationErrors.Add("Enter a valid email address.");
        }

        if (string.IsNullOrWhiteSpace(username))
        {
            registrationErrors.Add("Username cannot be empty.");
        }
        else
        {
            if (username.Length < 6)
            {
                registrationErrors.Add("Username must contain at least 6 characters.");
            }

            if (username.Any(character => character is < ' ' or > '~'))
            {
                registrationErrors.Add("Username may contain printable ASCII characters only.");
            }
        }

        if (string.IsNullOrEmpty(password))
        {
            registrationErrors.Add("Password cannot be empty.");
        }
        else
        {
            registrationErrors.AddRange(ValidatePassword(password));
        }

        if (registrationErrors.Count > 0)
        {
            SetRegisterMessage(string.Join("  •  ", registrationErrors), true);
            return;
        }

        SetRegisterMessage("Creating account...");
        SetAuthButtonsEnabled(false);

        try
        {
            LoginResponse response = await apiClient.RegisterAndLoginAsync(
                email,
                username,
                password,
                CancellationToken.None);
            DesktopLoginCredentialStore.Save(email, password);
            await StartSessionAsync(response);
        }
        catch (Exception ex)
        {
            SetRegisterMessage(ex.Message, true);
        }
        finally
        {
            SetAuthButtonsEnabled(true);
        }
    }

    private async void ChangePasswordButton_Click(object sender, RoutedEventArgs e)
    {
        if (session is null)
        {
            SetChangePasswordMessage("You must be logged in to change password.", true);
            return;
        }

        string currentPassword = CurrentPasswordBox.Password;
        string newPassword = NewPasswordBox.Password;
        string confirmNewPassword = ConfirmNewPasswordBox.Password;
        List<string> passwordErrors = [];

        if (string.IsNullOrWhiteSpace(currentPassword))
        {
            passwordErrors.Add("Current password is required.");
        }

        if (string.IsNullOrEmpty(newPassword))
        {
            passwordErrors.Add("New password is required.");
        }
        else
        {
            passwordErrors.AddRange(ValidatePassword(newPassword));
        }

        if (newPassword != confirmNewPassword)
        {
            passwordErrors.Add("New passwords do not match.");
        }

        if (!string.IsNullOrEmpty(newPassword) && newPassword == currentPassword)
        {
            passwordErrors.Add("New password must be different from the current password.");
        }

        if (passwordErrors.Count > 0)
        {
            SetChangePasswordMessage(string.Join("  •  ", passwordErrors), true);
            return;
        }

        ChangePasswordButton.IsEnabled = false;
        SetChangePasswordMessage("Changing password...");

        try
        {
            await apiClient.ChangePasswordAsync(
                session.AccessToken,
                currentPassword,
                newPassword,
                confirmNewPassword,
                CancellationToken.None);

            DesktopLoginCredentialStore.Save(session.User.Email, newPassword);
            PasswordBox.Password = newPassword;
            CurrentPasswordBox.Clear();
            NewPasswordBox.Clear();
            ConfirmNewPasswordBox.Clear();
            SetChangePasswordMessage("Password changed successfully.");
            DesktopDiagnosticLogger.Log(
                "desktop_password_change_succeeded",
                new Dictionary<string, string?>
                {
                    ["email"] = session.User.Email,
                    ["credentialsUpdated"] = "true"
                });
        }
        catch (Exception ex)
        {
            SetChangePasswordMessage(ex.Message, true);
            DesktopDiagnosticLogger.Log(
                "desktop_password_change_failed",
                new Dictionary<string, string?>
                {
                    ["email"] = session.User.Email,
                    ["errorType"] = ex.GetType().FullName,
                    ["message"] = ex.Message
                });
        }
        finally
        {
            ChangePasswordButton.IsEnabled = true;
        }
    }

    private void ShowRegisterButton_Click(object sender, RoutedEventArgs e)
    {
        RegisterEmailTextBox.Text = EmailTextBox.Text.Trim();
        RegisterPasswordBox.Password = PasswordBox.Password;
        SetMessage("");
        SetRegisterMessage("");
        LoginView.Visibility = Visibility.Collapsed;
        RegisterView.Visibility = Visibility.Visible;
    }

    private void BackToLoginButton_Click(object sender, RoutedEventArgs e)
    {
        EmailTextBox.Text = RegisterEmailTextBox.Text.Trim();
        PasswordBox.Password = RegisterPasswordBox.Password;
        SetRegisterMessage("");
        RegisterView.Visibility = Visibility.Collapsed;
        LoginView.Visibility = Visibility.Visible;
    }

    private void LogoutButton_Click(object sender, RoutedEventArgs e)
    {
        LogoutConfirmationOverlay.Visibility = Visibility.Visible;
    }

    private static List<string> ValidatePassword(string password)
    {
        List<string> errors = [];

        if (password.Length < 6)
        {
            errors.Add("Password must contain at least 6 characters.");
        }

        if (password.Length > 20)
        {
            errors.Add("Password must contain no more than 20 characters.");
        }

        if (!password.Any(character => character is >= 'A' and <= 'Z' or >= 'a' and <= 'z'))
        {
            errors.Add("Password must contain at least one English letter.");
        }

        if (!password.Any(character => character is >= '0' and <= '9'))
        {
            errors.Add("Password must contain at least one number.");
        }

        if (password.Any(character => character is not (>= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9')))
        {
            errors.Add("Password may contain English letters and numbers only.");
        }

        return errors;
    }

    private void CancelLogoutButton_Click(object sender, RoutedEventArgs e)
    {
        LogoutConfirmationOverlay.Visibility = Visibility.Collapsed;
    }

    private async void ConfirmLogoutButton_Click(object sender, RoutedEventArgs e)
    {
        LogoutConfirmationOverlay.Visibility = Visibility.Collapsed;
        statusRefreshTimer.Stop();
        metricsSendTimer.Stop();
        metricsPollingTimer.Stop();
        LogoutButton.IsEnabled = false;
        DesktopSession? currentSession = session;
        string logoutMessage = "Logged out.";
        bool logoutMessageIsError = false;

        try
        {
            if (currentSession is not null)
            {
                await apiClient.LogoutAsync(currentSession.AccessToken, CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            logoutMessage = $"Server logout failed: {ex.Message}";
            logoutMessageIsError = true;
        }
        finally
        {
            LogoutButton.IsEnabled = true;
            ClearLocalSession();
            SetMessage(logoutMessageIsError ? logoutMessage : "", logoutMessageIsError);
        }
    }

    private void ClearLocalSession()
    {
        session = null;
        metricsCollector.Stop();
        SetManualNotificationOverride(null);
        focusModeController.SetEnabled(false);
        CurrentPasswordBox.Clear();
        NewPasswordBox.Clear();
        ConfirmNewPasswordBox.Clear();
        SetChangePasswordMessage("");
        SetDesktopStatusMessage("");
        ResetStatusView();
        LoggedInView.Visibility = Visibility.Collapsed;
        RegisterView.Visibility = Visibility.Collapsed;
        LoginView.Visibility = Visibility.Visible;
        ApplySavedLoginCredentials();
    }

    private void ApplySavedLoginCredentials()
    {
        DesktopLoginCredentials credentials = DesktopLoginCredentialStore.Load();

        EmailTextBox.Text = credentials.Email;
        PasswordBox.Password = credentials.Password;
    }

    private void ShowLoggedInView(string email)
    {
        LoggedInEmailTextBlock.Text = $"Signed in as {email}";
        LoginView.Visibility = Visibility.Collapsed;
        RegisterView.Visibility = Visibility.Collapsed;
        LoggedInView.Visibility = Visibility.Visible;
        ShowDashboardPage(DashboardPage.Status);
    }

    private void StatusNavButton_Click(object sender, RoutedEventArgs e)
    {
        ShowDashboardPage(DashboardPage.Status);
    }

    private async void StatisticsNavButton_Click(object sender, RoutedEventArgs e)
    {
        ShowDashboardPage(DashboardPage.Statistics);
        await RefreshFirewallHistoryAsync();
    }

    private void SettingsNavButton_Click(object sender, RoutedEventArgs e)
    {
        ShowDashboardPage(DashboardPage.Settings);
    }

    private void ActivityCollectionSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        ShowSettingsSection(SettingsSection.ActivityCollection);
    }

    private void AccountSecuritySettingsButton_Click(object sender, RoutedEventArgs e)
    {
        ShowSettingsSection(SettingsSection.AccountSecurity);
    }

    private void FirewallControlSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        ShowSettingsSection(SettingsSection.FirewallControl);
    }

    private void PrivacyNavButton_Click(object sender, RoutedEventArgs e)
    {
        ShowDashboardPage(DashboardPage.Privacy);
    }

    private void ShowDashboardPage(DashboardPage page)
    {
        StatusPage.Visibility = page == DashboardPage.Status ? Visibility.Visible : Visibility.Collapsed;
        StatisticsPage.Visibility = page == DashboardPage.Statistics ? Visibility.Visible : Visibility.Collapsed;
        SettingsPage.Visibility = page == DashboardPage.Settings ? Visibility.Visible : Visibility.Collapsed;
        PrivacyPage.Visibility = page == DashboardPage.Privacy ? Visibility.Visible : Visibility.Collapsed;

        SetNavButtonState(StatusNavButton, page == DashboardPage.Status);
        SetNavButtonState(StatisticsNavButton, page == DashboardPage.Statistics);
        SetNavButtonState(SettingsNavButton, page == DashboardPage.Settings);
        SetNavButtonState(PrivacyNavButton, page == DashboardPage.Privacy);

        if (page == DashboardPage.Settings)
        {
            RefreshNotificationStateFromWindows();
            ShowSettingsSection(SettingsSection.ActivityCollection);
        }
    }

    private static void SetNavButtonState(System.Windows.Controls.Button button, bool isActive)
    {
        button.Background = new SolidColorBrush(isActive
            ? MediaColor.FromRgb(55, 65, 81)
            : MediaColor.FromArgb(0, 0, 0, 0));
        button.Foreground = new SolidColorBrush(isActive
            ? Colors.White
            : MediaColor.FromRgb(148, 163, 184));
    }

    private void ShowSettingsSection(SettingsSection section)
    {
        if (section == SettingsSection.FirewallControl)
        {
            RefreshNotificationStateFromWindows();
        }

        ActivityCollectionSettingsPanel.Visibility = section == SettingsSection.ActivityCollection
            ? Visibility.Visible
            : Visibility.Collapsed;
        AccountSecuritySettingsPanel.Visibility = section == SettingsSection.AccountSecurity
            ? Visibility.Visible
            : Visibility.Collapsed;
        FirewallControlSettingsPanel.Visibility = section == SettingsSection.FirewallControl
            ? Visibility.Visible
            : Visibility.Collapsed;

        SetSettingsMenuButtonState(ActivityCollectionSettingsButton, section == SettingsSection.ActivityCollection);
        SetSettingsMenuButtonState(AccountSecuritySettingsButton, section == SettingsSection.AccountSecurity);
        SetSettingsMenuButtonState(FirewallControlSettingsButton, section == SettingsSection.FirewallControl);
    }

    private void RefreshNotificationStateFromWindows()
    {
        bool notificationsAreSilenced = focusModeController.RefreshState();
        UpdateManualNotificationControls();
        UpdateLocalFocusModeIndicator();

        DesktopDiagnosticLogger.Log(
            "windows_notification_state_refreshed",
            new Dictionary<string, string?>
            {
                ["notificationsState"] = notificationsAreSilenced ? "off" : "on",
                ["source"] = "settings_navigation"
            });
    }

    private static void SetSettingsMenuButtonState(System.Windows.Controls.Button button, bool isActive)
    {
        button.Background = new SolidColorBrush(isActive
            ? MediaColor.FromRgb(37, 99, 235)
            : MediaColor.FromRgb(27, 38, 56));
        button.BorderBrush = new SolidColorBrush(isActive
            ? MediaColor.FromRgb(96, 165, 250)
            : MediaColor.FromRgb(51, 65, 85));
        button.Foreground = new SolidColorBrush(isActive
            ? Colors.White
            : MediaColor.FromRgb(203, 213, 225));
    }

    private async Task StartSessionAsync(LoginResponse response)
    {
        session = new DesktopSession(response.AccessToken, response.User);
        ShowLoggedInView(response.User.Email);
        ApplyDataCollectionState();
        await RefreshStatusAsync();
        await RefreshFirewallHistoryAsync();
        statusRefreshTimer.Start();
    }

    private void SetAuthButtonsEnabled(bool isEnabled)
    {
        LoginButton.IsEnabled = isEnabled;
        RegisterButton.IsEnabled = isEnabled;
        CreateAccountButton.IsEnabled = isEnabled;
        BackToLoginButton.IsEnabled = isEnabled;
    }

    private async void StatusRefreshTimer_Tick(object? sender, EventArgs e)
    {
        await RefreshStatusAsync();
    }

    private async void MetricsSendTimer_Tick(object? sender, EventArgs e)
    {
        await SendDesktopMetricsAsync();
    }

    private void MetricsPollingTimer_Tick(object? sender, EventArgs e)
    {
        if (agentSettings.IsDataCollectionEnabled)
        {
            metricsCollector.Poll();
        }
    }

    private async Task RefreshStatusAsync()
    {
        if (session is null || isRefreshingStatus)
        {
            return;
        }

        isRefreshingStatus = true;

        try
        {
            CurrentStatusResponse status = await apiClient.GetCurrentStatusAsync(
                session.AccessToken,
                CancellationToken.None);

            await ApplyAutomaticNotificationStateAsync(status.ShouldSilenceNotifications);
            UpdateStatusView(status);
            SetDesktopStatusMessage("Status synced with KungFlow server.");
        }
        catch (Exception ex)
        {
            SetDesktopStatusMessage(ex.Message, true);
        }
        finally
        {
            isRefreshingStatus = false;
        }
    }

    private async Task SendDesktopMetricsAsync()
    {
        if (session is null || isSendingMetrics)
        {
            return;
        }

        isSendingMetrics = true;

        try
        {
            DesktopMetricsSnapshot snapshot = metricsCollector.CaptureSnapshot(metricsCollectionWindow);
            UpdateCurrentMetricsView(snapshot);

            if (!snapshot.HasUserActivity)
            {
                SetDesktopStatusMessage("Desktop activity window skipped because no user activity was detected.");
                return;
            }

            MetricsResponse response = await apiClient.SendMetricsAsync(
                session.AccessToken,
                snapshot,
                CancellationToken.None);

            if (response.Status is not null)
            {
                await ApplyAutomaticNotificationStateAsync(response.Status.ShouldSilenceNotifications);
                UpdateStatusView(response.Status);
            }

            SetDesktopStatusMessage(
                response.Ignored == true
                    ? "Desktop activity window skipped as inactive."
                    : "Desktop activity synced with KungFlow server.");
        }
        catch (Exception ex)
        {
            SetDesktopStatusMessage($"Desktop metrics sync failed: {ex.Message}", true);
        }
        finally
        {
            isSendingMetrics = false;
        }
    }

    private void UpdateStatusView(CurrentStatusResponse status)
    {
        FirewallPresentation presentation = GetFirewallPresentation(status);

        StatusOrbEllipse.Fill = new SolidColorBrush(presentation.Color);
        StatusOrbEllipse.Stroke = new SolidColorBrush(presentation.LightColor);
        StatusBadgeTextBlock.Text = presentation.Badge;
        StatusBadgeTextBlock.Foreground = new SolidColorBrush(presentation.Color);
        StatusHeadlineTextBlock.Text = presentation.Headline;
        StatusBodyTextBlock.Text = presentation.Body;

        LoadStateTextBlock.Text = presentation.Badge;
        LoadStateTextBlock.Foreground = new SolidColorBrush(presentation.Color);
        ScoreTextBlock.Text = FormatNullableNumber(status.CognitiveLoadScore);
        BaselineTextBlock.Text = FormatNullableNumber(status.BaselineScore);
        BaselineProgressTextBlock.Text = FormatBaselineProgress(status);
        NotificationRecommendationTextBlock.Text =
            presentation.Action;
        NotificationRecommendationTextBlock.Foreground = new SolidColorBrush(
            presentation.Color);

        bool isFocusModeEnabled = focusModeController.IsEnabled();
        LocalFocusModeTextBlock.Text = isFocusModeEnabled ? "Active" : "Inactive";
        LocalFocusModeTextBlock.Foreground = new SolidColorBrush(
            isFocusModeEnabled
                ? MediaColor.FromRgb(220, 38, 38)
                : MediaColor.FromRgb(22, 163, 74));
        UpdateFirewallStatusSummary();

        LastStatusUpdateTextBlock.Text = DateTime.Now.ToString("HH:mm:ss");
    }

    private void ResetStatusView()
    {
        LoadStateTextBlock.Text = "Waiting";
        LoadStateTextBlock.Foreground = new SolidColorBrush(Colors.White);
        ScoreTextBlock.Text = "-";
        BaselineTextBlock.Text = "-";
        BaselineProgressTextBlock.Text = "-";
        NotificationRecommendationTextBlock.Text = "-";
        NotificationRecommendationTextBlock.Foreground = new SolidColorBrush(Colors.White);
        LocalFocusModeTextBlock.Text = "Inactive";
        LocalFocusModeTextBlock.Foreground = new SolidColorBrush(Colors.White);
        ResetFirewallStatusSummary();
        LastStatusUpdateTextBlock.Text = "Never";
        StatusOrbEllipse.Fill = new SolidColorBrush(MediaColor.FromRgb(148, 163, 184));
        StatusOrbEllipse.Stroke = new SolidColorBrush(MediaColor.FromRgb(226, 232, 240));
        StatusBadgeTextBlock.Text = "Waiting";
        StatusBadgeTextBlock.Foreground = new SolidColorBrush(MediaColor.FromRgb(51, 65, 85));
        StatusHeadlineTextBlock.Text = "KungFlow is waiting for activity data.";
        StatusBodyTextBlock.Text = "After the desktop agent collects enough computer activity, the firewall will decide whether interruptions should pass through.";
        ResetFirewallHistoryView();
    }

    private void DataCollectionEnabledCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (isLoadingSettings)
        {
            return;
        }

        agentSettings.IsDataCollectionEnabled = DataCollectionEnabledCheckBox.IsChecked == true;
        DesktopAgentSettingsStore.Save(agentSettings);
        DesktopDiagnosticLogger.Log(
            "desktop_activity_collection_setting_changed",
            new Dictionary<string, string?>
            {
                ["enabled"] = agentSettings.IsDataCollectionEnabled ? "true" : "false",
                ["hasSession"] = session is null ? "false" : "true"
            });
        ApplyDataCollectionState();
    }

    private void ApplySettingsToControls()
    {
        DataCollectionEnabledCheckBox.IsChecked = agentSettings.IsDataCollectionEnabled;
    }

    private async void ToggleNotificationsButton_Click(object sender, RoutedEventArgs e)
    {
        bool previousState = focusModeController.IsEnabled();
        bool shouldSilenceNotifications = !focusModeController.IsEnabled();

        DesktopDiagnosticLogger.Log(
            "manual_notification_toggle_started",
            new Dictionary<string, string?>
            {
                ["previousState"] = previousState ? "off" : "on",
                ["requestedState"] = shouldSilenceNotifications ? "off" : "on"
            });

        try
        {
            focusModeController.SetEnabled(shouldSilenceNotifications);
            SetManualNotificationOverride(shouldSilenceNotifications);
            UpdateManualNotificationControls();
            UpdateLocalFocusModeIndicator();
            bool currentState = focusModeController.IsEnabled();

            if (previousState != currentState)
            {
                await RecordFirewallEventAsync(currentState, "manual", "manual_toggle");
            }

            string message = shouldSilenceNotifications
                ? "KungFlow Firewall activated manually. Windows notifications are off."
                : "KungFlow Firewall deactivated manually. Windows notifications are on.";
            SetDesktopStatusMessage(message);
            DesktopDiagnosticLogger.Log(
                "manual_notification_toggle_succeeded",
                new Dictionary<string, string?>
                {
                    ["previousState"] = previousState ? "off" : "on",
                    ["newState"] = currentState ? "off" : "on",
                    ["message"] = message
                });
        }
        catch (Exception ex)
        {
            SetDesktopStatusMessage(ex.Message, true);
            DesktopDiagnosticLogger.Log(
                "manual_notification_toggle_failed",
                new Dictionary<string, string?>
                {
                    ["previousState"] = previousState ? "off" : "on",
                    ["requestedState"] = shouldSilenceNotifications ? "off" : "on",
                    ["errorType"] = ex.GetType().FullName,
                    ["message"] = ex.Message
                });
        }
    }

    private async void ResumeAutomaticControlButton_Click(object sender, RoutedEventArgs e)
    {
        bool hadManualOverride = manualNotificationOverride.HasValue;
        SetManualNotificationOverride(null);
        UpdateManualNotificationControls();

        DesktopDiagnosticLogger.Log(
            "manual_notification_override_cleared",
            new Dictionary<string, string?>
            {
                ["hadManualOverride"] = hadManualOverride ? "true" : "false",
                ["currentNotificationsState"] = focusModeController.IsEnabled() ? "off" : "on"
            });

        SetDesktopStatusMessage("KungFlow automatic notification control resumed.");
        await RefreshStatusAsync();
    }

    private async Task ApplyAutomaticNotificationStateAsync(bool shouldSilenceNotifications)
    {
        bool previousState = focusModeController.IsEnabled();
        bool requestedState = manualNotificationOverride ?? shouldSilenceNotifications;

        focusModeController.SetEnabled(requestedState);

        bool currentState = focusModeController.IsEnabled();

        if (previousState != currentState)
        {
            string controlMode = manualNotificationOverride.HasValue ? "manual" : "automatic";
            string reason = manualNotificationOverride.HasValue
                ? "manual_override_applied"
                : "server_recommendation";

            await RecordFirewallEventAsync(currentState, controlMode, reason);

            DesktopDiagnosticLogger.Log(
                "automatic_notification_state_applied",
                new Dictionary<string, string?>
                {
                    ["serverRecommendation"] = shouldSilenceNotifications ? "off" : "on",
                    ["appliedState"] = currentState ? "off" : "on",
                    ["controlMode"] = manualNotificationOverride.HasValue
                        ? "manual override"
                        : "automatic control"
                });
        }

        UpdateManualNotificationControls();
        UpdateLocalFocusModeIndicator();
    }

    private void UpdateManualNotificationControls()
    {
        bool notificationsAreSilenced = focusModeController.IsEnabled();
        ToggleNotificationsButton.Content = notificationsAreSilenced
            ? "Deactivate firewall"
            : "Activate firewall";
        string controlMode = manualNotificationOverride.HasValue
            ? "manual override"
            : "automatic control";
        ManualNotificationStatusTextBlock.Text =
            notificationsAreSilenced
                ? $"Firewall is active. Windows notifications are off ({controlMode})"
                : $"Firewall is inactive. Windows notifications are on ({controlMode})";
        ResumeAutomaticControlButton.IsEnabled = manualNotificationOverride.HasValue;
        ManualNotificationStatusTextBlock.Foreground = new SolidColorBrush(
            notificationsAreSilenced
                ? MediaColor.FromRgb(248, 113, 113)
                : MediaColor.FromRgb(74, 222, 128));
        UpdateFirewallStatusSummary();
    }

    private void ResetFirewallStatusSummary()
    {
        FirewallStatusSummaryBorder.BorderBrush = new SolidColorBrush(MediaColor.FromRgb(51, 65, 85));
        FirewallStatusSummaryTextBlock.Text = "Firewall inactive - notifications can pass through";
        FirewallStatusSummaryTextBlock.Foreground = new SolidColorBrush(Colors.White);
        FirewallStatusDetailTextBlock.Text = "Automatic protection";
        FirewallStatusSourceTextBlock.Text = "Automatic";
        FirewallStatusSourceTextBlock.Foreground = new SolidColorBrush(MediaColor.FromRgb(203, 213, 225));
        FirewallStatusSourceBadgeBorder.BorderBrush = new SolidColorBrush(MediaColor.FromRgb(71, 85, 105));
    }

    private void UpdateFirewallStatusSummary()
    {
        bool isFirewallActive = focusModeController.IsEnabled();
        bool isManualOverride = manualNotificationOverride.HasValue;
        MediaColor statusColor = isFirewallActive
            ? MediaColor.FromRgb(248, 113, 113)
            : MediaColor.FromRgb(74, 222, 128);
        MediaColor borderColor = isFirewallActive
            ? MediaColor.FromRgb(127, 29, 29)
            : MediaColor.FromRgb(22, 101, 52);

        FirewallStatusSummaryBorder.BorderBrush = new SolidColorBrush(borderColor);
        FirewallStatusSummaryTextBlock.Text = isFirewallActive
            ? "Firewall active - notifications are blocked"
            : "Firewall inactive - notifications can pass through";
        FirewallStatusSummaryTextBlock.Foreground = new SolidColorBrush(statusColor);
        FirewallStatusDetailTextBlock.Text = isManualOverride
            ? "Manual override is controlling notification protection."
            : "Automatic protection follows KungFlow's overload detection.";
        FirewallStatusSourceTextBlock.Text = isManualOverride ? "Manual" : "Automatic";
        FirewallStatusSourceTextBlock.Foreground = new SolidColorBrush(statusColor);
        FirewallStatusSourceBadgeBorder.BorderBrush = new SolidColorBrush(borderColor);
    }

    private void UpdateLocalFocusModeIndicator()
    {
        bool isFocusModeEnabled = focusModeController.IsEnabled();
        LocalFocusModeTextBlock.Text = isFocusModeEnabled ? "Active" : "Inactive";
        LocalFocusModeTextBlock.Foreground = new SolidColorBrush(
            isFocusModeEnabled
                ? MediaColor.FromRgb(220, 38, 38)
                : MediaColor.FromRgb(22, 163, 74));
        UpdateFirewallStatusSummary();
    }

    private void SetManualNotificationOverride(bool? value)
    {
        manualNotificationOverride = value;
        agentSettings.Firewall.ManualNotificationOverride = value;
        DesktopAgentSettingsStore.Save(agentSettings);
    }

    private async Task RecordFirewallEventAsync(
        bool notificationsSilenced,
        string controlMode,
        string reason)
    {
        if (session is null)
        {
            return;
        }

        try
        {
            await apiClient.SendFirewallEventAsync(
                session.AccessToken,
                notificationsSilenced,
                controlMode,
                reason,
                CancellationToken.None);

            DesktopDiagnosticLogger.Log(
                "firewall_event_sent",
                new Dictionary<string, string?>
                {
                    ["notificationsState"] = notificationsSilenced ? "off" : "on",
                    ["controlMode"] = controlMode,
                    ["reason"] = reason
                });

            await RefreshFirewallHistoryAsync();
        }
        catch (Exception ex)
        {
            DesktopDiagnosticLogger.Log(
                "firewall_event_send_failed",
                new Dictionary<string, string?>
                {
                    ["notificationsState"] = notificationsSilenced ? "off" : "on",
                    ["controlMode"] = controlMode,
                    ["reason"] = reason,
                    ["errorType"] = ex.GetType().FullName,
                    ["message"] = ex.Message
                });
        }
    }

    private async void RefreshFirewallHistoryButton_Click(object sender, RoutedEventArgs e)
    {
        await RefreshFirewallHistoryAsync();
    }

    private async Task RefreshFirewallHistoryAsync()
    {
        if (session is null || isRefreshingFirewallHistory)
        {
            return;
        }

        isRefreshingFirewallHistory = true;
        RefreshFirewallHistoryButton.IsEnabled = false;

        try
        {
            FirewallEventsResponse response = await apiClient.GetFirewallEventsAsync(
                session.AccessToken,
                100,
                CancellationToken.None);

            UpdateFirewallHistoryView(response.Events);
        }
        catch (Exception ex)
        {
            FirewallLatestActionTextBlock.Text = "History unavailable";
            FirewallLatestControlModeTextBlock.Text = "Server sync failed";
            DesktopDiagnosticLogger.Log(
                "firewall_history_refresh_failed",
                new Dictionary<string, string?>
                {
                    ["errorType"] = ex.GetType().FullName,
                    ["message"] = ex.Message
                });
        }
        finally
        {
            isRefreshingFirewallHistory = false;
            RefreshFirewallHistoryButton.IsEnabled = true;
        }
    }

    private void UpdateFirewallHistoryView(IReadOnlyList<FirewallEventResponse> events)
    {
        DateTime today = DateTime.Now.Date;
        int activationsToday = events.Count(firewallEvent =>
            firewallEvent.EventType == "activated" &&
            firewallEvent.OccurredAt.ToLocalTime().Date == today);

        FirewallActivationsTodayTextBlock.Text = activationsToday.ToString();

        FirewallEventResponse? latestEvent = events.FirstOrDefault();

        if (latestEvent is null)
        {
            ResetFirewallHistoryView();
            return;
        }

        FirewallLatestActionTextBlock.Text = FormatFirewallEventAction(latestEvent);
        FirewallLatestControlModeTextBlock.Text = FormatControlMode(latestEvent.ControlMode);

        RecentFirewallEventsPanel.Children.Clear();

        foreach (FirewallEventResponse firewallEvent in events.Take(10))
        {
            RecentFirewallEventsPanel.Children.Add(CreateFirewallEventRow(firewallEvent));
        }
    }

    private void ResetFirewallHistoryView()
    {
        FirewallActivationsTodayTextBlock.Text = "0";
        FirewallLatestActionTextBlock.Text = "No events yet";
        FirewallLatestControlModeTextBlock.Text = "-";
        RecentFirewallEventsPanel.Children.Clear();
        RecentFirewallEventsPanel.Children.Add(new TextBlock
        {
            Text = "No firewall events recorded yet.",
            Foreground = new SolidColorBrush(MediaColor.FromRgb(148, 163, 184)),
            TextWrapping = TextWrapping.Wrap
        });
    }

    private static Border CreateFirewallEventRow(FirewallEventResponse firewallEvent)
    {
        bool isActivated = firewallEvent.EventType == "activated";
        MediaColor actionColor = isActivated
            ? MediaColor.FromRgb(248, 113, 113)
            : MediaColor.FromRgb(74, 222, 128);

        Grid grid = new();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        StackPanel detailsPanel = new();
        detailsPanel.Children.Add(new TextBlock
        {
            Text = isActivated ? "Firewall activated" : "Firewall deactivated",
            Foreground = new SolidColorBrush(actionColor),
            FontWeight = FontWeights.Bold,
            FontSize = 14
        });
        detailsPanel.Children.Add(new TextBlock
        {
            Text = $"{FormatControlMode(firewallEvent.ControlMode)} - {FormatReason(firewallEvent.Reason)}",
            Foreground = new SolidColorBrush(MediaColor.FromRgb(175, 192, 212)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 3, 14, 0)
        });

        TextBlock timeTextBlock = new()
        {
            Text = FormatFirewallEventDateTime(firewallEvent.OccurredAt),
            Foreground = new SolidColorBrush(MediaColor.FromRgb(148, 163, 184)),
            FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center
        };

        Grid.SetColumn(detailsPanel, 0);
        Grid.SetColumn(timeTextBlock, 1);
        grid.Children.Add(detailsPanel);
        grid.Children.Add(timeTextBlock);

        return new Border
        {
            Background = new SolidColorBrush(MediaColor.FromRgb(27, 38, 56)),
            BorderBrush = new SolidColorBrush(MediaColor.FromRgb(51, 65, 85)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 8),
            Child = grid
        };
    }

    private static string FormatFirewallEventAction(FirewallEventResponse firewallEvent)
    {
        string action = firewallEvent.EventType == "activated"
            ? "Activated"
            : "Deactivated";

        return $"{action} on {FormatFirewallEventDateTime(firewallEvent.OccurredAt)}";
    }

    private static string FormatFirewallEventDateTime(DateTimeOffset occurredAt)
    {
        return occurredAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
    }

    private static string FormatControlMode(string controlMode)
    {
        return controlMode == "manual"
            ? "Manual control"
            : "Automatic control";
    }

    private static string FormatReason(string? reason)
    {
        return string.IsNullOrWhiteSpace(reason)
            ? "No reason"
            : reason.Replace("_", " ");
    }

    private void ApplyDataCollectionState()
    {
        if (session is null)
        {
            return;
        }

        if (agentSettings.IsDataCollectionEnabled)
        {
            metricsCollector.Start();
            metricsPollingTimer.Start();
            metricsSendTimer.Start();
            SetDesktopStatusMessage("Desktop activity collection is enabled.");
            DesktopDiagnosticLogger.Log(
                "desktop_activity_collection_started",
                new Dictionary<string, string?>
                {
                    ["metricsPollingTimer"] = metricsPollingTimer.IsEnabled ? "running" : "stopped",
                    ["metricsSendTimer"] = metricsSendTimer.IsEnabled ? "running" : "stopped"
                });
            return;
        }

        metricsSendTimer.Stop();
        metricsPollingTimer.Stop();
        metricsCollector.Stop();
        SetDesktopStatusMessage("Desktop activity collection is disabled.");
        DesktopDiagnosticLogger.Log(
            "desktop_activity_collection_stopped",
            new Dictionary<string, string?>
            {
                ["metricsPollingTimer"] = metricsPollingTimer.IsEnabled ? "running" : "stopped",
                ["metricsSendTimer"] = metricsSendTimer.IsEnabled ? "running" : "stopped"
            });
    }

    private void UpdateCurrentMetricsView(DesktopMetricsSnapshot snapshot)
    {
        CurrentOpenWindowsTextBlock.Text = snapshot.OpenWindowsCount.ToString();
        CurrentWindowSwitchesTextBlock.Text = snapshot.WindowSwitchCount.ToString();
        CurrentKeyPressesTextBlock.Text = snapshot.KeyPressCount.ToString();
        CurrentDeleteKeyTextBlock.Text = snapshot.DeleteKeyCount.ToString();
        CurrentTypingSpeedTextBlock.Text = snapshot.TypingSpeed.ToString("0.0");
        CurrentMouseSpeedTextBlock.Text = snapshot.MouseSpeed.ToString("0.0");
    }

    private static FirewallPresentation GetFirewallPresentation(CurrentStatusResponse status)
    {
        if (status.State == "overloaded")
        {
            return new FirewallPresentation(
                "Firewall active",
                "KungFlow detected high cognitive load.",
                "The notification firewall is protecting you from unnecessary interruptions.",
                "Reduce interruptions",
                MediaColor.FromRgb(220, 38, 38),
                MediaColor.FromRgb(254, 202, 202));
        }

        if (status.State == "collecting_baseline" || status.State == "no_metrics")
        {
            return new FirewallPresentation(
                "Calibrating",
                "KungFlow is learning your normal work rhythm.",
                "The firewall is not fully active yet. Keep working normally so the baseline becomes more accurate.",
                "Learning",
                MediaColor.FromRgb(234, 179, 8),
                MediaColor.FromRgb(254, 240, 138));
        }

        return new FirewallPresentation(
            "Open",
            "KungFlow does not detect cognitive overload.",
            "Notifications can pass through because you currently appear to have enough focus capacity.",
            "No action",
            MediaColor.FromRgb(22, 163, 74),
            MediaColor.FromRgb(187, 247, 208));
    }

    private static string FormatNullableNumber(double? value)
    {
        return value.HasValue ? value.Value.ToString("0.##") : "-";
    }

    private static string FormatBaselineProgress(CurrentStatusResponse status)
    {
        if (!status.BaselineSamplesCollected.HasValue || !status.BaselineSamplesRequired.HasValue)
        {
            return "-";
        }

        return $"{status.BaselineSamplesCollected}/{status.BaselineSamplesRequired}";
    }

    private void SetMessage(string message, bool isError = false)
    {
        MessageTextBlock.Text = message;
        MessageTextBlock.Foreground = new SolidColorBrush(
            isError ? MediaColor.FromRgb(248, 113, 113) : MediaColor.FromRgb(175, 192, 212));
    }

    private void SetRegisterMessage(string message, bool isError = false)
    {
        RegisterMessageTextBlock.Text = message;
        RegisterMessageTextBlock.Foreground = new SolidColorBrush(
            isError ? MediaColor.FromRgb(248, 113, 113) : MediaColor.FromRgb(175, 192, 212));
    }

    private void SetChangePasswordMessage(string message, bool isError = false)
    {
        ChangePasswordMessageTextBlock.Text = message;
        ChangePasswordMessageTextBlock.Foreground = new SolidColorBrush(
            isError ? MediaColor.FromRgb(248, 113, 113) : MediaColor.FromRgb(175, 192, 212));
    }

    private void SetDesktopStatusMessage(string message, bool isError = false)
    {
        DesktopStatusMessageTextBlock.Text = message;
        DesktopStatusMessageTextBlock.Foreground = new SolidColorBrush(
            isError ? MediaColor.FromRgb(248, 113, 113) : MediaColor.FromRgb(148, 163, 184));

        if (!string.IsNullOrWhiteSpace(message))
        {
            DesktopDiagnosticLogger.Log(
                "desktop_status_message",
                new Dictionary<string, string?>
                {
                    ["isError"] = isError ? "true" : "false",
                    ["message"] = message
                });
        }
    }

    private void ConfigureTrayIcon()
    {
        Forms.ToolStripMenuItem openMenuItem = new("Open KungFlow");
        openMenuItem.Click += (_, _) => ShowFromTray();

        Forms.ToolStripMenuItem exitMenuItem = new("Exit");
        exitMenuItem.Click += (_, _) => ExitFromTray();

        trayIcon.Text = "KungFlow";
        trayIcon.Icon = CreateTrayIcon();
        trayIcon.ContextMenuStrip = new Forms.ContextMenuStrip();
        trayIcon.ContextMenuStrip.Items.Add(openMenuItem);
        trayIcon.ContextMenuStrip.Items.Add(exitMenuItem);
        trayIcon.DoubleClick += (_, _) => ShowFromTray();
        trayIcon.Visible = true;
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void ExitFromTray()
    {
        isExitRequested = true;
        trayIcon.Visible = false;
        Close();
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);

        if (WindowState == WindowState.Minimized)
        {
            Hide();
        }
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!isExitRequested)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        metricsCollector.Dispose();
        trayIcon.Dispose();
        base.OnClosed(e);
    }

    private static System.Drawing.Icon CreateTrayIcon()
    {
        var resourceInfo = System.Windows.Application.GetResourceStream(
            new Uri("pack://application:,,,/Assets/icon128.png"));

        if (resourceInfo is null)
        {
            return System.Drawing.SystemIcons.Application;
        }

        using var bitmap = new System.Drawing.Bitmap(resourceInfo.Stream);
        IntPtr iconHandle = bitmap.GetHicon();

        try
        {
            using var icon = System.Drawing.Icon.FromHandle(iconHandle);
            return (System.Drawing.Icon)icon.Clone();
        }
        finally
        {
            DestroyIcon(iconHandle);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}

internal sealed record FirewallPresentation(
    string Badge,
    string Headline,
    string Body,
    string Action,
    MediaColor Color,
    MediaColor LightColor);

internal enum DashboardPage
{
    Status,
    Statistics,
    Settings,
    Privacy
}

internal enum SettingsSection
{
    ActivityCollection,
    AccountSecurity,
    FirewallControl
}
