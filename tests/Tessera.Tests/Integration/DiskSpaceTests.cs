using Tessera.Util;
using Xunit;

namespace Tessera.Tests.Integration;

/// <summary>
/// Free-space lookup against the real drives on this machine. Integration rather than
/// Unit: DriveInfo reads actual volumes, and the answer depends on the host.
/// </summary>
public class DiskSpaceTests
{
    /// <summary>
    /// Free space beside a scan of one folder would be measuring that folder against the
    /// whole disk, which says nothing about it — so only a drive root reports any.
    /// </summary>
    [Fact]
    public void FreeBytes_AreNullForAnythingThatIsNotADriveRoot()
    {
        Assert.Null(DiskSpace.FreeBytesForDriveRoot(Path.GetTempPath()));
        Assert.Null(DiskSpace.FreeBytesForDriveRoot(@"Z:\no\such\place"));
    }

    [WindowsFact]
    public void FreeBytes_AreReportedForADriveRoot()
    {
        string root = Path.GetPathRoot(Environment.SystemDirectory)!;
        Assert.NotNull(DiskSpace.FreeBytesForDriveRoot(root));
    }
}
