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
            var second = store.CreateSession(created.Project.Id, "Review", "PowerShell", "xagent", [directory]);
            window = new MainWindow();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var profileType = typeof(MainWindow).GetNestedType("TerminalProfile", BindingFlags.NonPublic)!;
            var profile = Enum.Parse(profileType, "PowerShell");
            var shellFactory = typeof(MainWindow).GetMethod("CreateShellMenuItem", flags)!;
            var shell = (MenuItem)shellFactory.Invoke(window, ["PowerShell", profile])!;
            var agent = (MenuItem)shell.Items.OfType<MenuItem>().Single(item => item.Header?.ToString() == "X Agents CLI");
            var project = (MenuItem)agent.Items.OfType<MenuItem>().Single(item => item.Header?.ToString() == "Menu project");
            var session = (MenuItem)project.Items.OfType<MenuItem>().Single(item => item.Header?.ToString() == "Main");
            AssertMenu(session.Items.OfType<MenuItem>().Select(item => item.Header?.ToString()).ToArray(),
                "Switch to this session", "+ Add session...", "Rename session...", "Folders", "Delete session...");
            AssertMenu(project.Items.OfType<MenuItem>().Select(item => item.Header?.ToString()).ToArray(),
                "Main", "Review", "+ Add session...", "Manage project");
            var projectManagement = project.Items.OfType<MenuItem>().Single(item => item.Header?.ToString() == "Manage project");
            AssertMenu(projectManagement.Items.OfType<MenuItem>().Select(item => item.Header?.ToString()).ToArray(),
                "Rename project...", "Folders", "Delete project...");
            var projectFolders = projectManagement.Items.OfType<MenuItem>().Single(item => item.Header?.ToString() == "Folders");
            AssertMenu(projectFolders.Items.OfType<MenuItem>().Select(item => item.Header?.ToString()).ToArray(),
                Path.GetFileName(directory), "+ Add folder...", "Edit folders...");
            var sessionFolders = session.Items.OfType<MenuItem>().Single(item => item.Header?.ToString() == "Folders");
            AssertMenu(sessionFolders.Items.OfType<MenuItem>().Select(item => item.Header?.ToString()).ToArray(),
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
            typeof(MainWindow).GetMethod("PopulateSessionsSubmenu", flags)!.Invoke(window, [tab, sessionsMenu]);
            AssertMenu(sessionsMenu.Items.OfType<MenuItem>().Select(item => item.Header?.ToString()).ToArray(), "Main", "Review", "+ Add session...");
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
