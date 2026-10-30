using System.Globalization;
using ClosedXML.Excel;
using KintaiReport.Config;
using KintaiReport.Models;

namespace KintaiReport.Excel;

public sealed class AttendanceReader(InputConfig config)
{
    public List<string> Warnings { get; } = new();

    public List<AttendanceRecord> Read(string path)
    {
        if (!File.Exists(path))
            throw new UserException($"Excelファイルが見つかりません: {path}");

        // Excel で開いたままでも読めるよう共有モードで開く
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var workbook = new XLWorkbook(stream);
        if (!workbook.TryGetWorksheet(config.SheetName, out var sheet))
            throw new UserException($"シート「{config.SheetName}」が見つかりません。存在するシート: {string.Join(", ", workbook.Worksheets.Select(w => w.Name))}");

        var columns = MapColumns(sheet);
        var records = new List<AttendanceRecord>();
        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 0;

        for (var r = config.HeaderRow + 1; r <= lastRow; r++)
        {
            var row = sheet.Row(r);
            var id = Text(row.Cell(columns.EmployeeId));
            var dateCell = row.Cell(columns.Date);
            if (id.Length == 0 && dateCell.IsEmpty()) continue; // 空行

            if (id.Length == 0) { Warnings.Add($"{r}行目: 社員番号が空のためスキップしました。"); continue; }
            if (!TryGetDate(dateCell, out var date)) { Warnings.Add($"{r}行目: 日付「{Text(dateCell)}」を解釈できないためスキップしました。"); continue; }

            var clockIn = ReadClock(row.Cell(columns.ClockIn), date, r, "出勤時刻");
            var clockOut = ReadClock(row.Cell(columns.ClockOut), date, r, "退勤時刻");
            if (clockIn.HasValue && clockOut.HasValue && clockOut <= clockIn)
                clockOut += 24 * 60; // 日付をまたいだ退勤

            int? breakMinutes = null;
            if (columns.BreakMinutes > 0)
                breakMinutes = ReadBreak(row.Cell(columns.BreakMinutes), r);

            records.Add(new AttendanceRecord
            {
                RowNumber = r,
                EmployeeId = id,
                Name = Text(row.Cell(columns.Name)),
                Department = Text(row.Cell(columns.Department)),
                Date = date,
                Calendar = Text(row.Cell(columns.Calendar)),
                ClockIn = clockIn,
                ClockOut = clockOut,
                BreakMinutes = breakMinutes,
                Approval = Text(row.Cell(columns.Approval)),
            });
        }

        return records;
    }

    private sealed record ColumnIndex(int EmployeeId, int Name, int Department, int Date, int Calendar, int ClockIn, int ClockOut, int BreakMinutes, int Approval);

    private ColumnIndex MapColumns(IXLWorksheet sheet)
    {
        var header = new Dictionary<string, int>();
        var headerRow = sheet.Row(config.HeaderRow);
        var lastCol = headerRow.LastCellUsed()?.Address.ColumnNumber ?? 0;
        for (var c = 1; c <= lastCol; c++)
        {
            var name = Normalize(Text(headerRow.Cell(c)));
            if (name.Length > 0) header.TryAdd(name, c);
        }

        var missing = new List<string>();
        int Find(string name, bool required = true)
        {
            if (string.IsNullOrWhiteSpace(name)) return 0;
            if (header.TryGetValue(Normalize(name), out var c)) return c;
            if (required) missing.Add(name);
            return 0;
        }

        var cols = config.Columns;
        var result = new ColumnIndex(
            Find(cols.EmployeeId), Find(cols.Name), Find(cols.Department), Find(cols.Date), Find(cols.Calendar),
            Find(cols.ClockIn), Find(cols.ClockOut), Find(cols.BreakMinutes, required: false), Find(cols.Approval));

        if (missing.Count > 0)
            throw new UserException(
                $"{config.HeaderRow}行目の見出しに必要な列がありません: {string.Join(", ", missing)}\n" +
                $"見つかった見出し: {string.Join(", ", header.Keys)}\n" +
                "列名が違う場合は settings.yaml の input.columns を修正してください。");
        return result;
    }

