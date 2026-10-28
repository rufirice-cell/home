using ClosedXML.Excel;
using KintaiReport.Config;
using KintaiReport.Excel;

namespace KintaiReport.Tests;

public class AttendanceReaderTests
{
    [Fact]
    public void 様々なセル形式の時刻を読める()
    {
        var path = Path.Combine(Path.GetTempPath(), $"kintai-test-{Guid.NewGuid():N}.xlsx");
        try
        {
            using (var wb = new XLWorkbook())
            {
                var ws = wb.AddWorksheet("勤怠");
                string[] headers = ["社員番号", "氏名", "部署", "日付", "カレンダー", "出勤時刻", "退勤時刻", "休憩（分）", "承認状況"];
                for (var c = 0; c < headers.Length; c++) ws.Cell(1, c + 1).Value = headers[c];

                void Row(int r, XLCellValue clockIn, XLCellValue clockOut, XLCellValue breakValue)
                {
                    ws.Cell(r, 1).Value = "E1";
                    ws.Cell(r, 2).Value = "見本 花子";
                    ws.Cell(r, 3).Value = "総務課";
                    ws.Cell(r, 4).Value = new DateTime(2026, 4, r);
                    ws.Cell(r, 5).Value = "平日";
                    ws.Cell(r, 6).Value = clockIn;
                    ws.Cell(r, 7).Value = clockOut;
                    ws.Cell(r, 8).Value = breakValue;
                    ws.Cell(r, 9).Value = "承認済";
                }
                Row(2, TimeSpan.FromHours(9), TimeSpan.FromHours(18.5), 60);                         // 時刻型
                Row(3, "9:15", "25:30", "1:00");                                                   // 文字列（翌1:30）
                Row(4, 9.0 / 24, new DateTime(2026, 4, 5, 2, 0, 0), Blank.Value);                  // シリアル値・翌日の日時
                Row(5, TimeSpan.FromHours(22), TimeSpan.FromHours(6), Blank.Value);                // 夜勤（退勤<出勤）
                ws.Column(6).Style.NumberFormat.Format = "h:mm";
                ws.Column(7).Style.NumberFormat.Format = "[h]:mm";
                wb.SaveAs(path);
            }

            var records = new AttendanceReader(new InputConfig()).Read(path);
            Assert.Equal(4, records.Count);
            Assert.Equal((540, 1110, 60), (records[0].ClockIn, records[0].ClockOut, records[0].BreakMinutes));
            Assert.Equal((555, 1530, 60), (records[1].ClockIn, records[1].ClockOut, records[1].BreakMinutes));
            Assert.Equal((540, 1560, (int?)null), (records[2].ClockIn, records[2].ClockOut, records[2].BreakMinutes));
            Assert.Equal((1320, 1800), (records[3].ClockIn, records[3].ClockOut));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void 必要な列が無ければ分かりやすいエラー()
    {
        var path = Path.Combine(Path.GetTempPath(), $"kintai-test-{Guid.NewGuid():N}.xlsx");
        try
        {
            using (var wb = new XLWorkbook())
            {
                var ws = wb.AddWorksheet("勤怠");
                ws.Cell(1, 1).Value = "社員番号";
                wb.SaveAs(path);
            }
            var ex = Assert.Throws<UserException>(() => new AttendanceReader(new InputConfig()).Read(path));
            Assert.Contains("氏名", ex.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
