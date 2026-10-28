using System.Globalization;

namespace KintaiReport.Config;

public static class TimeParser
{
    /// <summary>"9:00" "09:00:00" "25:30" "9時30分" などを 0:00 からの分数に変換する。</summary>
    public static bool TryParseClockText(string? text, out int minutes)
    {
        minutes = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var s = text.Trim()
            .Replace('：', ':')
            .Replace("時", ":")
            .Replace("分", "");
        s = s.Normalize(System.Text.NormalizationForm.FormKC);

        var parts = s.Split(':', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is < 1 or > 3) return false;
        if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var h)) return false;
        var m = 0;
        if (parts.Length >= 2 && !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out m)) return false;
        if (parts.Length == 1 && !s.Contains(':')) return false; // "9" 単体は時刻とみなさない
        if (m >= 60) return false;
        minutes = h * 60 + m;
        return true;
    }

    /// <summary>分数を "h:mm" 形式にする（負値は 0 扱い）。</summary>
    public static string FormatHours(int minutes)
    {
        if (minutes < 0) minutes = 0;
        return $"{minutes / 60}:{minutes % 60:00}";
    }
}
