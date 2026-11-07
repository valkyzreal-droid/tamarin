using System;

namespace Tamarin.App.Services;

internal static class RelativeTime
{
    /// <summary>"agora", "há 5 min", "ontem 14:30", "12 mar" for a unix-ms timestamp.</summary>
    public static string Format(long unixMs, DateTime? now = null)
    {
        var t = DateTimeOffset.FromUnixTimeMilliseconds(unixMs).LocalDateTime;
        var n = now ?? DateTime.Now;
        var pt = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "pt";
        var d = n - t;
        if (d.TotalSeconds < 45) return pt ? "agora" : "just now";
        if (d.TotalMinutes < 60) return pt ? $"há {(int)Math.Max(1, d.TotalMinutes)} min" : $"{(int)Math.Max(1, d.TotalMinutes)} min ago";
        if (t.Date == n.Date) return t.ToString("t");
        if (t.Date == n.Date.AddDays(-1)) return (pt ? "ontem " : "yesterday ") + t.ToString("t");
        return t.Year == n.Year ? t.ToString("m") : t.ToString("d");
    }
}
