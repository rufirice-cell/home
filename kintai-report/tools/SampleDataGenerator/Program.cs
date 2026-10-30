using ClosedXML.Excel;

// ダミーの勤怠Excelを生成する。氏名はすべて架空（「見本 花子」など）で、実在の人物とは無関係。
// 使い方: SampleDataGenerator [出力パス]
var output = args.Length > 0 ? args[0] : "sample/勤怠サンプル.xlsx";
var rnd = new Random(20260401);

var start = new DateTime(2026, 4, 1);
var end = new DateTime(2026, 9, 30);
var holidays = new HashSet<DateTime>
{
    new(2026, 4, 29), new(2026, 5, 4), new(2026, 5, 5), new(2026, 5, 6),
    new(2026, 7, 20), new(2026, 8, 11), new(2026, 9, 21), new(2026, 9, 22), new(2026, 9, 23),
};
var companyHolidays = new HashSet<DateTime> { new(2026, 8, 12), new(2026, 8, 13), new(2026, 8, 14) };

string CalendarOf(DateTime d) =>
    holidays.Contains(d) ? "祝日" :
    companyHolidays.Contains(d) ? "会社休日" :
    d.DayOfWeek == DayOfWeek.Saturday ? "土曜" :
    d.DayOfWeek == DayOfWeek.Sunday ? "日曜" : "平日";

// 部署, 人数, 所定開始(分), 所定終了(分), 残業傾向(平均分/日), 休日出勤率
var departments = new (string Name, int Count, int Start, int End, int Overtime, double HolidayRate)[]
{
    ("総務課", 10, 540, 1080, 25, 0.02),
    ("人事課", 8, 540, 1080, 35, 0.02),
    ("経理課", 9, 540, 1080, 45, 0.03),
    ("営業1課", 16, 540, 1080, 80, 0.06),
    ("営業2課", 14, 540, 1080, 65, 0.05),
    ("開発課", 22, 540, 1080, 95, 0.08),
    ("店舗課", 24, 660, 1200, 50, 0.04),
};

string[] familyNames = ["見本", "仮名", "架空", "試験", "模擬", "例示", "雛形", "試作", "検証", "標本"];
string[] givenNames = ["一郎", "花子", "二郎", "桜子", "三郎", "陽子", "健一", "由美", "翔", "葵", "大輔", "真央"];

var employees = new List<(string Id, string Name, string Dept, int Start, int End, double Overtime, double HolidayRate, double Absence, DateTime From, DateTime To)>();
var seq = 1;
foreach (var d in departments)
{
    for (var i = 0; i < d.Count; i++, seq++)
    {
        var id = $"E{seq:0000}";
        var name = $"{familyNames[seq % familyNames.Length]} {givenNames[seq / familyNames.Length % givenNames.Length]}";
        var overtime = d.Overtime * (0.3 + rnd.NextDouble() * 1.6);
        if (rnd.NextDouble() < 0.08) overtime *= 2.2; // 長時間残業が多い社員
        var absence = rnd.NextDouble() < 0.1 ? 0.08 + rnd.NextDouble() * 0.15 : rnd.NextDouble() * 0.04;
        employees.Add((id, name, d.Name, d.Start, d.End, overtime, d.HolidayRate, absence, start, end));
    }
}
// 入社・退職・除外対象（settings.yaml の employees / excludedEmployees と対応）
employees.Add(("E0108", "仮名 葵", "開発課", 540, 1080, 70, 0.05, 0.02, new(2026, 6, 1), end));
employees.Add(("E0109", "架空 真央", "店舗課", 660, 1200, 40, 0.03, 0.02, new(2026, 8, 17), end));
employees[41] = employees[41] with { To = new DateTime(2026, 7, 31) };
employees.Add(("E9001", "見本 社長", "総務課", 540, 1080, 120, 0.2, 0.0, start, end));
employees.Add(("E9002", "検証 太郎", "開発課", 540, 1080, 0, 0, 0.5, start, end));

