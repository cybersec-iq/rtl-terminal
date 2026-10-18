using System.IO;
using System.Security.Cryptography;

namespace RtlTerminal;

internal static class AgentEngineDeployment
{
    private static readonly string[] RuntimeFiles =
    ["xagent.js", "route-ui.js", "route-hub.js", "generic-broker.js", "registry.json"];

    public static string EnsureDeployed(string sessionId)
    {
        if (!Guid.TryParseExact(sessionId, "N", out var parsedSessionId))
            throw new ArgumentException("A valid project session ID is required.", nameof(sessionId));

        var bundledRoot = Path.Combine(AppContext.BaseDirectory, "AgentEngine");
        var dataRoot = Environment.GetEnvironmentVariable("RTLTERMINAL_DATA_DIR");
        if (string.IsNullOrWhiteSpace(dataRoot))
            dataRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AryanIQ", "Rtl Terminal");
        var localRoot = Path.Combine(dataRoot, "XAgentsRuntime", "project-sessions", parsedSessionId.ToString("N"));

        foreach (var file in RuntimeFiles)
        {
            var source = Path.Combine(bundledRoot, file);
            if (!File.Exists(source))
                throw new FileNotFoundException("The bundled X Agents CLI engine is incomplete.", source);
        }

        Directory.CreateDirectory(localRoot);

        foreach (var file in RuntimeFiles)
        {
            var source = Path.Combine(bundledRoot, file);
            var destination = Path.Combine(localRoot, file);
            if (!File.Exists(destination) || !SameHash(source, destination))
                File.Copy(source, destination, overwrite: true);
            if (!SameHash(source, destination))
                throw new IOException("The deployed X Agents CLI engine file failed its SHA-256 check.");
        }
        return localRoot;
    }

    public static void DeleteSessionRuntime(string sessionId)
    {
        if (!Guid.TryParseExact(sessionId, "N", out var parsedSessionId))
            throw new ArgumentException("A valid project session ID is required.", nameof(sessionId));
        var dataRoot = Environment.GetEnvironmentVariable("RTLTERMINAL_DATA_DIR");
        if (string.IsNullOrWhiteSpace(dataRoot))
            dataRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AryanIQ", "Rtl Terminal");
        var sessionsRoot = Path.GetFullPath(Path.Combine(dataRoot, "XAgentsRuntime", "project-sessions"));
        var sessionRoot = Path.GetFullPath(Path.Combine(sessionsRoot, parsedSessionId.ToString("N")));
        if (!sessionRoot.StartsWith(sessionsRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new IOException("The session runtime path escaped the project-sessions data folder.");
        if (Directory.Exists(sessionRoot)) Directory.Delete(sessionRoot, recursive: true);
    }

    private static bool SameHash(string left, string right)
    {
        using var leftStream = File.OpenRead(left);
        using var rightStream = File.OpenRead(right);
        return CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(leftStream), SHA256.HashData(rightStream));
    }
}
