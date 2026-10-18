using System.IO;
using System.Runtime.InteropServices;

namespace RtlTerminal;

internal static class ProcessTree
{
    private const uint SnapshotProcesses = 0x00000002;
    private const int MaxExeName = 260;

    public static bool HasDescendantNamed(int parentProcessId, string executableName)
        => FindDescendantNamed(parentProcessId, executableName) is not null;

    public static string? FindDescendantNamed(int parentProcessId, params string[] executableNames)
    {
        var snapshot = CreateToolhelp32Snapshot(SnapshotProcesses, 0);
        if (snapshot == IntPtr.Zero || snapshot == new IntPtr(-1)) return null;

        try
        {
            var names = new HashSet<string>(executableNames, StringComparer.OrdinalIgnoreCase);
            var children = new Dictionary<int, List<ProcessEntry>>();
            var entry = new ProcessEntry { Size = (uint)Marshal.SizeOf<ProcessEntry>() };
            if (!Process32First(snapshot, ref entry)) return null;
            do
            {
                if (!children.TryGetValue((int)entry.ParentProcessId, out var list))
                    children[(int)entry.ParentProcessId] = list = [];
                list.Add(entry);
                entry.Size = (uint)Marshal.SizeOf<ProcessEntry>();
            } while (Process32Next(snapshot, ref entry));

            var pending = new Queue<int>();
            var visited = new HashSet<int> { parentProcessId };
            pending.Enqueue(parentProcessId);
            while (pending.Count > 0)
            {
                if (!children.TryGetValue(pending.Dequeue(), out var descendants)) continue;
                foreach (var child in descendants)
                {
                    var childId = (int)child.ProcessId;
                    if (!visited.Add(childId)) continue;
                    var name = Path.GetFileNameWithoutExtension(child.ExeFile ?? string.Empty);
                    if (names.Contains(name)) return name;
                    pending.Enqueue(childId);
                }
            }

            return null;
        }
        finally { CloseHandle(snapshot); }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry
    {
        public uint Size;
        public uint UsageCount;
        public uint ProcessId;
        public UIntPtr DefaultHeapId;
        public uint ModuleId;
        public uint ThreadCount;
        public uint ParentProcessId;
        public int BasePriority;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = MaxExeName)]
        public string? ExeFile;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32First(IntPtr snapshot, ref ProcessEntry entry);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32Next(IntPtr snapshot, ref ProcessEntry entry);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}
