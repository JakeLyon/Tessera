using Avalonia.Controls;
using Tessera.Models;
using Tessera.Treemap;
using Tessera.Util;

namespace Tessera.UI;

/// <summary>Menu bar and context menu.</summary>
internal sealed partial class MainWindow
{
    internal readonly record struct ColorChoice(string Label, string Hint, TreemapColorMode Mode);

    internal static readonly ColorChoice[] ColorChoices =
    [
        new("Depth", "how deeply nested — the default", TreemapColorMode.Depth),
        new("File type", "by extension", TreemapColorMode.Extension),
    ];

    private Menu BuildMenuBar()
    {
        var colour = new MenuItem { Header = "_Colour" };
        foreach (var choice in ColorChoices)
        {
            var captured = choice;
            var item = new MenuItem
            {
                Header = $"{choice.Label} — {choice.Hint}",
                ToggleType = MenuItemToggleType.Radio,
                GroupName = "TreemapColour",
                IsChecked = choice.Mode == _treemap.ColorMode,
            };
            item.Click += (_, _) => Guarded("Changing colours", () => _treemap.ColorMode = captured.Mode);
            colour.Items.Add(item);
            _colorItems.Add(item);
        }

        // Off by default: it only means anything for a drive scan, and it takes space away
        // from the data the user came to look at.
        FreeSpaceMenuItem = new MenuItem
        {
            Header = "Show _free space",
            ToggleType = MenuItemToggleType.CheckBox,
            IsChecked = _treemap.ShowFreeSpace,
        };
        FreeSpaceMenuItem.Click += (_, _) => Guarded("Toggling free space", () => _treemap.ShowFreeSpace = FreeSpaceMenuItem.IsChecked);

        var view = new MenuItem { Header = "_View" };
        view.Items.Add(colour);
        view.Items.Add(FreeSpaceMenuItem);

        // The only in-app route to the version, the licence and the third-party notices.
        // The exe is routinely moved on its own, away from the files beside it, so this
        // is what the attribution actually travels in.
        AboutMenuItem = new MenuItem { Header = "_About Tessera" };
        AboutMenuItem.Click += (_, _) => Guarded("Opening About", () => new AboutWindow().Show(this));
        var help = new MenuItem { Header = "_Help" };
        help.Items.Add(AboutMenuItem);

        return new Menu { Items = { view, help } };
    }

    /// <summary>Help ▸ About Tessera (test seam).</summary>
    internal MenuItem AboutMenuItem { get; private set; } = null!;

    /// <summary>View ▸ Show free space (test seam).</summary>
    internal MenuItem FreeSpaceMenuItem { get; private set; } = null!;

    private readonly List<MenuItem> _colorItems = new();

    /// <summary>The colour menu items, in choice order (test seam).</summary>
    internal IReadOnlyList<MenuItem> ColorMenuItems => _colorItems;

    private ContextMenu BuildContextMenu()
    {
        var open = new MenuItem { Header = "Open in Explorer" };
        open.Click += (_, _) =>
        {
            if (_ctxNode is not { } n) return;
            if (ShellOps.RevealInFileManager(n.GetFullPath(), n.IsDir) is { Ok: false } result)
                _status.Text = $"Could not open the file manager: {result.Error}";
        };

        var copy = new MenuItem { Header = "Copy path" };
        // Another application holding the clipboard open is a routine Windows failure,
        // and SetTextAsync throwing here used to be fatal.
        copy.Click += (_, _) => Guarded("Copying the path", async () =>
        {
            if (_ctxNode is { } n && Clipboard is { } cb)
                await cb.SetTextAsync(n.GetFullPath());
        });

        var delete = new MenuItem { Header = "Delete (Recycle Bin)" };
        delete.Click += (_, _) => Guarded("Delete", async () =>
        {
            if (_ctxNode is { } n) await DeleteNodeAsync(n);
        });

        var rescan = new MenuItem { Header = "Rescan folder" };
        rescan.Click += (_, _) => Guarded("Rescan", async () =>
        {
            if (_ctxNode is { } n) await RescanNodeAsync(n);
        });

        var top = new MenuItem { Header = "Top 100 files here" };
        top.Click += (_, _) => Guarded("Opening the top files list", () =>
        {
            if (_ctxNode is { IsDir: true } n) new TopFilesWindow(n).Show(this);
        });

        var menu = new ContextMenu
        {
            Items = { open, copy, delete, new Separator(), rescan, top },
        };
        menu.Opening += (_, e) =>
        {
            // Opened over the tree: act on the tree's selected row. The treemap's
            // right-click handler has already set _ctxNode for the treemap case.
            if (!ReferenceEquals(menu.PlacementTarget, _treemap))
                _ctxNode = _source?.RowSelection?.SelectedItem;

            var state = GetContextMenuState(_ctxNode, IsBusy);
            if (!state.Show)
            {
                e.Cancel = true;
                return;
            }
            delete.IsEnabled = state.CanDelete;
            rescan.IsEnabled = state.CanRescan;
            top.IsEnabled = state.CanTopFiles;
        };
        return menu;
    }

    internal readonly record struct CtxMenuState(bool Show, bool CanDelete, bool CanRescan, bool CanTopFiles);

    internal static CtxMenuState GetContextMenuState(FsNode? node, bool isBusy)
    {
        if (node is null || isBusy)
            return new CtxMenuState(false, false, false, false);
        bool isDir = node.IsDir && !node.IsReparse;
        return new CtxMenuState(
            Show: true,
            CanDelete: node.Parent is not null, // never delete the scan root
            CanRescan: isDir,
            CanTopFiles: isDir);
    }
}
