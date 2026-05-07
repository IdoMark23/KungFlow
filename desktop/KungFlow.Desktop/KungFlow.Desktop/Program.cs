namespace KungFlow.Desktop;

internal static class Program
{
    private static int Main(string[] args)
    {
        var focusModeController = new FocusModeController();

        if (args.Length != 2 || args[0].ToLowerInvariant() != "focus")
        {
            PrintUsage();
            return 1;
        }

        string action = args[1].ToLowerInvariant();

        if (action == "on")
        {
            focusModeController.Enable();
            return 0;
        }

        if (action == "off")
        {
            focusModeController.Disable();
            return 0;
        }

        if (action == "status")
        {
            focusModeController.PrintStatus();
            return 0;
        }

        PrintUsage();


        return 1;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  KungFlow.Desktop focus on");
        Console.WriteLine("  KungFlow.Desktop focus off");
        Console.WriteLine("  KungFlow.Desktop focus status");
    }
}
