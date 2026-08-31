using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Tessera.Models;
using Tessera.Scanning;
using Tessera.Util;

namespace Tessera.UI;

/// <summary>Scanning: drive list, folder picking, and the scan lifecycle.</summary>
internal sealed partial class MainWindow
{
    /// <summary>Completes once the drive list has been populated (test seam).</summary>
    internal Task DrivesLoaded { get; }

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

        var scan = BeginScan();
        // A full scan additionally parks the buttons that would act on a tree it is about
        // to replace, and times itself; a rescan does neither.
        _topButton.IsEnabled = false;
        _upButton.IsEnabled = false;
        _scanWatch.Restart();

        try
        {
            var root = await ScanFunc(path, _progress, scan.Token);
            _scanWatch.Stop();
            bool cancelled = scan.IsCancellationRequested;

            LoadTree(root);
            // Recorded only on success, so "Rescan" never targets a path that failed.
            _lastPath = path;
            _treemap.FreeSpaceBytes = DiskSpace.FreeBytesForDriveRoot(path);

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
            EndScan(scan);
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

    /// <summary>
    /// The start-of-scan bookkeeping both scan paths share: a fresh cancellation source,
    /// the Rescan button turned into Cancel, the progress counters zeroed and the progress
    /// timer running. Paired with <see cref="EndScan"/> in a finally.
    ///
    /// What the two paths do NOT share stays at the call sites rather than becoming flags
    /// here — a full scan also parks the Top-100 and Up buttons and times itself, and a
    /// rescan clears the context node it was launched from. Those differences were
    /// previously invisible, spread across two hand-rolled copies of this sequence.
    /// </summary>
    private CancellationTokenSource BeginScan()
    {
        var cts = new CancellationTokenSource();
        _scanCts = cts;
        _scanButton.Content = "Cancel";
        _scanButton.IsEnabled = true;
        _progress.Reset();
        _progressTimer.Start();
        return cts;
    }

    /// <summary>Unwind <see cref="BeginScan"/>, however the scan ended.</summary>
    private void EndScan(CancellationTokenSource scan)
    {
        _progressTimer.Stop();
        scan.Dispose();
        _scanCts = null;
        _scanButton.Content = "Rescan";
        _scanButton.IsEnabled = _lastPath is not null;
    }
}
