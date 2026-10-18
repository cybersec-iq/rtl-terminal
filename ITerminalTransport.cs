namespace RtlTerminal;

/// <summary>Input/output surface shared by app-owned and reconnectable terminal sessions.</summary>
public interface ITerminalTransport : IDisposable
{
    bool HasCodexProcess { get; }
    int Read(byte[] buffer);
    void Write(string text);
    void Resize(short columns, short rows);
}
