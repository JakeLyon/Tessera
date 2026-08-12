using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Clone.Util;

namespace Clone.UI;

/// <summary>
/// Minimal modal confirm / message window. Avalonia ships no MessageBox and this
/// project takes no extra dependencies, so it is hand-rolled.
/// </summary>
internal sealed class ConfirmDialog : Window
{
    internal Button PrimaryButton { get; }
    internal Button? SecondaryButton { get; }
    internal string HeadlineText { get; }
    internal string BodyText { get; }

    private ConfirmDialog(string title, string headline, string body,
        string primaryText, string? secondaryText)
    {
        Title = title;
        HeadlineText = headline;
        BodyText = body;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        ShowInTaskbar = false;
        MinWidth = 420;
        MaxWidth = 620;

        PrimaryButton = new Button
        {
            Content = primaryText,
            MinWidth = 88,
            // Only make the primary the default when there is nothing to cancel:
            // for a destructive prompt, Enter must not delete.
            IsDefault = secondaryText is null,
        };
        PrimaryButton.Click += (_, _) => Confirm();

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
        };

        if (secondaryText is not null)
        {
            SecondaryButton = new Button
            {
                Content = secondaryText,
                MinWidth = 88,
                IsCancel = true,   // Escape
                IsDefault = true,  // Enter — the safe choice
            };
            SecondaryButton.Click += (_, _) => Cancel();
            buttons.Children.Add(SecondaryButton);
        }
        buttons.Children.Add(PrimaryButton);

        var stack = new StackPanel
        {
            Spacing = 12,
            Margin = new Thickness(20),
            Children =
            {
                new TextBlock
                {
                    Text = headline,
                    FontWeight = FontWeight.SemiBold,
                    TextWrapping = TextWrapping.Wrap,
                },
                new SelectableTextBlock
                {
                    Text = body,
                    TextWrapping = TextWrapping.Wrap,
                    Opacity = 0.85,
                },
                buttons,
            },
        };

        Content = stack;
    }

    internal void Confirm() => Close(true);
    internal void Cancel() => Close(false);

    internal static ConfirmDialog CreateDelete(MainWindow.DeleteRequest request) => new(
        title: "Delete to Recycle Bin",
        headline: $"Move “{request.Name}” to the Recycle Bin?",
        body: $"{request.FullPath}\n\n{Format.Bytes(request.Size)}",
        primaryText: "Delete",
        secondaryText: "Cancel");

    internal static ConfirmDialog CreateMessage(string title, string message) =>
        new(title, title, message, primaryText: "OK", secondaryText: null);

    /// <summary>Closing via the title bar yields default(bool) — i.e. "cancel" — by construction.</summary>
    internal static Task<bool> ConfirmDeleteAsync(Window owner, MainWindow.DeleteRequest request)
        => CreateDelete(request).ShowDialog<bool>(owner);

    internal static Task ShowMessageAsync(Window owner, string title, string message)
        => CreateMessage(title, message).ShowDialog<bool>(owner);
}
