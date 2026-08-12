using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Models.TreeDataGrid;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Clone.Models;
using Clone.Scanning;
using Clone.Util;

namespace Clone.UI;

public sealed class MainWindow : Window
{
    private readonly ComboBox _driveCombo;
    private readonly Button _scanButton;
    private readonly Button _upButton;
    private readonly Button _topButton;
    private readonly TextBlock _crumb;
    private readonly TextBlock _status;
    private readonly TreeDataGrid _tree;
    private readonly TreemapControl _treemap;
    private readonly DispatcherTimer _progressTimer;
    private readonly ScanProgress _progress = new();
    private readonly Stopwatch _scanWatch = new();

    private HierarchicalTreeDataGridSource<FsNode>? _source;
    private FsNode? _scanRoot;
    private string? _lastPath;
    private CancellationTokenSource? _scanCts;
    private bool _syncing;
    private FsNode? _ctxNode;

    private bool IsScanning => _scanCts is not null;

    // Test seams — the headless test suite drives the window through these.
    internal TreemapControl Treemap => _treemap;
    internal HierarchicalTreeDataGridSource<FsNode>? TreeSource => _source;
    internal string CrumbText => _crumb.Text ?? "";
    internal bool UpEnabled => _upButton.IsEnabled;

    public MainWindow()
    {
        Title = "Clone — Disk Space Analyzer";
        Width = 1280;
        Height = 800;

        // ---- Toolbar ----
        _driveCombo = new ComboBox { MinWidth = 90, PlaceholderText = "Drive" };
        _driveCombo.ItemsSource = DriveInfo.GetDrives()
            .Where(d => d.IsReady)
            .Select(d => d.Name)
            .ToArray();
        _driveCombo.SelectionChanged += (_, _) =>
        {
            if (_driveCombo.SelectedItem is string drive && !IsScanning)
                StartScan(drive);
        };

        var pickButton = new Button { Content = "Folder…" };
        pickButton.Click += async (_, _) => await PickFolderAsync();

        _scanButton = new Button { Content = "Rescan", IsEnabled = false };
        _scanButton.Click += (_, _) =>
        {
            if (IsScanning)
                _scanCts!.Cancel();
            else if (_lastPath is not null)
                StartScan(_lastPath);
        };

        _upButton = new Button { Content = "⬆ Up", IsEnabled = false };
        _upButton.Click += (_, _) => NavigateUp();

        _topButton = new Button { Content = "Top 100", IsEnabled = false };
        _topButton.Click += (_, _) =>
        {
            if (_scanRoot is not null)
                new TopFilesWindow(_scanRoot).Show(this);
        };

        _crumb = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0),
            Opacity = 0.8,
        };

        var toolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Margin = new Thickness(8, 8, 8, 4),
            Children = { _driveCombo, pickButton, _scanButton, _upButton, _topButton, _crumb },
        };
        DockPanel.SetDock(toolbar, Dock.Top);

        // ---- Status bar ----
        _status = new TextBlock { Margin = new Thickness(10, 5), Text = "Pick a drive or folder to scan." };
        var statusHost = new Border
        {
            Child = _status,
            BorderThickness = new Thickness(0, 1, 0, 0),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x30, 0x80, 0x80, 0x80)),
        };
        DockPanel.SetDock(statusHost, Dock.Bottom);

        // ---- Center: tree | splitter | treemap ----
        _tree = new TreeDataGrid { CanUserResizeColumns = true };
        _treemap = new TreemapControl();
        _treemap.NodeClicked += n => SelectFromTreemap(n, drill: false);
        _treemap.NodeDoubleClicked += n => SelectFromTreemap(n, drill: true);
        _treemap.NodeRightClicked += n =>
        {
            _ctxNode = n;
            SelectFromTreemap(n, drill: false);
        };

        var center = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("420,4,*"),
            Margin = new Thickness(8, 4, 8, 4),
        };
        var splitter = new GridSplitter
        {
            Background = new SolidColorBrush(Color.FromArgb(0x30, 0x80, 0x80, 0x80)),
            ResizeDirection = GridResizeDirection.Columns,
        };
        Grid.SetColumn(_tree, 0);
        Grid.SetColumn(splitter, 1);
        Grid.SetColumn(_treemap, 2);
        center.Children.Add(_tree);
        center.Children.Add(splitter);
        center.Children.Add(_treemap);

        Content = new DockPanel { Children = { toolbar, statusHost, center } };

        // ---- Context menu (shared by tree and treemap) ----
        var menu = BuildContextMenu();
        _tree.ContextMenu = menu;
        _treemap.ContextMenu = menu;

        // ---- Progress timer ----
        _progressTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _progressTimer.Tick += (_, _) => UpdateScanStatus();

        if (Program.InitialPath is { } initial)
            Opened += (_, _) => StartScan(initial);
    }

    // =====================================================================
    // Scanning
    // =====================================================================

    private async Task PickFolderAsync()
    {
        if (IsScanning) return;
        var picked = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose folder to scan",
            AllowMultiple = false,
        });
        if (picked.Count > 0 && picked[0].TryGetLocalPath() is { } path)
            StartScan(path);
    }

    private async void StartScan(string path)
    {
        if (IsScanning) return;

        _lastPath = path;
        _scanCts = new CancellationTokenSource();
        _scanButton.Content = "Cancel";
        _scanButton.IsEnabled = true;
        _topButton.IsEnabled = false;
        _upButton.IsEnabled = false;
        _progress.Reset();
        _scanWatch.Restart();
        _progressTimer.Start();

        try
        {
            var root = await Scanner.ScanAsync(path, _progress, _scanCts.Token);
            _scanWatch.Stop();
            bool cancelled = _scanCts.IsCancellationRequested;

            LoadTree(root);

            _status.Text =
                $"{(cancelled ? "Cancelled — partial results. " : "")}" +
                $"{Format.Count(Volatile.Read(ref _progress.Files))} files, " +
                $"{Format.Count(Volatile.Read(ref _progress.Dirs))} folders, " +
                $"{Format.Bytes(root.Size)}" +
                $"{(Volatile.Read(ref _progress.Errors) is var err and > 0 ? $", {err} inaccessible" : "")} " +
                $"— {_scanWatch.Elapsed.TotalSeconds:F1} s";
        }
        catch (Exception ex)
        {
            _status.Text = $"Scan failed: {ex.Message}";
        }
        finally
        {
            _progressTimer.Stop();
            _scanCts.Dispose();
            _scanCts = null;
            _scanButton.Content = "Rescan";
            _scanButton.IsEnabled = _lastPath is not null;
            _topButton.IsEnabled = _scanRoot is not null;
        }
    }

    /// <summary>Adopt a scanned tree as the current model (also the injection point for tests).</summary>
    internal void LoadTree(FsNode root)
    {
        _scanRoot = root;
        SetTreeSource(root);
        _treemap.RootNode = root;
        UpdateCrumb();
        _topButton.IsEnabled = true;
    }

    private void UpdateScanStatus()
    {
        string dir = _progress.CurrentDir ?? "";
        if (dir.Length > 70)
            dir = "…" + dir[^69..];
        _status.Text = $"Scanning — {Format.Count(Volatile.Read(ref _progress.Files))} files, " +
                       $"{Format.Bytes(Volatile.Read(ref _progress.Bytes))} — {dir}";
    }

    // =====================================================================
    // Tree pane
    // =====================================================================

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
            if (!IsUnder(node, _treemap.RootNode))
                _treemap.RootNode = _scanRoot;
            _treemap.SelectedNode = node;
        }
        finally { _syncing = false; }
    }

    private static bool IsUnder(FsNode node, FsNode? root)
    {
        for (FsNode? n = node; n is not null; n = n.Parent)
            if (ReferenceEquals(n, root))
                return true;
        return false;
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

    // =====================================================================
    // Treemap interaction
    // =====================================================================

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

    // =====================================================================
    // Context menu / node operations
    // =====================================================================

    private ContextMenu BuildContextMenu()
    {
        var open = new MenuItem { Header = "Open in Explorer" };
        open.Click += (_, _) => { if (_ctxNode is { } n) ShellOps.RevealInFileManager(n.GetFullPath(), n.IsDir); };

        var copy = new MenuItem { Header = "Copy path" };
        copy.Click += async (_, _) =>
        {
            if (_ctxNode is { } n && Clipboard is { } cb)
                await cb.SetTextAsync(n.GetFullPath());
        };

        var delete = new MenuItem { Header = "Delete (Recycle Bin)" };
        delete.Click += async (_, _) => { if (_ctxNode is { } n) await DeleteNodeAsync(n); };

        var rescan = new MenuItem { Header = "Rescan folder" };
        rescan.Click += async (_, _) => { if (_ctxNode is { } n) await RescanNodeAsync(n); };

        var top = new MenuItem { Header = "Top 100 files here" };
        top.Click += (_, _) => { if (_ctxNode is { IsDir: true } n) new TopFilesWindow(n).Show(this); };

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

            var state = GetContextMenuState(_ctxNode, IsScanning);
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

    internal static CtxMenuState GetContextMenuState(FsNode? node, bool isScanning)
    {
        if (node is null || isScanning)
            return new CtxMenuState(false, false, false, false);
        bool isDir = node.IsDir && !node.IsReparse;
        return new CtxMenuState(
            Show: true,
            CanDelete: node.Parent is not null, // never delete the scan root
            CanRescan: isDir,
            CanTopFiles: isDir);
    }

    private async Task DeleteNodeAsync(FsNode node)
    {
        if (node.Parent is null || IsScanning) return;

        string path = node.GetFullPath();
        bool deleted = await Task.Run(() => ShellOps.DeleteToRecycleBin(path));
        if (!deleted) return;

        var parent = node.Parent;
        FsTreeOps.RemoveChild(node);

        if (IsUnder(_treemap.RootNode ?? node, node) || ReferenceEquals(_treemap.RootNode, node))
            _treemap.RootNode = parent;

        RefreshAfterMutation(parent);
        _status.Text = $"Deleted {node.Name} ({Format.Bytes(node.Size)}) to Recycle Bin.";
    }

    private async Task RescanNodeAsync(FsNode node)
    {
        if (!node.IsDir || node.IsReparse || IsScanning) return;

        _scanCts = new CancellationTokenSource();
        _scanButton.Content = "Cancel";
        _progress.Reset();
        _progressTimer.Start();
        try
        {
            var fresh = await Scanner.ScanAsync(node.GetFullPath(), _progress, _scanCts.Token);
            FsTreeOps.SpliceRescan(node, fresh);
            RefreshAfterMutation(node);
            _status.Text = $"Rescanned {node.Name}: {Format.Bytes(node.Size)}.";
        }
        finally
        {
            _progressTimer.Stop();
            _scanCts.Dispose();
            _scanCts = null;
            _scanButton.Content = "Rescan";
        }
    }

    /// <summary>Rebuild the tree source (sizes/order changed) and restore selection near <paramref name="focus"/>.</summary>
    private void RefreshAfterMutation(FsNode focus)
    {
        if (_scanRoot is null) return;
        // Also resort the mutated node's own children container's parent — cheap safety net.
        SetTreeSource(_scanRoot);
        _syncing = true;
        try
        {
            SelectInTree(focus);
            _treemap.SelectedNode = focus;
        }
        finally { _syncing = false; }
        _treemap.InvalidateLayout();
        UpdateCrumb();
    }
}
