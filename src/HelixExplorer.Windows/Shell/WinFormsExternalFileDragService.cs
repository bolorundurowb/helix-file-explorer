using HelixExplorer.Core.Infrastructure;
using Microsoft.Extensions.Logging;
using CoreDragDropEffects = HelixExplorer.Core.Infrastructure.DragDropEffects;

namespace HelixExplorer.Windows.Shell;

/// <summary>
/// Native Windows drag-out service. Builds a WinForms <see cref="System.Windows.Forms.DataObject"/>
/// populated with <c>CF_HDROP</c> (a single HDROP listing every dragged path), the
/// <c>Preferred DropEffect</c> registered clipboard format set to <c>DROPEFFECT_COPY</c>, and the
/// legacy single-file <c>FileNameW</c> descriptor for older drop targets. The drag loop is started
/// via <see cref="System.Windows.Forms.Control.DoDragDrop"/> from a handle-only control on the UI
/// thread — no visible window is created.
///
/// <para>
/// Firefox's <c>nsDragService</c> only inspects <c>CF_HDROP</c> for native file drag-out; the
/// per-item Avolon DataTransfer payload (a single-file shell descriptor) is rejected, which is
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
            // Build the native data object using WinForms — its DataObject implements Win32
            // IDataObject semantics through FORMATETC/STGMEDIUM, which the OLE drag/drop helpers
            // marshal to out-of-process drop targets (Firefox, Chrome, Edge) correctly.
            var data = new System.Windows.Forms.DataObject();

            // CF_HDROP — the canonical file-list format every browser drop target understands.
            // A single HDROP containing ALL paths. Add before the legacy FileNameW so targets that
            // prefer CF_HDROP always see the full list.
            data.SetData(System.Windows.Forms.DataFormats.FileDrop, physicalPaths.ToArray());

            // Preferred DropEffect — DROPEFFECT_COPY so browser upload fields treat the drop as an
            // upload rather than refuse it for "non-copy" semantics. WinForms registers the
            // clipboard format by name and we write a DWORD (4-byte little-endian).
            data.SetData("Preferred DropEffect", new MemoryStream(BitConverter.GetBytes(DropEffectCopy)));

            // Single-file back-compat: older targets probe FileNameW before CF_HDROP.
            if (physicalPaths.Count == 1)
            {
                data.SetData("FileNameW", physicalPaths[0]);
            }

            // Use a handle-only WinForms control to start the drag. DoDragDrop spins a modal OLE
            // loop on the current STA thread — no visible window is required or created.
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