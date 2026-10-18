using System.Text.Json;
using System.IO;
using Microsoft.Data.Sqlite;

namespace RtlTerminal;

internal sealed record SavedProject(string Id, string Name, IReadOnlyList<string> Paths, string? ActiveSessionId = null);
internal sealed record SavedProjectSession(
    string Id,
    string ProjectId,
    string Name,
    string Profile,
    string Agent,
    IReadOnlyList<string> Paths,
    string Transcript = "",
    int CompletionCount = 0,
    string ObservedAgentExitsJson = "{}");

/// <summary>Persistent project and session metadata for the project tab workflow.</summary>
internal sealed class ProjectSessionStore
{
    private readonly string _connectionString;

    public ProjectSessionStore() : this(null) { }

    internal ProjectSessionStore(string? databasePath)
    {
        var configuredDataDirectory = Environment.GetEnvironmentVariable("RTLTERMINAL_DATA_DIR");
        var directory = Path.GetDirectoryName(databasePath) ??
            (!string.IsNullOrWhiteSpace(configuredDataDirectory)
                ? configuredDataDirectory
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AryanIQ", "Rtl Terminal"));
        Directory.CreateDirectory(directory);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath ?? Path.Combine(directory, "projects.db"),
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS Projects (
                Id TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                PathsJson TEXT NOT NULL,
                ActiveSessionId TEXT NULL,
                UpdatedAt TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS ProjectSessions (
                Id TEXT PRIMARY KEY,
                ProjectId TEXT NOT NULL REFERENCES Projects(Id) ON DELETE CASCADE,
                Name TEXT NOT NULL,
                Profile TEXT NOT NULL,
                Agent TEXT NOT NULL,
                PathsJson TEXT NOT NULL,
                Transcript TEXT NOT NULL DEFAULT '',
                CompletionCount INTEGER NOT NULL DEFAULT 0,
                ObservedAgentExitsJson TEXT NOT NULL DEFAULT '{}',
                UpdatedAt TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_ProjectSessions_ProjectId ON ProjectSessions(ProjectId);
            """;
        command.ExecuteNonQuery();
        using var projectColumns = connection.CreateCommand();
        projectColumns.CommandText = "PRAGMA table_info(Projects)";
        using (var projectReader = projectColumns.ExecuteReader())
        {
            var hasActiveSession = false;
            while (projectReader.Read())
                if (projectReader.GetString(1).Equals("ActiveSessionId", StringComparison.OrdinalIgnoreCase)) hasActiveSession = true;
            if (!hasActiveSession)
            {
                projectReader.Close();
                using var migrate = connection.CreateCommand();
                migrate.CommandText = "ALTER TABLE Projects ADD COLUMN ActiveSessionId TEXT NULL";
                migrate.ExecuteNonQuery();
            }
        }
        using var columns = connection.CreateCommand();
        columns.CommandText = "PRAGMA table_info(ProjectSessions)";
        using var reader = columns.ExecuteReader();
        var hasTranscript = false;
        while (reader.Read())
            if (reader.GetString(1).Equals("Transcript", StringComparison.OrdinalIgnoreCase)) hasTranscript = true;
        reader.Close();
        if (!hasTranscript)
        {
            using var migrate = connection.CreateCommand();
            migrate.CommandText = "ALTER TABLE ProjectSessions ADD COLUMN Transcript TEXT NOT NULL DEFAULT ''";
            migrate.ExecuteNonQuery();
        }
        using var sessionColumns = connection.CreateCommand();
        sessionColumns.CommandText = "PRAGMA table_info(ProjectSessions)";
        var hasCompletionCount = false;
        var hasObservedExits = false;
        using (var sessionReader = sessionColumns.ExecuteReader())
        {
            while (sessionReader.Read())
            {
                var name = sessionReader.GetString(1);
                if (name.Equals("CompletionCount", StringComparison.OrdinalIgnoreCase)) hasCompletionCount = true;
                if (name.Equals("ObservedAgentExitsJson", StringComparison.OrdinalIgnoreCase)) hasObservedExits = true;
            }
            sessionReader.Close();
        }
        if (!hasCompletionCount)
        {
            using var migrate = connection.CreateCommand();
            migrate.CommandText = "ALTER TABLE ProjectSessions ADD COLUMN CompletionCount INTEGER NOT NULL DEFAULT 0";
            migrate.ExecuteNonQuery();
        }
        if (!hasObservedExits)
        {
            using var migrate = connection.CreateCommand();
            migrate.CommandText = "ALTER TABLE ProjectSessions ADD COLUMN ObservedAgentExitsJson TEXT NOT NULL DEFAULT '{}'";
            migrate.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<SavedProject> GetProjects()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id, Name, PathsJson, ActiveSessionId FROM Projects ORDER BY Name COLLATE NOCASE";
        using var reader = command.ExecuteReader();
        var projects = new List<SavedProject>();
        while (reader.Read())
            projects.Add(new SavedProject(reader.GetString(0), reader.GetString(1), ReadPaths(reader.GetString(2)), reader.IsDBNull(3) ? null : reader.GetString(3)));
        return projects;
    }

    public SavedProject CreateProject(string name, IReadOnlyList<string> paths)
    {
        var project = new SavedProject(Guid.NewGuid().ToString("N"), name.Trim(), CleanPaths(paths));
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO Projects(Id,Name,PathsJson,ActiveSessionId,UpdatedAt) VALUES($id,$name,$paths,NULL,$time)";
        command.Parameters.AddWithValue("$id", project.Id);
        command.Parameters.AddWithValue("$name", project.Name);
        command.Parameters.AddWithValue("$paths", JsonSerializer.Serialize(project.Paths));
        command.Parameters.AddWithValue("$time", DateTimeOffset.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
        return project;
    }

    public (SavedProject Project, SavedProjectSession Session) CreateProjectWithFirstSession(
        string projectName, IReadOnlyList<string> projectPaths, string sessionName, string profile, string agent,
        IReadOnlyList<string> sessionPaths)
    {
        var projectId = Guid.NewGuid().ToString("N");
        var sessionId = Guid.NewGuid().ToString("N");
        var project = new SavedProject(projectId, projectName.Trim(), CleanPaths(projectPaths), sessionId);
        var session = new SavedProjectSession(sessionId, projectId, sessionName.Trim(), profile, agent, CleanPaths(sessionPaths));
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        using (var createProject = connection.CreateCommand())
        {
            createProject.Transaction = transaction;
            createProject.CommandText = "INSERT INTO Projects(Id,Name,PathsJson,ActiveSessionId,UpdatedAt) VALUES($id,$name,$paths,$session,$time)";
            createProject.Parameters.AddWithValue("$id", project.Id);
            createProject.Parameters.AddWithValue("$name", project.Name);
            createProject.Parameters.AddWithValue("$paths", JsonSerializer.Serialize(project.Paths));
            createProject.Parameters.AddWithValue("$session", session.Id);
            createProject.Parameters.AddWithValue("$time", DateTimeOffset.UtcNow.ToString("O"));
            createProject.ExecuteNonQuery();
        }
        using (var createSession = connection.CreateCommand())
        {
            createSession.Transaction = transaction;
            createSession.CommandText = "INSERT INTO ProjectSessions(Id,ProjectId,Name,Profile,Agent,PathsJson,Transcript,CompletionCount,ObservedAgentExitsJson,UpdatedAt) VALUES($id,$project,$name,$profile,$agent,$paths,'',0,'{}',$time)";
            createSession.Parameters.AddWithValue("$id", session.Id);
            createSession.Parameters.AddWithValue("$project", project.Id);
            createSession.Parameters.AddWithValue("$name", session.Name);
            createSession.Parameters.AddWithValue("$profile", session.Profile);
            createSession.Parameters.AddWithValue("$agent", session.Agent);
            createSession.Parameters.AddWithValue("$paths", JsonSerializer.Serialize(session.Paths));
            createSession.Parameters.AddWithValue("$time", DateTimeOffset.UtcNow.ToString("O"));
            createSession.ExecuteNonQuery();
        }
        transaction.Commit();
        return (project, session);
    }

    public void SetActiveSession(string projectId, string sessionId)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE Projects SET ActiveSessionId=$session,UpdatedAt=$time WHERE Id=$project";
        command.Parameters.AddWithValue("$project", projectId);
        command.Parameters.AddWithValue("$session", sessionId);
        command.Parameters.AddWithValue("$time", DateTimeOffset.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }

    public void UpdateProject(SavedProject project)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE Projects SET Name=$name,PathsJson=$paths,UpdatedAt=$time WHERE Id=$id";
        command.Parameters.AddWithValue("$id", project.Id);
        command.Parameters.AddWithValue("$name", project.Name.Trim());
        command.Parameters.AddWithValue("$paths", JsonSerializer.Serialize(CleanPaths(project.Paths)));
        command.Parameters.AddWithValue("$time", DateTimeOffset.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<SavedProjectSession> GetSessions(string projectId)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id,ProjectId,Name,Profile,Agent,PathsJson,Transcript,CompletionCount,ObservedAgentExitsJson FROM ProjectSessions WHERE ProjectId=$project ORDER BY UpdatedAt,Name COLLATE NOCASE";
        command.Parameters.AddWithValue("$project", projectId);
        using var reader = command.ExecuteReader();
        var sessions = new List<SavedProjectSession>();
        while (reader.Read())
            sessions.Add(new SavedProjectSession(reader.GetString(0), reader.GetString(1), reader.GetString(2),
                reader.GetString(3), reader.GetString(4), ReadPaths(reader.GetString(5)), reader.GetString(6), reader.GetInt32(7), reader.GetString(8)));
        return sessions;
    }

    public SavedProjectSession CreateSession(string projectId, string name, string profile, string agent, IReadOnlyList<string> paths)
    {
        var session = new SavedProjectSession(Guid.NewGuid().ToString("N"), projectId, name.Trim(), profile, agent, CleanPaths(paths));
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO ProjectSessions(Id,ProjectId,Name,Profile,Agent,PathsJson,Transcript,CompletionCount,ObservedAgentExitsJson,UpdatedAt) VALUES($id,$project,$name,$profile,$agent,$paths,$transcript,0,'{}',$time)";
        command.Parameters.AddWithValue("$id", session.Id);
        command.Parameters.AddWithValue("$project", projectId);
        command.Parameters.AddWithValue("$name", session.Name);
        command.Parameters.AddWithValue("$profile", session.Profile);
        command.Parameters.AddWithValue("$agent", session.Agent);
        command.Parameters.AddWithValue("$paths", JsonSerializer.Serialize(session.Paths));
        command.Parameters.AddWithValue("$transcript", session.Transcript);
        command.Parameters.AddWithValue("$time", DateTimeOffset.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
        return session;
    }

    public void UpdateSession(SavedProjectSession session)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE ProjectSessions SET Name=$name,Profile=$profile,Agent=$agent,PathsJson=$paths,UpdatedAt=$time WHERE Id=$id";
        command.Parameters.AddWithValue("$id", session.Id);
        command.Parameters.AddWithValue("$name", session.Name.Trim());
        command.Parameters.AddWithValue("$profile", session.Profile);
        command.Parameters.AddWithValue("$agent", session.Agent);
        command.Parameters.AddWithValue("$paths", JsonSerializer.Serialize(CleanPaths(session.Paths)));
        command.Parameters.AddWithValue("$time", DateTimeOffset.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }

    public void UpdateTranscript(string sessionId, string transcript)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE ProjectSessions SET Transcript=$transcript,UpdatedAt=$time WHERE Id=$id";
        command.Parameters.AddWithValue("$id", sessionId);
        command.Parameters.AddWithValue("$transcript", transcript);
        command.Parameters.AddWithValue("$time", DateTimeOffset.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }

    public bool RecordAgentCompletion(string sessionId, string agentKey, DateTimeOffset completedAt)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        using var read = connection.CreateCommand();
        read.Transaction = transaction;
        read.CommandText = "SELECT ObservedAgentExitsJson FROM ProjectSessions WHERE Id=$id";
        read.Parameters.AddWithValue("$id", sessionId);
        var json = read.ExecuteScalar() as string;
        if (json is null) return false;
        var observed = ReadObservedExits(json);
        if (observed.TryGetValue(agentKey, out var previous) && previous >= completedAt) return false;
        observed[agentKey] = completedAt;
        using var update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = "UPDATE ProjectSessions SET CompletionCount=CompletionCount+1,ObservedAgentExitsJson=$observed,UpdatedAt=$time WHERE Id=$id";
        update.Parameters.AddWithValue("$observed", JsonSerializer.Serialize(observed));
        update.Parameters.AddWithValue("$time", DateTimeOffset.UtcNow.ToString("O"));
        update.Parameters.AddWithValue("$id", sessionId);
        var changed = update.ExecuteNonQuery() > 0;
        transaction.Commit();
        return changed;
    }

    public void ClearCompletionCount(string sessionId)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE ProjectSessions SET CompletionCount=0,UpdatedAt=$time WHERE Id=$id";
        command.Parameters.AddWithValue("$id", sessionId);
        command.Parameters.AddWithValue("$time", DateTimeOffset.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }

    private static Dictionary<string, DateTimeOffset> ReadObservedExits(string json)
    {
        try { return JsonSerializer.Deserialize<Dictionary<string, DateTimeOffset>>(json) ?? new(StringComparer.Ordinal); }
        catch (JsonException) { return new(StringComparer.Ordinal); }
    }

    public void DeleteSession(string sessionId)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        string? projectId;
        using (var read = connection.CreateCommand())
        {
            read.Transaction = transaction;
            read.CommandText = "SELECT ProjectId FROM ProjectSessions WHERE Id=$id";
            read.Parameters.AddWithValue("$id", sessionId);
            projectId = read.ExecuteScalar() as string;
        }
        var wasActive = false;
        string? replacementSessionId = null;
        if (projectId is not null)
        {
            using var active = connection.CreateCommand();
            active.Transaction = transaction;
            active.CommandText = "SELECT ActiveSessionId FROM Projects WHERE Id=$project";
            active.Parameters.AddWithValue("$project", projectId);
            wasActive = string.Equals(active.ExecuteScalar() as string, sessionId, StringComparison.Ordinal);
            if (wasActive)
            {
                using var replacement = connection.CreateCommand();
                replacement.Transaction = transaction;
                replacement.CommandText = "SELECT Id FROM ProjectSessions WHERE ProjectId=$project AND Id<>$id ORDER BY UpdatedAt,Name COLLATE NOCASE LIMIT 1";
                replacement.Parameters.AddWithValue("$project", projectId);
                replacement.Parameters.AddWithValue("$id", sessionId);
                replacementSessionId = replacement.ExecuteScalar() as string;
            }
        }
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "DELETE FROM ProjectSessions WHERE Id=$id";
            command.Parameters.AddWithValue("$id", sessionId);
            command.ExecuteNonQuery();
        }
        if (wasActive && projectId is not null)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "UPDATE Projects SET ActiveSessionId=$session,UpdatedAt=$time WHERE Id=$project";
            command.Parameters.AddWithValue("$project", projectId);
            command.Parameters.AddWithValue("$session", (object?)replacementSessionId ?? DBNull.Value);
            command.Parameters.AddWithValue("$time", DateTimeOffset.UtcNow.ToString("O"));
            command.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    public void DeleteProject(string projectId)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Projects WHERE Id=$id";
        command.Parameters.AddWithValue("$id", projectId);
        command.ExecuteNonQuery();
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys=ON";
        command.ExecuteNonQuery();
        return connection;
    }

    private static string[] ReadPaths(string json)
    {
        try { return JsonSerializer.Deserialize<string[]>(json) ?? []; }
        catch (JsonException) { return []; }
    }

    private static string[] CleanPaths(IEnumerable<string> paths) => paths
        .Where(path => !string.IsNullOrWhiteSpace(path))
        .Select(path => path.Trim())
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();
}
