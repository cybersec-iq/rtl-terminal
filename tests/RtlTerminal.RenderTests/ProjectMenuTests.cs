using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using RtlTerminal;

internal static partial class Program
{
    private static void CheckProjectMenus()
    {
        var directory = Path.Combine(Path.GetTempPath(), "rtl-project-menu-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var previousData = Environment.GetEnvironmentVariable("RTLTERMINAL_DATA_DIR");
        Environment.SetEnvironmentVariable("RTLTERMINAL_DATA_DIR", directory);
        MainWindow? window = null;
        try
        {
            if (Application.Current is null) _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            AppTheme.Apply(AppThemeKind.Dark);
            var store = new ProjectSessionStore();
            var created = store.CreateProjectWithFirstSession("Menu project", [directory], "Main", "PowerShell", "xagent", [directory]);
            var second = store.CreateSession(created.Project.Id, "Review", "PowerShell", "codex", [directory]);
            window = new MainWindow();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var profileType = typeof(MainWindow).GetNestedType("TerminalProfile", BindingFlags.NonPublic)!;
            var profile = Enum.Parse(profileType, "PowerShell");
            var shellFactory = typeof(MainWindow).GetMethod("CreateShellMenuItem", flags)!;
            var shell = (MenuItem)shellFactory.Invoke(window, ["PowerShell", profile])!;
            var agent = (MenuItem)shell.Items.OfType<MenuItem>().Single(item => item.Header?.ToString() == "X Agents CLI");
            var project = (MenuItem)agent.Items.OfType<MenuItem>().Single(item => item.Header?.ToString() == "Menu project");
            var session = (MenuItem)project.Items.OfType<MenuItem>().Single(item => item.Header?.ToString() == "Main  [PowerShell]");
            var sessionContext = (ContextMenu)session.ContextMenu!;
            AssertMenu(sessionContext.Items.OfType<MenuItem>().Select(item => item.Header?.ToString()).ToArray(),
                "Switch to this session", "Rename session...", "+ New PowerShell session...", "Folders", "Delete session...");
            var review = (MenuItem)project.Items.OfType<MenuItem>().Single(item => item.Header?.ToString() == "Review  [PowerShell]");
            var reviewContext = (ContextMenu)review.ContextMenu!;
            AssertMenu(reviewContext.Items.OfType<MenuItem>().Select(item => item.Header?.ToString()).ToArray(),
                "Switch to X Agents CLI", "Switch to this session", "Rename session...", "+ New PowerShell session...", "Folders", "Delete session...");
            AssertProject(store.GetSessions(created.Project.Id).Single(item => item.Id == second.Id).Agent == "codex",
                "constructing the session menu changed the saved agent identity");
            AssertMenu(project.Items.OfType<MenuItem>().Select(item => item.Header?.ToString()).ToArray(),
                "Main  [PowerShell]", "Review  [PowerShell]", "+ New PowerShell session...", "Manage project");
            var projectManagement = project.Items.OfType<MenuItem>().Single(item => item.Header?.ToString() == "Manage project");
            AssertMenu(projectManagement.Items.OfType<MenuItem>().Select(item => item.Header?.ToString()).ToArray(),
                "Rename project...", "Folders", "Delete project...");
            var projectFolders = projectManagement.Items.OfType<MenuItem>().Single(item => item.Header?.ToString() == "Folders");
            AssertMenu(projectFolders.Items.OfType<MenuItem>().Select(item => item.Header?.ToString()).ToArray(),
                Path.GetFileName(directory), "+ Add folder...", "Edit folders...");
            AssertMenu(sessionContext.Items.OfType<MenuItem>().Single(item => item.Header?.ToString() == "Folders")
                    .Items.OfType<MenuItem>().Select(item => item.Header?.ToString()).ToArray(),
                Path.GetFileName(directory), "+ Add folder...", "Edit folders...");
            var projectContext = (ContextMenu)project.ContextMenu!;
            AssertMenu(projectContext.Items.OfType<MenuItem>().Select(item => item.Header?.ToString()).ToArray(),
                "Rename project...", "Folders", "Delete project...");

            var paneType = typeof(MainWindow).GetNestedType("TerminalPane", BindingFlags.NonPublic)!;
            var tabType = typeof(MainWindow).GetNestedType("TerminalTab", BindingFlags.NonPublic)!;
            var pane = Activator.CreateInstance(paneType, [1, profile, "PowerShell"])!;
            var tab = Activator.CreateInstance(tabType, [pane])!;
            tabType.GetProperty("Project")!.SetValue(tab, created.Project);
            var tabMenu = (ContextMenu)typeof(MainWindow).GetMethod("CreateTabContextMenu", flags)!.Invoke(window, [tab])!;
            var sessionsMenu = tabMenu.Items.OfType<MenuItem>().Single(item => item.Header?.ToString() == "Sessions");
            var switchSessionMenu = sessionsMenu.Items.OfType<MenuItem>().Single(item => item.Header?.ToString() == "Switch session");
            AssertMenu(switchSessionMenu.Items.OfType<MenuItem>().Select(item => item.Header?.ToString()).ToArray(), "Main  [PowerShell]", "Review  [PowerShell]");
            AssertMenu(sessionsMenu.Items.OfType<MenuItem>().Select(item => item.Header?.ToString()).ToArray(), "Switch session", "+ New PowerShell session...");

            // Exercise a normal menu click against an already-active runtime so the
            // regression test cannot start or replace a shell/broker process.
            var activePane = typeof(MainWindow).GetMethod("CreatePane", flags)!.Invoke(window, [profile])!;
            paneType.GetProperty("PersistentAttachPending")!.SetValue(activePane, true);
            var activeTab = Activator.CreateInstance(tabType, [activePane])!;
            tabType.GetProperty("Project")!.SetValue(activeTab, created.Project);
            tabType.GetProperty("ActiveProjectSession")!.SetValue(activeTab, second);
            var tabs = (System.Collections.IList)typeof(MainWindow).GetField("_tabs", flags)!.GetValue(window)!;
            tabs.Add(activeTab);
            review.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            AssertProject(store.GetSessions(created.Project.Id).Single(item => item.Id == second.Id).Agent == "codex",
                "selecting a saved session from the menu rewrote its saved agent identity");
            var sharedReplacement=Path.Combine(directory,"shared-replacement");
            var sessionOnly=Path.Combine(directory,"session-only");
            Directory.CreateDirectory(sharedReplacement);Directory.CreateDirectory(sessionOnly);
            var customSession=store.CreateSession(created.Project.Id,"Custom","PowerShell","codex",[directory,sessionOnly]);
            typeof(MainWindow).GetMethod("SaveProjectFolders",flags)!.Invoke(window,[created.Project,new[]{sharedReplacement}]);
            AssertProject(store.GetProjects().Single(item=>item.Id==created.Project.Id).Paths.SequenceEqual([sharedReplacement],StringComparer.OrdinalIgnoreCase),
                "project folder edit did not update project roots");
            AssertProject(store.GetSessions(created.Project.Id).Single(item=>item.Id==created.Session.Id).Paths.SequenceEqual([sharedReplacement],StringComparer.OrdinalIgnoreCase),
                "project folder edit did not propagate to the first session");
            AssertProject(store.GetSessions(created.Project.Id).Single(item=>item.Id==second.Id).Paths.SequenceEqual([sharedReplacement],StringComparer.OrdinalIgnoreCase),
                "project folder edit did not propagate to other sessions");
            AssertProject(store.GetSessions(created.Project.Id).Single(item=>item.Id==customSession.Id).Paths.SequenceEqual([sharedReplacement,sessionOnly],StringComparer.OrdinalIgnoreCase),
                "project folder edit did not preserve session-only folders");
            Console.WriteLine("PASS cascading shell/agent/project/session menu hierarchy and project/session management actions");
        }
        finally
        {
            window?.Close();
            Environment.SetEnvironmentVariable("RTLTERMINAL_DATA_DIR", previousData);
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private static void AssertMenu(string?[] actual, params string[] expected)
    {
        foreach (var text in expected)
            if (!actual.Contains(text, StringComparer.Ordinal))
                throw new Exception($"Expected menu item '{text}' was missing. Found: {string.Join(", ", actual)}");
    }
}
