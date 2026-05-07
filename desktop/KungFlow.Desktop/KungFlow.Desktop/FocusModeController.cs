namespace KungFlow.Desktop;

internal sealed class FocusModeController
{
    private readonly INotificationSilencer notificationSilencer;

    public FocusModeController()
        : this(new MockNotificationSilencer())
    {
    }

    private FocusModeController(INotificationSilencer notificationSilencer)
    {
        this.notificationSilencer = notificationSilencer;
    }

    public void Enable()
    {
        notificationSilencer.Enable();
        Console.WriteLine("KungFlow Desktop: simulated Do Not Disturb is ON.");
    }

    public void Disable()
    {
        notificationSilencer.Disable();
        Console.WriteLine("KungFlow Desktop: simulated Do Not Disturb is OFF.");
    }

    public void PrintStatus()
    {
        bool isEnabled = notificationSilencer.IsEnabled();
        Console.WriteLine($"KungFlow Desktop: simulated Do Not Disturb is {(isEnabled ? "ON" : "OFF")}.");
    }
}
