using Xunit;

namespace Tessera.Tests;

/// <summary>Fact that only runs on Windows (junctions, ACLs, drive semantics).</summary>
public sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
            Skip = "Windows only";
    }
}
