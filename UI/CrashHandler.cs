using System.Text;
using Avalonia.Controls;

namespace Clone.UI;

/// <summary>
/// The one place an unexpected exception becomes something the user can read.
/// Nothing is written to disk — the app reports and, where it can, keeps running.
/// </summary>
internal static class CrashHandler
{
    // An exception raised while a report is on screen (a failing render, a second
    // handler faulting) would otherwise stack dialogs or recurse straight back here.
    private static bool s_reporting;

    /// <summary>
    /// Human-readable description of a failure: the type and message, plus the
    /// innermost cause when it says something the outer exception does not.
    /// </summary>
    internal static string Describe(Exception ex)
    {
        // AggregateException's own message is the useless "One or more errors
        // occurred."; its contents are what the user needs.
        if (ex is AggregateException aggregate)
        {
            var inners = aggregate.Flatten().InnerExceptions;
            if (inners.Count == 1)
                return Describe(inners[0]);
            if (inners.Count > 1)
            {
                var sb = new StringBuilder($"{inners.Count} errors occurred:");
                foreach (var inner in inners)
                    sb.Append("\n\n• ").Append(Describe(inner));
                return sb.ToString();
            }
        }

        string text = $"{ex.GetType().Name}: {ex.Message}";

        var root = ex;
        while (root.InnerException is { } inner)
            root = inner;
        if (!ReferenceEquals(root, ex) && root.Message != ex.Message)
            text += $"\n\nCaused by {root.GetType().Name}: {root.Message}";

        return text;
    }

    /// <summary>
    /// Show <paramref name="ex"/> to the user. Modal when there is a window to own the
    /// dialog; a failure during startup has no owner, so it falls back to a loose window.
    /// Safe to call from an exception handler — it never throws.
    /// </summary>
    internal static void Report(Window? owner, string title, Exception ex)
    {
        if (s_reporting)
            return;
        s_reporting = true;
        try
        {
            var dialog = ConfirmDialog.CreateMessage(title, Describe(ex));
            dialog.Closed += (_, _) => s_reporting = false;
            if (owner is not null)
                _ = dialog.ShowDialog(owner);
            else
                dialog.Show();
        }
        catch (Exception)
        {
            // There is no reporting channel left; dying here would replace a
            // recoverable failure with an unrecoverable one.
            s_reporting = false;
        }
    }
}
