using System.IO;
using System.Globalization;
using System.Text.Json;
using System.Windows.Threading;

namespace RtlTerminal;

/// <summary>Raised once when a watched X Agents CLI lastExit record changes to a completed exit.</summary>
public sealed class AgentSessionCompletedEventArgs : EventArgs
{
    public AgentSessionCompletedEventArgs(string sessionId, string agentKey, int exitCode, DateTimeOffset completedAt)
    {
        SessionId = sessionId;
        AgentKey = agentKey;
        ExitCode = exitCode;
        CompletedAt = completedAt;
    }

    public string SessionId { get; }
    public string AgentKey { get; }
    public int ExitCode { get; }
    public DateTimeOffset CompletedAt { get; }
}

/// <summary>
/// Polls only the lastExit map in xagent-state.json. Call from the WPF dispatcher thread;
/// runtimePath may be the X Agents CLI runtime directory or the state file itself.
/// </summary>
public sealed class AgentCompletionMonitor : IDisposable
{
    private const string StateFileName = "xagent-state.json";
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _timer;
    private readonly Dictionary<(string SessionId, string AgentKey), WatchedSession> _sessions = [];
    private bool _disposed;

    public AgentCompletionMonitor(TimeSpan? pollInterval = null, Dispatcher? dispatcher = null)
    {
        _dispatcher = dispatcher ?? Dispatcher.CurrentDispatcher;
        var interval = pollInterval ?? TimeSpan.FromSeconds(1);
        if (interval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(pollInterval), "Poll interval must be positive.");

        _timer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher)
        {
            Interval = interval
        };
        _timer.Tick += OnTimerTick;
    }

    public event EventHandler<AgentSessionCompletedEventArgs>? SessionCompleted;

    /// <summary>Starts watching a session/provider key. The existing exit, if any, becomes the baseline.</summary>
    public void WatchSession(string sessionId, string agentKey, string runtimePath)
    {
        _dispatcher.VerifyAccess();
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(agentKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimePath);

        var statePath = Path.GetFileName(runtimePath).Equals(StateFileName, StringComparison.OrdinalIgnoreCase)
            ? runtimePath
            : Path.Combine(runtimePath, StateFileName);
        var key = (sessionId, agentKey);
        _sessions[key] = new WatchedSession(sessionId, agentKey, statePath)
        {
            LastExit = agentKey == "*" ? null : TryReadLastExit(statePath, agentKey),
            LastExits = agentKey == "*" ? ReadAllLastExits(statePath) : []
        };
        if (!_timer.IsEnabled)
            _timer.Start();
    }

    public void UnwatchSession(string sessionId, string agentKey)
    {
        _dispatcher.VerifyAccess();
        if (_disposed) return;

        _sessions.Remove((sessionId, agentKey));
        if (_sessions.Count == 0)
            _timer.Stop();
    }

    public void Dispose()
    {
        _dispatcher.VerifyAccess();
        if (_disposed) return;
        _disposed = true;
        _timer.Stop();
        _timer.Tick -= OnTimerTick;
        _sessions.Clear();
        SessionCompleted = null;
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        foreach (var watched in _sessions.Values.ToArray())
        {
            if (watched.AgentKey == "*")
            {
                foreach (var (agentKey, agentExit) in ReadAllLastExits(watched.StatePath))
                {
                    if (watched.LastExits.TryGetValue(agentKey, out var old) && old == agentExit)
                        continue;
                    watched.LastExits[agentKey] = agentExit;
                    SessionCompleted?.Invoke(this, new AgentSessionCompletedEventArgs(
                        watched.SessionId, agentKey, agentExit.ExitCode, agentExit.CompletedAt));
                }
                continue;
            }
            var current = TryReadLastExit(watched.StatePath, watched.AgentKey);
            if (current is null || current == watched.LastExit)
                continue;

            // Consume before raising the event so reentrant handlers cannot emit it twice.
            watched.LastExit = current;
            SessionCompleted?.Invoke(this, new AgentSessionCompletedEventArgs(
                watched.SessionId, watched.AgentKey, current.ExitCode, current.CompletedAt));
        }
    }

    private static LastExit? TryReadLastExit(string statePath, string agentKey)
    {
        try
        {
            using var stream = new FileStream(statePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var document = JsonDocument.Parse(stream);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("lastExit", out var lastExit) || lastExit.ValueKind != JsonValueKind.Object ||
                !lastExit.TryGetProperty(agentKey, out var entry) || !TryParseLastExit(entry, out var record))
                return null;

            // A null code is used by X Agents CLI for an abruptly closed tab, not a completed process.
            return record;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            // Missing, transiently partial, or malformed state is intentionally ignored and retried next tick.
            return null;
        }
    }

    private static Dictionary<string, LastExit> ReadAllLastExits(string statePath)
    {
        var result = new Dictionary<string, LastExit>(StringComparer.Ordinal);
        try
        {
            using var stream = new FileStream(statePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var document = JsonDocument.Parse(stream);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("lastExit", out var lastExit) || lastExit.ValueKind != JsonValueKind.Object)
                return result;
            foreach (var property in lastExit.EnumerateObject())
            {
                if (TryParseLastExit(property.Value, out var lastExitRecord))
                    result[property.Name] = lastExitRecord;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            // Missing, transiently partial, or malformed state is retried on the next tick.
        }
        return result;
    }

    private static bool TryParseLastExit(JsonElement entry, out LastExit lastExit)
    {
        lastExit = default!;
        if (entry.ValueKind != JsonValueKind.Object ||
            !entry.TryGetProperty("code", out var code) || code.ValueKind != JsonValueKind.Number ||
            !code.TryGetInt32(out var exitCode) ||
            !entry.TryGetProperty("at", out var at) || at.ValueKind != JsonValueKind.String ||
            !DateTimeOffset.TryParse(at.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var completedAt))
            return false;

        lastExit = new LastExit(exitCode, completedAt.ToUniversalTime());
        return true;
    }

    private sealed class WatchedSession(string sessionId, string agentKey, string statePath)
    {
        public string SessionId { get; } = sessionId;
        public string AgentKey { get; } = agentKey;
        public string StatePath { get; } = statePath;
        public LastExit? LastExit { get; set; }
        public Dictionary<string, LastExit> LastExits { get; set; } = new(StringComparer.Ordinal);
    }

    private sealed record LastExit(int ExitCode, DateTimeOffset CompletedAt);
}
