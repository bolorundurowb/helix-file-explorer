using System.Text.Json;

namespace HelixExplorer.Core.Infrastructure;

public static class AgentDebugLog
{
    private const string SessionId = "9dbecb";
    private const string LogFileName = "debug-9dbecb.log";

    public static void Write(string runId, string hypothesisId, string location, string message, object data)
    {
        try
        {
            var payload = new
            {
                sessionId = SessionId,
                runId,
                hypothesisId,
                location,
                message,
                data,
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
            File.AppendAllText(ResolveLogPath(), JsonSerializer.Serialize(payload) + Environment.NewLine);
        }
        catch
        {
        }
    }

    private static string ResolveLogPath()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "HelixExplorer.sln")))
                return Path.Combine(directory.FullName, LogFileName);
        }

        return LogFileName;
    }
}
