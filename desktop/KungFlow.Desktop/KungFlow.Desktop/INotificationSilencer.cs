namespace KungFlow.Desktop;

internal interface INotificationSilencer
{
    void Enable();

    void Disable();

    bool IsEnabled();
}
