using System.Text;
using HelixExplorer.Core.FileSystem;
using Microsoft.Extensions.Logging;
using Vanara.PInvoke;
using static Vanara.PInvoke.CredUI;
using static Vanara.PInvoke.Mpr;

namespace HelixExplorer.Windows.FileSystem;

/// <summary>
/// Prompts for SMB credentials via CredUI and connects with <c>WNetAddConnection2</c>.
/// </summary>
public sealed class WinNetworkConnectionService(ILogger<WinNetworkConnectionService> logger) : INetworkConnectionService
{
    private const int CredUiMaxUsername = 513;
    private const int CredUiMaxPassword = 256;

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

        var info = new CREDUI_INFO(HWND.NULL, "Connect to Network Share", $"Enter credentials for {remoteName}");

        var save = false;
        var credResult = CredUIPromptForCredentials(
            in info,
            remoteName,
            IntPtr.Zero,
            Win32Error.ERROR_SUCCESS,
            username,
            CredUiMaxUsername,
            password,
            CredUiMaxPassword,
            ref save,
            CredentialsDialogOptions.CREDUI_FLAGS_GENERIC_CREDENTIALS
            | CredentialsDialogOptions.CREDUI_FLAGS_ALWAYS_SHOW_UI
            | CredentialsDialogOptions.CREDUI_FLAGS_EXPECT_CONFIRMATION);

        if (credResult == Win32Error.ERROR_CANCELLED)
        {
            logger.LogDebug("Credential prompt cancelled for '{Remote}'", remoteName);
            return false;
        }

        if (credResult != Win32Error.ERROR_SUCCESS)
        {
            logger.LogWarning("CredUI failed ({Error}) for '{Remote}'", credResult, remoteName);
            return false;
        }

        try
        {
            var resource = new NETRESOURCE
            {
                dwType = NETRESOURCEType.RESOURCETYPE_DISK,
                lpRemoteName = remoteName
            };

            var passwordText = password.Length > 0 ? password.ToString() : null;
            var connectResult = WNetAddConnection2(
                resource,
                passwordText,
                username.Length > 0 ? username.ToString() : null,
                CONNECT.CONNECT_INTERACTIVE | CONNECT.CONNECT_PROMPT);

            if (connectResult == Win32Error.ERROR_SUCCESS || connectResult == Win32Error.ERROR_ALREADY_ASSIGNED)
            {
                CredUIConfirmCredentials(remoteName, true);
                logger.LogInformation("Connected to '{Remote}'", remoteName);
                return true;
            }

            if (connectResult == Win32Error.ERROR_SESSION_CREDENTIAL_CONFLICT)
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
}
