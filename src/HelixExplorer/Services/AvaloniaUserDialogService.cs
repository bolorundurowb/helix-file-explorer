using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using HelixExplorer.Core.Infrastructure;

namespace HelixExplorer.Services;

public sealed class AvaloniaUserDialogService : IUserDialogService
{
    private readonly IWindowOwnerContext _ownerContext;

    public AvaloniaUserDialogService(IWindowOwnerContext ownerContext)
    {
        _ownerContext = ownerContext;
    }

    public Task<bool> ConfirmAsync(string title, string message)
        => RunOnUiAsync(() => ShowConfirmAsync(title, message));

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

    private async Task<bool> ShowConfirmAsync(string title, string message)
    {
        var owner = GetOwnerWindow();
        var tcs = new TaskCompletionSource<bool>();
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
                    Spacing = 8,
                    Children =
                    {
                        CreateButton("Cancel", () => { tcs.TrySetResult(false); dialog.Close(); }),
                        CreateButton("OK", () => { tcs.TrySetResult(true); dialog.Close(); }, primary: true)
                    }
                }
            }
        };

        dialog.Closed += (_, _) => tcs.TrySetResult(false);
        await dialog.ShowDialog(owner!).ConfigureAwait(true);
        return await tcs.Task.ConfigureAwait(true);
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
        var tcs = new TaskCompletionSource<FileConflictResolution?>();
        var applyToAll = new CheckBox { Content = "Apply to all", IsVisible = canApplyToAll };
        var dialog = BuildDialog(owner, "Replace or skip files", 480);

        var sourceName = Path.GetFileName(conflict.SourcePath);
        var destName = Path.GetFileName(conflict.DestinationPath);
        dialog.Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 12,
            Children =
            {
                new TextBlock
                {
                    Text = $"A file or folder named \"{destName}\" already exists at the destination.",
                    TextWrapping = TextWrapping.Wrap
                },
                new TextBlock { Text = $"Source: {sourceName}", Opacity = 0.7 },
                applyToAll,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children =
                    {
                        CreateButton("Cancel", () => { tcs.TrySetResult(null); dialog.Close(); }),
                        CreateButton("Skip", () => Complete(FileConflictChoice.Skip)),
                        CreateButton("Keep both", () => Complete(FileConflictChoice.KeepBoth)),
                        CreateButton("Replace", () => Complete(FileConflictChoice.Replace), primary: true)
                    }
                }
            }
        };

        dialog.Closed += (_, _) => tcs.TrySetResult(null);
        await dialog.ShowDialog(owner!).ConfigureAwait(true);
        return await tcs.Task.ConfigureAwait(true);

        void Complete(FileConflictChoice choice)
        {
            tcs.TrySetResult(new FileConflictResolution(choice, applyToAll.IsChecked == true));
            dialog.Close();
        }
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

    private static Button CreateButton(string text, Action onClick, bool primary = false)
    {
        var button = new Button
        {
            Content = text,
            Padding = new Thickness(14, 8),
            MinWidth = 80
        };

        if (primary)
            button.Classes.Add("accent");

        button.Click += (_, _) => onClick();
        return button;
    }

    private Window? GetOwnerWindow()
        => _ownerContext.OwnerWindow ?? GetFallbackMainWindow();

    private static Window? GetFallbackMainWindow()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            return desktop.MainWindow;

        return null;
    }
}
