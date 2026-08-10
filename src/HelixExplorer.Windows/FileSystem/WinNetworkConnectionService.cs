using System.Runtime.InteropServices;
using System.Text;
using HelixExplorer.Core.FileSystem;
using Microsoft.Extensions.Logging;

namespace HelixExplorer.Windows.FileSystem;

/// <summary>
/// Prompts for SMB credentials via CredUI and connects with <c>WNetAddConnection2</c>.
/// </summary>
public sealed partial class WinNetworkConnectionService(ILogger<WinNetworkConnectionService> logger) : INetworkConnectionService
{
    private const int CredUiFlagsGeneric = 0x0001;
    private const int CredUiFlagsAlwaysShowUi = 0x0080;
    private const int CredUiFlagsExpectConfirmation = 0x20000;
    private const int CredUiMaxUsername = 513;
    private const int CredUiMaxPassword = 256;
    private const int ResourceTypeDisk = 0x00000001;
    private const int ConnectInteractive = 0x00000008;
    private const int ConnectPrompt = 0x00000010;
    private const int ErrorCancelled = 1223;
    private const int ErrorAlreadyAssigned = 85;
    private const int ErrorSessionCredentialConflict = 1219;

    public ValueTask<bool> EnsureConnectedAsync(string uncPath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var target = ResolveConnectTarget(uncPath);
        if (string.IsNullOrEmpty(target))
            return ValueTask.FromResult(false);

        return ValueTask.FromResult(ConnectWithPrompt(target));
    }

    private bool ConnectWithPrompt(string remoteName)
    {
        var username = new StringBuilder(CredUiMaxUsername);
        var password = new StringBuilder(CredUiMaxPassword);

        var info = new CredUiInfo
        {
            Size = Marshal.SizeOf<CredUiInfo>(),
            CaptionText = "Connect to Network Share",
            MessageText = $"Enter credentials for {remoteName}"
        };

        var save = false;
        var credResult = CredUIPromptForCredentials(
            ref info,
            remoteName,
            IntPtr.Zero,
            0,
            username,
            CredUiMaxUsername,
            password,
            CredUiMaxPassword,
            ref save,
            CredUiFlagsGeneric | CredUiFlagsAlwaysShowUi | CredUiFlagsExpectConfirmation);

        if (credResult == ErrorCancelled)
        {
            logger.LogDebug("Credential prompt cancelled for '{Remote}'", remoteName);
            return false;
        }

        if (credResult != 0)
        {
            logger.LogWarning("CredUI failed ({Error}) for '{Remote}'", credResult, remoteName);
            return false;
        }

        try
        {
            var resource = new NetResource
            {
                Type = ResourceTypeDisk,
                RemoteName = remoteName
            };

            // Copy the password into an unmanaged buffer instead of forming a managed
            // string via StringBuilder.ToString(); the latter produces an immutable copy
            // that cannot be zeroed and lingers on the GC heap.
            var passwordBuffer = new char[password.Length];
            password.CopyTo(0, passwordBuffer, 0, password.Length);
            var passwordPtr = Marshal.AllocCoTaskMem((passwordBuffer.Length + 1) * sizeof(short));
            try
            {
                for (var i = 0; i < passwordBuffer.Length; i++)
                    Marshal.WriteInt16(passwordPtr, i * sizeof(short), (short)passwordBuffer[i]);
                Marshal.WriteInt16(passwordPtr, passwordBuffer.Length * sizeof(short), 0);

                var connectResult = WNetAddConnection2(
                    resource,
                    passwordPtr,
                    username.Length > 0 ? username.ToString() : null,
                    ConnectInteractive | ConnectPrompt);

                if (connectResult is 0 or ErrorAlreadyAssigned)
                {
                    CredUIConfirmCredentials(remoteName, true);
                    logger.LogInformation("Connected to '{Remote}'", remoteName);
                    return true;
                }

                if (connectResult == ErrorSessionCredentialConflict)
                {
                    // Existing conflicting session — still try browsing; caller may succeed.
                    logger.LogDebug("Credential conflict for '{Remote}' ({Error})", remoteName, connectResult);
                    CredUIConfirmCredentials(remoteName, false);
                    return false;
                }

                CredUIConfirmCredentials(remoteName, false);
                logger.LogWarning("WNetAddConnection2 failed ({Error}) for '{Remote}'", connectResult, remoteName);
                return false;
            }
            finally
            {
                Array.Clear(passwordBuffer);
                ZeroCoTaskMemBuffer(passwordPtr, passwordBuffer.Length);
                Marshal.FreeCoTaskMem(passwordPtr);
            }
        }
        finally
        {
            password.Clear();
        }
    }

    private static void ZeroCoTaskMemBuffer(IntPtr ptr, int charCount)
    {
        for (var i = 0; i < charCount; i++)
            Marshal.WriteInt16(ptr, i * sizeof(short), 0);
    }

    private static string? ResolveConnectTarget(string uncPath)
    {
        var normalized = NetworkPath.Normalize(uncPath);
        if (!NetworkPath.IsUnc(normalized) || NetworkPath.IsNetworkRoot(normalized))
            return null;

        if (NetworkPath.HasShare(normalized))
        {
            var server = NetworkPath.GetServer(normalized);
            var share = NetworkPath.GetShare(normalized);
            return server is null || share is null ? null : $@"\\{server}\{share}";
        }

        return NetworkPath.IsServerRoot(normalized) ? normalized : null;
    }

    [DllImport("credui.dll", CharSet = CharSet.Unicode)]
    private static extern int CredUIPromptForCredentials(
        ref CredUiInfo pUiInfo,
        string pszTargetName,
        IntPtr Reserved,
        int dwAuthError,
        StringBuilder pszUserName,
        int ulUserNameMaxChars,
        StringBuilder pszPassword,
        int ulPasswordMaxChars,
        ref bool pfSave,
        int dwFlags);

    [LibraryImport("credui.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial void CredUIConfirmCredentials(string pszTargetName, [MarshalAs(UnmanagedType.Bool)] bool bConfirm);

    [LibraryImport("mpr.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int WNetAddConnection2(NetResource lpNetResource, IntPtr lpPassword, string? lpUsername, int dwFlags);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CredUiInfo
    {
        public int Size;
        public IntPtr Parent;
        public string? MessageText;
        public string? CaptionText;
        public IntPtr Banner;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private sealed class NetResource
    {
        public int Scope;
        public int Type;
        public int DisplayType;
        public int Usage;
        public string? LocalName;
        public string? RemoteName;
        public string? Comment;
        public string? Provider;
    }
}
