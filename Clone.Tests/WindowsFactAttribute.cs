using Xunit;

namespace Clone.Tests;

/// <summary>Fact that only runs on Windows (junctions, ACLs, drive semantics).</summary>
public sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
            Skip = "Windows only";
    }
}

/// <summary>Theory that only runs on Windows.</summary>
public sealed class WindowsTheoryAttribute : TheoryAttribute
{
    public WindowsTheoryAttribute()
    {
        if (!OperatingSystem.IsWindows())
            Skip = "Windows only";
    }
}
