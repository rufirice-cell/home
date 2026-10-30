using KintaiReport.Calc;
using KintaiReport.Config;
using KintaiReport.Models;

namespace KintaiReport.Tests;

public class WorkTimeCalculatorTests
{
    private static AppConfig Config()
    {
        var c = new AppConfig();
        c.Schedule.ByDepartment["店舗課"] = new TimeRangeConfig { Start = "11:00", End = "20:00" };
        c.Break.Rules.Add(new BreakRule { OverMinutes = 360, BreakMinutes = 45 });
        c.Break.Rules.Add(new BreakRule { OverMinutes = 480, BreakMinutes = 60 });
        return c;
    }

    private static AttendanceRecord Rec(string inTime, string outTime, string calendar = "平日", string dept = "総務課", int? breakMinutes = null)
    {
        TimeParser.TryParseClockText(inTime, out var i);
        TimeParser.TryParseClockText(outTime, out var o);
        if (o <= i) o += 1440;
        return new AttendanceRecord
        {
            RowNumber = 2, EmployeeId = "E1", Name = "見本 花子", Department = dept,
            Date = new DateTime(2026, 4, 1), Calendar = calendar, ClockIn = i, ClockOut = o, BreakMinutes = breakMinutes,
        };
    }

    [Fact]
    public void 所定通りなら残業なし()
    {
        var r = new WorkTimeCalculator(Config()).Calculate(Rec("9:00", "18:00"));
        Assert.Equal(480, r.WorkMinutes);
        Assert.Equal(0, r.TotalOvertime);
    }

    [Fact]
    public void 定時後の勤務は普通残業()
    {
        var r = new WorkTimeCalculator(Config()).Calculate(Rec("8:50", "20:30"));
        Assert.Equal(10 + 150, r.NormalOvertime);
        Assert.Equal(0, r.LateNightOvertime);
    }

    [Fact]
    public void 二十二時以降は深夜残業()
    {
        var r = new WorkTimeCalculator(Config()).Calculate(Rec("9:00", "23:30"));
        Assert.Equal(240, r.NormalOvertime);   // 18:00-22:00
        Assert.Equal(90, r.LateNightOvertime); // 22:00-23:30
    }

    [Fact]
    public void 日付をまたぐ勤務()
    {
        var r = new WorkTimeCalculator(Config()).Calculate(Rec("9:00", "2:00"));
        Assert.Equal(240, r.NormalOvertime);
        Assert.Equal(240, r.LateNightOvertime); // 22:00-翌2:00
    }

    [Fact]
    public void 店舗課は11時から20時が所定()
    {
        var calc = new WorkTimeCalculator(Config());
        Assert.Equal(0, calc.Calculate(Rec("11:00", "20:00", dept: "店舗課")).TotalOvertime);
        var r = calc.Calculate(Rec("11:00", "23:00", dept: "店舗課"));
        Assert.Equal(120, r.NormalOvertime);
        Assert.Equal(60, r.LateNightOvertime);
        // 総務課で同じ時間なら 9:00 前は無いが 18:00 以降が残業
        Assert.Equal(240 + 60, calc.Calculate(Rec("11:00", "23:00")).TotalOvertime);
    }

    [Fact]
    public void 休日は全時間が休日出勤で深夜帯は深夜残業()
    {
        var r = new WorkTimeCalculator(Config()).Calculate(Rec("13:00", "23:00", calendar: "土曜"));
        Assert.False(r.IsWorkday);
        Assert.Equal(600 - 60 - 60, r.HolidayWork); // 拘束10h - 休憩60分 - 深夜60分
        Assert.Equal(60, r.LateNightOvertime);
        Assert.Equal(0, r.NormalOvertime);
    }

    [Fact]
    public void 休憩ルールとExcel休憩列()
    {
        var calc = new WorkTimeCalculator(Config());
        Assert.Equal(0, calc.RuleBreak(360));
        Assert.Equal(45, calc.RuleBreak(361));
        Assert.Equal(60, calc.RuleBreak(481));
        // 半日勤務（9:00-15:00 = 6h ちょうど）は休憩なし
        Assert.Equal(360, calc.Calculate(Rec("9:00", "15:00")).WorkMinutes);
        // Excel の休憩列 90 分を優先
        Assert.Equal(450, calc.Calculate(Rec("9:00", "18:00", breakMinutes: 90)).WorkMinutes);
    }

    [Fact]
    public void 所定内を超える休憩は残業から差し引く()
    {
        // 16:00-20:00 勤務（所定内 2h）で休憩列 150 分 → 所定外 2h から 30 分差し引き
        var r = new WorkTimeCalculator(Config()).Calculate(Rec("16:00", "20:00", breakMinutes: 150));
        Assert.Equal(90, r.NormalOvertime);
    }

    [Fact]
    public void 丸め単位で切り捨て()
    {
        var c = Config();
        c.Rounding.OvertimeUnitMinutes = 15;
        Assert.Equal(15, new WorkTimeCalculator(c).Calculate(Rec("9:00", "18:29")).NormalOvertime);
    }

    [Fact]
    public void 打刻が片方だけなら出勤扱いで時間はゼロ()
    {
        var rec = Rec("9:00", "18:00") with { ClockOut = null };
        var r = new WorkTimeCalculator(Config()).Calculate(rec);
        Assert.True(r.Attended);
        Assert.Equal(0, r.WorkMinutes);
    }

    [Theory]
    [InlineData("9:00", 540)]
    [InlineData("09:05:00", 545)]
    [InlineData("25:30", 1530)]
    [InlineData("９：１５", 555)]
    [InlineData("9時30分", 570)]
    public void 時刻文字列の解釈(string text, int expected)
    {
        Assert.True(TimeParser.TryParseClockText(text, out var m));
        Assert.Equal(expected, m);
    }
}
