using KintaiReport.Config;
using KintaiReport.Models;

namespace KintaiReport.Calc;

/// <summary>
/// 1日分の勤務時間を所定内・残業・深夜・休日出勤に分解する。
/// 時刻はすべて「当日 0:00 からの分」で扱う（翌 1:00 = 1500）。
/// </summary>
public sealed class WorkTimeCalculator
{
    private readonly AppConfig _config;
    private readonly HashSet<string> _workdayValues;
    private readonly (int Start, int End) _defaultSchedule;
    private readonly Dictionary<string, (int Start, int End)> _deptSchedules;
    private readonly int _lateStart;
    private readonly int _lateEnd;

    public WorkTimeCalculator(AppConfig config)
    {
        _config = config;
        _workdayValues = config.Input.WorkdayCalendarValues.Select(v => v.Trim()).ToHashSet();
        _defaultSchedule = ToRange(config.Schedule.Default, "schedule.default");
        _deptSchedules = config.Schedule.ByDepartment.ToDictionary(kv => kv.Key.Trim(), kv => ToRange(kv.Value, $"schedule.byDepartment.{kv.Key}"));
        _lateStart = ConfigLoader.ParseTimeSetting(config.LateNight.Start, "lateNight.start");
        _lateEnd = ConfigLoader.ParseTimeSetting(config.LateNight.End, "lateNight.end");
    }

    private static (int, int) ToRange(TimeRangeConfig r, string key)
    {
        var s = ConfigLoader.ParseTimeSetting(r.Start, key + ".start");
        var e = ConfigLoader.ParseTimeSetting(r.End, key + ".end");
        if (e <= s) e += 24 * 60;
        return (s, e);
    }

    public bool IsWorkday(string calendar) => _workdayValues.Contains(calendar.Trim());

    public (int Start, int End) ScheduleFor(string department) =>
        _deptSchedules.TryGetValue(department.Trim(), out var s) ? s : _defaultSchedule;

    /// <summary>拘束時間から休憩ルールで休憩分数を決める。</summary>
    public int RuleBreak(int grossMinutes) =>
        _config.Break.Rules.Where(r => grossMinutes > r.OverMinutes)
            .Select(r => r.BreakMinutes).DefaultIfEmpty(0).Max();

    public DailyResult Calculate(AttendanceRecord rec)
    {
        var isWorkday = IsWorkday(rec.Calendar);
        if (rec.ClockIn is not int start || rec.ClockOut is not int end || end <= start)
            return new DailyResult { IsWorkday = isWorkday, Attended = rec.HasPunch };

        var gross = end - start;
        var breakMinutes = _config.Break.UseExcelBreakColumn && rec.BreakMinutes.HasValue
            ? rec.BreakMinutes.Value
            : RuleBreak(gross);
        breakMinutes = Math.Clamp(breakMinutes, 0, gross);
        var work = gross - breakMinutes;

        // 休憩は所定時間内に取ったものとして先に所定内から差し引き、余りを所定外から差し引く。
        int scheduledPart = 0;
        int lateInScheduled = 0;
        if (isWorkday)
        {
            var (s, e) = ScheduleFor(rec.Department);
            scheduledPart = Overlap(start, end, s, e);
            lateInScheduled = LateNightOverlap(Math.Max(start, s), Math.Min(end, e));
        }
        var outside = gross - scheduledPart;
        var breakLeft = Math.Max(0, breakMinutes - scheduledPart);
        var outsideNet = Math.Max(0, outside - breakLeft);

        var lateOutside = LateNightOverlap(start, end) - lateInScheduled;
        var late = Math.Min(Math.Max(0, lateOutside), outsideNet);
        var other = outsideNet - late;

        var unit = _config.Rounding.OvertimeUnitMinutes;
        late = Floor(late, unit);
        other = Floor(other, unit);

        return new DailyResult
        {
            IsWorkday = isWorkday,
            Attended = true,
            WorkMinutes = work,
            NormalOvertime = isWorkday ? other : 0,
            HolidayWork = isWorkday ? 0 : other,
            LateNightOvertime = late,
        };
    }

    private static int Floor(int value, int unit) => unit <= 1 ? value : value / unit * unit;

    private static int Overlap(int a1, int a2, int b1, int b2) => Math.Max(0, Math.Min(a2, b2) - Math.Max(a1, b1));

    /// <summary>区間 [start, end) と深夜帯（前日・当日・翌日分）の重なり。</summary>
    public int LateNightOverlap(int start, int end)
    {
        if (end <= start) return 0;
        var ls = _lateStart;
        var le = _lateEnd <= _lateStart ? _lateEnd + 24 * 60 : _lateEnd;
        var total = 0;
        for (var day = -1; day <= 2; day++)
            total += Overlap(start, end, ls + day * 1440, le + day * 1440);
        return total;
    }
}
