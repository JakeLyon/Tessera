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
using Tessera.Treemap;
using Tessera.Util;

namespace Tessera.UI;

internal sealed partial class MainWindow : Window
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
    private bool _mutating;
    /// <summary>Bumped whenever the whole model is replaced, so operations that
    /// awaited across the swap can tell their tree is gone.</summary>
    private int _treeGeneration;

    private bool IsScanning => _scanCts is not null;
    /// <summary>True while any operation owns the tree — a scan, or a delete that
    /// is waiting on the shell (where <see cref="IsScanning"/> is false).</summary>
    private bool IsBusy => IsScanning || _mutating;

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

    /// <summary>
    /// <see cref="Guarded(string, Func{Task})"/> for a handler whose body is synchronous.
    /// Most of them are, and wrapping each one in a lambda that returns Task.CompletedTask
    /// was pure ceremony.
    /// </summary>
    internal void Guarded(string what, Action body) =>
        Guarded(what, () => { body(); return Task.CompletedTask; });

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

}
