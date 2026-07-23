using System.Runtime.InteropServices;
using System.Text;
using HelixExplorer.Core.FileSystem;
using Microsoft.Extensions.Logging;

namespace HelixExplorer.Windows.FileSystem;

/// <summary>
/// Prompts for SMB credentials via CredUI and connects with <c>WNetAddConnection2</c>.
/// </summary>
public sealed class WinNetworkConnectionService(ILogger<WinNetworkConnectionService> logger) : INetworkConnectionService
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

            var connectResult = WNetAddConnection2(
                resource,
                password.ToString(),
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
            password.Clear();
        }
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

    [DllImport("credui.dll", CharSet = CharSet.Unicode)]
    private static extern void CredUIConfirmCredentials(string pszTargetName, bool bConfirm);

    [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
    private static extern int WNetAddConnection2(NetResource lpNetResource, string? lpPassword, string? lpUsername, int dwFlags);

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
