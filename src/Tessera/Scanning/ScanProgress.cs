namespace Tessera.Scanning;

/// <summary>
/// Lock-free scan counters. Workers bump these with Interlocked; the UI polls them
/// on a timer — no per-file events.
/// </summary>
public sealed class ScanProgress
{
    public long Files;
    public long Dirs;
    public long Bytes;
    public long Errors;
    public volatile string? CurrentDir;

    public void Reset()
    {
        Interlocked.Exchange(ref Files, 0);
        Interlocked.Exchange(ref Dirs, 0);
        Interlocked.Exchange(ref Bytes, 0);
        Interlocked.Exchange(ref Errors, 0);
        CurrentDir = null;
    }
}
