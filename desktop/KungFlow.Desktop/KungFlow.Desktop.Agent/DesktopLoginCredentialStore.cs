using System.Runtime.InteropServices;
using System.Text;

namespace KungFlow.Desktop.Agent;

public static class DesktopLoginCredentialStore
{
    private const string CredentialTargetName = "KungFlow.Desktop.Login";
    private const uint GenericCredentialType = 1;
    private const uint PersistLocalMachine = 2;
    private const int ErrorNotFound = 1168;

    public static DesktopLoginCredentials Load()
    {
        if (!CredRead(CredentialTargetName, GenericCredentialType, 0, out IntPtr credentialPointer))
        {
            int errorCode = Marshal.GetLastWin32Error();

            if (errorCode != ErrorNotFound)
            {
                DesktopDiagnosticLogger.Log(
                    "desktop_login_credentials_load_failed",
                    new Dictionary<string, string?>
                    {
                        ["errorCode"] = errorCode.ToString()
                    });
            }

            return DesktopLoginCredentials.Empty;
        }

        try
        {
            NativeCredential credential = Marshal.PtrToStructure<NativeCredential>(credentialPointer);
            string email = credential.UserName ?? string.Empty;
            string password = string.Empty;

            if (credential.CredentialBlob != IntPtr.Zero && credential.CredentialBlobSize > 0)
            {
                password = Marshal.PtrToStringUni(
                    credential.CredentialBlob,
                    (int)credential.CredentialBlobSize / sizeof(char)) ?? string.Empty;
            }

            return new DesktopLoginCredentials(email, password);
        }
        finally
        {
            CredFree(credentialPointer);
        }
    }

    public static void Save(string email, string password)
    {
        byte[] passwordBytes = Encoding.Unicode.GetBytes(password);
        IntPtr passwordPointer = Marshal.AllocCoTaskMem(passwordBytes.Length);

        try
        {
            Marshal.Copy(passwordBytes, 0, passwordPointer, passwordBytes.Length);

            NativeCredential credential = new()
            {
                Type = GenericCredentialType,
                TargetName = CredentialTargetName,
                CredentialBlobSize = (uint)passwordBytes.Length,
                CredentialBlob = passwordPointer,
                Persist = PersistLocalMachine,
                UserName = email
            };

            if (!CredWrite(ref credential, 0))
            {
                DesktopDiagnosticLogger.Log(
                    "desktop_login_credentials_save_failed",
                    new Dictionary<string, string?>
                    {
                        ["errorCode"] = Marshal.GetLastWin32Error().ToString()
                    });
            }
        }
        finally
        {
            Marshal.FreeCoTaskMem(passwordPointer);
        }
    }

    [DllImport("Advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(
        string target,
        uint type,
        uint reservedFlag,
        out IntPtr credentialPointer);

    [DllImport("Advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWrite(
        ref NativeCredential credential,
        uint flags);

    [DllImport("Advapi32.dll", SetLastError = true)]
    private static extern void CredFree(IntPtr buffer);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public uint Flags;
        public uint Type;
        public string TargetName;
        public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string? UserName;
    }
}

public sealed record DesktopLoginCredentials(string Email, string Password)
{
    public static DesktopLoginCredentials Empty { get; } = new(string.Empty, string.Empty);
}
