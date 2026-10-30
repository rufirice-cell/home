using KintaiReport.Calc;
using KintaiReport.Config;
using KintaiReport.Excel;

namespace KintaiReport.Tests;

/// <summary>リポジトリ同梱のサンプル（ダミー）データでの結合テスト。</summary>
public class SampleDataTests
{
    private static string Root([System.Runtime.CompilerServices.CallerFilePath] string path = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, "..", ".."));

    private static (AppConfig Config, ReportData Data) Load(DateTime? month = null)
    {
        var config = ConfigLoader.Load(Path.Combine(Root(), "src", "KintaiReport", "settings.yaml"));
        var records = new AttendanceReader(config.Input).Read(Path.Combine(Root(), "sample", "勤怠サンプル.xlsx"));
        return (config, new ReportAggregator(config).Aggregate(records, month));
    }

    [Fact]
    public void 年度開始月から最終月までを集計する()
    {
        var (_, data) = Load();
        Assert.Equal(new DateTime(2026, 4, 1), data.PeriodStart);
        Assert.Equal(new DateTime(2026, 9, 30), data.PeriodEnd);
        Assert.Equal(6, data.Months.Count);

        var (_, july) = Load(new DateTime(2026, 7, 1));
        Assert.Equal(new DateTime(2026, 7, 31), july.PeriodEnd);
        Assert.Equal(4, july.Months.Count);
    }

    [Fact]
    public void 部署は設定ファイルの順に並ぶ()
    {
        var (config, data) = Load();
        Assert.Equal(config.Departments.Order, data.Departments.Select(d => d.Name));
    }

    [Fact]
    public void 除外者は集計に含まれない()
    {
        var (_, data) = Load();
        Assert.DoesNotContain(data.Employees, e => e.Id is "E9001" or "E9002");
        Assert.Equal(2, data.ExcludedCount);
    }

    [Fact]
    public void 入社日前の平日は所定日数に数えない()
    {
        var (_, data) = Load();
        var e = data.Employees.Single(x => x.Id == "E0108"); // 2026/6/1 入社
        Assert.Equal(0, e.Monthly[new DateTime(2026, 4, 1)].ScheduledDays);
        Assert.Equal(0, e.Monthly[new DateTime(2026, 5, 1)].ScheduledDays);
        Assert.True(e.Monthly[new DateTime(2026, 6, 1)].ScheduledDays > 0);
    }

    [Fact]
    public void ランキングと未承認一覧()
    {
        var (_, data) = Load();
        Assert.Equal(20, data.TopOvertime.Count);
        Assert.True(data.TopOvertime.Zip(data.TopOvertime.Skip(1)).All(p => p.First.Total.TotalOvertime >= p.Second.Total.TotalOvertime));
        Assert.Equal(20, data.BottomAttendance.Count);
        Assert.True(data.BottomAttendance.Zip(data.BottomAttendance.Skip(1)).All(p => p.First.Total.AttendedDays <= p.Second.Total.AttendedDays));
        Assert.NotEmpty(data.Unapproved);
    }

    [Fact]
    public void 時間外時間が現実的な範囲()
    {
        var (_, data) = Load();
        var perHeadMonthly = data.CompanyTotal.TotalOvertime / 60.0 / data.CompanyTotal.Headcount / data.Months.Count;
        Assert.InRange(perHeadMonthly, 5, 60);
    }
}
