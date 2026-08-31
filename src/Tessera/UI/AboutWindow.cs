using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace Tessera.UI;

/// <summary>
/// Version, licence and third-party attribution, read out of the assembly rather than
/// off disk. The bundled dependencies' licences require their notices to travel with any
/// distributed copy, and Tessera ships as a single self-contained exe that a user may
/// well move on its own, away from the LICENSE and THIRD-PARTY-NOTICES.txt files sitting
/// beside it. This window is what keeps that copy compliant, so the texts are embedded
/// (see the EmbeddedResource items in Tessera.csproj) and nothing here touches the
/// filesystem.
/// </summary>
public sealed class AboutWindow : Window
{
    internal const string LicenceResourceName = "Tessera.LICENSE";
    internal const string NoticesResourceName = "Tessera.THIRD-PARTY-NOTICES.txt";

    internal string ProductText { get; }
    internal string DescriptionText { get; }
    internal string VersionText { get; }
    internal string CopyrightText { get; }
    internal string LicenceText { get; }
    internal string NoticesText { get; }
    internal Button CloseButton { get; }

    public AboutWindow()
    {
        var assembly = typeof(AboutWindow).Assembly;

        ProductText = Meta<AssemblyProductAttribute>(assembly, a => a.Product) ?? "Tessera";
        DescriptionText = Meta<AssemblyDescriptionAttribute>(assembly, a => a.Description) ?? "";
        CopyrightText = Meta<AssemblyCopyrightAttribute>(assembly, a => a.Copyright) ?? "";
        VersionText = "Version " + FormatVersion(
            Meta<AssemblyInformationalVersionAttribute>(assembly, a => a.InformationalVersion),
            assembly.GetName().Version);
        LicenceText = ReadEmbeddedText(assembly, LicenceResourceName);
        NoticesText = ReadEmbeddedText(assembly, NoticesResourceName);

        Title = $"About {ProductText}";
        Width = 760;
        Height = 600;
        MinWidth = 480;
        MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        CloseButton = new Button { Content = "Close", MinWidth = 88, IsDefault = true, IsCancel = true };
        CloseButton.Click += (_, _) => Close();

        var header = new StackPanel
        {
            Spacing = 4,
            Children =
            {
                new TextBlock { Text = ProductText, FontSize = 20, FontWeight = FontWeight.SemiBold },
                new TextBlock { Text = DescriptionText, TextWrapping = TextWrapping.Wrap, Opacity = 0.85 },
                new SelectableTextBlock { Text = VersionText, Opacity = 0.7 },
                new TextBlock { Text = CopyrightText, Opacity = 0.7, TextWrapping = TextWrapping.Wrap },
            },
        };

        var tabs = new TabControl
        {
            Items =
            {
                TextTab("Licence", LicenceText),
                TextTab("Third-party notices", NoticesText),
            },
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { CloseButton },
        };

        var root = new DockPanel { Margin = new Thickness(20) };

        DockPanel.SetDock(header, Dock.Top);
        header.Margin = new Thickness(0, 0, 0, 12);
        root.Children.Add(header);

        DockPanel.SetDock(buttons, Dock.Bottom);
        buttons.Margin = new Thickness(0, 12, 0, 0);
        root.Children.Add(buttons);

        root.Children.Add(tabs);   // fills what is left
        Content = root;
    }

    /// <summary>
    /// A licence pane is only useful if you can copy a clause out of it, and both texts
    /// are hard-wrapped at 80 columns upstream, so they are shown unwrapped in a
    /// monospace face and scroll in both directions rather than being re-flowed.
    /// </summary>
    private static TabItem TextTab(string header, string text) => new()
    {
        Header = header,
        Content = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(12, 8),
            Content = new SelectableTextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.NoWrap,
                FontFamily = new FontFamily("Consolas, Cascadia Mono, Courier New, monospace"),
                FontSize = 12,
            },
        },
    };

    /// <summary>
    /// Read an embedded text resource. These notices must be readable from inside the
    /// exe — that is the entire reason they are embedded — so a missing or unreadable
    /// resource says so in the pane rather than throwing out of the constructor or
    /// leaving the user looking at an empty box that reads like there is nothing to show.
    /// </summary>
    internal static string ReadEmbeddedText(Assembly assembly, string logicalName)
    {
        try
        {
            using var stream = assembly.GetManifestResourceStream(logicalName);
            if (stream is null)
                return $"{logicalName} is missing from this build. "
                     + "The text is also distributed as a file of the same name beside the executable.";
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch (Exception ex) when (ex is IOException or BadImageFormatException or NotSupportedException)
        {
            return $"{logicalName} could not be read: {ex.Message}";
        }
    }

    /// <summary>
    /// "1.0.0+abcdef123..." becomes "1.0.0 (abcdef1)". The source-revision suffix is what
    /// turns a bug report into something reproducible, so it is shown rather than trimmed,
    /// but seven characters of it is enough to identify a commit.
    /// </summary>
    internal static string FormatVersion(string? informational, Version? assemblyVersion)
    {
        if (!string.IsNullOrWhiteSpace(informational))
        {
            int plus = informational.IndexOf('+');
            if (plus < 0)
                return informational.Trim();

            string version = informational[..plus].Trim();
            string revision = informational[(plus + 1)..].Trim();
            if (revision.Length == 0)
                return version;
            return $"{version} ({revision[..Math.Min(7, revision.Length)]})";
        }

        return assemblyVersion?.ToString() ?? "unknown";
    }

    private static string? Meta<T>(Assembly assembly, Func<T, string?> select) where T : Attribute
    {
        if (assembly.GetCustomAttribute<T>() is not { } attribute)
            return null;
        string? value = select(attribute);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
