using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Models.TreeDataGrid;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Tessera.Models;
using Tessera.Scanning;
using Tessera.Util;

namespace Tessera.UI;

public sealed class MainWindow : Window
{
    private readonly ComboBox _driveCombo;
    private readonly Button _scanButton;
    private readonly Button _upButton;
    private readonly Button _topButton;
    private readonly TextBlock _crumb;
    private readonly TextBlock _status;
    private readonly TextBlock _detailNote;
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
    private bool _mutating;
    /// <summary>Bumped whenever the whole model is replaced, so operations that
    /// awaited across the swap can tell their tree is gone.</summary>
    private int _treeGeneration;

    private bool IsScanning => _scanCts is not null;
    /// <summary>True while any operation owns the tree — a scan, or a delete that
    /// is waiting on the shell (where <see cref="IsScanning"/> is false).</summary>
    private bool IsBusy => IsScanning || _mutating;

    internal readonly record struct DeleteRequest(string Name, string FullPath, long Size);

    // Injection seams — tests replace these to run without disk or modal dialogs.
    internal Func<string, ScanProgress, CancellationToken, Task<FsNode>> ScanFunc = Scanner.ScanAsync;
    internal Func<DeleteRequest, Task<bool>> ConfirmDelete;
    internal Func<string, string, Task> ReportProblem;

    // Test seams — the headless test suite drives the window through these.
    internal TreemapControl Treemap => _treemap;
    internal HierarchicalTreeDataGridSource<FsNode>? TreeSource => _source;
    internal string CrumbText => _crumb.Text ?? "";
    internal bool UpEnabled => _upButton.IsEnabled;
    internal FsNode? ContextNode => _ctxNode;
    internal string StatusText => _status.Text ?? "";
    internal string? LastScanPath => _lastPath;
    internal void CancelCurrentScan() => _scanCts?.Cancel();

    /// <summary>
    /// Run an async event-handler body so a failure reaches the user instead of killing
    /// the process: an exception escaping an `async void` handler is unhandled by
    /// definition. Every menu and toolbar action goes through here.
    /// </summary>
    internal async void Guarded(string what, Func<Task> body)
    {
        try
        {
            await body();
        }
        catch (Exception ex)
        {
            _status.Text = $"{what} failed: {ex.Message}";
            try { await ReportProblem($"{what} failed", CrashHandler.Describe(ex)); }
            catch (Exception) { /* the status bar already carries the message */ }
        }
    }

