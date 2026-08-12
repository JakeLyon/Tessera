using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Clone.Util;

/// <summary>OS shell integration: recycle-bin delete, reveal in file manager.</summary>
public static class ShellOps
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
    private const ushort FOF_ALLOWUNDO = 0x0040;
    private const ushort FOF_NOCONFIRMATION = 0x0010;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperationW(ref SHFILEOPSTRUCTW op);

    /// <summary>
    /// Move a file or directory to the Recycle Bin (Windows) or trash (elsewhere).
    /// Returns true when the item was actually deleted.
    /// </summary>
    public static bool DeleteToRecycleBin(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            var op = new SHFILEOPSTRUCTW
            {
                wFunc = FO_DELETE,
                // SHFileOperationW requires pFrom to be double-null-terminated;
                // the marshaller adds one terminator, we add the other.
                pFrom = path + "\0",
                fFlags = FOF_ALLOWUNDO, // shell shows its own confirmation dialog
            };
            int result = SHFileOperationW(ref op);
            return result == 0 && !op.fAnyOperationsAborted;
        }

        if (OperatingSystem.IsMacOS())
        {
            var psi = new ProcessStartInfo("osascript")
            {
                ArgumentList = { "-e", $"tell application \"Finder\" to delete POSIX file \"{path}\"" },
            };
            using var p = Process.Start(psi);
            p?.WaitForExit();
            return p?.ExitCode == 0;
        }

        // Linux: gio handles the freedesktop trash spec.
        var gio = new ProcessStartInfo("gio") { ArgumentList = { "trash", path } };
        using var proc = Process.Start(gio);
        proc?.WaitForExit();
        return proc?.ExitCode == 0;
    }

    /// <summary>Open the containing folder with the item selected (or the folder itself).</summary>
    public static void RevealInFileManager(string path, bool isDirectory)
    {
        if (OperatingSystem.IsWindows())
        {
            if (isDirectory)
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
            else
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
            return;
        }

        string opener = OperatingSystem.IsMacOS() ? "open" : "xdg-open";
        string target = isDirectory ? path : (Path.GetDirectoryName(path) ?? path);
        Process.Start(new ProcessStartInfo(opener) { ArgumentList = { target } });
    }
}
