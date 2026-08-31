using Avalonia.Controls;
using Avalonia.Controls.Models.TreeDataGrid;
using Tessera.Models;
using Tessera.Util;

namespace Tessera.UI;

/// <summary>The tree source, two-way selection sync, and drill/up navigation.</summary>
internal sealed partial class MainWindow
{
    private static HierarchicalTreeDataGridSource<FsNode> CreateSource(FsNode root) => new(new[] { root })
    {
        Columns =
        {
            new HierarchicalExpanderColumn<FsNode>(
                new TextColumn<FsNode, string>("Name", x => x.Name, new GridLength(1, GridUnitType.Star)),
                x => x.Children ?? Array.Empty<FsNode>(),
                x => x.Children != null && x.Children.Length > 0),
            new TextColumn<FsNode, string>("Size", x => Format.Bytes(x.Size), new GridLength(85)),
            new TextColumn<FsNode, string>("%", x => Format.Percent(x.PercentOfParent), new GridLength(70)),
        },
    };

    private void SetTreeSource(FsNode root)
    {
        _source = CreateSource(root);
        _source.RowSelection!.SelectionChanged += (_, _) => OnTreeSelectionChanged();
        _tree.Source = _source;
        _source.Expand(new IndexPath(0));
    }

    private void OnTreeSelectionChanged()
    {
        if (_syncing || _source?.RowSelection?.SelectedItem is not { } node)
            return;
        _syncing = true;
        try
        {
            // If the selection lies outside the current treemap root, pop the treemap back out.
            if (!FsTreeOps.IsDescendantOrSelf(node, _treemap.RootNode))
            {
                _treemap.RootNode = _scanRoot;
                UpdateCrumb();
            }
            _treemap.SelectedNode = node;
        }
        finally { _syncing = false; }
    }

    private IndexPath PathTo(FsNode node)
    {
        var indices = new List<int>();
        for (FsNode? n = node; n?.Parent is not null; n = n.Parent)
            indices.Add(Array.IndexOf(n.Parent.Children!, n));
        indices.Add(0); // the scan root sits at index 0 of the source
        indices.Reverse();
        return new IndexPath(indices.ToArray());
    }

    private void SelectInTree(FsNode node)
    {
        if (_source is null) return;
        var path = PathTo(node);
        // Expand every ancestor so the row exists, then select it.
        for (int len = 1; len < path.Count; len++)
        {
            var prefix = new int[len];
            for (int i = 0; i < len; i++) prefix[i] = path[i];
            _source.Expand(new IndexPath(prefix));
        }
        _source.RowSelection!.SelectedIndex = path;
    }


    internal void SelectFromTreemap(FsNode node, bool drill)
    {
        if (_syncing) return;
        _syncing = true;
        try
        {
            if (drill && node.IsDir && node.Children is { Length: > 0 })
            {
                _treemap.RootNode = node;
                UpdateCrumb();
            }
            _treemap.SelectedNode = node;
            SelectInTree(node);
        }
        finally { _syncing = false; }
    }

    internal void NavigateUp()
    {
        if (_treemap.RootNode?.Parent is { } parent)
        {
            _treemap.RootNode = parent;
            UpdateCrumb();
        }
    }

    private void UpdateCrumb()
    {
        _crumb.Text = _treemap.RootNode?.GetFullPath() ?? "";
        _upButton.IsEnabled = _treemap.RootNode?.Parent is not null;
    }
}
