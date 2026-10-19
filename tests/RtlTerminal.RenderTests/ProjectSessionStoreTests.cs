using System.IO;
using System.Text.Json;
using RtlTerminal;
using System.Windows.Threading;
using Microsoft.Data.Sqlite;

internal static partial class Program
{
    private static void CheckProjectSessionStore()
    {
        var directory = Path.Combine(Path.GetTempPath(), "rtl-project-store-test-" + Guid.NewGuid().ToString("N"));
        var database = Path.Combine(directory, "projects.db");
        var previousDataDirectory = Environment.GetEnvironmentVariable("RTLTERMINAL_DATA_DIR");
        Environment.SetEnvironmentVariable("RTLTERMINAL_DATA_DIR", directory);
        try
        {
            var store = new ProjectSessionStore(database);
            var created = store.CreateProjectWithFirstSession(
                "Test project", [directory, Path.GetTempPath()], "Main", "PowerShell", "xagent", [directory]);
            store.SetActiveSession(created.Project.Id, created.Session.Id);
            store.UpdateTranscript(created.Session.Id, "persisted terminal output");

            var session = store.GetSessions(created.Project.Id).Single();
            AssertProject(session.Name == "Main", "session name was not stored");
            AssertProject(session.Paths.Count == 1 && session.Paths[0] == directory, "session paths were not stored");
            AssertProject(session.Transcript == "persisted terminal output", "transcript was not stored");
            AssertProject(store.GetProjects().Single().ActiveSessionId == session.Id, "active session was not stored");
            var latest = store.GetMostRecentlyActiveSession();
            AssertProject(latest?.Project.Id == created.Project.Id && latest.Value.Session.Id == session.Id,
                "the active project session was not available for restart recovery");

            var second = store.CreateProjectWithFirstSession(
                "Second project", [directory], "Second session", "CommandPrompt", "", [directory]);
            Thread.Sleep(5);
            store.SetActiveSession(second.Project.Id, second.Session.Id);
            latest = store.GetMostRecentlyActiveSession();
            AssertProject(latest?.Project.Id == second.Project.Id && latest.Value.Session.Id == second.Session.Id,
                "restart recovery did not select the most recently active project session");
            store.DeleteProject(second.Project.Id);

            var completedAt = DateTimeOffset.UtcNow;
            AssertProject(store.RecordAgentCompletion(session.Id, "agent-codex", completedAt), "first completion was not recorded");
            AssertProject(!store.RecordAgentCompletion(session.Id, "agent-codex", completedAt), "duplicate completion was counted twice");
            AssertProject(store.GetSessions(created.Project.Id).Single().CompletionCount == 1, "completion counter was not durable");
            store.ClearCompletionCount(session.Id);
            AssertProject(store.GetSessions(created.Project.Id).Single().CompletionCount == 0, "completion acknowledgement was not saved");

            var removable = store.CreateSession(created.Project.Id, "Disposable", "PowerShell", "xagent", [directory]);
            store.SetActiveSession(created.Project.Id, removable.Id);
            var projectFolderSentinel = Path.Combine(directory, "keep-this-folder.txt");
            File.WriteAllText(projectFolderSentinel, "folder contents stay on disk");
            var sessionRuntime = Path.Combine(directory, "XAgentsRuntime", "project-sessions", removable.Id);
            Directory.CreateDirectory(sessionRuntime);
            File.WriteAllText(Path.Combine(sessionRuntime, "handoff.md"), "session-only data");
            AgentEngineDeployment.DeleteSessionRuntime(removable.Id);
            AssertProject(!Directory.Exists(sessionRuntime), "deleted session runtime data was retained");
            AssertProject(File.Exists(projectFolderSentinel), "deleting session runtime data removed project folder contents");
            store.DeleteSession(removable.Id);
            AssertProject(store.GetSessions(created.Project.Id).Count == 1, "deleted session record was retained");
            AssertProject(store.GetProjects().Single().ActiveSessionId == session.Id, "deleting the active session did not select a surviving session");
            AssertProject(File.Exists(Path.Combine(directory, "projects.db")), "session deletion removed project data from disk");

            store.DeleteProject(created.Project.Id);
            AssertProject(store.GetProjects().Count == 0, "deleted project record was retained");
            AssertProject(store.GetSessions(created.Project.Id).Count == 0, "project deletion did not cascade to its session records");
            AssertProject(Directory.Exists(directory), "project deletion removed its real folder");

            Console.WriteLine("PASS project/session persistence, completion tracking, and metadata-only project/session deletion");
        }
        finally
        {
            Environment.SetEnvironmentVariable("RTLTERMINAL_DATA_DIR", previousDataDirectory);
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private static void AssertProject(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static void CheckAgentCompletionMonitor()
    {
        var directory = Path.Combine(Path.GetTempPath(), "rtl-agent-monitor-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var state = Path.Combine(directory, "xagent-state.json");
        var dispatcher = Dispatcher.CurrentDispatcher;
        var wildcardCompletedCount = 0;
        var exactCompletedCount = 0;
        var receivedKey = "";
        try
        {
            File.WriteAllText(state, "{\"lastExit\":{}}");
            using var monitor = new AgentCompletionMonitor(TimeSpan.FromMilliseconds(30), dispatcher);
            monitor.SessionCompleted += (_, completed) =>
            {
                if (completed.SessionId == "wildcard-session") wildcardCompletedCount++;
                if (completed.SessionId == "exact-session") exactCompletedCount++;
                receivedKey = completed.AgentKey;
            };
            monitor.WatchSession("wildcard-session", "*", directory);
            monitor.WatchSession("exact-session", "agent-codex", directory);
            File.WriteAllText(state, "{\"lastExit\":{\"agent-codex\":{\"code\":null,\"at\":null}}}");
            PumpDispatcher(TimeSpan.FromMilliseconds(200));
            if (wildcardCompletedCount != 0 || exactCompletedCount != 0)
                throw new Exception("null X Agents CLI exit code was treated as a completed process");

            var at = DateTimeOffset.UtcNow;
            var exits = new Dictionary<string, object> { ["agent-codex"] = new { code = 0, at = at.ToString("O") } };
            File.WriteAllText(state, JsonSerializer.Serialize(new { lastExit = exits }));
            PumpDispatcher(TimeSpan.FromMilliseconds(500));
            if (wildcardCompletedCount != 1 || exactCompletedCount != 1 || receivedKey != "agent-codex")
                throw new Exception("completion polling did not recover after null and report the updated X Agents CLI provider record");
            PumpDispatcher(TimeSpan.FromMilliseconds(120));
            if (wildcardCompletedCount != 1 || exactCompletedCount != 1)
                throw new Exception("the same provider completion was emitted more than once");
            Console.WriteLine("PASS X Agents CLI null exit rejection, exact/wildcard polling, recovery and duplicate suppression");
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private static void PumpDispatcher(TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher.CurrentDispatcher) { Interval = duration };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }
}
