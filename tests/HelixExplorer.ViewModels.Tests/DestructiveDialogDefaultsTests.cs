using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using HelixExplorer.Core.Formatting;
using HelixExplorer.Core.Infrastructure;
using HelixExplorer.Localization;
using HelixExplorer.Services;

namespace HelixExplorer.ViewModels.Tests;

public sealed class DestructiveDialogDefaultsTests
{
    [Fact]
    public void PermanentlyDeleteMessage_NamesSingleFileOrCount()
    {
        UiStrings.PermanentlyDeleteMessage("notes.txt", 1)
            .Must().Be("\"notes.txt\" will be permanently deleted and cannot be restored.");
        UiStrings.PermanentlyDeleteMessage(null, 3)
            .Must().Be("3 items will be permanently deleted and cannot be restored.");
        UiStrings.DeletePermanentlyButton.Must().Be("Delete permanently");
        UiStrings.EmptyRecycleBinButton.Must().Be("Empty Recycle Bin");
    }

    [Fact]
    public void DestructiveConfirm_CancelIsDefault_ConfirmIsNotAccent()
    {
        HeadlessSession.RunOnUiThread(() =>
        {
            var (window, _) = CreateConfirm(destructive: true, confirmLabel: UiStrings.DeletePermanentlyButton);
            Show(window, shown =>
            {
                var cancel = Button(shown, "Cancel");
                var confirm = Button(shown, "Delete permanently");

                cancel.IsDefault.Must().BeTrue();
                cancel.IsCancel.Must().BeTrue();
                confirm.IsDefault.Must().BeFalse();
                confirm.IsCancel.Must().BeFalse();
                confirm.Classes.Contains("accent").Must().BeFalse();
                cancel.Classes.Contains("accent").Must().BeFalse();
            });
        });
    }

    [Fact]
    public void OrdinaryConfirm_OkIsAccentAndDefault_EnterConfirms_EscapeCancels()
    {
        HeadlessSession.RunOnUiThread(() =>
        {
            var (window, _) = CreateConfirm(destructive: false);
            Show(window, shown =>
            {
                var ok = Button(shown, "OK");
                var cancel = Button(shown, "Cancel");

                ok.IsDefault.Must().BeTrue();
                ok.Classes.Contains("accent").Must().BeTrue();
                cancel.IsCancel.Must().BeTrue();
                cancel.IsDefault.Must().BeFalse();
            });

            Press(CreateConfirm(destructive: false), Key.Enter, PhysicalKey.Enter).Must().BeTrue();
            Press(CreateConfirm(destructive: false), Key.Escape, PhysicalKey.Escape).Must().BeFalse();
        });
    }

    [Fact]
    public void DestructiveConfirm_EnterAndEscapeBothCancel()
    {
        HeadlessSession.RunOnUiThread(() =>
        {
            Press(
                CreateConfirm(destructive: true, confirmLabel: UiStrings.DeletePermanentlyButton),
                Key.Enter,
                PhysicalKey.Enter).Must().BeFalse();
            Press(
                CreateConfirm(destructive: true, confirmLabel: UiStrings.EmptyRecycleBinButton),
                Key.Escape,
                PhysicalKey.Escape).Must().BeFalse();
        });
    }

