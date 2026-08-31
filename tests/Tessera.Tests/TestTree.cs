using Tessera.Models;

namespace Tessera.Tests;

/// <summary>
/// Builder for synthetic FsNode trees that reproduces the invariants the scanner
/// guarantees: Parent links set, directory sizes equal the sum of their descendants,
/// and every Children array sorted size-descending. Squarify and the tree-mutation
/// helpers REQUIRE these invariants, so all synthetic trees must go through Seal.
/// </summary>
internal static class TestTree
{
    internal static FsNode File(string name, long size) => new(name) { Size = size };

    internal static FsNode Dir(string name, params FsNode[] children) =>
        new(name, FsNode.FlagDir) { Children = children };

    internal static FsNode Reparse(string name) =>
        new(name, FsNode.FlagDir | FsNode.FlagReparse);

    /// <summary>Set Parent links, aggregate dir sizes bottom-up, sort children size-desc.</summary>
    internal static FsNode Seal(FsNode root)
    {
        SealNode(root);
        return root;
    }

    private static long SealNode(FsNode node)
    {
        if (node.Children is { } children)
        {
            long sum = 0;
            foreach (var c in children)
            {
                c.Parent = node;
                sum += SealNode(c);
            }
            node.Size = sum;
            Array.Sort(children, (a, b) => b.Size.CompareTo(a.Size));
        }
        return node.Size;
    }

    /// <summary>All file (leaf, non-dir) nodes in the subtree.</summary>
    internal static IEnumerable<FsNode> Files(FsNode root)
    {
        var stack = new Stack<FsNode>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            if (n.Children is { } children)
                foreach (var c in children)
                    stack.Push(c);
            else if (!n.IsDir)
                yield return n;
        }
    }

    /// <summary>Find a node by name anywhere in the subtree (names must be unique in test trees).</summary>
    internal static FsNode Find(FsNode root, string name)
    {
        var stack = new Stack<FsNode>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            if (n.Name == name)
                return n;
            if (n.Children is { } children)
                foreach (var c in children)
                    stack.Push(c);
        }
        throw new InvalidOperationException($"Node '{name}' not found");
    }
}
