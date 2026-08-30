using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Rendering;
using Avalonia.Threading;
using Tessera.Models;
using Tessera.Util;

namespace Tessera.UI;

/// <summary>
/// Squarified treemap of a scanned tree. The layout (a flat list of rectangles,
/// parent-first) is cached and only recomputed when the root node, the data, or the
/// control size changes; hover/selection changes just repaint from the cache.
/// </summary>
public sealed class TreemapControl : Control
{
    private readonly List<TmRect> _layout = new();
    private bool _layoutDirty = true;
    private FsNode? _root;
    private FsNode? _selected;
    private FsNode? _hover;
    // The static scene (fills, borders, labels) is expensive — thousands of draw
    // calls plus a FormattedText per label — so it is rendered once into a bitmap
    // per layout change; hover/selection frames just blit it and draw two outlines.
    private RenderTargetBitmap? _scene;
    private bool _sceneDirty = true;
    private TreemapLimits _limits = TreemapLimits.Full;

    // Node -> index into _layout. DrawOutline runs twice per frame and used to scan the
    // whole list for one node; at Full's half-million rectangles that is the difference
    // between an O(1) probe and two full passes every time the pointer moves.
    private readonly Dictionary<FsNode, int> _indexByNode = new(ReferenceEqualityComparer.Instance);
    // Uniform bucket grid over the control, built with the layout. Hit-testing walks one
    // cell instead of the entire list, so pointer cost stops scaling with detail.
    private int[]? _cellStart;      // CSR offsets, length _cols * _rows + 1
    private int[]? _cellItems;      // rect indices, ascending within each cell
    private int _cols, _rows;
    private double _cellSize;

