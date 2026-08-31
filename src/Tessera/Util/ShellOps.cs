using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Tessera.Util;

/// <summary>OS shell integration: recycle-bin delete, reveal in file manager.</summary>
internal static class ShellOps
{
    // ---- Windows: SHFileOperationW ----

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCTW
    {
        public IntPtr hwnd;
        public uint wFunc;
        public string pFrom;
        public string? pTo;
        public ushort fFlags;
        [MarshalAs(UnmanagedType.Bool)] public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string? lpszProgressTitle;
    }

    private const uint FO_DELETE = 0x0003;
    private const ushort FOF_NOCONFIRMATION = 0x0010;
    private const ushort FOF_ALLOWUNDO = 0x0040;
    private const ushort FOF_WANTNUKEWARNING = 0x4000;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperationW(ref SHFILEOPSTRUCTW op);

    /// <summary>
    /// <see cref="DeleteToRecycleBin"/> on a dedicated STA thread. SHFileOperationW shows
    /// shell UI and must run in an STA apartment; a thread-pool thread is MTA, which is
    /// what made the delete dialog non-modal. The requirement belongs to the P/Invoke, so
    /// the thread that satisfies it lives here rather than in the calling window.
    /// </summary>
    internal static Task<ShellResult> DeleteToRecycleBinOnStaThreadAsync(string path)
    {
        var completion = new TaskCompletionSource<ShellResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { completion.SetResult(DeleteToRecycleBin(path)); }
            catch (Exception ex) { completion.SetResult(ShellResult.Fail(ex.Message)); }
        })
        { IsBackground = true, Name = "Tessera.Delete" };

        if (OperatingSystem.IsWindows())
            thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    /// <summary>
    /// Move a file or directory to the Recycle Bin (Windows) or trash (elsewhere).
    /// Never throws — transport and shell failures come back as <see cref="ShellResult"/>.
    /// </summary>
    public static ShellResult DeleteToRecycleBin(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return ShellResult.Fail("no path was given.");

        if (OperatingSystem.IsWindows())
            return WindowsRecycle(path);

        if (OperatingSystem.IsMacOS())
            return RunAndWait(BuildMacTrashPsi(path), "osascript");

        return RunAndWait(BuildLinuxTrashPsi(path), "gio");
    }

    private static ShellResult WindowsRecycle(string path)
    {
        string full;
        try { full = Path.GetFullPath(path); }
        catch (Exception ex) { return ShellResult.Fail($"the path is not usable: {ex.Message}"); }

        // SHFileOperationW is documented as MAX_PATH-bound regardless of the
        // process's long-path awareness, and fails obscurely past it. A disk
        // analyzer surfaces exactly these paths (node_modules), so say so plainly.
        if (full.Length >= 260)
            return ShellResult.Fail(
                $"the path is {full.Length} characters long. The Windows Recycle Bin API " +
                "cannot handle paths of 260 characters or more. Shorten or move it first.");

        var op = new SHFILEOPSTRUCTW
        {
            wFunc = FO_DELETE,
            // pFrom must be double-null-terminated; the marshaller adds one
            // terminator, we add the other. Without it the shell reads past the
            // string and can pick up an unrelated neighbouring path.
            pFrom = full + "\0",
            // The app always confirms first, so the shell's own prompt would be a
            // second dialog — but keep NUKEWARNING, the one prompt that matters:
            // "too large for the Recycle Bin, this will be permanent".
            fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_WANTNUKEWARNING,
        };

        int code;
        try { code = SHFileOperationW(ref op); }
        catch (Exception ex) { return ShellResult.Fail($"the shell call failed: {ex.Message}"); }

        if (op.fAnyOperationsAborted)
            return ShellResult.Fail("the operation was cancelled.");

        return code == 0 ? ShellResult.Success : ShellResult.Fail(DescribeShellError(code));
    }

    /// <summary>SHFileOperation returns its own pre-Win32 codes, not HRESULTs.</summary>
    private static string DescribeShellError(int code) => code switch
    {
        0x71 => "the source and destination are the same file.",
        0x75 => "the operation was cancelled by the user.",
        0x78 => "access was denied to the source file.",
        0x79 => "the path is too deep for the shell to process.",
        0x7C => "the path is invalid.",
        0x80 => "an item with the same name already exists in the Recycle Bin.",
        0x402 => "the path could not be found (unknown shell error).",
        0x10000 => "an unspecified error occurred during the operation.",
        _ => $"the shell returned error 0x{code:X}.",
    };

    // ---- macOS / Linux ----

    /// <summary>
    /// osascript arguments that trash <paramref name="path"/>. The path is passed as
    /// an argv operand, never interpolated into the script — a filename containing a
    /// quote (or a whole AppleScript fragment) is then just data.
    /// </summary>
    internal static string[] BuildMacTrashArgs(string path) =>
    [
        "-e", "on run argv",
        "-e", "tell application \"Finder\" to delete POSIX file (item 1 of argv)",
        "-e", "end run",
        path,
    ];

    private static ProcessStartInfo BuildMacTrashPsi(string path)
    {
        var psi = new ProcessStartInfo("osascript") { UseShellExecute = false, CreateNoWindow = true };
        foreach (var arg in BuildMacTrashArgs(path))
            psi.ArgumentList.Add(arg);
        return psi;
    }

    private static ProcessStartInfo BuildLinuxTrashPsi(string path)
    {
        // "--" stops a leading-dash filename being read as an option.
        var psi = new ProcessStartInfo("gio") { UseShellExecute = false, CreateNoWindow = true };
        psi.ArgumentList.Add("trash");
        psi.ArgumentList.Add("--");
        psi.ArgumentList.Add(path);
        return psi;
    }

    /// <summary>Run a helper to completion. A missing binary is a failure, not an exception.</summary>
    internal static ShellResult RunAndWait(ProcessStartInfo psi, string tool)
    {
        try
        {
            using var process = Process.Start(psi);
            if (process is null)
                return ShellResult.Fail($"{tool} could not be started.");
            process.WaitForExit();
            return process.ExitCode == 0
                ? ShellResult.Success
                : ShellResult.Fail($"{tool} exited with code {process.ExitCode}.");
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException
                                      or PlatformNotSupportedException or IOException)
        {
            return ShellResult.Fail($"{tool} is not available: {ex.Message}");
        }
    }

    /// <summary>Start a helper without waiting. A missing binary is a failure, not an exception.</summary>
    private static ShellResult RunNoWait(ProcessStartInfo psi, string tool)
    {
        try
        {
            using var process = Process.Start(psi);
            return process is null ? ShellResult.Fail($"{tool} could not be started.") : ShellResult.Success;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException
                                      or PlatformNotSupportedException or IOException)
        {
            return ShellResult.Fail($"{tool} is not available: {ex.Message}");
        }
    }

    /// <summary>Open the containing folder with the item selected (or the folder itself).</summary>
    public static ShellResult RevealInFileManager(string path, bool isDirectory)
    {
        if (string.IsNullOrWhiteSpace(path))
            return ShellResult.Fail("no path was given.");

        if (OperatingSystem.IsWindows())
        {
            // UseShellExecute = false so .NET quotes the argument itself, and so a
            // failure surfaces here rather than as a shell error dialog. explorer.exe
            // legitimately exits non-zero, so never wait on or check its exit code.
            var psi = new ProcessStartInfo("explorer.exe") { UseShellExecute = false };
            psi.ArgumentList.Add(isDirectory ? path : $"/select,{path}");
            return RunNoWait(psi, "Explorer");
        }

        string opener = OperatingSystem.IsMacOS() ? "open" : "xdg-open";
        string target = isDirectory ? path : (Path.GetDirectoryName(path) ?? path);
        var openPsi = new ProcessStartInfo(opener) { UseShellExecute = false };
        openPsi.ArgumentList.Add(target);
        return RunNoWait(openPsi, opener);
    }
}
