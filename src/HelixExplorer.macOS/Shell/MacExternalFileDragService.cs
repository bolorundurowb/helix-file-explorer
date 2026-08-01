using AppKit;
using Foundation;
using HelixExplorer.Core.Infrastructure;
using Microsoft.Extensions.Logging;

namespace HelixExplorer.macOS.Shell;

public sealed class MacExternalFileDragService(ILogger<MacExternalFileDragService> logger) : IExternalFileDragService
{
    public DragDropEffects DoDragDrop(IReadOnlyList<string> physicalPaths, DragDropEffects allowedEffects)
    {
        if (physicalPaths.Count == 0)
            return DragDropEffects.None;

        try
        {
            var pasteboard = NSPasteboard.FromName(NSPasteboardName.Drag.GetConstant()!);
            if (pasteboard is null)
                return DragDropEffects.None;

            pasteboard.ClearContents();

            var items = new List<NSPasteboardItem>(physicalPaths.Count);
            foreach (var path in physicalPaths)
            {
                using var url = NSUrl.FromFilename(path);
                if (url is null)
                    continue;
                var item = new NSPasteboardItem();
                item.SetStringForType(url.AbsoluteString!, NSPasteboardType.FileUrl.GetConstant()!);
                items.Add(item);
            }

            if (items.Count == 0)
                return DragDropEffects.None;

            pasteboard.WriteObjects([.. items]);

            return (allowedEffects & DragDropEffects.Copy) != 0
                ? DragDropEffects.Copy
                : allowedEffects;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "External drag failed");
            return DragDropEffects.None;
        }
    }
}