using System.Runtime.InteropServices;

namespace KungFlow.Desktop.Agent;

internal static class WindowsNotificationDatabaseController
{
    private const int SqliteOk = 0;
    private const int SqliteOpenReadWrite = 0x00000002;
    private const int SqliteOpenUri = 0x00000040;

    public static WindowsNotificationDatabaseApplyResult SetToastEnabledForApplicationKeys(
        IReadOnlyCollection<string> applicationKeys,
        bool isEnabled)
    {
        if (applicationKeys.Count == 0)
        {
            return new WindowsNotificationDatabaseApplyResult(
                new Dictionary<string, string>(),
                null);
        }

        string databasePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft",
            "Windows",
            "Notifications",
            "wpndatabase.db");

        if (!File.Exists(databasePath))
        {
            return new WindowsNotificationDatabaseApplyResult(
                new Dictionary<string, string>(),
                $"Windows notification database was not found at {databasePath}.");
        }

        IntPtr database = IntPtr.Zero;

        try
        {
            int openResult = sqlite3_open_v2(
                databasePath,
                out database,
                SqliteOpenReadWrite | SqliteOpenUri,
                IntPtr.Zero);

            if (openResult != SqliteOk)
            {
                return new WindowsNotificationDatabaseApplyResult(
                    new Dictionary<string, string>(),
                    $"Could not open Windows notification database: {GetDatabaseError(database)}");
            }

            _ = sqlite3_busy_timeout(database, 1000);

            string primaryIdList = string.Join(
                ",",
                applicationKeys.Select(applicationKey => $"'{EscapeSqlLiteral(applicationKey)}'"));
            int toastValue = isEnabled ? 1 : 0;

            Execute(
                database,
                $"""
                UPDATE HandlerSettings
                SET Value = {toastValue}
                WHERE SettingKey = 's:toast'
                  AND HandlerId IN (
                      SELECT RecordId
                      FROM NotificationHandler
                      WHERE PrimaryId IN ({primaryIdList})
                  );

                INSERT INTO HandlerSettings (HandlerId, SettingKey, Value)
                SELECT RecordId, 's:toast', {toastValue}
                FROM NotificationHandler
                WHERE PrimaryId IN ({primaryIdList})
                  AND NOT EXISTS (
                      SELECT 1
                      FROM HandlerSettings
                      WHERE HandlerSettings.HandlerId = NotificationHandler.RecordId
                        AND HandlerSettings.SettingKey = 's:toast'
                  );
                """);

            Dictionary<string, string> toastStates = ReadToastStates(database, primaryIdList);

            return new WindowsNotificationDatabaseApplyResult(toastStates, null);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or InvalidOperationException)
        {
            return new WindowsNotificationDatabaseApplyResult(
                new Dictionary<string, string>(),
                ex.Message);
        }
        finally
        {
            if (database != IntPtr.Zero)
            {
                _ = sqlite3_close(database);
            }
        }
    }

    private static Dictionary<string, string> ReadToastStates(IntPtr database, string primaryIdList)
    {
        Dictionary<string, string> states = new(StringComparer.OrdinalIgnoreCase);
        SqliteCallback callback = (_, columnCount, values, _) =>
        {
            if (columnCount < 2)
            {
                return 0;
            }

            string primaryId = ReadString(values, 0) ?? "unknown";
            string value = ReadString(values, 1) ?? "missing";
            states[primaryId] = value;
            return 0;
        };

        Execute(
            database,
            $"""
            SELECT NotificationHandler.PrimaryId,
                   COALESCE(CAST(HandlerSettings.Value AS TEXT), 'missing') AS ToastValue
            FROM NotificationHandler
            LEFT JOIN HandlerSettings
              ON HandlerSettings.HandlerId = NotificationHandler.RecordId
             AND HandlerSettings.SettingKey = 's:toast'
            WHERE NotificationHandler.PrimaryId IN ({primaryIdList});
            """,
            callback);

        return states;
    }

    private static void Execute(IntPtr database, string sql, SqliteCallback? callback = null)
    {
        int result = sqlite3_exec(database, sql, callback, IntPtr.Zero, out IntPtr errorMessage);

        if (result == SqliteOk)
        {
            return;
        }

        string message = errorMessage == IntPtr.Zero
            ? GetDatabaseError(database)
            : Marshal.PtrToStringUTF8(errorMessage) ?? "Unknown SQLite error.";

        if (errorMessage != IntPtr.Zero)
        {
            sqlite3_free(errorMessage);
        }

        throw new InvalidOperationException(message);
    }

    private static string? ReadString(IntPtr values, int index)
    {
        IntPtr valuePointer = Marshal.ReadIntPtr(values, index * IntPtr.Size);
        return valuePointer == IntPtr.Zero
            ? null
            : Marshal.PtrToStringUTF8(valuePointer);
    }

    private static string EscapeSqlLiteral(string value)
    {
        return value.Replace("'", "''", StringComparison.Ordinal);
    }

    private static string GetDatabaseError(IntPtr database)
    {
        if (database == IntPtr.Zero)
        {
            return "Database handle was not created.";
        }

        IntPtr message = sqlite3_errmsg(database);
        return Marshal.PtrToStringUTF8(message) ?? "Unknown SQLite error.";
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int SqliteCallback(
        IntPtr data,
        int columnCount,
        IntPtr values,
        IntPtr columnNames);

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_open_v2(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string filename,
        out IntPtr database,
        int flags,
        IntPtr vfs);

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_close(IntPtr database);

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_busy_timeout(IntPtr database, int milliseconds);

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int sqlite3_exec(
        IntPtr database,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string sql,
        SqliteCallback? callback,
        IntPtr callbackArgument,
        out IntPtr errorMessage);

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr sqlite3_errmsg(IntPtr database);

    [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern void sqlite3_free(IntPtr pointer);
}

public sealed record WindowsNotificationDatabaseApplyResult(
    IReadOnlyDictionary<string, string> ToastStates,
    string? ErrorMessage)
{
    public bool Succeeded => string.IsNullOrWhiteSpace(ErrorMessage);
}
