using System.Runtime.InteropServices;
using System.Text;

namespace KungFlow.Desktop.Agent;

public sealed class DesktopMetricsCollector : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int VK_BACK = 0x08;
    private const int VK_DELETE = 0x2E;
    private const double MouseActivityThresholdPixels = 8;

    private readonly object syncRoot = new();
    private readonly LowLevelKeyboardProc keyboardProc;
    private IntPtr keyboardHook = IntPtr.Zero;
    private IntPtr lastForegroundWindow = IntPtr.Zero;
    private POINT? lastCursorPoint;
    private int windowSwitchCount;
    private int keyPressCount;
    private int deleteKeyCount;
    private double mouseDistancePixels;

    public DesktopMetricsCollector()
    {
        keyboardProc = KeyboardHookCallback;
    }

    public void Start()
    {
        if (keyboardHook == IntPtr.Zero)
        {
            keyboardHook = SetWindowsHookEx(
                WH_KEYBOARD_LL,
                keyboardProc,
                GetModuleHandle(null),
                0);
        }

        lastForegroundWindow = GetForegroundWindow();

        if (GetCursorPos(out POINT point))
        {
            lastCursorPoint = point;
        }
    }

    public void Stop()
    {
        if (keyboardHook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(keyboardHook);
            keyboardHook = IntPtr.Zero;
        }
    }

    public void Poll()
    {
        lock (syncRoot)
        {
            IntPtr currentForegroundWindow = GetForegroundWindow();

            if (
                currentForegroundWindow != IntPtr.Zero &&
                lastForegroundWindow != IntPtr.Zero &&
                currentForegroundWindow != lastForegroundWindow)
            {
                windowSwitchCount++;
            }

            if (currentForegroundWindow != IntPtr.Zero)
            {
                lastForegroundWindow = currentForegroundWindow;
            }

            if (GetCursorPos(out POINT currentPoint))
            {
                if (lastCursorPoint.HasValue)
                {
                    int dx = currentPoint.X - lastCursorPoint.Value.X;
                    int dy = currentPoint.Y - lastCursorPoint.Value.Y;
                    mouseDistancePixels += Math.Sqrt(dx * dx + dy * dy);
                }

                lastCursorPoint = currentPoint;
            }
        }
    }

    public DesktopMetricsSnapshot CaptureSnapshot(TimeSpan collectionWindow)
    {
        lock (syncRoot)
        {
            var snapshot = new DesktopMetricsSnapshot(
                DateTimeOffset.UtcNow,
                HasUserActivity(),
                CountVisibleApplicationWindows(),
                windowSwitchCount,
                deleteKeyCount,
                keyPressCount,
                CalculateRatePerMinute(keyPressCount, collectionWindow),
                CalculateDistancePerSecond(mouseDistancePixels, collectionWindow));

            windowSwitchCount = 0;
            keyPressCount = 0;
            deleteKeyCount = 0;
            mouseDistancePixels = 0;

            return snapshot;
        }
    }

    public void Dispose()
    {
        Stop();
    }

    private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (wParam == WM_KEYDOWN || wParam == WM_SYSKEYDOWN))
        {
            int virtualKeyCode = Marshal.ReadInt32(lParam);

            lock (syncRoot)
            {
                keyPressCount++;

                if (virtualKeyCode == VK_BACK || virtualKeyCode == VK_DELETE)
                {
                    deleteKeyCount++;
                }
            }
        }

        return CallNextHookEx(keyboardHook, nCode, wParam, lParam);
    }

    private bool HasUserActivity()
    {
        return keyPressCount > 0 ||
            windowSwitchCount > 0 ||
            mouseDistancePixels >= MouseActivityThresholdPixels;
    }

    private static int CountVisibleApplicationWindows()
    {
        int count = 0;

        EnumWindows((windowHandle, _) =>
        {
            if (IsVisibleApplicationWindow(windowHandle))
            {
                count++;
            }

            return true;
        }, IntPtr.Zero);

        return count;
    }

    private static bool IsVisibleApplicationWindow(IntPtr windowHandle)
    {
        if (!IsWindowVisible(windowHandle))
        {
            return false;
        }

        if (GetWindowTextLength(windowHandle) == 0)
        {
            return false;
        }

        var className = new StringBuilder(256);
        GetClassName(windowHandle, className, className.Capacity);
        string classNameValue = className.ToString();

        return classNameValue is not "Progman" and not "WorkerW" and not "Shell_TrayWnd";
    }

    private static double CalculateRatePerMinute(int count, TimeSpan window)
    {
        return window.TotalMinutes > 0 ? count / window.TotalMinutes : 0;
    }

    private static double CalculateDistancePerSecond(double distancePixels, TimeSpan window)
    {
        return window.TotalSeconds > 0 ? distancePixels / window.TotalSeconds : 0;
    }

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    private delegate bool EnumWindowsProc(IntPtr windowHandle, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(
        int idHook,
        LowLevelKeyboardProc callback,
        IntPtr hMod,
        uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hookHandle);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(
        IntPtr hookHandle,
        int nCode,
        IntPtr wParam,
        IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? moduleName);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr windowHandle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr windowHandle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(
        IntPtr windowHandle,
        StringBuilder className,
        int maxCount);

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct POINT
    {
        public readonly int X;
        public readonly int Y;
    }
}
