using System.Diagnostics;
using System.Runtime.InteropServices;
using DotNetTestRunner.Application.Abstractions;

namespace DotNetTestRunner.Infrastructure.Services;

/// <summary>
/// <see cref="IProcessTreeInspector"/> implementation backed by the Windows tool help
/// snapshot API, which exposes the parent of every running process and therefore allows the
/// full descendant tree of a test run to be reconstructed.
/// </summary>
public sealed class ProcessTreeInspector : IProcessTreeInspector
{
    private const uint TH32CS_SNAPPROCESS = 0x00000002;
    private const int MAX_PATH = 260;

    /// <inheritdoc />
    public int CountDescendantsWithVisibleWindow(int rootProcessId)
    {
        var childrenByParent = TryReadProcessParents();

        if (childrenByParent.Count == 0)
        {
            return 0;
        }

        var visibleCount = 0;

        foreach (var descendantId in EnumerateDescendants(rootProcessId, childrenByParent))
        {
            if (HasVisibleWindow(descendantId))
            {
                visibleCount++;
            }
        }

        return visibleCount;
    }

    /// <summary>
    /// Walks the descendant processes of <paramref name="rootProcessId"/> breadth-first,
    /// guarding against the cycles that can appear when a process identifier is reused.
    /// </summary>
    private static IEnumerable<int> EnumerateDescendants(int rootProcessId, Dictionary<int, List<int>> childrenByParent)
    {
        var visited = new HashSet<int> { rootProcessId };
        var pending = new Queue<int>();
        pending.Enqueue(rootProcessId);

        while (pending.Count > 0)
        {
            var currentId = pending.Dequeue();

            if (!childrenByParent.TryGetValue(currentId, out var children))
            {
                continue;
            }

            foreach (var childId in children)
            {
                if (!visited.Add(childId))
                {
                    continue;
                }

                pending.Enqueue(childId);
                yield return childId;
            }
        }
    }

    /// <summary>
    /// Determines whether a process currently owns a main window. Console helpers such as
    /// <c>conhost</c>, <c>MSBuild</c>, and the test host itself report no main window, so only
    /// the graphical application launched by the tests is counted.
    /// </summary>
    private static bool HasVisibleWindow(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.MainWindowHandle != nint.Zero;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or NotSupportedException)
        {
            // The process exited between enumeration and inspection, or cannot be queried.
            return false;
        }
    }

    /// <summary>
    /// Builds a parent-to-children map of every running process. Returns an empty map when the
    /// snapshot cannot be taken, so process inspection never interrupts a test run.
    /// </summary>
    private static Dictionary<int, List<int>> TryReadProcessParents()
    {
        var childrenByParent = new Dictionary<int, List<int>>();
        var snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);

        if (snapshot == nint.Zero || snapshot == new nint(-1))
        {
            return childrenByParent;
        }

        try
        {
            var entry = new PROCESSENTRY32 { dwSize = Marshal.SizeOf<PROCESSENTRY32>() };

            if (!Process32First(snapshot, ref entry))
            {
                return childrenByParent;
            }

            do
            {
                var parentId = (int)entry.th32ParentProcessID;
                var processId = (int)entry.th32ProcessID;

                if (!childrenByParent.TryGetValue(parentId, out var children))
                {
                    children = [];
                    childrenByParent[parentId] = children;
                }

                children.Add(processId);
            }
            while (Process32Next(snapshot, ref entry));
        }
        finally
        {
            CloseHandle(snapshot);
        }

        return childrenByParent;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32First(nint hSnapshot, ref PROCESSENTRY32 lppe);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32Next(nint hSnapshot, ref PROCESSENTRY32 lppe);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint hObject);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private struct PROCESSENTRY32
    {
        public int dwSize;
        public uint cntUsage;
        public uint th32ProcessID;
        public nint th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public uint th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = MAX_PATH)]
        public string szExeFile;
    }
}
