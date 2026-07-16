using HelixExplorer.Core.Infrastructure;
using Microsoft.Extensions.Logging;
using CoreDragDropEffects = HelixExplorer.Core.Infrastructure.DragDropEffects;

namespace HelixExplorer.Windows.Shell;

/// <summary>
/// Native Windows drag-out via WinForms <see cref="System.Windows.Forms.DataObject"/> and a
/// handle-only control's modal OLE <see cref="System.Windows.Forms.Control.DoDragDrop"/> loop
/// (no visible window).
///
/// <para>
/// Firefox's <c>nsDragService</c> only inspects <c>CF_HDROP</c> for native file drag-out; the
/// per-item Avalonia DataTransfer payload (a single-file shell descriptor) is rejected, which is
/// why drag-out to Chrome worked but Firefox did not. Building one native <c>CF_HDROP</c> over
/// every path fixes Firefox, Edge, and Explorer in one go.
/// </para>
/// </summary>
public sealed class WinFormsExternalFileDragService(ILogger<WinFormsExternalFileDragService> logger)
    : IExternalFileDragService
{
    private const int DropEffectCopy = 1;

    public CoreDragDropEffects DoDragDrop(IReadOnlyList<string> physicalPaths, CoreDragDropEffects allowedEffects)
    {
        if (physicalPaths.Count == 0)
            return CoreDragDropEffects.None;

        try
        {
            // WinForms DataObject implements Win32 IDataObject so OLE can marshal to out-of-process
            // drop targets (Firefox, Chrome, Edge).
            var data = new System.Windows.Forms.DataObject();

            data.SetData(System.Windows.Forms.DataFormats.FileDrop, physicalPaths.ToArray());

            // DROPEFFECT_COPY so browser upload fields accept the drop instead of refusing non-copy.
            data.SetData("Preferred DropEffect", new MemoryStream(BitConverter.GetBytes(DropEffectCopy)));

            // Older targets probe FileNameW before CF_HDROP.
            if (physicalPaths.Count == 1)
            {
                data.SetData("FileNameW", physicalPaths[0]);
            }

            // Handle-only control: DoDragDrop needs an HWND and spins a modal OLE loop on this STA thread.
            using var host = new System.Windows.Forms.Control
            {
                Width = 0,
                Height = 0,
                Visible = false
            };
            _ = host.Handle;

            var winformsEffects = (System.Windows.Forms.DragDropEffects)allowedEffects;
            var result = host.DoDragDrop(data, winformsEffects);
            return (CoreDragDropEffects)result;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Native drag-out failed");
            return CoreDragDropEffects.None;
        }
    }
}