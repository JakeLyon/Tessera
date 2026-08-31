using System.Text;

namespace Tessera.Models;

/// <summary>
/// One scanned file or directory. Kept deliberately small — a full drive scan can
/// produce millions of these. Full paths are never stored; they are rebuilt on
/// demand by walking <see cref="Parent"/> (the root node's Name holds the absolute path).
/// </summary>
internal sealed class FsNode
{
    public const byte FlagDir = 1;
    public const byte FlagReparse = 2;
    public const byte FlagAccessDenied = 4;
    /// <summary>The directory could not be read for a reason other than permissions.</summary>
    public const byte FlagScanError = 8;

    public string Name;
    public long Size;
    public FsNode? Parent;
    /// <summary>null = file; empty = empty directory. Sorted by Size descending after the scan post-pass.</summary>
    public FsNode[]? Children;
    public byte Flags;

    public FsNode(string name, byte flags = 0)
    {
        Name = name;
        Flags = flags;
    }

    public bool IsDir => (Flags & FlagDir) != 0;
    public bool IsReparse => (Flags & FlagReparse) != 0;
    public bool IsAccessDenied => (Flags & FlagAccessDenied) != 0;
    public bool IsScanError => (Flags & FlagScanError) != 0;

    public double PercentOfParent => Parent is { Size: > 0 } p ? (double)Size / p.Size : 1.0;

    public string GetFullPath()
    {
        if (Parent is null)
            return Name;

        var stack = new Stack<FsNode>();
        for (FsNode? n = this; n is not null; n = n.Parent)
            stack.Push(n);

        var sb = new StringBuilder(260);
        foreach (var n in stack)
        {
            if (sb.Length > 0 && sb[^1] != Path.DirectorySeparatorChar)
                sb.Append(Path.DirectorySeparatorChar);
            sb.Append(n.Name);
        }
        return sb.ToString();
    }

    /// <summary>Extension including the dot, lower-cased; empty for directories/extensionless files.</summary>
    public string Extension => IsDir ? "" : Path.GetExtension(Name).ToLowerInvariant();

    /// <summary>
    /// Size descending — the order every <see cref="Children"/> array is kept in. It lives
    /// here because that invariant belongs to the node: the scanner establishes it and
    /// every mutation restores it, and both used to declare this comparer for themselves.
    /// </summary>
    public static readonly Comparison<FsNode> SizeDescending = (a, b) => b.Size.CompareTo(a.Size);
}
