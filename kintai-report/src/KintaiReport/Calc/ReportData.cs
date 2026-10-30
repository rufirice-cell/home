namespace KintaiReport.Calc;

/// <summary>月ごと（または期間累計）の集計値。時間は分。</summary>
public sealed class Stats
{
    /// <summary>在籍中の所定出勤日数（カレンダー＝平日の日数）。</summary>
    public int ScheduledDays { get; set; }
    /// <summary>所定出勤日のうち出勤した日数。</summary>
    public int AttendedWorkdays { get; set; }
    /// <summary>休日に出勤した日数。</summary>
    public int HolidayWorkDays { get; set; }
    public int NormalOvertime { get; set; }
    public int LateNightOvertime { get; set; }
    public int HolidayWork { get; set; }
    public int Headcount { get; set; }

    public int AttendedDays => AttendedWorkdays + HolidayWorkDays;
    public int TotalOvertime => NormalOvertime + LateNightOvertime + HolidayWork;
    public double? AttendanceRate => ScheduledDays == 0 ? null : (double)AttendedWorkdays / ScheduledDays;

    public void Add(Stats o)
    {
        ScheduledDays += o.ScheduledDays;
        AttendedWorkdays += o.AttendedWorkdays;
        HolidayWorkDays += o.HolidayWorkDays;
        NormalOvertime += o.NormalOvertime;
        LateNightOvertime += o.LateNightOvertime;
        HolidayWork += o.HolidayWork;
    }
}

public sealed class EmployeeSummary
{
    public required string Id { get; init; }
    public required string Name { get; set; }
    /// <summary>期間内で最後に所属していた部署。</summary>
    public required string Department { get; set; }
    public DateTime? HireDate { get; init; }
    public DateTime? RetireDate { get; init; }
    /// <summary>キーは各月の1日。</summary>
    public Dictionary<DateTime, Stats> Monthly { get; } = new();
    public Dictionary<DateTime, string> MonthlyDepartment { get; } = new();
    public Stats Total { get; } = new();
}

public sealed class DepartmentSummary
{
    public required string Name { get; init; }
    public Dictionary<DateTime, Stats> Monthly { get; } = new();
    public Stats Total { get; } = new();
    public List<EmployeeSummary> Members { get; } = new();
}

public sealed record UnapprovedEntry(string EmployeeId, string Name, string Department, int Count, IReadOnlyList<DateTime> Dates);

public sealed class ReportData
{
    public required DateTime PeriodStart { get; init; }
    public required DateTime PeriodEnd { get; init; }
    public required IReadOnlyList<DateTime> Months { get; init; }
    public required IReadOnlyList<DepartmentSummary> Departments { get; init; }
    public required IReadOnlyList<EmployeeSummary> Employees { get; init; }
    public required Stats CompanyTotal { get; init; }
    public required Dictionary<DateTime, Stats> CompanyMonthly { get; init; }
    public required IReadOnlyList<EmployeeSummary> TopOvertime { get; init; }
    public required IReadOnlyList<EmployeeSummary> BottomAttendance { get; init; }
    public required IReadOnlyList<UnapprovedEntry> Unapproved { get; init; }
    public required int ExcludedCount { get; init; }
}
