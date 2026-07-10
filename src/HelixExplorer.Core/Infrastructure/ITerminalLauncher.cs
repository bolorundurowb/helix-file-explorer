namespace HelixExplorer.Core.Infrastructure;

public interface ITerminalLauncher
{
    bool TryOpenInDirectory(string directoryPath);
}
