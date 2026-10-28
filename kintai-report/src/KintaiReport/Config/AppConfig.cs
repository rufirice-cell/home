namespace KintaiReport.Config;

/// <summary>settings.yaml の内容。キー名は camelCase で記述する。</summary>
public sealed class AppConfig
{
    public InputConfig Input { get; set; } = new();
    public ReportConfig Report { get; set; } = new();
    public DepartmentConfig Departments { get; set; } = new();
    public ScheduleConfig Schedule { get; set; } = new();
    public TimeRangeConfig LateNight { get; set; } = new() { Start = "22:00", End = "06:00" };
    public BreakConfig Break { get; set; } = new();
    public RoundingConfig Rounding { get; set; } = new();
    public List<string> ExcludedEmployees { get; set; } = new();
    public List<EmployeeConfig> Employees { get; set; } = new();
    public HeatmapConfig Heatmap { get; set; } = new();
}

public sealed class InputConfig
{
    public string SheetName { get; set; } = "勤怠";
    public int HeaderRow { get; set; } = 1;
    public ColumnConfig Columns { get; set; } = new();
    /// <summary>カレンダー欄がこの値なら所定出勤日（それ以外は休日扱い）。</summary>
    public List<string> WorkdayCalendarValues { get; set; } = new() { "平日" };
    /// <summary>承認欄がこの値なら承認済み。</summary>
    public List<string> ApprovedValues { get; set; } = new() { "承認済" };
}

public sealed class ColumnConfig
{
    public string EmployeeId { get; set; } = "社員番号";
    public string Name { get; set; } = "氏名";
    public string Department { get; set; } = "部署";
    public string Date { get; set; } = "日付";
    public string Calendar { get; set; } = "カレンダー";
    public string ClockIn { get; set; } = "出勤時刻";
    public string ClockOut { get; set; } = "退勤時刻";
    /// <summary>任意列。空文字なら休憩ルールのみで計算する。</summary>
    public string BreakMinutes { get; set; } = "休憩(分)";
    public string Approval { get; set; } = "承認状況";
}

public sealed class ReportConfig
{
    public string Title { get; set; } = "部署別 月次累計勤怠分析レポート";
    public string CompanyName { get; set; } = "";
    /// <summary>累計の起点となる月（年度開始月）。</summary>
    public int FiscalYearStartMonth { get; set; } = 4;
    public int TopOvertimeCount { get; set; } = 20;
    public int BottomAttendanceCount { get; set; } = 20;
    public bool ShowDepartmentDetails { get; set; } = true;
    public FontConfig Font { get; set; } = new();
}

public sealed class FontConfig
{
    /// <summary>先頭から順に使用を試みるフォント名。</summary>
    public List<string> Families { get; set; } = new() { "Yu Gothic", "Meiryo", "MS Gothic", "IPAexGothic", "IPAGothic", "Noto Sans CJK JP" };
    /// <summary>フォントファイル（.ttf/.otf/.ttc）を直接指定する場合のパス。設定ファイルからの相対パス可。</summary>
    public string File { get; set; } = "";
}

public sealed class DepartmentConfig
{
    /// <summary>レポート上の部署の並び順。</summary>
    public List<string> Order { get; set; } = new();
}

public sealed class ScheduleConfig
{
    public TimeRangeConfig Default { get; set; } = new() { Start = "09:00", End = "18:00" };
    public Dictionary<string, TimeRangeConfig> ByDepartment { get; set; } = new();
}

public sealed class TimeRangeConfig
{
    public string Start { get; set; } = "";
    public string End { get; set; } = "";
}

public sealed class BreakConfig
{
    /// <summary>Excel の休憩列に値があればそれを優先する。</summary>
    public bool UseExcelBreakColumn { get; set; } = true;
    public List<BreakRule> Rules { get; set; } = new();
}

public sealed class BreakRule
{
    /// <summary>拘束時間（退勤−出勤）がこの分数を「超えた」ら適用。</summary>
    public int OverMinutes { get; set; }
    public int BreakMinutes { get; set; }
}

public sealed class RoundingConfig
{
    /// <summary>1日ごとの残業・深夜・休日出勤時間を、この分単位で切り捨てる（1 なら丸めなし）。</summary>
    public int OvertimeUnitMinutes { get; set; } = 1;
}

public sealed class EmployeeConfig
{
    public string Id { get; set; } = "";
    public DateTime? HireDate { get; set; }
    public DateTime? RetireDate { get; set; }
}

public sealed class HeatmapConfig
{
    /// <summary>この出勤率以下は最も濃い色で表示（0〜1）。</summary>
    public double MinRate { get; set; } = 0.80;
    /// <summary>この出勤率以上は最も薄い色で表示（0〜1）。</summary>
    public double MaxRate { get; set; } = 1.00;
}