    private static string Normalize(string s) => s.Normalize(System.Text.NormalizationForm.FormKC).Replace(" ", "").Replace("　", "");

    private static string Text(IXLCell cell)
    {
        var v = cell.Value;
        if (v.IsBlank) return "";
        if (v.IsNumber) return v.GetNumber().ToString(CultureInfo.InvariantCulture);
        return cell.GetFormattedString().Trim();
    }

    private static bool TryGetDate(IXLCell cell, out DateTime date)
    {
        var v = cell.Value;
        if (v.IsDateTime) { date = v.GetDateTime().Date; return true; }
        if (v.IsNumber) { date = DateTime.FromOADate(v.GetNumber()).Date; return true; }
        var s = Text(cell);
        string[] formats = ["yyyy/M/d", "yyyy-M-d", "yyyy年M月d日", "yyyy/M/d H:mm", "yyyy/M/d H:mm:ss"];
        if (DateTime.TryParseExact(s, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
        {
            date = date.Date;
            return true;
        }
        if (!DateTime.TryParse(s, CultureInfo.GetCultureInfo("ja-JP"), DateTimeStyles.None, out date)) return false;
        date = date.Date;
        return true;
    }

    /// <summary>時刻セルを当日 0:00 からの分に変換する。日時型で翌日の値なら 1440 以上になる。</summary>
    private int? ReadClock(IXLCell cell, DateTime date, int row, string label)
    {
        var v = cell.Value;
        if (v.IsBlank) return null;

        if (v.IsTimeSpan) return (int)Math.Round(v.GetTimeSpan().TotalMinutes);
        if (v.IsDateTime)
        {
            var dt = v.GetDateTime();
            return dt.Year < 1901 ? TimeOnlyMinutes(dt) : (int)Math.Round((dt - date).TotalMinutes);
        }
        if (v.IsNumber)
        {
            var serial = v.GetNumber();
            if (serial is >= 0 and < 3) return (int)Math.Round(serial * 24 * 60);
            return (int)Math.Round((DateTime.FromOADate(serial) - date).TotalMinutes);
        }

        var text = Text(cell);
        if (text is "-" or "－" or "ー") return null;
        if (TimeParser.TryParseClockText(text, out var minutes)) return minutes;
        if (DateTime.TryParse(text, CultureInfo.GetCultureInfo("ja-JP"), DateTimeStyles.None, out var parsed))
            return (int)Math.Round((parsed - date).TotalMinutes);

        Warnings.Add($"{row}行目: {label}「{text}」を解釈できないため空欄として扱いました。");
        return null;
    }

    /// <summary>
    /// 時刻のみのセル（シリアル値 1 未満、または 25:30 のような 1 以上 2 未満）の分数。
    /// ClosedXML はシリアル値 0 を 1899/12/31 として返すため、そこを起点に数える。
    /// </summary>
    private static int TimeOnlyMinutes(DateTime dt)
    {
        var origin = dt < new DateTime(1899, 12, 31) ? new DateTime(1899, 12, 30) : new DateTime(1899, 12, 31);
        return (int)Math.Round((dt - origin).TotalMinutes);
    }

    /// <summary>休憩列。数値なら分、時刻形式（1:00 など）なら時間として読む。</summary>
    private int? ReadBreak(IXLCell cell, int row)
    {
        var v = cell.Value;
        if (v.IsBlank) return null;
        if (v.IsTimeSpan) return (int)Math.Round(v.GetTimeSpan().TotalMinutes);
        if (v.IsDateTime) return TimeOnlyMinutes(v.GetDateTime());
        if (v.IsNumber)
        {
            var n = v.GetNumber();
            // 1 未満の小数は Excel の時刻シリアル値（0.0416… = 1:00）とみなす
            return n is > 0 and < 1 ? (int)Math.Round(n * 24 * 60) : (int)Math.Round(n);
        }
        var text = Text(cell);
        if (TimeParser.TryParseClockText(text, out var m)) return m;
        if (int.TryParse(text.Replace("分", ""), out var n2)) return n2;
        Warnings.Add($"{row}行目: 休憩「{text}」を解釈できないため休憩ルールで計算しました。");
        return null;
    }
}
