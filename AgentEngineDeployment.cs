using System.IO;
using System.Security.Cryptography;

namespace RtlTerminal;

internal static class AgentEngineDeployment
{
    private static readonly string[] RuntimeFiles =
    ["xagent.js", "codex-account-status.js", "route-ui.js", "route-hub.js", "generic-broker.js", "registry.json"];
    private static readonly string[] ShellShimFiles = ["xagent.cmd", "xagent.ps1", "xagent"];

    public static string EnsureDeployed(string sessionId)
    {
        if (!Guid.TryParseExact(sessionId, "N", out var parsedSessionId))
            throw new ArgumentException("A valid project session ID is required.", nameof(sessionId));

        var bundledRoot = Path.Combine(AppContext.BaseDirectory, "AgentEngine");
        var dataRoot = GetDataRoot();
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
            CopyVerified(source, destination, "The deployed X Agents CLI engine file failed its SHA-256 check.");
        }
        EnsureShellShims();
        return localRoot;
    }

    public static string EnsureGlobalRuntime()
    {
        var bundledRoot = Path.Combine(AppContext.BaseDirectory, "AgentEngine");
        var runtimeRoot = GetGlobalRuntimeRoot();
        foreach (var file in RuntimeFiles)
        {
            var source = Path.Combine(bundledRoot, file);
            if (!File.Exists(source))
                throw new FileNotFoundException("The bundled X Agents CLI engine is incomplete.", source);
        }

        Directory.CreateDirectory(runtimeRoot);
        foreach (var file in RuntimeFiles)
        {
            var source = Path.Combine(bundledRoot, file);
            var destination = Path.Combine(runtimeRoot, file);
            CopyVerified(source, destination, "The deployed global X Agents CLI engine file failed its SHA-256 check.");
        }
        EnsureShellShims();
        return runtimeRoot;
    }

    public static string GetGlobalRuntimeRoot() => Path.Combine(GetDataRoot(), "XAgentsRuntime", "global");

    public static Dictionary<string, string> CreateShellEnvironment(string? runtimeRoot = null) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["XAGENT_ENGINE_ROOT"] = runtimeRoot ?? GetGlobalRuntimeRoot(),
            ["XAGENT_SHIM_ROOT"] = GetShellShimRoot()
        };

    public static string EnsureShellShims()
    {
        var bundledRoot = Path.Combine(AppContext.BaseDirectory, "AgentEngine", "Shims");
        foreach (var file in ShellShimFiles)
        {
            var source = Path.Combine(bundledRoot, file);
            if (!File.Exists(source))
                throw new FileNotFoundException("The bundled X Agents shell shims are incomplete.", source);
        }

        var localRoot = GetShellShimRoot();
        Directory.CreateDirectory(localRoot);
        foreach (var file in ShellShimFiles)
        {
            var source = Path.Combine(bundledRoot, file);
            var destination = Path.Combine(localRoot, file);
            CopyVerified(source, destination, "The deployed X Agents shell shim failed its SHA-256 check.");
        }
        return localRoot;
    }

    public static string GetShellShimRoot() => Path.Combine(GetDataRoot(), "XAgentsRuntime", "bin");

    public static void DeleteSessionRuntime(string sessionId)
    {
        if (!Guid.TryParseExact(sessionId, "N", out var parsedSessionId))
            throw new ArgumentException("A valid project session ID is required.", nameof(sessionId));
        var dataRoot = GetDataRoot();
        var sessionsRoot = Path.GetFullPath(Path.Combine(dataRoot, "XAgentsRuntime", "project-sessions"));
        var sessionRoot = Path.GetFullPath(Path.Combine(sessionsRoot, parsedSessionId.ToString("N")));
        if (!sessionRoot.StartsWith(sessionsRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new IOException("The session runtime path escaped the project-sessions data folder.");
        if (Directory.Exists(sessionRoot)) Directory.Delete(sessionRoot, recursive: true);
    }

    public static string GetDataRoot()
    {
        var dataRoot = Environment.GetEnvironmentVariable("RTLTERMINAL_DATA_DIR");
        if (string.IsNullOrWhiteSpace(dataRoot))
            dataRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AryanIQ", "Rtl Terminal");
        return Path.GetFullPath(dataRoot);
    }

    private static bool SameHash(string left, string right)
    {
        using var leftStream = File.OpenRead(left);
        using var rightStream = File.OpenRead(right);
        return CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(leftStream), SHA256.HashData(rightStream));
    }

    private static void CopyVerified(string source, string destination, string failureMessage)
    {
        if (File.Exists(destination) && SameHash(source, destination)) return;
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.Copy(source, temporary);
            if (!SameHash(source, temporary)) throw new IOException(failureMessage);
            File.Move(temporary, destination, overwrite: true);
            if (!SameHash(source, destination)) throw new IOException(failureMessage);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
        }
    }
}