    private static readonly Dictionary<string, IBrush> s_extBrushes = new();
    private static readonly IBrush s_dirFill = new SolidColorBrush(Color.FromRgb(0x3a, 0x3f, 0x46));
    private static readonly Pen s_dirBorder = new(new SolidColorBrush(Color.FromRgb(0x1e, 0x21, 0x25)), 1);
    private static readonly Pen s_hoverPen = new(Brushes.White, 1.5);
    private static readonly Pen s_selectPen = new(new SolidColorBrush(Color.FromRgb(0x00, 0xb0, 0xff)), 2.5);
    private static readonly IBrush s_labelBrush = new SolidColorBrush(Color.FromArgb(0xE6, 0x10, 0x12, 0x14));
    private static readonly IBrush s_dirLabelBrush = new SolidColorBrush(Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF));
    private static readonly Typeface s_typeface = new(FontFamily.Default, weight: FontWeight.Medium);

    public event Action<FsNode>? NodeClicked;
    public event Action<FsNode>? NodeDoubleClicked;
    public event Action<FsNode>? NodeRightClicked;
    public event Action<FsNode?>? HoverChanged;
    /// <summary>
    /// Raised when the layout starts or stops being cut short by the rectangle cap.
    /// The layout is lazy — recomputed during render — so this is the only way for the
    /// window to learn that the view is incomplete without polling every frame.
    /// </summary>
    public event Action<bool>? LayoutTruncatedChanged;

    public TreemapControl()
    {
        ClipToBounds = true;
        Focusable = true;
    }

    public FsNode? RootNode
    {
        get => _root;
        set
        {
            if (ReferenceEquals(_root, value)) return;
            _root = value;
            _selected = null;
            _hover = null;
            InvalidateLayout();
        }
    }

    /// <summary>How far the layout may go. Changing it re-lays out on the next render.</summary>
    public TreemapLimits Limits
    {
        get => _limits;
        set
        {
            if (_limits.Equals(value)) return;
            _limits = value;
            InvalidateLayout();
        }
    }

    /// <summary>True when the rectangle cap cut the last layout short.</summary>
    public bool LayoutTruncated { get; private set; }

    public FsNode? SelectedNode
    {
        get => _selected;
        set
        {
            if (ReferenceEquals(_selected, value)) return;
            _selected = value;
            InvalidateVisual();
        }
    }

    /// <summary>Call after the underlying tree data changed (rescan, delete).</summary>
    public void InvalidateLayout()
    {
        _layoutDirty = true;
        InvalidateVisual();
    }

    /// <summary>Recompute the layout if dirty and return it. Render and HitTest both rely on this.</summary>
    internal IReadOnlyList<TmRect> EnsureLayout()
    {
        if (_layoutDirty)
        {
            _layout.Clear();
            bool truncated = _root is not null
                && Squarify.Layout(_root, new Rect(Bounds.Size).Deflate(1), 0, _layout, _limits);
            _layoutDirty = false;
            _sceneDirty = true;
            BuildLookups();

            // Only on a transition: EnsureLayout runs per render, and the window would
            // otherwise rewrite the status bar on every frame.
            if (truncated != LayoutTruncated)
            {
                LayoutTruncated = truncated;
                // Posted, never called inline. Render calls EnsureLayout, and the
                // handler writes to a TextBlock — mutating another visual mid-pass
                // throws "Visual was invalidated during the render pass", which kills
                // the renderer, so the window freezes on its last frame. Only layouts
                // over MaxRects transition, so this hit exactly the big scans (C:\).
                if (LayoutTruncatedChanged is { } handler)
                    Dispatcher.UIThread.Post(() => handler(truncated));
            }
        }
        return _layout;
    }

    /// <summary>
    /// Build the node index and the hit-test grid for the current layout. Both are pure
    /// functions of <see cref="_layout"/> and are rebuilt with it.
    /// </summary>
    private void BuildLookups()
    {
        _indexByNode.Clear();
        _indexByNode.EnsureCapacity(_layout.Count);
        for (int i = 0; i < _layout.Count; i++)
            _indexByNode[_layout[i].Node] = i;   // each node is emitted exactly once

        if (_layout.Count == 0 || Bounds.Width < 1 || Bounds.Height < 1)
        {
            _cellStart = null;
            _cellItems = null;
            return;
        }

        // ~32px cells: small enough that a cell holds few rectangles, large enough that
        // a full-canvas parent is not copied into thousands of buckets.
        _cellSize = 32;
        _cols = Math.Max(1, (int)Math.Ceiling(Bounds.Width / _cellSize));
        _rows = Math.Max(1, (int)Math.Ceiling(Bounds.Height / _cellSize));

        // CSR build: count per cell, prefix-sum to offsets, then fill. One int[] for the
        // whole grid rather than a List<int> per cell, which at this size would be
        // hundreds of thousands of allocations.
        int cellCount = _cols * _rows;
        var counts = new int[cellCount + 1];
        for (int i = 0; i < _layout.Count; i++)
        {
            CellRange(_layout[i].Bounds, out int c0, out int r0, out int c1, out int r1);
            for (int r = r0; r <= r1; r++)
                for (int c = c0; c <= c1; c++)
                    counts[r * _cols + c]++;
        }

        var start = new int[cellCount + 1];
        int running = 0;
        for (int i = 0; i < cellCount; i++)
        {
            start[i] = running;
            running += counts[i];
        }
        start[cellCount] = running;

        var items = new int[running];
        var cursor = new int[cellCount];
        for (int i = 0; i < _layout.Count; i++)
        {
            CellRange(_layout[i].Bounds, out int c0, out int r0, out int c1, out int r1);
            for (int r = r0; r <= r1; r++)
                for (int c = c0; c <= c1; c++)
                {
                    int cell = r * _cols + c;
                    items[start[cell] + cursor[cell]++] = i;   // ascending: i grows
                }
        }

        _cellStart = start;
        _cellItems = items;
    }

    /// <summary>Clamped grid cells a rectangle overlaps.</summary>
    private void CellRange(Rect b, out int c0, out int r0, out int c1, out int r1)
    {
        c0 = Math.Clamp((int)(b.X / _cellSize), 0, _cols - 1);
        r0 = Math.Clamp((int)(b.Y / _cellSize), 0, _rows - 1);
        c1 = Math.Clamp((int)(b.Right / _cellSize), 0, _cols - 1);
        r1 = Math.Clamp((int)(b.Bottom / _cellSize), 0, _rows - 1);
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        _layoutDirty = true;
    }

    public FsNode? HitTest(Point p)
    {
        var layout = EnsureLayout();
        if (layout.Count == 0)
            return null;

        if (_cellStart is { } start && _cellItems is { } items)
        {
            if (p.X < 0 || p.Y < 0 || p.X >= _cols * _cellSize || p.Y >= _rows * _cellSize)
                return HitTestLinear(p);   // outside the grid; the oracle still answers correctly

            int cell = Math.Clamp((int)(p.Y / _cellSize), 0, _rows - 1) * _cols
                     + Math.Clamp((int)(p.X / _cellSize), 0, _cols - 1);
            // Descending: indices ascend within a cell and children are appended after
            // their parent, so the last match is the deepest — same rule as the scan below.
            for (int k = start[cell + 1] - 1; k >= start[cell]; k--)
            {
                int i = items[k];
                if (layout[i].Bounds.Contains(p))
                    return layout[i].Node;
            }
            return null;
        }

        return HitTestLinear(p);
    }

    /// <summary>
    /// The original full scan. Kept as the definition of correct: the grid is an index
    /// over this, and a test asserts the two agree point for point.
    /// </summary>
    internal FsNode? HitTestLinear(Point p)
    {
        var layout = EnsureLayout();
        // Backwards: children are appended after their parent, so the first hit is the deepest node.
        for (int i = layout.Count - 1; i >= 0; i--)
            if (layout[i].Bounds.Contains(p))
                return layout[i].Node;
        return null;
    }

    public override void Render(DrawingContext ctx)
    {
        var bounds = new Rect(Bounds.Size);

        if (_root is null)
        {
            ctx.FillRectangle(s_dirFill, bounds);
            return;
        }

        EnsureLayout();

        if (_sceneDirty)
        {
            _scene?.Dispose();
            _scene = TryRenderScene(bounds);
            _sceneDirty = false;
        }

        if (_scene is not null)
        {
            ctx.DrawImage(_scene, bounds);
        }
        else
        {
            // Fallback when render-target bitmaps are unavailable (e.g. headless
            // test platform): draw the scene directly, exactly as before.
            ctx.FillRectangle(s_dirFill, bounds);
            DrawScene(ctx);
        }

        DrawOutline(ctx, _hover, s_hoverPen);
        DrawOutline(ctx, _selected, s_selectPen);
    }

    private RenderTargetBitmap? TryRenderScene(Rect bounds)
    {
        if (bounds.Width < 1 || bounds.Height < 1)
            return null;
        try
        {
            double scaling = (VisualRoot as IRenderRoot)?.RenderScaling ?? 1.0;
            var pixelSize = new PixelSize(
                Math.Max(1, (int)Math.Ceiling(bounds.Width * scaling)),
                Math.Max(1, (int)Math.Ceiling(bounds.Height * scaling)));
            var bitmap = new RenderTargetBitmap(pixelSize, new Vector(96 * scaling, 96 * scaling));
            using (var dc = bitmap.CreateDrawingContext())
            {
                dc.FillRectangle(s_dirFill, bounds);
                DrawScene(dc);
            }
            return bitmap;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void DrawScene(DrawingContext ctx)
    {
        foreach (var tm in _layout)
        {
            var fill = tm.Node.IsDir ? s_dirFill : GetExtensionBrush(tm.Node.Extension);
            ctx.FillRectangle(fill, tm.Bounds);
            ctx.DrawRectangle(s_dirBorder, tm.Bounds.Deflate(0.5));
        }

        // Labels in a second pass so nested directory fills never cover file labels.
        foreach (var tm in _layout)
        {
            var b = tm.Bounds;
            if (b.Width < 44 || b.Height < 15)
                continue;
            // A directory's interior is tiled by its children; only leaves get labels.
            if (!tm.IsLeaf)
                continue;

            var text = new FormattedText(tm.Node.Name, System.Globalization.CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight, s_typeface, 11,
                tm.Node.IsDir ? s_dirLabelBrush : s_labelBrush)
            {
                MaxTextWidth = Math.Max(4, b.Width - 6),
                MaxTextHeight = Math.Max(4, b.Height - 2),
                Trimming = TextTrimming.CharacterEllipsis,
                MaxLineCount = 1,
            };
            ctx.DrawText(text, new Point(b.X + 3, b.Y + 1));
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _scene?.Dispose();
        _scene = null;
        _sceneDirty = true;
    }

    private void DrawOutline(DrawingContext ctx, FsNode? node, Pen pen)
    {
        if (node is null) return;
        if (_indexByNode.TryGetValue(node, out int i))
            ctx.DrawRectangle(pen, _layout[i].Bounds.Deflate(pen.Thickness / 2));
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var hit = HitTest(e.GetPosition(this));
        if (!ReferenceEquals(hit, _hover))
        {
            _hover = hit;
            ToolTip.SetTip(this, hit is null ? null : $"{hit.GetFullPath()}\n{Format.Bytes(hit.Size)}");
            HoverChanged?.Invoke(hit);
            InvalidateVisual();
        }
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (_hover is not null)
        {
            _hover = null;
            HoverChanged?.Invoke(null);
            InvalidateVisual();
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var hit = HitTest(e.GetPosition(this));
        if (hit is null) return;

        var props = e.GetCurrentPoint(this).Properties;
        if (props.IsRightButtonPressed)
        {
            NodeRightClicked?.Invoke(hit);
        }
        else if (props.IsLeftButtonPressed)
        {
            if (e.ClickCount == 2)
                NodeDoubleClicked?.Invoke(hit);
            else
                NodeClicked?.Invoke(hit);
        }
    }

    // ---- Coloring ----

    private static IBrush GetExtensionBrush(string ext)
    {
        if (s_extBrushes.TryGetValue(ext, out var brush))
            return brush;

        double hue = (Fnv1a(ext) % 360 + 360) % 360;
        var mid = Hsl(hue, 0.52, 0.58);
        var brush2 = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Hsl(hue, 0.55, 0.70), 0),
                new GradientStop(mid, 0.55),
                new GradientStop(Hsl(hue, 0.50, 0.42), 1),
            },
        };
        s_extBrushes[ext] = brush2;
        return brush2;
    }

    internal static int Fnv1a(string s)
    {
        unchecked
        {
            uint hash = 2166136261;
            foreach (char c in s)
            {
                hash ^= c;
                hash *= 16777619;
            }
            return (int)hash;
        }
    }

    internal static Color Hsl(double h, double s, double l)
    {
        double c = (1 - Math.Abs(2 * l - 1)) * s;
        double x = c * (1 - Math.Abs(h / 60 % 2 - 1));
        double m = l - c / 2;
        (double r, double g, double b) = ((int)(h / 60) % 6) switch
        {
            0 => (c, x, 0.0),
            1 => (x, c, 0.0),
            2 => (0.0, c, x),
            3 => (0.0, x, c),
            4 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };
        return Color.FromRgb((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
    }
}
