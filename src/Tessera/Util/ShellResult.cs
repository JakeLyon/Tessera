namespace Tessera.Util;

/// <summary>Outcome of a shell operation. Failures are reported, never thrown.</summary>
internal readonly record struct ShellResult(bool Ok, string? Error)
{
    public static ShellResult Success => new(true, null);
    public static ShellResult Fail(string why) => new(false, why);
}
