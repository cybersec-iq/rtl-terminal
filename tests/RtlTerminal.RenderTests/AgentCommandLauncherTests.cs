using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using RtlTerminal;

internal static class AgentCommandLauncherTests
{
    public static void Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "rtl launcher test with spaces " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var runtime = Path.Combine(root, "runtime with spaces");
            Directory.CreateDirectory(runtime);
            var entry = Path.Combine(runtime, "xagent.js");
            var handoff = Path.Combine(runtime, "handoff folder", "handoff.md");
            Directory.CreateDirectory(Path.GetDirectoryName(handoff)!);
            const string sessionId = "session-test-47";
            string[] projectPaths = [Path.Combine(root, "project one"), Path.Combine(root, "project two")];
            foreach (var path in projectPaths) Directory.CreateDirectory(path);
            File.WriteAllText(entry, """
                const paths = JSON.parse(Buffer.from(process.env.XAGENT_PROJECT_PATHS_B64, 'base64').toString('utf8'));
                console.log(JSON.stringify({
                  noFcm: process.env.XAGENT_NO_FCM_AUTOSTART,
                  terminal: process.env.XAGENT_RTLTERMINAL,
                  session: process.env.XAGENT_PROJECT_SESSION_ID,
                  pathsFile: process.env.XAGENT_PROJECT_PATHS_FILE,
                  paths,
                  handoff: process.env.XAGENT_TRANSCRIPT_HANDOFF_FILE
                }));
                """);

            var launch = typeof(MainWindow).GetMethod("AgentCommand", BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new Exception("Private AgentCommand method was not found");
            var profileType = typeof(MainWindow).GetNestedType("TerminalProfile", BindingFlags.NonPublic)
                ?? throw new Exception("TerminalProfile enum was not found");

            var cmdProfile = Enum.Parse(profileType, "CommandPrompt");
            var cmd = (string?)launch.Invoke(null, ["xagent", cmdProfile, entry, projectPaths, sessionId, handoff])
                ?? throw new Exception("AgentCommand did not return a CMD launcher");
            // The user can type `xagent` at the returned CMD prompt after Ctrl+C exits the CLI.
            var cmdResult = RunThroughCmd(root, cmd + "\r\nxagent --probe");
            AssertEnvironment(cmdResult, projectPaths, sessionId, handoff, runtime);
            if (cmdResult.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries)
                .Count(line => line.TrimStart().StartsWith('{')) < 2)
                throw new Exception("The xagent command alias did not relaunch the session CLI from CMD");

            var powershellProfile = Enum.Parse(profileType, "PowerShell");
            var powershell = (string?)launch.Invoke(null, ["xagent", powershellProfile, entry, projectPaths, sessionId, handoff])
                ?? throw new Exception("AgentCommand did not return a PowerShell launcher");
            var powershellResult = RunPowerShell(root, "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", powershell + "; xagent --probe");
            AssertEnvironment(powershellResult, projectPaths, sessionId, handoff, runtime);
            if (powershellResult.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries)
                .Count(line => line.TrimStart().StartsWith('{')) < 2)
                throw new Exception("The xagent command alias did not relaunch the session CLI from PowerShell");

            // WSL starts the generated PowerShell script through powershell.exe.
            var wslProfile = Enum.Parse(profileType, "Wsl");
            var wsl = (string?)launch.Invoke(null, ["xagent", wslProfile, entry, projectPaths, sessionId, handoff])
                ?? throw new Exception("AgentCommand did not return a WSL launcher");
            var wslResult = RunThroughCmd(root, wsl);
            AssertEnvironment(wslResult, projectPaths, sessionId, handoff, runtime);

            Console.WriteLine("PASS isolated X Agent CMD, PowerShell and WSL launchers preserve environment and handoff paths with spaces");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static string RunThroughCmd(string workingDirectory, string command)
    {
        var driver = Path.Combine(workingDirectory, "run launcher.cmd");
        File.WriteAllText(driver, "@echo off\r\n" + command + "\r\nexit /b %ERRORLEVEL%\r\n");
        return RunProcess("cmd.exe", workingDirectory, ["/d", "/c", driver]);
    }

    private static string RunPowerShell(string workingDirectory, params string[] arguments) =>
        RunProcess("powershell.exe", workingDirectory, arguments);

    private static string RunProcess(string executable, string workingDirectory, IEnumerable<string> arguments)
    {
        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new Exception($"Could not start {executable}");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        if (!process.WaitForExit(20000))
        {
            process.Kill(entireProcessTree: true);
            throw new Exception($"{executable} launcher timed out");
        }
        if (process.ExitCode != 0)
            throw new Exception($"{executable} launcher failed ({process.ExitCode}): {stderr}\n{stdout}");
        return stdout;
    }

    private static void AssertEnvironment(string output, string[] expectedPaths, string expectedSession, string expectedHandoff, string runtime)
    {
        var jsonLine = output.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries)
            .LastOrDefault(line => line.TrimStart().StartsWith('{'))
            ?? throw new Exception("xagent.js stub did not emit its environment JSON: " + output);
        using var document = JsonDocument.Parse(jsonLine);
        var root = document.RootElement;
        Equal("1", root.GetProperty("noFcm").GetString(), "XAGENT_NO_FCM_AUTOSTART");
        Equal("1", root.GetProperty("terminal").GetString(), "XAGENT_RTLTERMINAL");
        Equal(expectedSession, root.GetProperty("session").GetString(), "XAGENT_PROJECT_SESSION_ID");
        Equal(Path.Combine(runtime, "run", "project-paths.json"), root.GetProperty("pathsFile").GetString(), "XAGENT_PROJECT_PATHS_FILE");
        var paths = root.GetProperty("paths").EnumerateArray().Select(value => value.GetString()).ToArray();
        if (!paths.SequenceEqual(expectedPaths, StringComparer.Ordinal))
            throw new Exception("XAGENT_PROJECT_PATHS_B64 did not round-trip exactly");
        Equal(expectedHandoff, root.GetProperty("handoff").GetString(), "XAGENT_TRANSCRIPT_HANDOFF_FILE");
    }

    private static void Equal(string expected, string? actual, string name)
    {
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
            throw new Exception($"{name} mismatch. Expected '{expected}', got '{actual}'");
    }
}
