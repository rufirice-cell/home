namespace KintaiReport.Models;

/// <summary>勤怠シートの1行。</summary>
public sealed record AttendanceRecord
{
    public required int RowNumber { get; init; }
    public required string EmployeeId { get; init; }
    public required string Name { get; init; }
    public required string Department { get; init; }
    public required DateTime Date { get; init; }
    public required string Calendar { get; init; }
    /// <summary>出勤時刻（当日 0:00 からの分）。</summary>
    public int? ClockIn { get; init; }
    /// <summary>退勤時刻（当日 0:00 からの分。翌日なら 1440 以上）。</summary>
    public int? ClockOut { get; init; }
    public int? BreakMinutes { get; init; }
    public string Approval { get; init; } = "";

    public bool HasPunch => ClockIn.HasValue || ClockOut.HasValue;
}

/// <summary>1日分の計算結果（分）。</summary>
public sealed record DailyResult
{
    public bool IsWorkday { get; init; }
    public bool Attended { get; init; }
    public int WorkMinutes { get; init; }
    /// <summary>平日の所定時間外（深夜帯を除く）。</summary>
    public int NormalOvertime { get; init; }
    /// <summary>所定時間外かつ深夜帯（平日・休日とも）。</summary>
    public int LateNightOvertime { get; init; }
    /// <summary>休日の勤務時間（深夜帯を除く）。</summary>
    public int HolidayWork { get; init; }

    public int TotalOvertime => NormalOvertime + LateNightOvertime + HolidayWork;
}