    [Fact]
    public void ConflictDialog_KeepBothIsDefault_ReplaceIsNotAccent_ShowsFileInfo()
    {
        var root = Path.Combine(Path.GetTempPath(), "helix-conflict-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var sourcePath = Path.Combine(root, "source.txt");
            var destinationPath = Path.Combine(root, "dest.txt");
            File.WriteAllBytes(sourcePath, new byte[2048]);
            File.WriteAllBytes(destinationPath, new byte[4096]);
            File.SetLastWriteTime(sourcePath, new DateTime(2024, 3, 15, 14, 30, 0, DateTimeKind.Local));
            File.SetLastWriteTime(destinationPath, new DateTime(2023, 1, 2, 8, 5, 0, DateTimeKind.Local));
            var source = new FileInfo(sourcePath);
            var destination = new FileInfo(destinationPath);

            HeadlessSession.RunOnUiThread(() =>
            {
                var result = new TaskCompletionSource<FileConflictResolution?>();
                var window = Service.CreateConflictWindow(
                    new FileConflictInfo(sourcePath, destinationPath, IsDirectory: false),
                    canApplyToAll: true,
                    result);
                Show(window, shown =>
                {
                    var keepBoth = Button(shown, "Keep both");
                    var replace = Button(shown, "Replace");
                    var cancel = Button(shown, "Cancel");

                    keepBoth.IsDefault.Must().BeTrue();
                    replace.IsDefault.Must().BeFalse();
                    replace.Classes.Contains("accent").Must().BeFalse();
                    cancel.IsCancel.Must().BeTrue();
                    cancel.IsDefault.Must().BeFalse();

                    var text = DialogText(shown);
                    text.Must().Contain(FileSizeFormatter.FormatBinary(source.Length));
                    text.Must().Contain(FileSizeFormatter.FormatBinary(destination.Length));
                    text.Must().Contain(source.LastWriteTime.ToString("g"));
                    text.Must().Contain(destination.LastWriteTime.ToString("g"));
                    text.Must().Contain("source.txt");
                    text.Must().Contain("dest.txt");
                });
            });
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public void ConflictDialog_DirectoryShowsFolderAndLastWrite_EnterKeepsBoth_EscapeCancels()
    {
        var root = Path.Combine(Path.GetTempPath(), "helix-conflict-dir-" + Guid.NewGuid().ToString("N"));
        var sourcePath = Path.Combine(root, "source-folder");
        var destinationPath = Path.Combine(root, "dest-folder");
        Directory.CreateDirectory(sourcePath);
        Directory.CreateDirectory(destinationPath);
        try
        {
            var written = new DateTime(2022, 6, 1, 9, 15, 0, DateTimeKind.Local);
            Directory.SetLastWriteTime(sourcePath, written);
            Directory.SetLastWriteTime(destinationPath, written);
            var expected = new FileInfo(sourcePath).LastWriteTime.ToString("g");

            HeadlessSession.RunOnUiThread(() =>
            {
                var preview = new TaskCompletionSource<FileConflictResolution?>();
                var previewWindow = Service.CreateConflictWindow(
                    new FileConflictInfo(sourcePath, destinationPath, IsDirectory: true),
                    canApplyToAll: false,
                    preview);
                Show(previewWindow, shown =>
                {
                    Button(shown, "Merge").Classes.Contains("accent").Must().BeFalse();
                    var text = DialogText(shown);
                    text.Must().Contain("Folder");
                    text.Must().Contain(expected);
                    text.Must().Contain("source-folder");
                    text.Must().Contain("dest-folder");
                });

                var kept = PressConflict(sourcePath, destinationPath, Key.Enter, PhysicalKey.Enter);
                kept.Must().NotBeNull();
                kept!.Choice.Must().Be(FileConflictChoice.KeepBoth);

                PressConflict(sourcePath, destinationPath, Key.Escape, PhysicalKey.Escape).Must().BeNull();
            });
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { /* best-effort */ }
        }
    }

    private static AvaloniaUserDialogService Service { get; } = new(new WindowOwnerContext());

    private static (Window Window, TaskCompletionSource<bool> Result) CreateConfirm(
        bool destructive,
        string? confirmLabel = null)
    {
        var result = new TaskCompletionSource<bool>();
        var window = Service.CreateConfirmWindow(
            "Title",
            "Message",
            confirmLabel,
            cancelLabel: null,
            destructive,
            result);
        return (window, result);
    }

    private static bool Press((Window Window, TaskCompletionSource<bool> Result) dialog, Key key, PhysicalKey physicalKey)
    {
        Show(dialog.Window, shown => shown.KeyPress(key, RawInputModifiers.None, physicalKey, keySymbol: null));
        dialog.Result.Task.IsCompleted.Must().BeTrue();
        return dialog.Result.Task.Result;
    }

    private static FileConflictResolution? PressConflict(
        string sourcePath,
        string destinationPath,
        Key key,
        PhysicalKey physicalKey)
    {
        var result = new TaskCompletionSource<FileConflictResolution?>();
        var window = Service.CreateConflictWindow(
            new FileConflictInfo(sourcePath, destinationPath, IsDirectory: true),
            canApplyToAll: false,
            result);
        Show(window, shown => shown.KeyPress(key, RawInputModifiers.None, physicalKey, keySymbol: null));
        result.Task.IsCompleted.Must().BeTrue();
        return result.Task.Result;
    }

    private static void Show(Window window, Action<Window> action)
    {
        window.Show();
        try
        {
            action(window);
        }
        finally
        {
            window.Close();
        }
    }

    private static Button Button(Window window, string label)
        => window.GetVisualDescendants().OfType<Button>().Single(button => button.Content as string == label);

    private static string DialogText(Window window)
        => string.Join("\n", window.GetVisualDescendants().OfType<TextBlock>().Select(block => block.Text));
}
