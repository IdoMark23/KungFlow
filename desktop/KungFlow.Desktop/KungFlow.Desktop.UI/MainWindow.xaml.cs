using KungFlow.Desktop.Agent;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using System.Runtime.InteropServices;
using Forms = System.Windows.Forms;
using MediaColor = System.Windows.Media.Color;

namespace KungFlow.Desktop.UI;

public partial class MainWindow : Window
{
    private readonly KungFlowApiClient apiClient = new();
    private readonly DesktopAgentSettings agentSettings = new();
    private readonly LocalFocusModeController focusModeController = new();
    private readonly DesktopMetricsCollector metricsCollector = new();
    private readonly DispatcherTimer statusRefreshTimer = new();
    private readonly DispatcherTimer metricsSendTimer = new();
    private readonly DispatcherTimer metricsPollingTimer = new();
    private readonly Forms.NotifyIcon trayIcon = new();
    private readonly TimeSpan metricsCollectionWindow = TimeSpan.FromMinutes(1);
    private DesktopSession? session;
    private bool isRefreshingStatus;
    private bool isSendingMetrics;
    private bool isExitRequested;

    public MainWindow()
    {
        InitializeComponent();

        statusRefreshTimer.Interval = TimeSpan.FromSeconds(10);
        statusRefreshTimer.Tick += StatusRefreshTimer_Tick;
        metricsSendTimer.Interval = metricsCollectionWindow;
        metricsSendTimer.Tick += MetricsSendTimer_Tick;
        metricsPollingTimer.Interval = TimeSpan.FromMilliseconds(250);
        metricsPollingTimer.Tick += MetricsPollingTimer_Tick;

        ConfigureTrayIcon();
        UpdateFirewallSettingsSummary();
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

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            SetRegisterMessage("Email, username and password are required.", true);
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

    private async void LogoutButton_Click(object sender, RoutedEventArgs e)
    {
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
            SetMessage(logoutMessage, logoutMessageIsError);
        }
    }

    private void ClearLocalSession()
    {
        session = null;
        metricsCollector.Stop();
        focusModeController.SetEnabled(false);
        PasswordBox.Clear();
        SetDesktopStatusMessage("");
        ResetStatusView();
        LoggedInView.Visibility = Visibility.Collapsed;
        RegisterView.Visibility = Visibility.Collapsed;
        LoginView.Visibility = Visibility.Visible;
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

    private void StatisticsNavButton_Click(object sender, RoutedEventArgs e)
    {
        ShowDashboardPage(DashboardPage.Statistics);
    }

    private void SettingsNavButton_Click(object sender, RoutedEventArgs e)
    {
        ShowDashboardPage(DashboardPage.Settings);
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

    private async Task StartSessionAsync(LoginResponse response)
    {
        session = new DesktopSession(response.AccessToken, response.User);
        ShowLoggedInView(response.User.Email);
        ApplyDataCollectionState();
        await RefreshStatusAsync();
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

            focusModeController.SetEnabled(status.ShouldSilenceNotifications);
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
                focusModeController.SetEnabled(response.Status.ShouldSilenceNotifications);
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

        LastStatusUpdateTextBlock.Text = DateTime.Now.ToString("HH:mm:ss");
    }

    private void ResetStatusView()
    {
        LoadStateTextBlock.Text = "Waiting";
        ScoreTextBlock.Text = "-";
        BaselineTextBlock.Text = "-";
        BaselineProgressTextBlock.Text = "-";
        NotificationRecommendationTextBlock.Text = "-";
        NotificationRecommendationTextBlock.Foreground = new SolidColorBrush(Colors.White);
        LocalFocusModeTextBlock.Text = "Inactive";
        LocalFocusModeTextBlock.Foreground = new SolidColorBrush(Colors.White);
        LastStatusUpdateTextBlock.Text = "Never";
        StatusOrbEllipse.Fill = new SolidColorBrush(MediaColor.FromRgb(148, 163, 184));
        StatusOrbEllipse.Stroke = new SolidColorBrush(MediaColor.FromRgb(226, 232, 240));
        StatusBadgeTextBlock.Text = "Waiting";
        StatusBadgeTextBlock.Foreground = new SolidColorBrush(MediaColor.FromRgb(51, 65, 85));
        StatusHeadlineTextBlock.Text = "KungFlow is waiting for activity data.";
        StatusBodyTextBlock.Text = "After the desktop agent collects enough computer activity, the firewall will decide whether interruptions should pass through.";
    }

    private void DataCollectionEnabledCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        agentSettings.IsDataCollectionEnabled = DataCollectionEnabledCheckBox.IsChecked == true;
        ApplyDataCollectionState();
    }

    private void FirewallSettingsCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        agentSettings.Firewall.UseDefaultDoNotDisturb = DefaultDndCheckBox.IsChecked == true;
        agentSettings.Firewall.SetApplicationMuted(
            FirewallTargetCatalog.WhatsAppId,
            WhatsAppFirewallCheckBox.IsChecked == true);
        agentSettings.Firewall.SetApplicationMuted(
            FirewallTargetCatalog.OutlookId,
            OutlookFirewallCheckBox.IsChecked == true);
        UpdateFirewallSettingsSummary();
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
            return;
        }

