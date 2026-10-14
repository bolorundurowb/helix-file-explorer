using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using HelixExplorer.Core.Formatting;
using HelixExplorer.Core.Infrastructure;

namespace HelixExplorer.Services;

public sealed class AvaloniaUserDialogService(IWindowOwnerContext ownerContext) : IUserDialogService
{
    public Task<bool> ConfirmAsync(
        string title,
        string message,
        string? confirmLabel = null,
        string? cancelLabel = null,
        bool destructive = false)
        => RunOnUiAsync(() => ShowConfirmAsync(title, message, confirmLabel, cancelLabel, destructive));

    public Task ShowErrorAsync(string title, string message)
        => RunOnUiAsync(async () =>
        {
            await ShowMessageAsync(title, message, "OK").ConfigureAwait(true);
        });

    public Task ShowOperationSummaryAsync(FileOperationResult result, string operationName)
        => RunOnUiAsync(async () =>
        {
            var lines = new List<string>();
            if (result.Succeeded > 0)
                lines.Add($"{result.Succeeded} item(s) completed.");
            if (result.Skipped > 0)
                lines.Add($"{result.Skipped} item(s) skipped.");
            if (result.Failed > 0)
                lines.Add($"{result.Failed} item(s) failed.");

            foreach (var failure in result.Failures.Take(5))
                lines.Add($"• {Path.GetFileName(failure.Path)}: {failure.Message}");

            if (result.Failures.Count > 5)
                lines.Add($"…and {result.Failures.Count - 5} more.");

            await ShowMessageAsync($"{operationName} summary", string.Join(Environment.NewLine, lines), "OK")
                .ConfigureAwait(true);
        });

    public Task<FileConflictResolution?> ResolveConflictAsync(FileConflictInfo conflict, bool canApplyToAll)
        => RunOnUiAsync(() => ShowConflictAsync(conflict, canApplyToAll));

    private static async Task RunOnUiAsync(Func<Task> action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            await action().ConfigureAwait(true);
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(action).ConfigureAwait(true);
    }

    private static async Task<T> RunOnUiAsync<T>(Func<Task<T>> action)
    {
        if (Dispatcher.UIThread.CheckAccess())
            return await action().ConfigureAwait(true);

        return await Dispatcher.UIThread.InvokeAsync(action).ConfigureAwait(true);
    }

    private async Task<bool> ShowConfirmAsync(
        string title,
        string message,
        string? confirmLabel,
        string? cancelLabel,
        bool destructive)
    {
        var owner = GetOwnerWindow();
        var result = new TaskCompletionSource<bool>();
        var dialog = CreateConfirmWindow(title, message, confirmLabel, cancelLabel, destructive, result, owner);
        await dialog.ShowDialog(owner!).ConfigureAwait(true);
        return await result.Task.ConfigureAwait(true);
    }