    public MainWindow()
    {
        Title = "Tessera — Disk Space Analyzer";
        Width = 1280;
        Height = 800;

        ConfirmDelete = request => ConfirmDialog.ConfirmDeleteAsync(this, request);
        ReportProblem = (title, message) => ConfirmDialog.ShowMessageAsync(this, title, message);

        // ---- Toolbar ----
        _driveCombo = new ComboBox { MinWidth = 90, PlaceholderText = "Drive" };
        _driveCombo.SelectionChanged += (_, _) =>
        {
            if (_driveCombo.SelectedItem is string drive && !IsBusy)
                StartScan(drive);
        };

        var pickButton = new Button { Content = "Folder…" };
        pickButton.Click += (_, _) => Guarded("Choosing a folder", PickFolderAsync);

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
        _topButton.Click += (_, _) => Guarded("Opening the top files list", () =>
        {
            if (_scanRoot is not null)
                new TopFilesWindow(_scanRoot).Show(this);
            return Task.CompletedTask;
        });

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
        // A separate control from _status: the scan summary lives there and must not be
        // overwritten every time the treemap re-lays out.
        _detailNote = new TextBlock
        {
            Margin = new Thickness(10, 5),
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0.75,
            Text = "",
        };
        DockPanel.SetDock(_detailNote, Dock.Right);
        var statusHost = new Border
        {
            Child = new DockPanel { Children = { _detailNote, _status } },
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
        _treemap.LayoutTruncatedChanged += OnLayoutTruncatedChanged;

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

        // ---- Menu bar (docked above the toolbar) ----
        var menuBar = BuildMenuBar();
        DockPanel.SetDock(menuBar, Dock.Top);

        Content = new DockPanel { Children = { menuBar, toolbar, statusHost, center } };

        // ---- Context menu (shared by tree and treemap) ----
        var menu = BuildContextMenu();
        _tree.ContextMenu = menu;
        _treemap.ContextMenu = menu;

        // ---- Progress timer ----
        _progressTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _progressTimer.Tick += (_, _) => UpdateScanStatus();

        if (Program.InitialPath is { } initial)
            Opened += (_, _) => StartScan(initial);

        DrivesLoaded = LoadDrivesAsync();
    }

    /// <summary>Completes once the drive list has been populated (test seam).</summary>
    internal Task DrivesLoaded { get; }

    internal int DriveCount => (_driveCombo.ItemsSource as string[])?.Length ?? 0;

    /// <summary>
    /// Enumerate drives off the UI thread: DriveInfo.IsReady blocks (and can throw)
    /// on disconnected network mappings, which would otherwise delay first paint.
    /// </summary>
    private async Task LoadDrivesAsync()
    {
        var names = await Task.Run(() =>
        {
            try
            {
                return DriveInfo.GetDrives()
                    .Where(d =>
                    {
                        try { return d.IsReady; }
                        catch (IOException) { return false; }
                        catch (UnauthorizedAccessException) { return false; }
                    })
                    .Select(d => d.Name)
                    .ToArray();
            }
            catch (Exception)
            {
                return Array.Empty<string>();
            }
        });

        // Assigning ItemsSource does not set SelectedItem, so no scan auto-fires.
        _driveCombo.ItemsSource = names;
    }

    // =====================================================================
    // Scanning
    // =====================================================================

    private async Task PickFolderAsync()
    {
        if (IsBusy) return;
        var picked = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose folder to scan",
            AllowMultiple = false,
        });
        if (picked.Count > 0 && picked[0].TryGetLocalPath() is { } path)
            StartScan(path);
    }

    private void StartScan(string path) => Guarded("Scan", () => StartScanAsync(path));

    internal async Task StartScanAsync(string path)
    {
        if (IsBusy) return;

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
            var root = await ScanFunc(path, _progress, _scanCts.Token);
            _scanWatch.Stop();
            bool cancelled = _scanCts.IsCancellationRequested;

            LoadTree(root);
            // Recorded only on success, so "Rescan" never targets a path that failed.
            _lastPath = path;

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
        _treeGeneration++;
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

    // =====================================================================
    // Menu bar
    // =====================================================================

    internal readonly record struct DetailPreset(string Label, string Hint, TreemapLimits Limits);

    internal static readonly DetailPreset[] DetailPresets =
    [
        new("Low", "fewest rectangles, fastest", TreemapLimits.Low),
        new("Medium", "balanced", TreemapLimits.Medium),
        new("High", "more detail", TreemapLimits.High),
        new("Full", "everything visible — the default", TreemapLimits.Full),
    ];

    private Menu BuildMenuBar()
    {
        var detail = new MenuItem { Header = "_Detail" };
        foreach (var preset in DetailPresets)
        {
            var captured = preset;
            var item = new MenuItem
            {
                Header = $"{preset.Label} — {preset.Hint}",
                ToggleType = MenuItemToggleType.Radio,
                GroupName = "TreemapDetail",
                IsChecked = preset.Limits.Equals(_treemap.Limits),
            };
            item.Click += (_, _) => Guarded("Changing detail", () =>
            {
                _treemap.Limits = captured.Limits;
                return Task.CompletedTask;
            });
            detail.Items.Add(item);
            _detailItems.Add(item);
        }

        var view = new MenuItem { Header = "_View" };
        view.Items.Add(detail);

        // The only in-app route to the version, the licence and the third-party notices.
        // The exe is routinely moved on its own, away from the files beside it, so this
        // is what the attribution actually travels in.
        AboutMenuItem = new MenuItem { Header = "_About Tessera" };
        AboutMenuItem.Click += (_, _) => Guarded("Opening About", () =>
        {
            new AboutWindow().Show(this);
            return Task.CompletedTask;
        });
        var help = new MenuItem { Header = "_Help" };
        help.Items.Add(AboutMenuItem);

        return new Menu { Items = { view, help } };
    }

    /// <summary>Help ▸ About Tessera (test seam).</summary>
    internal MenuItem AboutMenuItem { get; private set; } = null!;

    private readonly List<MenuItem> _detailItems = new();

    /// <summary>The detail menu items, in preset order (test seam).</summary>
    internal IReadOnlyList<MenuItem> DetailMenuItems => _detailItems;

    internal string DetailNoteText => _detailNote.Text ?? "";

    /// <summary>
    /// A limit that quietly hid part of the disk would defeat the point of the app, so
    /// say plainly that the view is incomplete and how to get the rest of it.
    /// </summary>
    private void OnLayoutTruncatedChanged(bool truncated) =>
        _detailNote.Text = truncated
            ? $"Showing the first {Format.Count(_treemap.Limits.MaxRects)} rectangles — raise View ▸ Detail for more."
            : "";

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
            return Task.CompletedTask;
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

    /// <summary>
    /// SHFileOperationW shows shell UI and must run on an STA thread — a thread-pool
    /// thread is MTA, which is what made the delete dialog non-modal.
    /// </summary>
    private static Task<ShellResult> DeleteOnStaThreadAsync(string path)
    {
        var completion = new TaskCompletionSource<ShellResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { completion.SetResult(ShellOps.DeleteToRecycleBin(path)); }
            catch (Exception ex) { completion.SetResult(ShellResult.Fail(ex.Message)); }
        })
        { IsBackground = true, Name = "Tessera.Delete" };

        if (OperatingSystem.IsWindows())
            thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    /// <summary>Disable the actions that would race a tree mutation.</summary>
    private void SetMutating(bool on)
    {
        _mutating = on;
        _driveCombo.IsEnabled = !on;
        _scanButton.IsEnabled = !on && _lastPath is not null;
        _topButton.IsEnabled = !on && _scanRoot is not null;
    }

    internal async Task DeleteNodeAsync(FsNode node)
    {
        if (node.Parent is null || IsBusy) return;
        // Refuse nodes already detached from the live tree (e.g. a stale context node).
        if (_scanRoot is null || !FsTreeOps.IsDescendantOrSelf(node, _scanRoot)) return;

        // Capture before mutating: both are wrong once the node is detached.
        string path = node.GetFullPath();
        long size = node.Size;

        if (!await ConfirmDelete(new DeleteRequest(node.Name, path, size)))
            return;

        int generation = _treeGeneration;
        var parent = node.Parent;
        SetMutating(true);
        try
        {
            _status.Text = $"Deleting {node.Name}…";
            var result = await DeleteOnStaThreadAsync(path);

            if (generation != _treeGeneration)
            {
                _status.Text = $"Deleted {node.Name} on disk; the tree was replaced meanwhile.";
                return;
            }

            if (!result.Ok)
            {
                _status.Text = $"Delete failed: {result.Error}";
                await ReportProblem("Delete failed", $"{path}\n\n{result.Error}");
                return;
            }

            if (FsTreeOps.IsDescendantOrSelf(_treemap.RootNode, node))
                _treemap.RootNode = parent;

            FsTreeOps.RemoveChild(node);
            RefreshAfterMutation(parent);
            _status.Text = $"Deleted {node.Name} ({Format.Bytes(size)}) to Recycle Bin.";
        }
        finally
        {
            SetMutating(false);
            _ctxNode = null;
        }
    }

    internal async Task RescanNodeAsync(FsNode node)
    {
        if (!node.IsDir || node.IsReparse || IsBusy) return;
        if (_scanRoot is null || !FsTreeOps.IsDescendantOrSelf(node, _scanRoot)) return;

        int generation = _treeGeneration;
        _scanCts = new CancellationTokenSource();
        _scanButton.Content = "Cancel";
        _scanButton.IsEnabled = true;
        _progress.Reset();
        _progressTimer.Start();
        try
        {
            var fresh = await ScanFunc(node.GetFullPath(), _progress, _scanCts.Token);

            // A cancelled scan returns a PARTIAL tree. Splicing it would overwrite
            // accurate data with an undercount and shrink every ancestor.
            if (_scanCts.IsCancellationRequested)
            {
                _status.Text = $"Rescan of {node.Name} cancelled — tree unchanged.";
                return;
            }

            if (generation != _treeGeneration)
            {
                _status.Text = "Rescan discarded — a new scan replaced the tree.";
                return;
            }

            // SpliceRescan is about to orphan this node's old children; if the
            // treemap is drilled into one of them it would render a detached tree.
            if (FsTreeOps.IsDescendantOrSelf(_treemap.RootNode, node))
                _treemap.RootNode = node;

            FsTreeOps.SpliceRescan(node, fresh);
            RefreshAfterMutation(node);
            _status.Text = $"Rescanned {node.Name}: {Format.Bytes(node.Size)}.";
        }
        catch (Exception ex)
        {
            _status.Text = $"Rescan failed: {ex.Message}";
        }
        finally
        {
            _progressTimer.Stop();
            _scanCts.Dispose();
            _scanCts = null;
            _scanButton.Content = "Rescan";
            _scanButton.IsEnabled = _lastPath is not null;
            _ctxNode = null;
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
