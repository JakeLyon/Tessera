using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Rendering;
using Tessera.Models;
using Tessera.Util;

namespace Tessera.UI;

/// <summary>
/// SpaceMonger-style squarified treemap. The layout (a flat list of rectangles,
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
    private TreemapLimits _limits = TreemapLimits.Medium;

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

            // Only on a transition: EnsureLayout runs per render, and the window would
            // otherwise rewrite the status bar on every frame.
            if (truncated != LayoutTruncated)
            {
                LayoutTruncated = truncated;
                LayoutTruncatedChanged?.Invoke(truncated);
            }
        }
        return _layout;
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        _layoutDirty = true;
    }

    public FsNode? HitTest(Point p)
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
        foreach (var tm in _layout)
        {
            if (ReferenceEquals(tm.Node, node))
            {
                ctx.DrawRectangle(pen, tm.Bounds.Deflate(pen.Thickness / 2));
                return;
            }
        }
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
