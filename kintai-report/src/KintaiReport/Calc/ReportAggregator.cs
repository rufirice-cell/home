using KintaiReport.Config;
using KintaiReport.Models;

namespace KintaiReport.Calc;

public sealed class ReportAggregator(AppConfig config)
{
    private readonly WorkTimeCalculator _calc = new(config);

    public List<string> Warnings { get; } = new();

    /// <param name="targetMonth">累計の最終月。null ならデータ内の最終月。</param>
    public ReportData Aggregate(IReadOnlyList<AttendanceRecord> allRecords, DateTime? targetMonth)
    {
        var excluded = config.ExcludedEmployees.Select(x => x.Trim()).ToHashSet();
        var records = allRecords.Where(r => !excluded.Contains(r.EmployeeId)).ToList();
        if (records.Count == 0)
            throw new UserException("集計対象のデータがありません（全員が除外されているか、シートが空です）。");

        // ---- 期間 ----
        var dataMax = records.Max(r => r.Date);
        var lastMonth = MonthOf(targetMonth ?? dataMax);
        var startYear = lastMonth.Month >= config.Report.FiscalYearStartMonth ? lastMonth.Year : lastMonth.Year - 1;
        var periodStart = new DateTime(startYear, config.Report.FiscalYearStartMonth, 1);
        var periodEnd = lastMonth.AddMonths(1).AddDays(-1);
        if (dataMax < periodEnd && dataMax >= lastMonth) periodEnd = dataMax; // 月途中までのデータ
        var months = new List<DateTime>();
        for (var m = periodStart; m <= lastMonth; m = m.AddMonths(1)) months.Add(m);

        var inPeriod = records.Where(r => r.Date >= periodStart && r.Date <= periodEnd)
            .OrderBy(r => r.Date).ThenBy(r => r.RowNumber).ToList();
        if (inPeriod.Count == 0)
            throw new UserException($"{periodStart:yyyy/MM/dd}〜{periodEnd:yyyy/MM/dd} のデータがありません。--month の指定を確認してください。");

        // ---- 会社カレンダー（日付ごとに最も多いカレンダー区分） ----
        var workdays = inPeriod.GroupBy(r => r.Date)
            .Where(g => _calc.IsWorkday(g.GroupBy(r => r.Calendar).OrderByDescending(x => x.Count()).First().Key))
            .Select(g => g.Key).ToHashSet();

        // ---- 社員ごと ----
        var employeeSettings = config.Employees.Where(e => !string.IsNullOrWhiteSpace(e.Id))
            .GroupBy(e => e.Id.Trim()).ToDictionary(g => g.Key, g => g.Last());
        var firstSeen = records.GroupBy(r => r.EmployeeId).ToDictionary(g => g.Key, g => g.Min(r => r.Date));

        var employees = new List<EmployeeSummary>();
        foreach (var g in inPeriod.GroupBy(r => r.EmployeeId))
        {
            employeeSettings.TryGetValue(g.Key, out var setting);
            var last = g.Last();
            var emp = new EmployeeSummary
            {
                Id = g.Key,
                Name = last.Name,
                Department = last.Department,
                HireDate = setting?.HireDate?.Date,
                RetireDate = setting?.RetireDate?.Date,
            };
            var from = emp.HireDate ?? firstSeen[g.Key];
            var to = emp.RetireDate ?? periodEnd;

            foreach (var month in months)
            {
                var stats = new Stats();
                var monthEnd = month.AddMonths(1).AddDays(-1);
                stats.ScheduledDays = workdays.Count(d => d >= month && d <= monthEnd && d >= from && d <= to);

                var monthRecords = g.Where(r => MonthOf(r.Date) == month).ToList();
                foreach (var day in monthRecords.GroupBy(r => r.Date))
                {
                    var results = day.Select(_calc.Calculate).ToList();
                    if (results.Any(x => x.Attended))
                    {
                        if (results[0].IsWorkday) stats.AttendedWorkdays++;
                        else stats.HolidayWorkDays++;
                    }
                    foreach (var res in results)
                    {
                        stats.NormalOvertime += res.NormalOvertime;
                        stats.LateNightOvertime += res.LateNightOvertime;
                        stats.HolidayWork += res.HolidayWork;
                    }
                    if (day.Count() > 1)
                        Warnings.Add($"{emp.Id} {emp.Name}: {day.Key:yyyy/MM/dd} の行が{day.Count()}行あります（すべて合算しました）。");
                }
                foreach (var r in monthRecords.Where(r => r.HasPunch && (!r.ClockIn.HasValue || !r.ClockOut.HasValue)))
                    Warnings.Add($"{emp.Id} {emp.Name}: {r.Date:yyyy/MM/dd} は出勤・退勤の片方が空欄です（出勤日には数え、時間は0として扱いました）。");

                // 所定日より多く出勤扱いにならないよう補正（入社日設定より前の出勤など）
                if (stats.AttendedWorkdays > stats.ScheduledDays)
                {
                    Warnings.Add($"{emp.Id} {emp.Name}: {month:yyyy/MM} は在籍期間外の平日出勤があります（入社日・退職日の設定を確認してください）。");
                    stats.ScheduledDays = stats.AttendedWorkdays;
                }

                emp.Monthly[month] = stats;
                emp.MonthlyDepartment[month] = monthRecords.Count > 0 ? monthRecords[^1].Department : "";
                emp.Total.Add(stats);
            }

            // 記録のない月は直近（無ければ最初）の所属部署とみなす
            string? known = null;
            foreach (var m in months)
                if (emp.MonthlyDepartment[m].Length > 0) known = emp.MonthlyDepartment[m];
                else if (known != null) emp.MonthlyDepartment[m] = known;
            foreach (var m in months.Where(m => emp.MonthlyDepartment[m].Length == 0))
                emp.MonthlyDepartment[m] = emp.Department;

            employees.Add(emp);
        }

        // ---- 部署ごと ----
        var deptNames = employees.SelectMany(e => e.MonthlyDepartment.Values).Distinct().ToList();
        var departments = OrderDepartments(deptNames).Select(n => new DepartmentSummary { Name = n }).ToList();
        var deptMap = departments.ToDictionary(d => d.Name);
        foreach (var d in departments)
            foreach (var m in months) d.Monthly[m] = new Stats();

        foreach (var emp in employees)
        {
            deptMap[emp.Department].Members.Add(emp);
            foreach (var m in months)
            {
                var s = emp.Monthly[m];
                var dept = deptMap[emp.MonthlyDepartment[m]];
                dept.Monthly[m].Add(s);
                dept.Total.Add(s);
                if (s.ScheduledDays > 0 || s.AttendedDays > 0) dept.Monthly[m].Headcount++;
            }
        }
        foreach (var d in departments)
        {
            d.Total.Headcount = d.Members.Count;
            d.Members.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
        }

        var companyMonthly = months.ToDictionary(m => m, m =>
        {
            var s = new Stats();
            foreach (var d in departments) { s.Add(d.Monthly[m]); s.Headcount += d.Monthly[m].Headcount; }
            return s;
        });
        var companyTotal = new Stats { Headcount = employees.Count };
        foreach (var d in departments) companyTotal.Add(d.Total);

        // ---- ランキング ----
        var deptOrder = departments.Select((d, i) => (d.Name, i)).ToDictionary(x => x.Name, x => x.i);
        var top = employees.Where(e => e.Total.TotalOvertime > 0)
            .OrderByDescending(e => e.Total.TotalOvertime).ThenBy(e => e.Id, StringComparer.Ordinal)
            .Take(config.Report.TopOvertimeCount).ToList();
        var bottom = employees.Where(e => e.Total.ScheduledDays > 0)
            .OrderBy(e => e.Total.AttendedDays).ThenBy(e => e.Total.AttendanceRate).ThenBy(e => e.Id, StringComparer.Ordinal)
            .Take(config.Report.BottomAttendanceCount).ToList();

        // ---- 未承認 ----
        var approved = config.Input.ApprovedValues.Select(v => v.Trim()).ToHashSet();
        var unapproved = inPeriod
            .Where(r => (r.HasPunch || r.Approval.Length > 0) && !approved.Contains(r.Approval))
            .GroupBy(r => r.EmployeeId)
            .Select(g => new UnapprovedEntry(g.Key, g.Last().Name, g.Last().Department, g.Count(), g.Select(r => r.Date).Distinct().ToList()))
            .OrderBy(u => deptOrder.GetValueOrDefault(u.Department, int.MaxValue))
            .ThenByDescending(u => u.Count).ThenBy(u => u.EmployeeId, StringComparer.Ordinal)
            .ToList();

        return new ReportData
        {
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            Months = months,
            Departments = departments,
            Employees = employees,
            CompanyTotal = companyTotal,
            CompanyMonthly = companyMonthly,
            TopOvertime = top,
            BottomAttendance = bottom,
            Unapproved = unapproved,
            ExcludedCount = allRecords.Where(r => excluded.Contains(r.EmployeeId)).Select(r => r.EmployeeId).Distinct().Count(),
        };
    }

    /// <summary>設定の並び順 → 未設定の部署は名前順で末尾。</summary>
    public IEnumerable<string> OrderDepartments(IEnumerable<string> names)
    {
        var order = config.Departments.Order.Select(o => o.Trim()).ToList();
        var list = names.ToList();
        var unknown = list.Where(n => !order.Contains(n)).OrderBy(n => n, StringComparer.Ordinal).ToList();
        if (unknown.Count > 0 && order.Count > 0)
            Warnings.Add($"departments.order に無い部署は末尾に表示しました: {string.Join(", ", unknown)}");
        return order.Where(list.Contains).Concat(unknown);
    }

    private static DateTime MonthOf(DateTime d) => new(d.Year, d.Month, 1);
}
