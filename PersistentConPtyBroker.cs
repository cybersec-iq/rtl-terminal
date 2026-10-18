using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;

namespace RtlTerminal;

/// <summary>
/// Client for a ConPTY hosted by a detached instance of RtlTerminal. The broker owns
/// the pseudoconsole and shell, so disposing this client only detaches the UI.
/// Persist BrokerId with the project/session and use Attach to reconnect later.
/// </summary>
public sealed class PersistentConPtySession : ITerminalTransport
{
    private const string BrokerSwitch = "--rtl-pty-broker";
    private readonly string _brokerId;
    private NamedPipeClientStream? _pipe;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private byte[] _pending = Array.Empty<byte>();
    private int _pendingOffset;
    private bool _disposed;

    private PersistentConPtySession(string brokerId, NamedPipeClientStream pipe, bool reattached)
    {
        _brokerId = brokerId;
        _pipe = pipe;
        WasReattached = reattached;
    }

    public string BrokerId => _brokerId;
    public bool WasReattached { get; }
    public bool HasCodexProcess => false;

    public static PersistentConPtySession Start(
        string commandLine, string? workingDirectory, short columns, short rows,
        string? brokerExecutablePath = null)
        => Start(Guid.NewGuid().ToString("N"), commandLine, workingDirectory, columns, rows, brokerExecutablePath);

