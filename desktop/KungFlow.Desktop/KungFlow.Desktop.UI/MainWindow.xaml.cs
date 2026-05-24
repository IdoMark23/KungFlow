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
    private readonly LocalFocusModeController focusModeController = new();
    private readonly DispatcherTimer statusRefreshTimer = new();
    private readonly Forms.NotifyIcon trayIcon = new();
    private DesktopSession? session;
    private bool isRefreshingStatus;
    private bool isExitRequested;

    public MainWindow()
    {
        InitializeComponent();

        statusRefreshTimer.Interval = TimeSpan.FromSeconds(10);
        statusRefreshTimer.Tick += StatusRefreshTimer_Tick;

        ConfigureTrayIcon();
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
        LoginButton.IsEnabled = false;

        try
        {
            LoginResponse response = await apiClient.LoginAsync(email, password, CancellationToken.None);
            session = new DesktopSession(response.AccessToken, response.User);
            ShowLoggedInView(response.User.Email);
            await RefreshStatusAsync();
            statusRefreshTimer.Start();
        }
        catch (Exception ex)
        {
            SetMessage(ex.Message, true);
        }
        finally
        {
            LoginButton.IsEnabled = true;
        }
    }

    private async void LogoutButton_Click(object sender, RoutedEventArgs e)
    {
        statusRefreshTimer.Stop();
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
        focusModeController.SetEnabled(false);
        PasswordBox.Clear();
        SetDesktopStatusMessage("");
        ResetStatusView();
        LoggedInView.Visibility = Visibility.Collapsed;
        LoginView.Visibility = Visibility.Visible;
    }

    private void ShowLoggedInView(string email)
    {
        LoggedInEmailTextBlock.Text = $"Signed in as {email}";
        LoginView.Visibility = Visibility.Collapsed;
        LoggedInView.Visibility = Visibility.Visible;
    }

    private async void StatusRefreshTimer_Tick(object? sender, EventArgs e)
    {
        await RefreshStatusAsync();
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

    private void UpdateStatusView(CurrentStatusResponse status)
    {
        LoadStateTextBlock.Text = status.State;
        ScoreTextBlock.Text = FormatNullableNumber(status.CognitiveLoadScore);
        BaselineTextBlock.Text = FormatNullableNumber(status.BaselineScore);
        BaselineProgressTextBlock.Text = FormatBaselineProgress(status);
        NotificationRecommendationTextBlock.Text =
            status.ShouldSilenceNotifications ? "Reduce interruptions" : "No action";
        NotificationRecommendationTextBlock.Foreground = new SolidColorBrush(
            status.ShouldSilenceNotifications
                ? MediaColor.FromRgb(220, 38, 38)
                : MediaColor.FromRgb(22, 163, 74));

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
        NotificationRecommendationTextBlock.Foreground = new SolidColorBrush(MediaColor.FromRgb(17, 24, 39));
        LocalFocusModeTextBlock.Text = "Inactive";
        LocalFocusModeTextBlock.Foreground = new SolidColorBrush(MediaColor.FromRgb(17, 24, 39));
        LastStatusUpdateTextBlock.Text = "Never";
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

    private void SetDesktopStatusMessage(string message, bool isError = false)
    {
        DesktopStatusMessageTextBlock.Text = message;
        DesktopStatusMessageTextBlock.Foreground = new SolidColorBrush(
            isError ? MediaColor.FromRgb(220, 38, 38) : MediaColor.FromRgb(107, 114, 128));
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
