namespace Tessera.Util;

/// <summary>Free-space queries against the drive a path sits on.</summary>
internal static class DiskSpace
{
    /// <summary>
    /// Free bytes on the drive, but only when <paramref name="path"/> IS that drive — free
    /// space beside a scan of one folder would be comparing it against the whole disk,
    /// which says nothing about the folder. Null means the treemap shows no free-space
    /// block. DriveInfo throws on an unready or disconnected drive, so never let that fail
    /// a scan that has already succeeded.
    /// </summary>
    internal static long? FreeBytesForDriveRoot(string path)
    {
        try
        {
            string full = Path.GetFullPath(path);
            if (!string.Equals(full, Path.GetPathRoot(full), StringComparison.OrdinalIgnoreCase))
                return null;
            var drive = new DriveInfo(full);
            return drive.IsReady ? drive.TotalFreeSpace : null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
