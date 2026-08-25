using KungFlow.Desktop.Agent;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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
    private readonly ApplicationNotificationFirewallController applicationFirewallController = new();
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
    private bool currentFirewallActiveState;
    private string latestLoadState = "waiting";
    private IReadOnlyList<FirewallTarget> availableFirewallTargets = [];

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
        currentFirewallActiveState = agentSettings.Firewall.UseGlobalNotificationFirewall
            ? focusModeController.IsEnabled()
            : manualNotificationOverride ?? false;
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
        ApplyFirewallState(false);
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
        if (agentSettings.Firewall.UseGlobalNotificationFirewall)
        {
            currentFirewallActiveState = notificationsAreSilenced;
        }

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
        latestLoadState = status.State;
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
        UpdateLocalFocusModeIndicator();

        LastStatusUpdateTextBlock.Text = DateTime.Now.ToString("HH:mm:ss");
    }

    private void ResetStatusView()
    {
        latestLoadState = "waiting";
        LoadStateTextBlock.Text = "Waiting";
        LoadStateTextBlock.Foreground = new SolidColorBrush(Colors.White);
        ScoreTextBlock.Text = "-";
        BaselineTextBlock.Text = "-";
        BaselineProgressTextBlock.Text = "-";
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
        GlobalFirewallModeRadioButton.IsChecked = agentSettings.Firewall.UseGlobalNotificationFirewall;
        SelectiveFirewallModeRadioButton.IsChecked = !agentSettings.Firewall.UseGlobalNotificationFirewall;
        RenderFirewallTargetControls();
        UpdateFirewallModeControls();
    }

    private void RenderFirewallTargetControls()
    {
        FirewallTargetsPanel.Children.Clear();
        RefreshAvailableFirewallTargets();

        if (availableFirewallTargets.Count == 0)
        {
            UpdateFirewallTargetAvailabilityText();
            FirewallTargetsPanel.Children.Add(new TextBlock
            {
                Text = "No supported app notification entries were found yet. Open a supported app once, or let it send a notification, then return here.",
                Foreground = new SolidColorBrush(MediaColor.FromRgb(175, 192, 212)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10)
            });
            return;
        }

        foreach (FirewallTarget target in availableFirewallTargets)
        {
            System.Windows.Controls.CheckBox checkBox = new()
            {
                Content = CreateFirewallTargetContent(target),
                Tag = target.Id,
                IsChecked = agentSettings.Firewall.IsApplicationMuted(target.Id),
                Foreground = new SolidColorBrush(MediaColor.FromRgb(226, 232, 240)),
                HorizontalContentAlignment = System.Windows.HorizontalAlignment.Stretch,
                Margin = new Thickness(0, 0, 0, 10),
                ToolTip = target.Description
            };
            checkBox.Checked += FirewallTargetCheckBox_Changed;
            checkBox.Unchecked += FirewallTargetCheckBox_Changed;
            FirewallTargetsPanel.Children.Add(checkBox);
        }

        UpdateFirewallTargetAvailabilityText();
    }

    private static Border CreateFirewallTargetContent(FirewallTarget target)
    {
        Grid contentGrid = new()
        {
            Margin = new Thickness(8, 0, 0, 0)
        };
        contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        Border badge = new()
        {
            Width = 38,
            Height = 38,
            CornerRadius = new CornerRadius(19),
            Background = new SolidColorBrush(MediaColor.FromRgb(15, 23, 42)),
            BorderBrush = new SolidColorBrush(GetFirewallTargetBadgeColor(target.Id)),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 0, 12, 0),
            Child = CreateFirewallTargetLogo(target)
        };

        StackPanel copyStack = new();
        copyStack.Children.Add(new TextBlock
        {
            Text = target.DisplayName,
            Foreground = System.Windows.Media.Brushes.White,
            FontSize = 14,
            FontWeight = FontWeights.Bold
        });
        copyStack.Children.Add(new TextBlock
        {
            Text = target.Description,
            Foreground = new SolidColorBrush(MediaColor.FromRgb(175, 192, 212)),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 3, 0, 0)
        });

        Grid.SetColumn(badge, 0);
        Grid.SetColumn(copyStack, 1);
        contentGrid.Children.Add(badge);
        contentGrid.Children.Add(copyStack);

        return new Border
        {
            Background = new SolidColorBrush(MediaColor.FromRgb(23, 32, 51)),
            BorderBrush = new SolidColorBrush(MediaColor.FromRgb(51, 65, 85)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12),
            Child = contentGrid
        };
    }

    private static FrameworkElement CreateFirewallTargetLogo(FirewallTarget target)
    {
        string? logoFile = GetFirewallTargetLogoFile(target.Id);
        if (logoFile is null)
        {
            return new TextBlock
            {
                Text = "APP",
                Foreground = System.Windows.Media.Brushes.White,
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        return new System.Windows.Controls.Image
        {
            Source = new BitmapImage(new Uri($"Assets/{logoFile}", UriKind.Relative)),
            Width = 27,
            Height = 27,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
    }

    private static string? GetFirewallTargetLogoFile(string targetId)
    {
        return targetId switch
        {
            "whatsapp" => "whatsapp-logo.png",
            "outlook" => "outlook-logo.png",
            "teams" => "teams-logo.png",
            "slack" => "slack-logo.png",
            "discord" => "discord-logo.png",
            "chrome" => "chrome-logo.png",
            "edge" => "edge-logo.png",
            _ => null
        };
    }

    private static MediaColor GetFirewallTargetBadgeColor(string targetId)
    {
        return targetId switch
        {
            "whatsapp" => MediaColor.FromRgb(22, 163, 74),
            "outlook" => MediaColor.FromRgb(37, 99, 235),
            "teams" => MediaColor.FromRgb(99, 102, 241),
            "slack" => MediaColor.FromRgb(14, 165, 233),
            "discord" => MediaColor.FromRgb(88, 101, 242),
            "chrome" => MediaColor.FromRgb(234, 179, 8),
            "edge" => MediaColor.FromRgb(6, 182, 212),
            _ => MediaColor.FromRgb(15, 118, 110)
        };
    }

    private void FirewallModeRadioButton_Checked(object sender, RoutedEventArgs e)
    {
        if (isLoadingSettings)
        {
            return;
        }

        bool wasUsingGlobalFirewall = agentSettings.Firewall.UseGlobalNotificationFirewall;
        bool shouldUseGlobalFirewall = GlobalFirewallModeRadioButton.IsChecked == true;

        if (!shouldUseGlobalFirewall && focusModeController.RefreshState())
        {
            isLoadingSettings = true;
            GlobalFirewallModeRadioButton.IsChecked = true;
            SelectiveFirewallModeRadioButton.IsChecked = false;
            isLoadingSettings = false;
            currentFirewallActiveState = true;
            UpdateManualNotificationControls();
            UpdateLocalFocusModeIndicator();
            SetDesktopStatusMessage(
                "Selective mode requires Windows Notifications to be on. Deactivate the global firewall first, then choose selective mode.",
                true);
            return;
        }

        try
        {
            if (!wasUsingGlobalFirewall && shouldUseGlobalFirewall)
            {
                applicationFirewallController.SetApplicationsSilenced(
                    RefreshAvailableFirewallTargets(),
                    agentSettings.Firewall.MutedApplicationIds,
                    shouldSilence: false);
            }

            agentSettings.Firewall.UseGlobalNotificationFirewall = shouldUseGlobalFirewall;
            DesktopAgentSettingsStore.Save(agentSettings);
            UpdateFirewallModeControls();
            ApplyFirewallState(currentFirewallActiveState);
            UpdateManualNotificationControls();
            UpdateLocalFocusModeIndicator();
            SetDesktopStatusMessage("Firewall mode updated.");
        }
        catch (Exception ex)
        {
            SetDesktopStatusMessage(ex.Message, true);
        }
    }

    private void FirewallTargetCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (isLoadingSettings ||
            sender is not System.Windows.Controls.CheckBox checkBox ||
            checkBox.Tag is not string applicationId)
        {
            return;
        }

        agentSettings.Firewall.SetApplicationMuted(applicationId, checkBox.IsChecked == true);
        DesktopAgentSettingsStore.Save(agentSettings);
        UpdateFirewallModeControls();

        if (!agentSettings.Firewall.UseGlobalNotificationFirewall && currentFirewallActiveState)
        {
            try
            {
                ApplyFirewallState(true);
                UpdateManualNotificationControls();
                UpdateLocalFocusModeIndicator();
                SetDesktopStatusMessage("Selective firewall targets updated.");
            }
            catch (Exception ex)
            {
                SetDesktopStatusMessage(ex.Message, true);
            }
        }
    }

    private void UpdateFirewallModeControls()
    {
        bool usesGlobalFirewall = agentSettings.Firewall.UseGlobalNotificationFirewall;
        FirewallModeStatusTextBlock.Text = usesGlobalFirewall
            ? "Global mode is active. KungFlow turns off all Windows notifications when the firewall is active."
            : "Selective mode is active. KungFlow only changes notification settings for selected apps when Windows exposes matching app entries.";

        FirewallTargetStatusTextBlock.Text = usesGlobalFirewall
            ? "Selected apps are saved for selective mode, but global mode currently protects all notifications."
            : BuildSelectedFirewallTargetsText();

        FirewallTargetsPanel.IsEnabled = !usesGlobalFirewall;
        FirewallTargetsPanel.Opacity = usesGlobalFirewall ? 0.62 : 1.0;
    }

    private string BuildSelectedFirewallTargetsText()
    {
        if (availableFirewallTargets.Count == 0)
        {
            return "No supported app notification entries are currently available for selective mode.";
        }

        List<string> selectedTargets = availableFirewallTargets
            .Where(target => agentSettings.Firewall.IsApplicationMuted(target.Id))
            .Select(target => target.DisplayName)
            .ToList();

        return selectedTargets.Count == 0
            ? "No app targets selected. Choose at least one app or use global mode."
            : $"Selected app targets: {string.Join(", ", selectedTargets)}.";
    }

    private void UpdateFirewallTargetAvailabilityText()
    {
        int totalSupportedTargets = FirewallTargetCatalog.Defaults.Count;
        int availableTargetCount = availableFirewallTargets.Count;

        FirewallTargetAvailabilityTextBlock.Text = availableTargetCount == 0
            ? $"Showing 0 of {totalSupportedTargets} supported apps on this computer."
            : $"Showing {availableTargetCount} of {totalSupportedTargets} supported apps available on this computer.";
    }

    private IReadOnlyList<FirewallTarget> RefreshAvailableFirewallTargets()
    {
        availableFirewallTargets = applicationFirewallController.GetAvailableTargets(FirewallTargetCatalog.Defaults);
        return availableFirewallTargets;
    }

    private async void ToggleNotificationsButton_Click(object sender, RoutedEventArgs e)
    {
        bool previousState = IsFirewallActive();
        bool shouldSilenceNotifications = !IsFirewallActive();

        DesktopDiagnosticLogger.Log(
            "manual_notification_toggle_started",
            new Dictionary<string, string?>
            {
                ["previousState"] = previousState ? "off" : "on",
                ["requestedState"] = shouldSilenceNotifications ? "off" : "on"
            });

        try
        {
            ApplyFirewallState(shouldSilenceNotifications);
            SetManualNotificationOverride(shouldSilenceNotifications);
            UpdateManualNotificationControls();
            UpdateLocalFocusModeIndicator();
            bool currentState = IsFirewallActive();

            if (previousState != currentState)
            {
                await RecordFirewallEventAsync(currentState, "manual", BuildFirewallReason("manual_toggle"));
            }

            string message = shouldSilenceNotifications
                ? $"KungFlow Firewall activated manually ({GetFirewallModeLabel()})."
                : $"KungFlow Firewall deactivated manually ({GetFirewallModeLabel()}).";
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
                ["currentNotificationsState"] = IsFirewallActive() ? "off" : "on"
            });

        SetDesktopStatusMessage("KungFlow automatic notification control resumed.");
        await RefreshStatusAsync();
    }

    private async Task ApplyAutomaticNotificationStateAsync(bool shouldSilenceNotifications)
    {
        bool previousState = IsFirewallActive();
        bool requestedState = manualNotificationOverride ?? shouldSilenceNotifications;

        if (previousState == requestedState)
        {
            DesktopDiagnosticLogger.Log(
                "automatic_notification_state_skipped",
                new Dictionary<string, string?>
                {
                    ["serverRecommendation"] = shouldSilenceNotifications ? "off" : "on",
                    ["requestedState"] = requestedState ? "off" : "on",
                    ["currentState"] = previousState ? "off" : "on",
                    ["controlMode"] = manualNotificationOverride.HasValue
                        ? "manual override"
                        : "automatic control",
                    ["reason"] = "state_unchanged"
                });

            UpdateManualNotificationControls();
            UpdateLocalFocusModeIndicator();
            return;
        }

        ApplyFirewallState(requestedState);

        bool currentState = IsFirewallActive();

        if (previousState != currentState)
        {
            string controlMode = manualNotificationOverride.HasValue ? "manual" : "automatic";
            string reason = manualNotificationOverride.HasValue
                ? BuildFirewallReason("manual_override_applied")
                : BuildFirewallReason("server_recommendation");

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

    private bool IsFirewallActive()
    {
        return currentFirewallActiveState;
    }

    private void ApplyFirewallState(bool shouldActivate)
    {
        IReadOnlyList<FirewallTarget> targets = RefreshAvailableFirewallTargets();

        if (agentSettings.Firewall.UseGlobalNotificationFirewall)
        {
            focusModeController.SetEnabled(shouldActivate);
            currentFirewallActiveState = focusModeController.IsEnabled();
            FirewallTargetStatusTextBlock.Text = shouldActivate
                ? "Global firewall is active. All Windows notifications are off."
                : "Global firewall is inactive. Windows notifications are on.";
        }
        else
        {
            if (shouldActivate && !targets.Any(target => agentSettings.Firewall.IsApplicationMuted(target.Id)))
            {
                throw new InvalidOperationException(
                    "Selective firewall has no selected app targets. Choose at least one app or switch to global mode.");
            }

            focusModeController.SetEnabled(false);

            ApplicationFirewallApplyResult result = applicationFirewallController.SetApplicationsSilenced(
                targets,
                agentSettings.Firewall.MutedApplicationIds,
                shouldActivate);

            if (shouldActivate && !result.HasUpdates)
            {
                throw new InvalidOperationException(
                    $"{result.Summary} Open Windows Settings > System > Notifications, toggle the target app once, then try again.");
            }

            currentFirewallActiveState = shouldActivate && result.HasUpdates;
            FirewallTargetStatusTextBlock.Text = result.Summary;
        }
    }

    private string GetFirewallModeLabel()
    {
        return agentSettings.Firewall.UseGlobalNotificationFirewall
            ? "global mode"
            : "selective app mode";
    }

    private string BuildFirewallReason(string reason)
    {
        return agentSettings.Firewall.UseGlobalNotificationFirewall
            ? $"{reason}_global"
            : $"{reason}_selective";
    }

    private void UpdateManualNotificationControls()
    {
        bool notificationsAreSilenced = IsFirewallActive();
        ToggleNotificationsButton.Content = notificationsAreSilenced
            ? "Deactivate firewall manually"
            : "Activate firewall manually";
        bool isManualOverride = manualNotificationOverride.HasValue;
        string controlMode = isManualOverride ? "manual override" : "automatic control";
        ManualNotificationStatusTextBlock.Text =
            notificationsAreSilenced
                ? $"Firewall is active in {GetFirewallModeLabel()} ({controlMode})"
                : $"Firewall is inactive in {GetFirewallModeLabel()} ({controlMode})";
        ResumeAutomaticControlButton.IsEnabled = isManualOverride;
        ResumeAutomaticControlButton.Content = isManualOverride
            ? "Resume automatic control"
            : "Automatic control active";
        ManualNotificationStatusTextBlock.Foreground = new SolidColorBrush(
            notificationsAreSilenced
                ? MediaColor.FromRgb(248, 113, 113)
                : MediaColor.FromRgb(74, 222, 128));
        FirewallControlModeStatusBorder.BorderBrush = new SolidColorBrush(
            isManualOverride
                ? MediaColor.FromRgb(234, 179, 8)
                : MediaColor.FromRgb(37, 99, 235));
        FirewallControlModeTitleTextBlock.Text = isManualOverride
            ? "Manual override is active"
            : "Automatic control active (recommended)";
        FirewallControlModeTitleTextBlock.Foreground = new SolidColorBrush(
            isManualOverride
                ? MediaColor.FromRgb(250, 204, 21)
                : Colors.White);
        FirewallControlModeDetailTextBlock.Text = isManualOverride
            ? "KungFlow will keep the current firewall state until you resume automatic control."
            : "Recommended: KungFlow activates the firewall when cognitive load rises and restores notifications when load returns to normal.";
        UpdateFirewallStatusSummary();
    }

    private void ResetFirewallStatusSummary()
    {
        FirewallStatusSummaryBorder.BorderBrush = new SolidColorBrush(MediaColor.FromRgb(51, 65, 85));
        FirewallShieldIconBorder.Background = new SolidColorBrush(MediaColor.FromRgb(51, 65, 85));
        FirewallShieldPath.Fill = new SolidColorBrush(Colors.White);
        FirewallShieldBreakPath.Visibility = Visibility.Collapsed;
        FirewallProtectionBadgeBorder.Background = new SolidColorBrush(MediaColor.FromRgb(31, 41, 55));
        FirewallProtectionBadgeBorder.BorderBrush = new SolidColorBrush(MediaColor.FromRgb(71, 85, 105));
        FirewallProtectionBadgeTextBlock.Text = "Waiting";
        FirewallProtectionBadgeTextBlock.Foreground = new SolidColorBrush(MediaColor.FromRgb(203, 213, 225));
        FirewallStatusSummaryTextBlock.Text = "KungFlow is waiting for activity data.";
        FirewallStatusSummaryTextBlock.Foreground = new SolidColorBrush(Colors.White);
        FirewallStatusDetailTextBlock.Text = "Once enough activity is collected, KungFlow will decide whether the firewall should protect your focus.";
        FirewallStatusModeTextBlock.Text = GetFirewallModeDisplayName();
        FirewallStatusProtectedAppsTextBlock.Text = BuildFirewallProtectedAppsSummary();
        FirewallStatusSourceTextBlock.Text = "Automatic";
        FirewallStatusSourceTextBlock.Foreground = new SolidColorBrush(MediaColor.FromRgb(203, 213, 225));
        FirewallStatusSourceBadgeBorder.BorderBrush = new SolidColorBrush(MediaColor.FromRgb(71, 85, 105));
    }

    private void UpdateFirewallStatusSummary()
    {
        bool isFirewallActive = IsFirewallActive();
        bool isManualOverride = manualNotificationOverride.HasValue;
        bool isOverloaded = latestLoadState == "overloaded";
        bool isLearning = latestLoadState is "collecting_baseline" or "no_metrics" or "waiting";
        bool shouldShowBrokenShield = !isFirewallActive && isOverloaded;
        MediaColor statusColor;
        MediaColor borderColor;
        MediaColor badgeBackground;
        MediaColor badgeBorder;
        string badgeText;
        string summary;
        string detail;

        if (isFirewallActive)
        {
            statusColor = MediaColor.FromRgb(74, 222, 128);
            borderColor = MediaColor.FromRgb(22, 101, 52);
            badgeBackground = MediaColor.FromRgb(20, 83, 45);
            badgeBorder = MediaColor.FromRgb(34, 197, 94);
            badgeText = "Protected";
            summary = "You are protected right now.";
            detail = $"The KungFlow Firewall is active in {GetFirewallModeLabel()} and reducing interruption flow.";
        }
        else if (isOverloaded)
        {
            statusColor = MediaColor.FromRgb(248, 113, 113);
            borderColor = MediaColor.FromRgb(127, 29, 29);
            badgeBackground = MediaColor.FromRgb(69, 10, 10);
            badgeBorder = MediaColor.FromRgb(248, 113, 113);
            badgeText = "Not protected";
            summary = "High load detected, but the firewall is inactive.";
            detail = isManualOverride
                ? "Manual override is controlling the firewall. Resume automatic control or activate protection manually."
                : "Check firewall control settings so KungFlow can activate protection automatically.";
        }
        else if (isLearning)
        {
            statusColor = MediaColor.FromRgb(250, 204, 21);
            borderColor = MediaColor.FromRgb(161, 98, 7);
            badgeBackground = MediaColor.FromRgb(66, 32, 6);
            badgeBorder = MediaColor.FromRgb(234, 179, 8);
            badgeText = "Learning";
            summary = "KungFlow is calibrating your work rhythm.";
            detail = "The firewall is ready, and automatic protection will become more accurate as your baseline improves.";
        }
        else
        {
            statusColor = MediaColor.FromRgb(74, 222, 128);
            borderColor = MediaColor.FromRgb(22, 101, 52);
            badgeBackground = MediaColor.FromRgb(20, 83, 45);
            badgeBorder = MediaColor.FromRgb(34, 197, 94);
            badgeText = "Ready";
            summary = "No firewall activation needed right now.";
            detail = "KungFlow is monitoring your state. Notifications can pass through while cognitive load is low.";
        }

        FirewallStatusSummaryBorder.BorderBrush = new SolidColorBrush(borderColor);
        FirewallShieldIconBorder.Background = new SolidColorBrush(borderColor);
        FirewallShieldPath.Fill = new SolidColorBrush(Colors.White);
        FirewallShieldBreakPath.Visibility = shouldShowBrokenShield
            ? Visibility.Visible
            : Visibility.Collapsed;
        FirewallProtectionBadgeBorder.Background = new SolidColorBrush(badgeBackground);
        FirewallProtectionBadgeBorder.BorderBrush = new SolidColorBrush(badgeBorder);
        FirewallProtectionBadgeTextBlock.Text = badgeText;
        FirewallProtectionBadgeTextBlock.Foreground = new SolidColorBrush(statusColor);
        FirewallStatusSummaryTextBlock.Text = summary;
        FirewallStatusSummaryTextBlock.Foreground = new SolidColorBrush(statusColor);
        FirewallStatusDetailTextBlock.Text = detail;
        FirewallStatusModeTextBlock.Text = GetFirewallModeDisplayName();
        FirewallStatusProtectedAppsTextBlock.Text = BuildFirewallProtectedAppsSummary();
        FirewallStatusSourceTextBlock.Text = isManualOverride ? "Manual" : "Automatic";
        FirewallStatusSourceTextBlock.Foreground = new SolidColorBrush(statusColor);
        FirewallStatusSourceBadgeBorder.BorderBrush = new SolidColorBrush(borderColor);
    }

    private string GetFirewallModeDisplayName()
    {
        return agentSettings.Firewall.UseGlobalNotificationFirewall
            ? "Global"
            : "Selective";
    }

    private string BuildFirewallProtectedAppsSummary()
    {
        if (agentSettings.Firewall.UseGlobalNotificationFirewall)
        {
            return "All notifications";
        }

        IReadOnlyList<FirewallTarget> targets = availableFirewallTargets.Count > 0
            ? availableFirewallTargets
            : RefreshAvailableFirewallTargets();

        List<string> selectedTargets = targets
            .Where(target => agentSettings.Firewall.IsApplicationMuted(target.Id))
            .Select(target => target.DisplayName)
            .ToList();

        return selectedTargets.Count == 0
            ? "No apps selected"
            : string.Join(", ", selectedTargets);
    }

    private void UpdateLocalFocusModeIndicator()
    {
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
        FirewallProtectedTodayTextBlock.Text = FormatProtectedDuration(
            CalculateProtectedDurationToday(events));

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
        FirewallProtectedTodayTextBlock.Text = "0 min";
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

    private static TimeSpan CalculateProtectedDurationToday(IReadOnlyList<FirewallEventResponse> events)
    {
        DateTimeOffset now = DateTimeOffset.Now;
        DateTime today = now.Date;
        DateTimeOffset dayStart = new(today, now.Offset);
        DateTimeOffset? activeSince = null;
        TimeSpan total = TimeSpan.Zero;

        foreach (FirewallEventResponse firewallEvent in events.OrderBy(firewallEvent => firewallEvent.OccurredAt))
        {
            DateTimeOffset occurredAt = firewallEvent.OccurredAt.ToLocalTime();

            if (firewallEvent.EventType == "activated")
            {
                activeSince = occurredAt < dayStart ? dayStart : occurredAt;
                continue;
            }

            if (firewallEvent.EventType == "deactivated" && activeSince.HasValue)
            {
                DateTimeOffset endedAt = occurredAt > now ? now : occurredAt;

                if (endedAt > dayStart)
                {
                    total += endedAt - activeSince.Value;
                }

                activeSince = null;
            }
        }

        if (activeSince.HasValue)
        {
            total += now - activeSince.Value;
        }

        return total < TimeSpan.Zero ? TimeSpan.Zero : total;
    }

    private static string FormatProtectedDuration(TimeSpan duration)
    {
        if (duration.TotalMinutes < 1)
        {
            return "0 min";
        }

        if (duration.TotalHours < 1)
        {
            return $"{Math.Floor(duration.TotalMinutes)} min";
        }

        return $"{Math.Floor(duration.TotalHours)}h {duration.Minutes}m";
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