        metricsSendTimer.Stop();
        metricsPollingTimer.Stop();
        metricsCollector.Stop();
        SetDesktopStatusMessage("Desktop activity collection is disabled.");
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

    private void UpdateFirewallSettingsSummary()
    {
        List<string> selectedRules = new();

        if (agentSettings.Firewall.UseDefaultDoNotDisturb)
        {
            selectedRules.Add("Default Windows Do Not Disturb");
        }

        selectedRules.AddRange(FirewallTargetCatalog.Defaults
            .Where(target => agentSettings.Firewall.IsApplicationMuted(target.Id))
            .Select(target => target.DisplayName));

        FirewallSettingsSummaryTextBlock.Text = selectedRules.Count == 0
            ? "No application-specific firewall rules selected."
            : $"Firewall rules selected: {string.Join(", ", selectedRules)}.";
    }

    private static FirewallPresentation GetFirewallPresentation(CurrentStatusResponse status)
    {
        if (status.State == "overloaded")
        {
            return new FirewallPresentation(
                "Red - Firewall active",
                "KungFlow detected high cognitive load.",
                "The notification firewall is protecting you from unnecessary interruptions.",
                "Reduce interruptions",
                MediaColor.FromRgb(220, 38, 38),
                MediaColor.FromRgb(254, 202, 202));
        }

        if (status.State == "collecting_baseline" || status.State == "no_metrics")
        {
            return new FirewallPresentation(
                "Orange - Calibrating",
                "KungFlow is learning your normal work rhythm.",
                "The firewall is not fully active yet. Keep working normally so the baseline becomes more accurate.",
                "Learning",
                MediaColor.FromRgb(217, 119, 6),
                MediaColor.FromRgb(254, 215, 170));
        }

        return new FirewallPresentation(
            "Green - Open",
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
            isError ? MediaColor.FromRgb(220, 38, 38) : MediaColor.FromRgb(107, 114, 128));
    }

    private void SetRegisterMessage(string message, bool isError = false)
    {
        RegisterMessageTextBlock.Text = message;
        RegisterMessageTextBlock.Foreground = new SolidColorBrush(
            isError ? MediaColor.FromRgb(220, 38, 38) : MediaColor.FromRgb(107, 114, 128));
    }

    private void SetDesktopStatusMessage(string message, bool isError = false)
    {
        DesktopStatusMessageTextBlock.Text = message;
        DesktopStatusMessageTextBlock.Foreground = new SolidColorBrush(
            isError ? MediaColor.FromRgb(248, 113, 113) : MediaColor.FromRgb(148, 163, 184));
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
