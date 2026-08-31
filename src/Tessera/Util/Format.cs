namespace Tessera.Util;

internal static class Format
{
    private static readonly string[] s_units = ["B", "KB", "MB", "GB", "TB", "PB"];

    /// <summary>1024-based, ~3 significant figures: "532 B", "12.4 KB", "1.24 GB".</summary>
    public static string Bytes(long bytes)
    {
        if (bytes < 1024)
            return $"{bytes} B";

        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < s_units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        string fmt = value >= 100 ? "F0" : value >= 10 ? "F1" : "F2";
        return $"{value.ToString(fmt)} {s_units[unit]}";
    }

    public static string Percent(double fraction) => $"{fraction * 100:F1}%";

    public static string Count(long n) => n.ToString("N0");
}