    /// <summary>
    /// Builds the confirm dialog without showing it so tests can assert button defaults.
    /// Enter activates <see cref="Button.IsDefault"/> and Escape activates <see cref="Button.IsCancel"/>.
    /// A destructive confirm puts both on Cancel so the keyboard cannot destroy files.
    /// </summary>
    internal Window CreateConfirmWindow(
        string title,
        string message,
        string? confirmLabel,
        string? cancelLabel,
        bool destructive,
        TaskCompletionSource<bool> result,
        Window? owner = null)
    {
        var confirmText = string.IsNullOrWhiteSpace(confirmLabel) ? "OK" : confirmLabel;
        var cancelText = string.IsNullOrWhiteSpace(cancelLabel) ? "Cancel" : cancelLabel;
        var dialog = BuildDialog(owner, title, 420);

        var cancel = CreateButton(
            cancelText,
            () => { result.TrySetResult(false); dialog.Close(); },
            isDefault: destructive,
            isCancel: true);
        var confirm = CreateButton(
            confirmText,
            () => { result.TrySetResult(true); dialog.Close(); },
            primary: !destructive,
            isDefault: !destructive);

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 16,
            Children =
            {
                new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { cancel, confirm }
                }
            }
        };

        // Focus the safe action so the first Enter matches IsDefault even if a button takes focus.
        var defaultButton = destructive ? cancel : confirm;
        dialog.Opened += (_, _) => defaultButton.Focus();
        dialog.Closed += (_, _) => result.TrySetResult(false);
        return dialog;
    }

    private async Task ShowMessageAsync(string title, string message, string buttonText)
    {
        var owner = GetOwnerWindow();
        var dialog = BuildDialog(owner, title, 420);
        dialog.Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 16,
            Children =
            {
                new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { CreateButton(buttonText, () => dialog.Close(), primary: true) }
                }
            }
        };

        await dialog.ShowDialog(owner!).ConfigureAwait(true);
    }

    private async Task<FileConflictResolution?> ShowConflictAsync(FileConflictInfo conflict, bool canApplyToAll)
    {
        var owner = GetOwnerWindow();
        var result = new TaskCompletionSource<FileConflictResolution?>();
        var dialog = CreateConflictWindow(conflict, canApplyToAll, result, owner);
        await dialog.ShowDialog(owner!).ConfigureAwait(true);
        return await result.Task.ConfigureAwait(true);
    }

    internal Window CreateConflictWindow(
        FileConflictInfo conflict,
        bool canApplyToAll,
        TaskCompletionSource<FileConflictResolution?> result,
        Window? owner = null)
    {
        var applyToAll = new CheckBox { Content = "Apply to all", IsVisible = canApplyToAll };
        var dialog = BuildDialog(
            owner,
            conflict.IsDirectory ? "Folder already exists" : "Replace or skip files",
            520);

        var destName = FileNameOf(conflict.DestinationPath);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8
        };
        buttons.Children.Add(CreateButton(
            "Cancel",
            () => { result.TrySetResult(null); dialog.Close(); },
            isCancel: true));
        buttons.Children.Add(CreateButton("Skip", () => Complete(FileConflictChoice.Skip)));
        var keepBoth = CreateButton(
            "Keep both",
            () => Complete(FileConflictChoice.KeepBoth),
            isDefault: true);
        buttons.Children.Add(keepBoth);
        // Merge only makes sense for a folder — combine the two trees, resolving nested conflicts.
        if (conflict.IsDirectory)
            buttons.Children.Add(CreateButton("Merge", () => Complete(FileConflictChoice.Merge)));
        // Replace stays off the accent style so the destructive choice is not the one that looks primary.
        buttons.Children.Add(CreateButton("Replace", () => Complete(FileConflictChoice.Replace)));

        var message = conflict.IsDirectory
            ? $"A folder named \"{destName}\" already exists at the destination. Merge combines the two trees. Replace overwrites the existing folder."
            : $"A file or folder named \"{destName}\" already exists at the destination.";

        dialog.Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 12,
            Children =
            {
                new TextBlock
                {
                    Text = message,
                    TextWrapping = TextWrapping.Wrap
                },
                new TextBlock
                {
                    Text = FormatConflictSide("Source", conflict.SourcePath),
                    TextWrapping = TextWrapping.Wrap,
                    Opacity = 0.7
                },
                new TextBlock
                {
                    Text = FormatConflictSide("Destination", conflict.DestinationPath),
                    TextWrapping = TextWrapping.Wrap,
                    Opacity = 0.7
                },
                applyToAll,
                buttons
            }
        };

        dialog.Opened += (_, _) => keepBoth.Focus();
        dialog.Closed += (_, _) => result.TrySetResult(null);
        return dialog;

        void Complete(FileConflictChoice choice)
        {
            result.TrySetResult(new FileConflictResolution(choice, applyToAll.IsChecked == true));
            dialog.Close();
        }
    }

    /// <summary>
    /// Size and last-write time for one side of a conflict. Directories have no single length;
    /// <see cref="FileInfo.Exists"/> is false for them, but <see cref="FileSystemInfo.LastWriteTime"/>
    /// still reads the directory timestamp without walking the tree.
    /// </summary>
    internal static string FormatConflictSide(string heading, string path)
    {
        var name = FileNameOf(path);
        try
        {
            var info = new FileInfo(path);
            if (info.Exists)
            {
                var size = FileSizeFormatter.FormatBinary(info.Length);
                return $"{heading}: {name}{Environment.NewLine}{size}{Environment.NewLine}Modified {info.LastWriteTime:g}";
            }

            if (Directory.Exists(path))
            {
                return $"{heading}: {name}{Environment.NewLine}Folder{Environment.NewLine}Modified {info.LastWriteTime:g}";
            }
        }
        catch (IOException)
        {
            // A locked path must not take down the conflict dialog.
        }
        catch (UnauthorizedAccessException)
        {
            // An unreadable path must not take down the conflict dialog.
        }

        return $"{heading}: {name}{Environment.NewLine}Size and modified time are unavailable.";
    }

    private static string FileNameOf(string path)
    {
        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var name = Path.GetFileName(trimmed);
        return string.IsNullOrEmpty(name) ? path : name;
    }

    private static Window BuildDialog(Window? owner, string title, double width)
    {
        return new Window
        {
            Title = title,
            Width = width,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
            CanResize = false,
            ShowInTaskbar = false
        };
    }

    private static Button CreateButton(
        string text,
        Action onClick,
        bool primary = false,
        bool isDefault = false,
        bool isCancel = false)
    {
        var button = new Button
        {
            Content = text,
            Padding = new Thickness(14, 8),
            MinWidth = 80,
            IsDefault = isDefault,
            IsCancel = isCancel
        };

        if (primary)
            button.Classes.Add("accent");

        button.Click += (_, _) => onClick();
        return button;
    }

    private Window? GetOwnerWindow()
        => ownerContext.OwnerWindow ?? GetFallbackMainWindow();

    private static Window? GetFallbackMainWindow()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            return desktop.MainWindow;

        return null;
    }
}
