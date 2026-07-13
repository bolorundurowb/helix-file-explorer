namespace HelixExplorer.Core.Theming;

public interface IUiFontService
{
    UiFontFamily Current { get; }

    void ApplyFont(UiFontFamily font);
}
