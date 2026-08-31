using Avalonia;
using Tessera.Models;

namespace Tessera.UI;

/// <summary>One laid-out treemap rectangle.</summary>
internal readonly record struct TmRect(FsNode Node, Rect Bounds, int Depth, bool IsLeaf);

/// <summary>What a rectangle's fill colour tells you.</summary>
internal enum TreemapColorMode
{
    /// <summary>How deeply the item is nested, as SpaceMonger does it. The default.</summary>
    Depth,

    /// <summary>The file's extension, so one kind of file is one colour across the drive.</summary>
    Extension,
}

/// <summary>
/// Squarified treemap layout (Bruls, Huizing, van Wijk). Pure geometry — no UI state.
/// Rectangles are appended parent-first, so painting in list order layers children
/// over parents and a backwards scan hit-tests deepest-first.
/// </summary>
internal static class Squarify
{
    /// <summary>
    /// Don't recurse into a rectangle narrower or shorter than this. Gated below as
    /// <c>MinSide + 2</c> to offset the per-level <see cref="Rect.Deflate"/> of 1, so the
    /// descent stops at 3px — the narrowest rectangle that still leaves an inner pixel to
    /// draw. Below that there is no pixel to put a child in, so there is nothing to show.
    /// </summary>
    internal const double MinSide = 1;

    /// <summary>Don't recurse into a rectangle smaller than this many square px.</summary>
    internal const double MinArea = 2;

    /// <summary>
    /// A sanity bound, not a detail setting. Real trees are far shallower; this only stops
    /// a pathological one from recursing without end.
    /// </summary>
    internal const int MaxDepth = 64;

    /// <summary>
    /// Lay out <paramref name="dir"/>'s children inside <paramref name="rect"/> and recurse.
    /// Everything large enough to see is drawn — there is no cap and nothing is grouped
    /// away, so the picture is never quietly incomplete. Detail beyond the pixel floor is
    /// reached by drilling into a folder, which lays it out again in the whole pane.
    /// </summary>
    public static void Layout(FsNode dir, Rect rect, int depth, List<TmRect> output)
        => LayoutCore(dir, rect, depth, output);

    private static void LayoutCore(FsNode dir, Rect rect, int depth, List<TmRect> output)
    {
        var children = dir.Children;
        if (children is null || children.Length == 0 || rect.Width <= 1 || rect.Height <= 1)
            return;

        long total = 0;
        foreach (var c in children)
            total += c.Size;
        if (total <= 0)
            return;

        double scale = rect.Width * rect.Height / total;

        // Current row state. Children are pre-sorted size-descending, which squarify requires.
        double x = rect.X, y = rect.Y, w = rect.Width, h = rect.Height;
        int rowStart = 0;
        double rowArea = 0, rowMin = double.MaxValue, rowMax = 0;

        for (int i = 0; i < children.Length; i++)
        {
            double area = children[i].Size * scale;
            if (area <= 0)
            {
                // Sorted descending: everything from here on is zero-size. Flush and stop.
                if (i > rowStart)
                    FlushRow(children, rowStart, i, rowArea, scale, ref x, ref y, ref w, ref h, depth, output);
                return;
            }

            if (i == rowStart)
            {
                rowArea = area; rowMin = area; rowMax = area;
                continue;
            }

            double side = Math.Min(w, h);
            double before = WorstAspect(rowArea, rowMin, rowMax, side);
            double after = WorstAspect(rowArea + area, Math.Min(rowMin, area), Math.Max(rowMax, area), side);
            if (after <= before)
            {
                rowArea += area; rowMin = Math.Min(rowMin, area); rowMax = Math.Max(rowMax, area);
            }
            else
            {
                FlushRow(children, rowStart, i, rowArea, scale, ref x, ref y, ref w, ref h, depth, output);
                rowStart = i;
                rowArea = area; rowMin = area; rowMax = area;
            }
        }

        FlushRow(children, rowStart, children.Length, rowArea, scale, ref x, ref y, ref w, ref h, depth, output);
    }

    private static double WorstAspect(double rowArea, double minArea, double maxArea, double side)
    {
        double s2 = rowArea * rowArea;
        double w2 = side * side;
        return Math.Max(w2 * maxArea / s2, s2 / (w2 * minArea));
    }

    /// <summary>Place children[start..end) as one strip along the shorter side of the remaining rect.</summary>
    private static void FlushRow(FsNode[] children, int start, int end, double rowArea, double scale,
        ref double x, ref double y, ref double w, ref double h, int depth, List<TmRect> output)
    {
        if (end <= start || rowArea <= 0 || w <= 0 || h <= 0)
            return;

        bool vertical = w >= h; // strip is a column on the left when the rect is wide
        double side = vertical ? h : w;
        double thickness = Math.Min(rowArea / side, vertical ? w : h);

        double offset = 0;
        for (int i = start; i < end; i++)
        {
            var child = children[i];
            double length = child.Size * scale / thickness;
            var bounds = vertical
                ? new Rect(x, y + offset, thickness, length)
                : new Rect(x + offset, y, length, thickness);
            offset += length;

            // Declining to descend still emits the node, marked IsLeaf, so it paints as a
            // solid labelled block: every level that is drawn stays complete — no holes.
            // Only geometry stops the descent now; nothing is dropped for want of budget.
            bool recurse = child.IsDir && depth < MaxDepth
                && bounds.Width >= MinSide + 2 && bounds.Height >= MinSide + 2
                && bounds.Width * bounds.Height >= MinArea
                && child.Children is { Length: > 0 };

            output.Add(new TmRect(child, bounds, depth, !recurse));

            if (recurse)
                LayoutCore(child, bounds.Deflate(1), depth + 1, output);
        }

        if (vertical) { x += thickness; w -= thickness; }
        else { y += thickness; h -= thickness; }
    }
}
