namespace HelixExplorer.Localization;

/// <summary>Single table of user-facing strings for localization review.</summary>
public static class UiStrings
{
    public static string ClipboardHasNoFiles => "Clipboard has no files";

    public static string PasteFailed => "Paste failed";

    public static string DropFailed => "Drop failed";

    public static string DeleteFailed => "Delete failed";

    public static string OperationCancelled => "Operation cancelled";

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

    public static string NewFolderDefaultName => "New Folder";

    public static string ExtractLocationUnknown => "Could not determine extract location";

    public static string RestorePartiallyFailed(int failed)
        => $"Could not restore {failed} item{(failed == 1 ? "" : "s")}";

    public static string Undoing => "Undoing…";

    public static string Redoing => "Redoing…";

    public static string NothingToUndo => "Nothing to undo";

    public static string NothingToRedo => "Nothing to redo";

    public static string UndoFailed => "Undo failed";

    public static string RedoFailed => "Redo failed";

    public static string UndoBusy => "Wait for the current operation to finish";

    public static string UndoStale => "The files have changed since then";

    public static string UndoStaleDetail =>
        "The items involved have been moved, renamed, or deleted since that operation, so it can no longer be reversed.";

    public static string UndidOperation(string description) => $"Undid {description}";

    public static string RedidOperation(string description) => $"Redid {description}";

    public static string UndoCopyDescription(int count) => $"copy of {count} item{(count == 1 ? "" : "s")}";

    public static string UndoMoveDescription(int count) => $"move of {count} item{(count == 1 ? "" : "s")}";

    public static string UndoDeleteDescription(int count) => $"delete of {count} item{(count == 1 ? "" : "s")}";

    public static string UndoRenameDescription(string name) => $"rename to {name}";

    public static string UndoNewFolderDescription => "new folder";

    public static string UndoExtractDescription(int count) => $"extract of {count} item{(count == 1 ? "" : "s")}";

    public static string UndoCompressDescription(string name) => $"creating {name}";
}
