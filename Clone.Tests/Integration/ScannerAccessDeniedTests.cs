using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using Clone.Models;
using Clone.Scanning;
using Xunit;

namespace Clone.Tests.Integration;

/// <summary>
/// Access-denied handling via a deny-ListDirectory ACE for the current user's own
/// SID — needs no elevation, and teardown removes the ACE before deleting.
/// All tests are [WindowsFact]-gated; the attribute satisfies the platform analyzer.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ScannerAccessDeniedTests : IDisposable
{
    private readonly string _root;
    private readonly List<string> _deniedDirs = new();

    public ScannerAccessDeniedTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"CloneTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        foreach (var dir in _deniedDirs)
            RemoveDeny(dir);
        Directory.Delete(_root, recursive: true);
    }

    private string CreateDeniedDir(string name)
    {
        string path = Path.Combine(_root, name);
        Directory.CreateDirectory(path);
        File.WriteAllBytes(Path.Combine(path, "secret.bin"), new byte[777]);

        var info = new DirectoryInfo(path);
        var security = info.GetAccessControl();
        security.AddAccessRule(new FileSystemAccessRule(
            WindowsIdentity.GetCurrent().User!,
            FileSystemRights.ListDirectory,
            AccessControlType.Deny));
        info.SetAccessControl(security);
        _deniedDirs.Add(path);
        return path;
    }

    private static void RemoveDeny(string path)
    {
        if (!Directory.Exists(path))
            return;
        var info = new DirectoryInfo(path);
        var security = info.GetAccessControl();
        security.RemoveAccessRule(new FileSystemAccessRule(
            WindowsIdentity.GetCurrent().User!,
            FileSystemRights.ListDirectory,
            AccessControlType.Deny));
        info.SetAccessControl(security);
    }

    private (FsNode Root, ScanProgress Progress) Scan()
    {
        var progress = new ScanProgress();
        var task = Scanner.ScanAsync(_root, progress, CancellationToken.None);
        Assert.True(task.Wait(TimeSpan.FromSeconds(30)), "scan did not complete within 30s");
        return (task.Result, progress);
    }

    [WindowsFact]
    public void DeniedDir_Flagged_ErrorCounted_ChildrenEmpty()
    {
        CreateDeniedDir("denied");
        var (root, progress) = Scan();

        var denied = root.Children!.Single(c => c.Name == "denied");
        Assert.True(denied.IsAccessDenied);
        Assert.NotNull(denied.Children);
        Assert.Empty(denied.Children!);
        Assert.Equal(0, denied.Size);
        Assert.Equal(1, Volatile.Read(ref progress.Errors));
    }

    [WindowsFact]
    public void DeniedDir_SiblingsStillScanned()
    {
        CreateDeniedDir("denied");
        string ok = Path.Combine(_root, "readable");
        Directory.CreateDirectory(ok);
        File.WriteAllBytes(Path.Combine(ok, "visible.bin"), new byte[1234]);

        var (root, progress) = Scan();

        Assert.Equal(1234, root.Size);
        Assert.Equal(1234, root.Children!.Single(c => c.Name == "readable").Size);
        Assert.Equal(1, Volatile.Read(ref progress.Files));
        Assert.Equal(1, Volatile.Read(ref progress.Errors));
    }

    [WindowsFact]
    public void TwoDeniedDirs_ErrorsIsTwo()
    {
        CreateDeniedDir("denied1");
        CreateDeniedDir("denied2");

        var (_, progress) = Scan();

        Assert.Equal(2, Volatile.Read(ref progress.Errors));
    }
}