// 異動する社員（7月から営業2課 → 営業1課）
var transferIndex = employees.FindIndex(e => e.Dept == "営業2課");

using var wb = new XLWorkbook();
var ws = wb.AddWorksheet("勤怠");
string[] headers = ["社員番号", "氏名", "部署", "日付", "曜日", "カレンダー", "出勤時刻", "退勤時刻", "休憩(分)", "承認状況", "備考"];
for (var c = 0; c < headers.Length; c++) ws.Cell(1, c + 1).Value = headers[c];

var row = 2;
var youbi = "日月火水木金土";
for (var ei = 0; ei < employees.Count; ei++)
{
    var e = employees[ei];
    for (var day = e.From; day <= e.To; day = day.AddDays(1))
    {
        var dept = ei == transferIndex && day >= new DateTime(2026, 7, 1) ? "営業1課" : e.Dept;
        var cal = CalendarOf(day);
        var isWorkday = cal == "平日";
        ws.Cell(row, 1).Value = e.Id;
        ws.Cell(row, 2).Value = e.Name;
        ws.Cell(row, 3).Value = dept;
        ws.Cell(row, 4).Value = day;
        ws.Cell(row, 4).Style.DateFormat.Format = "yyyy/mm/dd";
        ws.Cell(row, 5).Value = youbi[(int)day.DayOfWeek].ToString();
        ws.Cell(row, 6).Value = cal;

        var works = isWorkday ? rnd.NextDouble() >= e.Absence : rnd.NextDouble() < e.HolidayRate;
        if (works)
        {
            int inMin, outMin;
            if (isWorkday)
            {
                inMin = e.Start - rnd.Next(0, 25) + (rnd.NextDouble() < 0.03 ? rnd.Next(30, 120) : 0);
                var ot = rnd.NextDouble() < 0.25 ? 0 : (int)(e.Overtime * (0.2 + rnd.NextDouble() * 1.6));
                if (rnd.NextDouble() < 0.02 && e.Overtime > 60) ot += 240; // 深夜に及ぶ日
                outMin = e.End + rnd.Next(0, 10) + ot;
            }
            else
            {
                inMin = e.Start + rnd.Next(-30, 60);
                outMin = inMin + rnd.Next(180, 540);
            }
            SetTime(ws.Cell(row, 7), inMin, row);
            SetTime(ws.Cell(row, 8), outMin, row);
            if (rnd.NextDouble() < 0.1) ws.Cell(row, 9).Value = 60;

            var monthsAgo = (end.Year - day.Year) * 12 + end.Month - day.Month;
            var pending = monthsAgo == 0 ? 0.04 : monthsAgo == 1 ? 0.004 : 0;
            ws.Cell(row, 10).Value = rnd.NextDouble() < pending ? (rnd.NextDouble() < 0.7 ? "未承認" : "差戻し") : "承認済";
        }
        else if (isWorkday)
        {
            ws.Cell(row, 10).Value = "承認済";
            ws.Cell(row, 11).Value = rnd.NextDouble() < 0.6 ? "年休" : "欠勤";
        }
        row++;
    }
}

ws.Column(7).Style.NumberFormat.Format = "[h]:mm";
ws.Column(8).Style.NumberFormat.Format = "[h]:mm";
ws.Row(1).Style.Font.Bold = true;
ws.SheetView.FreezeRows(1);
ws.RangeUsed()!.SetAutoFilter();
ws.Columns().AdjustToContents(1, 200);

Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
wb.SaveAs(output);
Console.WriteLine($"{output} を作成しました（{row - 2:N0}行、{employees.Count}名）");

// 一部は文字列（"25:10" 形式など）で書き、読み込み側の解釈を確認できるようにする
static void SetTime(IXLCell cell, int minutes, int row)
{
    if (row % 37 == 0) cell.Value = $"{minutes / 60}:{minutes % 60:00}";
    else cell.Value = TimeSpan.FromMinutes(minutes);
}
