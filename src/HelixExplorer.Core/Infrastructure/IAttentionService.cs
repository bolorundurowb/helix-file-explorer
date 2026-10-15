namespace HelixExplorer.Core.Infrastructure;

/// <summary>
/// Window-level signals for when the user is not looking at the window: taskbar progress and error
/// state, a toast when an operation ends, and a flash when a decision is waiting.
/// </summary>
/// <remarks>
/// One instance per window. Callers do not check focus themselves; the "if inactive" members decide,
/// so a focused window is never interrupted by its own toast or flash.
/// </remarks>
public interface IAttentionService
{
    /// <param name="fraction">0 to 1, or null while the total is unknown.</param>
    void ShowProgress(double? fraction);

    /// <summary>Keeps the taskbar button in the error state until <see cref="ClearProgress"/>.</summary>
    void ShowError();

    void ClearProgress();

    void NotifyIfInactive(string title, string message, bool isError);

    void FlashIfInactive();
}
