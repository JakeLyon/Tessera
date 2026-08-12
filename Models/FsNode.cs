using System.Text;

namespace Clone.Models;

/// <summary>
/// One scanned file or directory. Kept deliberately small — a full drive scan can
/// produce millions of these. Full paths are never stored; they are rebuilt on
/// demand by walking <see cref="Parent"/> (the root node's Name holds the absolute path).
/// </summary>
public sealed class FsNode
{
    public const byte FlagDir = 1;
    public const byte FlagReparse = 2;
    public const byte FlagAccessDenied = 4;

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
}
