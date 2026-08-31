using System.Collections.Concurrent;
using System.IO.Enumeration;
using Tessera.Models;

namespace Tessera.Scanning;

/// <summary>
/// Parallel directory scanner. A shared queue of pending directories is consumed by
/// one worker per core; each directory is enumerated exactly once with
/// FileSystemEnumerable (size/attributes come straight from the OS directory entry,
/// no extra stat calls). Reparse points (junctions/symlinks) become zero-size leaves
/// and are never descended into, so cycles and double-counting are impossible.
/// </summary>
internal static class Scanner
{
    private readonly record struct Entry(string Name, long Length, bool IsDir, bool IsReparse);

    private sealed class ScanState
    {
        public readonly ConcurrentQueue<(FsNode Node, string Path)> Queue = new();
        public int Pending;
        public required ScanProgress Progress { get; init; }
        public CancellationToken Ct { get; init; }
    }

    private static readonly EnumerationOptions s_options = new()
    {
        // false so denied directories throw into our catch, get flagged AccessDenied,
        // and are counted — instead of silently appearing empty.
        IgnoreInaccessible = false,
        AttributesToSkip = 0,           // include hidden/system — a disk analyzer must see everything
        RecurseSubdirectories = false,
    };

    private static readonly Comparison<FsNode> s_sizeDesc = (a, b) => b.Size.CompareTo(a.Size);

    /// <summary>
    /// Test seam: called with each directory path as a worker picks it up, so a test can
    /// inject the failures real filesystems produce too rarely to reproduce on demand.
    /// </summary>
    internal static Action<string>? OnDirectoryEnter;

    public static Task<FsNode> ScanAsync(string rootPath, ScanProgress progress, CancellationToken ct)
        => Task.Run(() => Scan(rootPath, progress, ct), CancellationToken.None);

    private static FsNode Scan(string rootPath, ScanProgress progress, CancellationToken ct)
    {
        rootPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
        // Drive roots ("D:") need the separator back to be a valid path.
        if (rootPath.Length == 2 && rootPath[1] == ':')
            rootPath += Path.DirectorySeparatorChar;

        var root = new FsNode(rootPath, FsNode.FlagDir);
        var state = new ScanState { Progress = progress, Ct = ct };
        state.Pending = 1;
        state.Queue.Enqueue((root, rootPath));

        int workerCount = Math.Max(1, Environment.ProcessorCount);
        var workers = new Task[workerCount];
        for (int i = 0; i < workerCount; i++)
            workers[i] = Task.Run(() => WorkerLoop(state));

        try
        {
            Task.WaitAll(workers);
        }
        catch (AggregateException ex) when (ex.Flatten().InnerExceptions.Count > 0)
        {
            // WaitAll wraps everything, so the caller would otherwise report the
            // useless "One or more errors occurred." Surface what actually failed.
            System.Runtime.ExceptionServices.ExceptionDispatchInfo
                .Capture(ex.Flatten().InnerExceptions[0]).Throw();
        }

        Aggregate(root);
        return root;
    }

    private static void WorkerLoop(ScanState state)
    {
        var spin = new SpinWait();
        while (true)
        {
            if (state.Ct.IsCancellationRequested)
                return;

            if (!state.Queue.TryDequeue(out var item))
            {
                if (Volatile.Read(ref state.Pending) == 0)
                    return;
                spin.SpinOnce();
                continue;
            }

            spin.Reset();
            bool drained;
            try
            {
                ProcessDirectory(item.Node, item.Path, state);
            }
            finally
            {
                // In a finally: a worker that faulted without decrementing would strand
                // Pending above zero forever, leaving every other worker spinning on a
                // queue that never refills and Task.WaitAll never returning.
                drained = Interlocked.Decrement(ref state.Pending) == 0;
            }

            if (drained)
                return;
        }
    }

    private static void ProcessDirectory(FsNode dir, string path, ScanState state)
    {
        var progress = state.Progress;
        progress.CurrentDir = path;
        var children = new List<FsNode>();

        // Counters are accumulated locally and published once per directory: with a
        // worker per core, per-file Interlocked ops on the shared (same-cache-line)
        // counters ping-pong that line millions of times per scan.
        long fileCount = 0, byteSum = 0, dirCount = 0;

        try
        {
            // Inside the try, standing in for a failure of the enumeration itself —
            // outside it, an injected fault would bypass the handling under test.
            OnDirectoryEnter?.Invoke(path);

            var entries = new FileSystemEnumerable<Entry>(
                path,
                (ref FileSystemEntry e) => new Entry(
                    e.FileName.ToString(),
                    e.IsDirectory ? 0 : e.Length,
                    e.IsDirectory,
                    (e.Attributes & FileAttributes.ReparsePoint) != 0),
                s_options);

            foreach (var entry in entries)
            {
                if (entry.IsDir)
                {
                    if (entry.IsReparse)
                    {
                        // Junction/symlink: show it, but never descend or count it.
                        children.Add(new FsNode(entry.Name, FsNode.FlagDir | FsNode.FlagReparse) { Parent = dir });
                    }
                    else
                    {
                        var node = new FsNode(entry.Name, FsNode.FlagDir) { Parent = dir };
                        children.Add(node);
                        dirCount++;
                        Interlocked.Increment(ref state.Pending);
                        state.Queue.Enqueue((node, Path.Join(path, entry.Name)));
                    }
                }
                else
                {
                    children.Add(new FsNode(entry.Name) { Parent = dir, Size = entry.Length });
                    fileCount++;
                    byteSum += entry.Length;
                }
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            dir.Flags |= FsNode.FlagAccessDenied;
            Interlocked.Increment(ref progress.Errors);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException
                                     or System.Security.SecurityException)
        {
            // A directory the OS will hand us but the path APIs reject — a name with
            // characters Path.Join cannot round-trip, say. Not "denied", but the same
            // outcome for the user: this subtree is unreadable. Anything outside this
            // set (OutOfMemoryException) still propagates, which is now safe.
            dir.Flags |= FsNode.FlagScanError;
            Interlocked.Increment(ref progress.Errors);
        }

        if (fileCount > 0)
        {
            Interlocked.Add(ref progress.Files, fileCount);
            Interlocked.Add(ref progress.Bytes, byteSum);
        }
        if (dirCount > 0)
            Interlocked.Add(ref progress.Dirs, dirCount);

        dir.Children = children.ToArray();
    }

    /// <summary>
    /// Single-threaded post-pass: post-order sum of directory sizes, then sort every
    /// child array size-descending. Fast even for millions of nodes.
    /// </summary>
    private static void Aggregate(FsNode root)
    {
        var stack = new Stack<(FsNode Node, bool ChildrenDone)>();
        stack.Push((root, false));
        while (stack.Count > 0)
        {
            var (node, childrenDone) = stack.Pop();
            var children = node.Children;
            if (!childrenDone && children is { Length: > 0 })
            {
                stack.Push((node, true));
                foreach (var c in children)
                    if (c.Children is { Length: > 0 })
                        stack.Push((c, false));
                continue;
            }

            if (children is not null)
            {
                long sum = 0;
                foreach (var c in children)
                    sum += c.Size;
                node.Size = sum;
                Array.Sort(children, s_sizeDesc);
            }
        }
    }
}
