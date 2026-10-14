using HelixExplorer.Core.Theming;
using Microsoft.Win32;

namespace HelixExplorer.Windows.Theming;

public sealed class WinAccentColorSource : IAccentColorSource
{
    public uint? TryReadAccentColor()
    {
        // A missing or unreadable accent falls back to Windows blue; it must not block startup.
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM", writable: false);
            return key?.GetValue("AccentColor") switch
            {
                int abgr => AccentColorDefaults.FromAbgr(unchecked((uint)abgr)),
                uint abgr => AccentColorDefaults.FromAbgr(abgr),
                _ => null,
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return null;
        }
    }
}
