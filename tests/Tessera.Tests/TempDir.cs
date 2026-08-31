using System.Security.AccessControl;
using System.Security.Principal;
using System.Runtime.Versioning;

namespace Tessera.Tests;

/// <summary>
/// A throwaway directory under %TEMP%, and the one teardown that copes with everything
/// this suite deliberately creates in one.
///
/// Six places used to build their own with <c>Path.Combine(Path.GetTempPath(), ...)</c>,
/// and four of them disposed differently — one retried on IOException, one walked
/// junctions by hand, one removed deny-ACEs, one swallowed the exception. Each strategy
/// was right for what that file created and wrong for what the others did. This does all
/// of it, so no caller has to remember which hazard applies to it.
/// </summary>
internal sealed class TempDir : IDisposable
{
    /// <summary>Directories with a deny-ACE that must be lifted before the delete.</summary>
    private readonly List<string> _denied = new();

    internal string Path { get; }

    internal TempDir(string prefix = "TesseraTests")
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"{prefix}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path);
    }

    /// <summary>A path inside this directory. Does not create anything.</summary>
    internal string Sub(params string[] parts) =>
        System.IO.Path.Combine(new[] { Path }.Concat(parts).ToArray());

    /// <summary>Create a directory that the current user cannot list, and remember to unlock it.</summary>
    [SupportedOSPlatform("windows")]
    internal string CreateDeniedDir(string name, byte[]? content = null)
    {
        string path = System.IO.Path.Combine(Path, name);
        Directory.CreateDirectory(path);
        File.WriteAllBytes(System.IO.Path.Combine(path, "secret.bin"), content ?? new byte[777]);

        var info = new DirectoryInfo(path);
        var security = info.GetAccessControl();
        security.AddAccessRule(new FileSystemAccessRule(
            WindowsIdentity.GetCurrent().User!,
            FileSystemRights.ListDirectory,
            AccessControlType.Deny));
        info.SetAccessControl(security);
        _denied.Add(path);
        return path;
    }

    public void Dispose()
    {
        try
        {
            if (!Directory.Exists(Path))
                return;

            // Order matters, and all three steps must precede the delete:
            //   1. lift deny-ACEs, or enumerating below throws on those directories;
            //   2. remove junctions, BEFORE any recursive walk — the reparse tests build
            //      one pointing back up its own tree, so a walk would follow it round;
            //   3. clear attributes, which is when a recursive walk is finally safe.
            if (OperatingSystem.IsWindows())
                foreach (var dir in _denied)
                    RemoveDeny(dir);

            RemoveJunctions();
            ClearAttributes();
            DeleteWithOneRetry();
        }
        catch (Exception)
        {
            // A leftover directory under %TEMP% is untidy; a failing teardown that masks
            // the real assertion is worse.
        }
    }

    [SupportedOSPlatform("windows")]
    private static void RemoveDeny(string dir)
    {
        var info = new DirectoryInfo(dir);
        var security = info.GetAccessControl();
        security.RemoveAccessRuleAll(new FileSystemAccessRule(
            WindowsIdentity.GetCurrent().User!,
            FileSystemRights.ListDirectory,
            AccessControlType.Deny));
        info.SetAccessControl(security);
    }

    /// <summary>Hidden and read-only entries refuse to delete until they are plain.</summary>
    private void ClearAttributes()
    {
        foreach (var f in new DirectoryInfo(Path).EnumerateFiles("*", SearchOption.AllDirectories))
        {
            try { f.Attributes = FileAttributes.Normal; }
            catch (Exception) { /* best effort */ }
        }
    }

    /// <summary>
    /// Junctions must go first and non-recursively: a recursive delete refuses them, and
    /// the reparse tests deliberately build one pointing back up its own tree, so walking
    /// through it would not terminate.
    /// </summary>
    private void RemoveJunctions()
    {
        var stack = new Stack<string>();
        stack.Push(Path);
        var junctions = new List<string>();
        while (stack.Count > 0)
        {
            foreach (var sub in Directory.EnumerateDirectories(stack.Pop()))
            {
                if ((File.GetAttributes(sub) & FileAttributes.ReparsePoint) != 0)
                    junctions.Add(sub);
                else
                    stack.Push(sub);
            }
        }
        foreach (var junction in junctions)
            Directory.Delete(junction, recursive: false);
    }

    /// <summary>A scan that has just finished may still hold a handle for a moment.</summary>
    private void DeleteWithOneRetry()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            Thread.Sleep(200);
            Directory.Delete(Path, recursive: true);
        }
    }
}
