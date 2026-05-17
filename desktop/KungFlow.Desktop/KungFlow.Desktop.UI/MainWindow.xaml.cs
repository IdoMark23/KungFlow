using System.Windows;
using System.Windows.Media;

namespace KungFlow.Desktop.UI;

public partial class MainWindow : Window
{
    private readonly KungFlowApiClient apiClient = new();
    private DesktopSession? session;

    public MainWindow()
    {
        InitializeComponent();
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

    private void LogoutButton_Click(object sender, RoutedEventArgs e)
    {
        session = null;
        PasswordBox.Clear();
        SetMessage("");
        LoggedInView.Visibility = Visibility.Collapsed;
        LoginView.Visibility = Visibility.Visible;
    }

    private void ShowLoggedInView(string email)
    {
        LoggedInEmailTextBlock.Text = $"Signed in as {email}";
        LoginView.Visibility = Visibility.Collapsed;
        LoggedInView.Visibility = Visibility.Visible;
    }

    private void SetMessage(string message, bool isError = false)
    {
        MessageTextBlock.Text = message;
        MessageTextBlock.Foreground = new SolidColorBrush(
            isError ? Color.FromRgb(220, 38, 38) : Color.FromRgb(107, 114, 128));
    }
}
