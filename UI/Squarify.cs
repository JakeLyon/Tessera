using Avalonia;
using Tessera.Models;

namespace Tessera.UI;

/// <summary>One laid-out treemap rectangle.</summary>
public readonly record struct TmRect(FsNode Node, Rect Bounds, int Depth, bool IsLeaf);

/// <summary>
/// How far the layout is allowed to go. A full drive produces more rectangles than
/// can be seen or hit-tested cheaply, so the recursion stops on all four counts:
/// too small to read, too deep to matter, or simply too many.
/// </summary>
/// <param name="MinSide">Don't recurse into a rectangle narrower or shorter than this.</param>
/// <param name="MinArea">Don't recurse into a rectangle smaller than this many square px.</param>
/// <param name="MaxDepth">Hard cap on nesting depth.</param>
/// <param name="MaxRects">Hard cap on the total number of rectangles emitted.</param>
public readonly record struct TreemapLimits(double MinSide, double MinArea, int MaxDepth, int MaxRects)
{
    public static readonly TreemapLimits Low = new(8, 64, 8, 5_000);

    /// <summary>
    /// The cutoffs the layout used before detail limits existed, kept as the baseline
    /// the presets are measured against. No longer the app's default — see <see cref="Full"/>.
    /// </summary>
    public static readonly TreemapLimits Medium = new(4, 20, 24, 20_000);

    public static readonly TreemapLimits High = new(2, 8, 40, 100_000);

    /// <summary>
    /// The app's default: everything big enough to see. MinSide 1 is gated as
    /// <c>MinSide + 2</c> below to offset the per-level <c>Deflate(1)</c>, so the
    /// recursion stops at 3px — the narrowest rectangle that still leaves an inner
    /// pixel to draw. Past that a rectangle is sub-pixel and cannot be shown at all,
    /// which is what makes this "full" in the only sense the screen allows.
    /// </summary>
    public static readonly TreemapLimits Full = new(1, 2, 64, 500_000);
}

/// <summary>
/// Squarified treemap layout (Bruls, Huizing, van Wijk). Pure geometry — no UI state.
/// Rectangles are appended parent-first, so painting in list order layers children
/// over parents and a backwards scan hit-tests deepest-first.
/// </summary>
public static class Squarify
{
    /// <summary>
    /// Lay out <paramref name="dir"/>'s children inside <paramref name="rect"/> and recurse.
    /// Returns true when <see cref="TreemapLimits.MaxRects"/> cut the output short — the
    /// caller is showing an incomplete picture and needs to say so.
    /// </summary>
    public static bool Layout(FsNode dir, Rect rect, int depth, List<TmRect> output,
        TreemapLimits? limits = null)
    {
        // Medium, not the app default: this fallback exists for callers that omit the
        // argument, and pins the historical cutoffs the layout tests are written against.
        // The app always passes TreemapControl.Limits explicitly.
        var effective = limits ?? TreemapLimits.Medium;
        LayoutCore(dir, rect, depth, output, effective);
        return output.Count >= effective.MaxRects;
    }

    private static void LayoutCore(FsNode dir, Rect rect, int depth, List<TmRect> output,
        TreemapLimits limits)
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
                    FlushRow(children, rowStart, i, rowArea, scale, ref x, ref y, ref w, ref h, depth, output, limits);
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
                FlushRow(children, rowStart, i, rowArea, scale, ref x, ref y, ref w, ref h, depth, output, limits);
                rowStart = i;
                rowArea = area; rowMin = area; rowMax = area;
            }
        }

        FlushRow(children, rowStart, children.Length, rowArea, scale, ref x, ref y, ref w, ref h, depth, output, limits);
    }

    private static double WorstAspect(double rowArea, double minArea, double maxArea, double side)
    {
        double s2 = rowArea * rowArea;
        double w2 = side * side;
        return Math.Max(w2 * maxArea / s2, s2 / (w2 * minArea));
    }

    /// <summary>Place children[start..end) as one strip along the shorter side of the remaining rect.</summary>
    private static void FlushRow(FsNode[] children, int start, int end, double rowArea, double scale,
        ref double x, ref double y, ref double w, ref double h, int depth, List<TmRect> output,
        TreemapLimits limits)
    {
        if (end <= start || rowArea <= 0 || w <= 0 || h <= 0)
            return;

        bool vertical = w >= h; // strip is a column on the left when the rect is wide
        double side = vertical ? h : w;
        double thickness = Math.Min(rowArea / side, vertical ? w : h);

        double offset = 0;
        for (int i = start; i < end; i++)
        {
            // The hard cap. Only reached on huge single-level fan-out — a directory
            // with more files than the whole budget — where a truncated row is the
            // only option left. The recursion gate below handles every other case.
            if (output.Count >= limits.MaxRects)
                return;

            var child = children[i];
            double length = child.Size * scale / thickness;
            var bounds = vertical
                ? new Rect(x, y + offset, thickness, length)
                : new Rect(x + offset, y, length, thickness);
            offset += length;

            // Running out of budget stops the descent exactly as a too-small rectangle
            // does: the node is still emitted, marked IsLeaf, and painted as a solid
            // block. Levels that are drawn stay complete — no holes.
            bool recurse = child.IsDir && depth < limits.MaxDepth
                && bounds.Width >= limits.MinSide + 2 && bounds.Height >= limits.MinSide + 2
                && bounds.Width * bounds.Height >= limits.MinArea
                && child.Children is { Length: > 0 }
                && output.Count < limits.MaxRects - 1;

            output.Add(new TmRect(child, bounds, depth, !recurse));

            if (recurse)
                LayoutCore(child, bounds.Deflate(1), depth + 1, output, limits);
        }

        if (vertical) { x += thickness; w -= thickness; }
        else { y += thickness; h -= thickness; }
    }
}
