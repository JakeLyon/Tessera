using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Tessera.UI;
using Xunit;

namespace Tessera.Tests.Headless;

/// <summary>
/// These are licence-compliance tests as much as UI tests. A single-file exe handed to
/// someone on its own carries no LICENSE or THIRD-PARTY-NOTICES.txt beside it, so this
/// window is the only place those notices survive — if the embedded resources ever stop
/// being embedded, that must fail here rather than in a distributed build.
/// </summary>
public class AboutWindowTests
{
    private static Assembly AppAssembly => typeof(AboutWindow).Assembly;

    // =====================================================================
    // The embedded notices
    // =====================================================================

    [Fact]
    public void BothLicenceResources_AreEmbeddedInTheAssembly()
    {
        var names = AppAssembly.GetManifestResourceNames();
        Assert.Contains(AboutWindow.LicenceResourceName, names);
        Assert.Contains(AboutWindow.NoticesResourceName, names);
    }

    [AvaloniaFact]
    public void Notices_NameEveryLicenceTheBundledDependenciesAreUnder()
    {
        var text = new AboutWindow().NoticesText;

        Assert.Contains("MIT", text);
        Assert.Contains("BSD-3-Clause", text);
        Assert.Contains("SIL OPEN FONT LICENSE", text, StringComparison.OrdinalIgnoreCase);
    }

    [AvaloniaTheory]
    [InlineData("Avalonia")]
    [InlineData("Avalonia.Controls.TreeDataGrid")]
    [InlineData("SkiaSharp")]
    [InlineData("HarfBuzz")]
    [InlineData("ANGLE")]
    [InlineData("Inter")]
    [InlineData("System.Reactive")]
    public void Notices_AttributeEveryBundledComponent(string component)
        => Assert.Contains(component, new AboutWindow().NoticesText);

    [AvaloniaFact]
    public void Licence_IsTheEula()
    {
        var text = new AboutWindow().LicenceText;
        Assert.Contains("End User Licence Agreement", text);
        Assert.Contains("DISCLAIMER OF WARRANTY", text);
    }

    [Fact]
    public void MissingResource_ReportsItselfRatherThanThrowingOrComingBackEmpty()
    {
        var text = AboutWindow.ReadEmbeddedText(AppAssembly, "Tessera.NoSuchResource.txt");

        Assert.Contains("Tessera.NoSuchResource.txt", text);
        Assert.Contains("missing", text, StringComparison.OrdinalIgnoreCase);
    }

    // =====================================================================
    // Identity, read from the assembly rather than hardcoded
    // =====================================================================

    [Theory]
    // The shipping shape: MSBuild appends "+<sha>" via SourceLink.
    [InlineData("1.0.0+ec1f6ace1047e7aa601ab22ced0057c153f2c9bc", "1.0.0 (ec1f6ac)")]
    [InlineData("1.2.3+abc", "1.2.3 (abc)")]
    [InlineData("1.0.0+", "1.0.0")]
    [InlineData("2.0.0-beta.1", "2.0.0-beta.1")]
    public void FormatVersion_KeepsSevenCharactersOfTheRevision(string informational, string expected)
        => Assert.Equal(expected, AboutWindow.FormatVersion(informational, new Version(9, 9, 9, 9)));

    [Fact]
    public void FormatVersion_FallsBackToTheAssemblyVersionThenToUnknown()
    {
        Assert.Equal("1.0.0.0", AboutWindow.FormatVersion(null, new Version(1, 0, 0, 0)));
        Assert.Equal("1.0.0.0", AboutWindow.FormatVersion("   ", new Version(1, 0, 0, 0)));
        Assert.Equal("unknown", AboutWindow.FormatVersion(null, null));
    }

    [AvaloniaFact]
    public void Header_ReportsTheAssemblysOwnIdentity()
    {
        var window = new AboutWindow();
        var assembly = AppAssembly;

        Assert.Equal(assembly.GetCustomAttribute<AssemblyProductAttribute>()!.Product, window.ProductText);
        Assert.Equal(assembly.GetCustomAttribute<AssemblyCopyrightAttribute>()!.Copyright, window.CopyrightText);
        Assert.NotEmpty(window.DescriptionText);

        // Not a hardcoded "1.0.0": assert it tracks the assembly, so a version bump
        // cannot leave the About box reporting the old one.
        Assert.Equal(
            "Version " + AboutWindow.FormatVersion(
                assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
                assembly.GetName().Version),
            window.VersionText);
    }

    // =====================================================================
    // The window itself
    // =====================================================================

    [AvaloniaFact]
    public void Window_ShowsBothTextsInTabsAndCloses()
    {
        var window = new AboutWindow();
        window.Show();

        var root = Assert.IsType<DockPanel>(window.Content);
        var tabs = Assert.Single(root.Children.OfType<TabControl>());
        var texts = tabs.Items.Cast<TabItem>()
            .Select(t => ((SelectableTextBlock)((ScrollViewer)t.Content!).Content!).Text)
            .ToList();

        Assert.Equal(new[] { window.LicenceText, window.NoticesText }, texts);

        window.Close();
        Assert.False(window.IsVisible);
    }
}
