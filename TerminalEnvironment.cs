using System.Collections;
using System.IO;

namespace RtlTerminal;

public static class TerminalEnvironment
{
    public static Dictionary<string, string> Create(IDictionary inherited, IReadOnlyDictionary<string, string>? overrides = null)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry entry in inherited) values[(string)entry.Key] = (string)entry.Value!;
        if (overrides is not null)
            foreach (var pair in overrides) values[pair.Key] = pair.Value;
        values["COLORTERM"] = "truecolor";
        values["TERM_PROGRAM"] = "RtlTerminal";
        values[ShellPath.PercentVariable] = "%";

        if (overrides is not null && overrides.TryGetValue("XAGENT_SHIM_ROOT", out var shimRoot) && !string.IsNullOrWhiteSpace(shimRoot))
        {
            var path = values.GetValueOrDefault("PATH", "");
            var firstPath = path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (!string.Equals(firstPath, shimRoot, StringComparison.OrdinalIgnoreCase))
                values["PATH"] = shimRoot + Path.PathSeparator + path;
        }

        var pathVariables = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "COLORTERM", "TERM_PROGRAM", "XAGENT_ENGINE_ROOT", "XAGENT_SHIM_ROOT",
            "XAGENT_PROJECT_PATHS_FILE", "XAGENT_TRANSCRIPT_HANDOFF_FILE",
            "XAGENT_NO_FCM_AUTOSTART", "XAGENT_RTLTERMINAL", "XAGENT_PROJECT_SESSION_ID"
        };
        var entries = values.GetValueOrDefault("WSLENV", "").Split(':', StringSplitOptions.RemoveEmptyEntries)
            .Where(e => !pathVariables.Contains(e.Split('/')[0]));
        var wslEntries = new List<string> { "COLORTERM", "TERM_PROGRAM" };
        var wslPathVariables = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "XAGENT_ENGINE_ROOT", "XAGENT_SHIM_ROOT", "XAGENT_PROJECT_PATHS_FILE", "XAGENT_TRANSCRIPT_HANDOFF_FILE"
        };
        foreach (var name in new[]
        {
            "XAGENT_ENGINE_ROOT", "XAGENT_SHIM_ROOT", "XAGENT_PROJECT_PATHS_FILE", "XAGENT_TRANSCRIPT_HANDOFF_FILE",
            "XAGENT_NO_FCM_AUTOSTART", "XAGENT_RTLTERMINAL", "XAGENT_PROJECT_SESSION_ID"
        })
            if (values.ContainsKey(name)) wslEntries.Add(name + (wslPathVariables.Contains(name) ? "/p" : ""));
        values["WSLENV"] = string.Join(':', entries.Concat(wslEntries));
        return values;
    }
    public static string Block(IReadOnlyDictionary<string, string>? overrides = null) => string.Join('\0', Create(Environment.GetEnvironmentVariables(), overrides)
        .OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase).Select(p => p.Key + "=" + p.Value)) + "\0\0";
}
