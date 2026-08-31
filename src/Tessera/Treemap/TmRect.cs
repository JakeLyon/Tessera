using Avalonia;
using Tessera.Models;

namespace Tessera.Treemap;

/// <summary>One laid-out treemap rectangle.</summary>
internal readonly record struct TmRect(FsNode Node, Rect Bounds, int Depth, bool IsLeaf);
