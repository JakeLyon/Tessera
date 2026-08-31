using Avalonia.Controls;
using Tessera.Models;
using Tessera.Util;

namespace Tessera.UI;

/// <summary>Operations that mutate the tree: delete, rescan, and the refresh after.</summary>
internal sealed partial class MainWindow
{
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
            var result = await ShellOps.DeleteToRecycleBinOnStaThreadAsync(path);

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
        var scan = BeginScan();
        try
        {
            var fresh = await ScanFunc(node.GetFullPath(), _progress, scan.Token);

            // A cancelled scan returns a PARTIAL tree. Splicing it would overwrite
            // accurate data with an undercount and shrink every ancestor.
            if (scan.IsCancellationRequested)
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
            EndScan(scan);
            _ctxNode = null;
        }
    }

    /// <summary>Rebuild the tree source (sizes/order changed) and restore selection near <paramref name="focus"/>.</summary>
    private void RefreshAfterMutation(FsNode focus)
    {
        if (_scanRoot is null) return;
        // A full rebuild rather than a targeted refresh: a mutation re-sorts sibling
        // arrays all the way to the root, so every row's position may have moved.
        SetTreeSource(_scanRoot);
        // Restoring selection would otherwise bounce back through the tree's
        // SelectionChanged handler and fight the treemap for the same node.
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