    public static PersistentConPtySession Start(
        string brokerId, string commandLine, string? workingDirectory, short columns, short rows,
        string? brokerExecutablePath = null)
    {
        brokerId = ValidateId(brokerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(commandLine);
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Persistent ConPTY is Windows-only.");
        var exe = brokerExecutablePath ?? Process.GetCurrentProcess().MainModule?.FileName
            ?? throw new InvalidOperationException("Cannot locate the running executable to start the PTY broker.");
        var command = Convert.ToBase64String(Encoding.UTF8.GetBytes(commandLine));
        var directory = Convert.ToBase64String(Encoding.UTF8.GetBytes(workingDirectory ?? string.Empty));
        var info = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            Arguments = $"{BrokerSwitch} {brokerId} {columns} {rows} {command} {directory}"
        };
        using var broker = Process.Start(info) ?? throw new InvalidOperationException("Could not start the ConPTY broker process.");
        try { return Connect(brokerId, TimeSpan.FromSeconds(20), reattached: false); }
        catch
        {
            try { if (!broker.HasExited) broker.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            throw;
        }
    }

    public static PersistentConPtySession Attach(string brokerId) => Connect(ValidateId(brokerId), TimeSpan.FromSeconds(5), reattached: true);

    public static PersistentConPtySession AttachOrStart(string brokerId, string commandLine,
        string? workingDirectory, short columns, short rows)
    {
        try { return Attach(brokerId); }
        catch (Exception exception) when (exception is TimeoutException or IOException)
        {
            return Start(brokerId, commandLine, workingDirectory, columns, rows);
        }
    }

    private static PersistentConPtySession Connect(string id, TimeSpan timeout, bool reattached)
    {
        var pipe = new NamedPipeClientStream(".", PipeName(id), PipeDirection.InOut, PipeOptions.Asynchronous);
        var until = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                pipe.Connect(250);
                pipe.ReadMode = PipeTransmissionMode.Byte;
                return new PersistentConPtySession(id, pipe, reattached);
            }
            catch (TimeoutException) when (until.Elapsed < timeout) { }
            catch (IOException) when (until.Elapsed < timeout) { }
            if (until.Elapsed >= timeout)
            {
                pipe.Dispose();
                throw new TimeoutException("The ConPTY broker did not accept a connection before the timeout.");
            }
            Thread.Sleep(50);
        }
    }

    public int Read(byte[] buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (buffer.Length == 0 || _disposed || _pipe is null) return 0;
        if (_pendingOffset < _pending.Length)
        {
            var count = Math.Min(buffer.Length, _pending.Length - _pendingOffset);
            Buffer.BlockCopy(_pending, _pendingOffset, buffer, 0, count);
            _pendingOffset += count;
            return count;
        }
        while (true)
        {
            var (type, payload) = ReadFrame(_pipe);
            if (type == BrokerProtocol.Exited) return 0;
            if (type != BrokerProtocol.Output) throw new IOException("Unexpected response from the ConPTY broker.");
            _pending = payload;
            _pendingOffset = 0;
            if (_pending.Length == 0) continue;
            var count = Math.Min(buffer.Length, _pending.Length);
            Buffer.BlockCopy(_pending, 0, buffer, 0, count);
            _pendingOffset = count;
            return count;
        }
    }

    public void Write(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        Send(BrokerProtocol.Input, Encoding.UTF8.GetBytes(text));
    }

    public void Resize(short columns, short rows)
    {
        Span<byte> payload = stackalloc byte[4];
        BinaryPrimitives.WriteInt16LittleEndian(payload, columns);
        BinaryPrimitives.WriteInt16LittleEndian(payload[2..], rows);
        Send(BrokerProtocol.Resize, payload.ToArray());
    }

    /// <summary>Stops the detached broker and its shell. Dispose only detaches.</summary>
    public void StopBroker() => Send(BrokerProtocol.Stop, Array.Empty<byte>());

    private void Send(byte type, byte[] payload)
    {
        if (_disposed || _pipe is null) throw new ObjectDisposedException(nameof(PersistentConPtySession));
        _writeGate.Wait();
        try { WriteFrame(_pipe, type, payload); }
        finally { _writeGate.Release(); }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _pipe?.Dispose(); } catch (IOException) { }
        _pipe = null;
        _writeGate.Dispose();
    }

    private static string ValidateId(string id)
    {
        if (!Guid.TryParseExact(id, "N", out _)) throw new ArgumentException("Invalid ConPTY broker id.", nameof(id));
        return id.ToLowerInvariant();
    }

    private static string PipeName(string id) => $"RtlTerminal.ConPty.{id}";

    private static void WriteFrame(Stream stream, byte type, ReadOnlySpan<byte> payload)
    {
        Span<byte> header = stackalloc byte[5];
        header[0] = type;
        BinaryPrimitives.WriteInt32LittleEndian(header[1..], payload.Length);
        stream.Write(header);
        stream.Write(payload);
        stream.Flush();
    }

    private static (byte Type, byte[] Payload) ReadFrame(Stream stream)
    {
        Span<byte> header = stackalloc byte[5];
        ReadExactly(stream, header);
        var length = BinaryPrimitives.ReadInt32LittleEndian(header[1..]);
        if (length < 0 || length > BrokerProtocol.MaximumFrameSize) throw new IOException("Invalid ConPTY broker frame size.");
        var payload = new byte[length];
        ReadExactly(stream, payload);
        return (header[0], payload);
    }

    private static void ReadExactly(Stream stream, Span<byte> buffer)
    {
        while (!buffer.IsEmpty)
        {
            var read = stream.Read(buffer);
            if (read == 0) throw new EndOfStreamException();
            buffer = buffer[read..];
        }
    }

    internal static void DispatchBrokerProcess()
    {
        var args = Environment.GetCommandLineArgs();
        var marker = Array.FindIndex(args, arg => string.Equals(arg, BrokerSwitch, StringComparison.Ordinal));
        if (marker < 0) return;
        try
        {
            if (args.Length != marker + 6) throw new ArgumentException("Invalid broker arguments.");
            var id = ValidateId(args[marker + 1]);
            var columns = short.Parse(args[marker + 2], System.Globalization.CultureInfo.InvariantCulture);
            var rows = short.Parse(args[marker + 3], System.Globalization.CultureInfo.InvariantCulture);
            var command = Encoding.UTF8.GetString(Convert.FromBase64String(args[marker + 4]));
            var directory = Encoding.UTF8.GetString(Convert.FromBase64String(args[marker + 5]));
            BrokerHost.Run(id, columns, rows, command, string.IsNullOrEmpty(directory) ? null : directory)
                .GetAwaiter().GetResult();
            Environment.Exit(0);
        }
        catch
        {
            Environment.Exit(71);
        }
    }

    private static class BrokerProtocol
    {
        internal const byte Output = 1, Input = 2, Resize = 3, Stop = 4, Exited = 5;
        internal const int MaximumFrameSize = 1024 * 1024;
    }

    private static class BrokerHost
    {
        private const int ReplayLimit = 512 * 1024;

        internal static async Task Run(string id, short columns, short rows, string command, string? directory)
        {
            using var session = new ConPtySession(columns, rows);
            session.Start(command, directory);
            using var outputStop = new CancellationTokenSource();
            var state = new HostState();
            var outputTask = Task.Run(() => ReadPtyOutput(session, state, outputStop.Token));
            try
            {
                while (session.IsProcessRunning && !state.Stopped)
                {
                    await using var pipe = new NamedPipeServerStream(PipeName(id), PipeDirection.InOut, 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly,
                        64 * 1024, 64 * 1024);
                    try
                    {
                        using var waitCancellation = new CancellationTokenSource();
                        var waitTask = pipe.WaitForConnectionAsync(waitCancellation.Token);
                        var tick = Task.Delay(250);
                        while (!waitTask.IsCompleted && session.IsProcessRunning && !state.Stopped)
                        {
                            await Task.WhenAny(waitTask, tick).ConfigureAwait(false);
                            tick = Task.Delay(250);
                        }
                        if (!waitTask.IsCompleted)
                        {
                            waitCancellation.Cancel();
                            try { await waitTask.ConfigureAwait(false); } catch (OperationCanceledException) { }
                            continue;
                        }
                        await waitTask.ConfigureAwait(false);
                        await state.Gate.WaitAsync().ConfigureAwait(false);
                        try
                        {
                            foreach (var chunk in state.Replay.ToArray()) WriteFrame(pipe, BrokerProtocol.Output, chunk);
                            state.Client = pipe;
                        }
                        finally { state.Gate.Release(); }
                        await ReadClient(pipe, session, state).ConfigureAwait(false);
                    }
                    catch (IOException) { }
                    finally
                    {
                        await state.Gate.WaitAsync().ConfigureAwait(false);
                        try { if (ReferenceEquals(state.Client, pipe)) state.Client = null; }
                        finally { state.Gate.Release(); }
                    }
                }
                var exitGateHeld = false;
                try
                {
                    await state.Gate.WaitAsync().ConfigureAwait(false);
                    exitGateHeld = true;
                    if (state.Client is not null) WriteFrame(state.Client, BrokerProtocol.Exited, Array.Empty<byte>());
                }
                catch (IOException) { }
                finally { if (exitGateHeld) state.Gate.Release(); }
            }
            finally
            {
                outputStop.Cancel();
                session.Dispose();
                try { await outputTask.ConfigureAwait(false); }
                catch (OperationCanceledException) { }
                catch (ObjectDisposedException) { }
                catch (IOException) { }
                state.Gate.Dispose();
            }
        }

        private static void ReadPtyOutput(ConPtySession session, HostState state, CancellationToken token)
        {
            var buffer = new byte[16 * 1024];
            while (!token.IsCancellationRequested)
            {
                var count = session.Read(buffer);
                if (count == 0) break;
                var chunk = buffer.AsSpan(0, count).ToArray();
                state.Gate.Wait(token);
                try
                {
                    state.Replay.Enqueue(chunk);
                    state.ReplaySize += chunk.Length;
                    while (state.ReplaySize > ReplayLimit && state.Replay.TryDequeue(out var old)) state.ReplaySize -= old.Length;
                    if (state.Client is not null) WriteFrame(state.Client, BrokerProtocol.Output, chunk);
                }
                catch (IOException) { state.Client = null; }
                finally { state.Gate.Release(); }
            }
        }

        private static async Task ReadClient(Stream pipe, ConPtySession session, HostState state)
        {
            while (session.IsProcessRunning && !state.Stopped)
            {
                var readTask = Task.Run(() => ReadFrame(pipe));
                while (!readTask.IsCompleted && session.IsProcessRunning && !state.Stopped)
                    await Task.WhenAny(readTask, Task.Delay(250)).ConfigureAwait(false);
                if (!session.IsProcessRunning || state.Stopped) return;
                var (type, payload) = await readTask.ConfigureAwait(false);
                switch (type)
                {
                    case BrokerProtocol.Input: session.Write(Encoding.UTF8.GetString(payload)); break;
                    case BrokerProtocol.Resize when payload.Length == 4:
                        session.Resize(BinaryPrimitives.ReadInt16LittleEndian(payload), BinaryPrimitives.ReadInt16LittleEndian(payload.AsSpan(2)));
                        break;
                    case BrokerProtocol.Stop: state.Stopped = true; return;
                    default: throw new IOException("Invalid command from the ConPTY client.");
                }
            }
        }

        private sealed class HostState
        {
            internal readonly SemaphoreSlim Gate = new(1, 1);
            internal readonly Queue<byte[]> Replay = new();
            internal int ReplaySize;
            internal Stream? Client;
            internal volatile bool Stopped;
        }
    }
}
