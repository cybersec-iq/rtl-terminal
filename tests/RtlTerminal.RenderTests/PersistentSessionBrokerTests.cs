using System.IO;
using System.Text;
using RtlTerminal;

/// <summary>Manual Windows integration checks; intentionally not invoked by the normal suite.</summary>
internal static class PersistentSessionBrokerTests
{
    internal static void RunDetachReconnectRoundTrip()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        const string marker = "PERSISTENT_RECONNECT_OK";
        var repoRoot = new DirectoryInfo(Environment.CurrentDirectory);
        while (repoRoot is not null && !File.Exists(Path.Combine(repoRoot.FullName, "RtlTerminal.csproj")))
            repoRoot = repoRoot.Parent!;
        if (repoRoot is null) throw new DirectoryNotFoundException("Could not locate RtlTerminal.csproj for broker integration test.");
        var brokerExe = Path.Combine(repoRoot.FullName, "bin", "Release", "net8.0-windows", "RtlTerminal.exe");
        if (!File.Exists(brokerExe)) throw new FileNotFoundException("Build the RtlTerminal apphost before this integration test.", brokerExe);
        var first = PersistentConPtySession.Start("cmd.exe /q", Environment.CurrentDirectory, 100, 28, brokerExe);
        var id = first.BrokerId;
        try
        {
            first.Write($"echo {marker}\r");
            first.Dispose();
            Thread.Sleep(250); // let the detached broker drain the ConPTY output

            using var resumed = PersistentConPtySession.Attach(id);
            var bytes = new byte[4096];
            var collected = new StringBuilder();
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < deadline && !collected.ToString().Contains(marker, StringComparison.Ordinal))
            {
                var readTask = Task.Run(() => resumed.Read(bytes));
                if (!readTask.Wait(TimeSpan.FromSeconds(10)))
                    throw new TimeoutException("Timed out waiting for detached shell output.");
                var read = readTask.GetAwaiter().GetResult();
                if (read == 0) throw new EndOfStreamException("The broker ended before replaying the marker.");
                collected.Append(Encoding.UTF8.GetString(bytes, 0, read));
            }
            if (!collected.ToString().Contains(marker, StringComparison.Ordinal))
                throw new TimeoutException("The detached shell output was not replayed after reconnect.");
            resumed.StopBroker();
        }
        finally
        {
            first.Dispose();
            try
            {
                using var cleanup = PersistentConPtySession.Attach(id);
                cleanup.StopBroker();
            }
            catch (Exception) { /* the broker already exited or was stopped */ }
        }
    }

    internal static void InvalidBrokerIdsAreRejected()
    {
        try
        {
            using var ignored = PersistentConPtySession.Attach("not-a-guid");
            throw new Exception("Malformed broker id unexpectedly connected.");
        }
        catch (ArgumentException) { }
    }
}
