using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Tessera.UI;
using Xunit;

namespace Tessera.Tests.Headless;

public class ConfirmDialogTests
{
    private static DeleteRequest Request() =>
        new("video.mp4", @"C:\scan\media\video.mp4", 3_221_225_472);

    [AvaloniaFact]
    public void CreateDelete_BodyContainsFullPathAndFormattedSize()
    {
        var dialog = ConfirmDialog.CreateDelete(Request());

        Assert.Contains(@"C:\scan\media\video.mp4", dialog.BodyText);
        Assert.Contains("3.00 GB", dialog.BodyText);
        Assert.Contains("video.mp4", dialog.HeadlineText);
        Assert.Equal("Delete to Recycle Bin", dialog.Title);
    }

    [AvaloniaFact]
    public void CreateDelete_CancelIsTheSafeDefault()
    {
        var dialog = ConfirmDialog.CreateDelete(Request());

        Assert.NotNull(dialog.SecondaryButton);
        // Enter picks Cancel, Escape picks Cancel — the destructive button is neither.
        Assert.True(dialog.SecondaryButton!.IsDefault);
        Assert.True(dialog.SecondaryButton.IsCancel);
        Assert.False(dialog.PrimaryButton.IsDefault);
        Assert.Equal("Delete", dialog.PrimaryButton.Content);
    }

    [AvaloniaFact]
    public void CreateMessage_HasSingleAcknowledgeButton()
    {
        var dialog = ConfirmDialog.CreateMessage("Delete failed", "the path is too long");

        Assert.Null(dialog.SecondaryButton);
        Assert.Equal("OK", dialog.PrimaryButton.Content);
        Assert.True(dialog.PrimaryButton.IsDefault);
        Assert.Contains("too long", dialog.BodyText);
    }

    [AvaloniaFact]
    public async Task Confirm_ReturnsTrueFromShowDialog()
    {
        var owner = new Window();
        owner.Show();
        var dialog = ConfirmDialog.CreateDelete(Request());

        var result = dialog.ShowDialog<bool>(owner);
        dialog.Confirm();

        Assert.True(await result);
    }

    [AvaloniaFact]
    public async Task Cancel_ReturnsFalseFromShowDialog()
    {
        var owner = new Window();
        owner.Show();
        var dialog = ConfirmDialog.CreateDelete(Request());

        var result = dialog.ShowDialog<bool>(owner);
        dialog.Cancel();

        Assert.False(await result);
    }

    [AvaloniaFact]
    public async Task ClosingViaTitleBar_IsTreatedAsCancel()
    {
        // No explicit result — ShowDialog<bool> yields default(bool), i.e. don't delete.
        var owner = new Window();
        owner.Show();
        var dialog = ConfirmDialog.CreateDelete(Request());

        var result = dialog.ShowDialog<bool>(owner);
        dialog.Close();

        Assert.False(await result);
    }
}
