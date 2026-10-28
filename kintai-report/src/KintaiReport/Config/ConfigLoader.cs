using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace KintaiReport.Config;

public static class ConfigLoader
{
    public static AppConfig Load(string path)
    {
        if (!File.Exists(path))
            throw new UserException($"設定ファイルが見つかりません: {path}");

        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();

        AppConfig config;
        try
        {
            config = deserializer.Deserialize<AppConfig?>(File.ReadAllText(path)) ?? new AppConfig();
        }
        catch (YamlDotNet.Core.YamlException ex)
        {
            throw new UserException($"設定ファイルの書式が正しくありません（{ex.Start.Line}行目付近）: {ex.InnerException?.Message ?? ex.Message}");
        }

        Validate(config);
        return config;
    }

    private static void Validate(AppConfig c)
    {
        if (c.Report.FiscalYearStartMonth is < 1 or > 12)
            throw new UserException("report.fiscalYearStartMonth は 1〜12 で指定してください。");
        if (c.Rounding.OvertimeUnitMinutes < 1)
            throw new UserException("rounding.overtimeUnitMinutes は 1 以上で指定してください。");
        if (c.Heatmap.MinRate >= c.Heatmap.MaxRate)
            throw new UserException("heatmap.minRate は heatmap.maxRate より小さくしてください。");

        ParseTimeSetting(c.Schedule.Default.Start, "schedule.default.start");
        ParseTimeSetting(c.Schedule.Default.End, "schedule.default.end");
        foreach (var (dept, range) in c.Schedule.ByDepartment)
        {
            ParseTimeSetting(range.Start, $"schedule.byDepartment.{dept}.start");
            ParseTimeSetting(range.End, $"schedule.byDepartment.{dept}.end");
        }
        ParseTimeSetting(c.LateNight.Start, "lateNight.start");
        ParseTimeSetting(c.LateNight.End, "lateNight.end");
    }

    /// <summary>"HH:mm" 形式を 0:00 からの分数に変換する。</summary>
    public static int ParseTimeSetting(string value, string key)
    {
        if (TimeParser.TryParseClockText(value, out var minutes) && minutes is >= 0 and <= 48 * 60)
            return minutes;
        throw new UserException($"{key} の時刻 \"{value}\" を解釈できません（例: \"09:00\"）。");
    }
}

/// <summary>利用者向けのメッセージだけを表示して終了するための例外。</summary>
public sealed class UserException(string message) : Exception(message);
