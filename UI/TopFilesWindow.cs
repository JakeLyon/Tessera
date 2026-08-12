using Avalonia.Controls;
using Avalonia.Controls.Models.TreeDataGrid;
using Clone.Models;
using Clone.Util;

namespace Clone.UI;

/// <summary>Flat list of the 100 largest files under a given directory node.</summary>
public sealed class TopFilesWindow : Window
{
    private const int TopCount = 100;

    public TopFilesWindow(FsNode root)
    {
        Title = $"Top {TopCount} largest files — {root.GetFullPath()}";
        Width = 900;
        Height = 620;

        var files = FsTreeOps.FindLargest(root, TopCount);

        var source = new FlatTreeDataGridSource<FsNode>(files)
        {
            Columns =
            {
                new TextColumn<FsNode, string>("Size", x => Format.Bytes(x.Size), new GridLength(90)),
                new TextColumn<FsNode, string>("Name", x => x.Name, new GridLength(260)),
                new TextColumn<FsNode, string>("Path", x => x.GetFullPath(), new GridLength(1, GridUnitType.Star)),
            },
        };

        var grid = new TreeDataGrid { Source = source, CanUserResizeColumns = true };
        grid.DoubleTapped += (_, _) =>
        {
            if (source.RowSelection?.SelectedItem is { } node)
                ShellOps.RevealInFileManager(node.GetFullPath(), isDirectory: false);
        };

        Content = grid;
    }
}
