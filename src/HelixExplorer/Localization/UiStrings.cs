namespace HelixExplorer.Localization;

/// <summary>
/// Centralized user-facing strings. Keeping them in one place makes future localization,
/// accessibility review, and consistency changes much easier.
/// </summary>
public static class UiStrings
{
    public static string ClipboardHasNoFiles => "Clipboard has no files";

    public static string PasteFailed => "Paste failed";

    public static string DropFailed => "Drop failed";

    public static string DeleteFailed => "Delete failed";

    public static string RenameFailed => "Rename failed";

    public static string NewFolderFailed => "Could not create folder";

    public static string CompressToZipFailed => "Could not create archive";

    public static string ExtractFailed => "Could not extract";

    public static string OpenInTerminalFailed => "Could not open terminal";

    public static string CopyPathFailed => "Could not copy path";

    public static string ShowPropertiesFailed => "Could not open properties";

    public static string ShowMoreOptionsFailed => "Could not show more options";

    public static string ListBranchesFailed => "Could not list branches";

    public static string CheckoutBranchFailed(string branch) => $"Could not checkout {branch}";

    public static string MovedItems(int count) => $"Moved {count} item{(count == 1 ? "" : "s")}";

    public static string CopiedItems(int count) => $"Copied {count} item{(count == 1 ? "" : "s")}";

    public static string MovingItems => "Moving items…";

    public static string CopyingItems => "Copying items…";

    public static string DeletingItems => "Deleting items…";

    public static string DeletedItems(int count) => $"Deleted {count} item{(count == 1 ? "" : "s")}";

    public static string NoItemsDeleted => "No items were deleted";

    public static string PermanentlyDeleteTitle => "Permanently delete?";

    public static string PermanentlyDeleteMessage => "Selected items will be permanently deleted and cannot be restored.";

    public static string EmptyRecycleBinTitle => "Empty Recycle Bin?";

    public static string EmptyRecycleBinMessage => "All items in the Recycle Bin will be permanently deleted.";

    public static string RecycleBinEmptied => "Recycle Bin emptied";

    public static string RestoredFromRecycleBin => "Restored selected item(s)";

    public static string RestoreFailed => "Restore failed";

    public static string EmptyRecycleBinFailed => "Empty Recycle Bin failed";

    public static string PathCopied => "Path copied";

    public static string PathsCopied => "Paths copied";

    public static string Extracted => "Extracted";

    public static string CreatedArchive(string name) => $"Created {name}";

    public static string FolderColored(string name) => $"Colored {name}";

    public static string FolderColorCleared(string name) => $"Cleared color for {name}";

    public static string NoItemsCopied => "No items copied";

    public static string NetworkDiscoveryBanner => "Discovering network shares…";

    public static string NetworkNoSharesFound => "No network shares discovered";

    public static string NetworkDiscoveryFailed => "Network discovery unavailable";
}
