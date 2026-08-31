namespace Tessera.Models;

/// <summary>
/// UI-free mutations and queries over a scanned FsNode tree. All methods preserve
/// the tree invariants the rest of the app relies on: Parent links are correct,
/// directory sizes equal the sum of their descendants, and every Children array
/// stays sorted size-descending.
/// </summary>
internal static class FsTreeOps
{
    private static readonly Comparison<FsNode> s_sizeDesc = (a, b) => b.Size.CompareTo(a.Size);

    /// <summary>Add <paramref name="delta"/> to every ancestor and keep sibling arrays sorted.</summary>
    public static void PropagateSizeDelta(FsNode? from, long delta)
    {
        for (FsNode? a = from; a is not null; a = a.Parent)
        {
            a.Size += delta;
            if (a.Parent?.Children is { } siblings)
                Array.Sort(siblings, s_sizeDesc);
        }
    }

    /// <summary>Detach <paramref name="node"/> from its parent and shrink all ancestor sizes.</summary>
    public static void RemoveChild(FsNode node)
    {
        if (node.Parent is not { } parent)
            return;

        parent.Children = parent.Children!.Where(c => !ReferenceEquals(c, node)).ToArray();
        // Cut the upward link before propagating: a detached node that kept its
        // Parent chain would subtract its size into the live tree a second time
        // if it were ever passed to another mutation (e.g. deleting its old parent).
        node.Parent = null;
        PropagateSizeDelta(parent, -node.Size);
    }

    /// <summary>True when <paramref name="node"/> is <paramref name="root"/> or sits beneath it.</summary>
    public static bool IsDescendantOrSelf(FsNode? node, FsNode? root)
    {
        for (FsNode? n = node; n is not null; n = n.Parent)
            if (ReferenceEquals(n, root))
                return true;
        return false;
    }

    /// <summary>
    /// Replace <paramref name="node"/>'s contents with a freshly scanned subtree and
    /// propagate the size change to its ancestors. Returns the size delta.
    /// </summary>
    public static long SpliceRescan(FsNode node, FsNode fresh)
    {
        long delta = fresh.Size - node.Size;

        node.Children = fresh.Children ?? Array.Empty<FsNode>();
        foreach (var child in node.Children)
            child.Parent = node;

        // Propagate starting AT the node: this both applies the delta (bringing
        // node.Size to fresh.Size) and re-sorts node's own sibling array — which
        // starting at node.Parent would skip.
        PropagateSizeDelta(node, delta);
        return delta;
    }

    /// <summary>Bounded min-heap over an iterative DFS — O(n log k), no recursion-depth risk.</summary>
    public static List<FsNode> FindLargest(FsNode root, int count)
    {
        var heap = new PriorityQueue<FsNode, long>(count + 1);
        var stack = new Stack<FsNode>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (node.Children is { } children)
            {
                foreach (var c in children)
                    stack.Push(c);
            }
            else if (!node.IsDir)
            {
                if (heap.Count < count)
                    heap.Enqueue(node, node.Size);
                else if (heap.TryPeek(out _, out long min) && node.Size > min)
                    heap.DequeueEnqueue(node, node.Size);
            }
        }

        var result = new List<FsNode>(heap.Count);
        while (heap.TryDequeue(out var item, out _))
            result.Add(item);
        result.Reverse(); // heap drains smallest-first
        return result;
    }
}
