using KintaiReport.Calc;
using KintaiReport.Config;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace KintaiReport.Pdf;

public sealed class ReportDocument(ReportData data, AppConfig config, string[] fontFamilies, DateTime createdAt)
{
    // 配色: 文字色は無彩色、ヒートマップは青の単色シーケンシャル（低い出勤率ほど濃い）
    private const string White = "#ffffff";
    private const string Ink = "#1f1f1d";
    private const string InkMuted = "#6b6a66";
    private const string Rule = "#d6d4cf";
    private const string HeaderBg = "#eeece8";
    private const string TotalBg = "#f6f5f2";
    private const string Accent = "#1c5cab";

    private static readonly string[] BlueRamp =
    [
        "#f3f7fd", "#cde2fb", "#b7d3f6", "#9ec5f4", "#86b6ef", "#6da7ec", "#5598e7",
        "#3987e5", "#2a78d6", "#256abf", "#1c5cab", "#184f95", "#104281", "#0d366b",
    ];

    private IReadOnlyList<DateTime> Months => data.Months;

    public void GeneratePdf(string path) =>
        Document.Create(Compose).WithMetadata(new DocumentMetadata
        {
            Title = config.Report.Title,
            Creator = "KintaiReport",
            CreationDate = createdAt,
        }).GeneratePdf(path);

    private void Compose(IDocumentContainer container)
    {
        AddPage(container, "概要", ComposeSummary);
        AddPage(container, "部署別 月次推移", ComposeMonthlyTrend);
        AddPage(container, "部署別 出勤率ヒートマップ", ComposeHeatmap);
        AddPage(container, $"時間外時間 上位{config.Report.TopOvertimeCount}名", ComposeTopOvertime);
        AddPage(container, $"出勤日数 下位{config.Report.BottomAttendanceCount}名", ComposeBottomAttendance);
        AddPage(container, "未承認者一覧", ComposeUnapproved);
        if (config.Report.ShowDepartmentDetails)
            foreach (var dept in data.Departments)
                AddPage(container, $"部署別明細：{dept.Name}", c => ComposeDepartmentDetail(c, dept));
    }

    private void AddPage(IDocumentContainer container, string sectionTitle, Action<IContainer> content)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4.Landscape());
            page.Margin(24);
            page.PageColor(White);
            page.DefaultTextStyle(t => t.FontFamily(fontFamilies).FontSize(8).FontColor(Ink));

            page.Header().PaddingBottom(8).Column(col =>
            {
                col.Item().Row(row =>
                {
                    row.RelativeItem().Text(t =>
                    {
                        t.Span(config.Report.Title).FontSize(9).FontColor(InkMuted);
                        if (config.Report.CompanyName.Length > 0) t.Span($"　{config.Report.CompanyName}").FontSize(9).FontColor(InkMuted);
                    });
                    row.AutoItem().Text($"対象期間 {data.PeriodStart:yyyy/MM/dd}〜{data.PeriodEnd:yyyy/MM/dd}").FontSize(9).FontColor(InkMuted);
                });
                col.Item().PaddingTop(2).Text(sectionTitle).FontSize(15).Bold();
                col.Item().PaddingTop(4).LineHorizontal(1.2f).LineColor(Accent);
            });

            page.Content().Element(content);

            page.Footer().Row(row =>
            {
                row.RelativeItem().Text($"作成日時 {createdAt:yyyy/MM/dd HH:mm}").FontSize(7).FontColor(InkMuted);
                row.RelativeItem().AlignRight().Text(t =>
                {
                    t.DefaultTextStyle(s => s.FontSize(7).FontColor(InkMuted));
                    t.CurrentPageNumber();
                    t.Span(" / ");
                    t.TotalPages();
                });
            });
        });
    }

    // ------------------------------------------------------------ 概要

    private void ComposeSummary(IContainer c)
    {
        var total = data.CompanyTotal;
        var monthsCount = Months.Count;
        var unapprovedCount = data.Unapproved.Sum(u => u.Count);

        c.Column(col =>
        {
            col.Spacing(12);
            col.Item().Row(row =>
            {
                row.Spacing(10);
                Tile(row.RelativeItem(), "時間外時間 合計（累計）", H(total.TotalOvertime), $"普通 {H(total.NormalOvertime)} ／ 深夜 {H(total.LateNightOvertime)} ／ 休日 {H(total.HolidayWork)}");
                Tile(row.RelativeItem(), "1人あたり時間外（月平均）", H(PerHead(total.TotalOvertime, total.Headcount) / Math.Max(1, monthsCount)), $"対象 {total.Headcount}名 × {monthsCount}か月");
                Tile(row.RelativeItem(), "出勤率（全社累計）", Pct(total.AttendanceRate), $"出勤 {total.AttendedWorkdays:N0}日 ／ 所定 {total.ScheduledDays:N0}日");
                Tile(row.RelativeItem(), "未承認", $"{data.Unapproved.Count}名", $"{unapprovedCount}件");
            });

            col.Item().Text("部署別サマリー（期間累計）").FontSize(10).Bold();
            col.Item().Table(table =>
            {
                table.ColumnsDefinition(cd =>
                {
                    cd.RelativeColumn(2.2f);
                    for (var i = 0; i < 10; i++) cd.RelativeColumn();
                });
                table.Header(h =>
                {
                    foreach (var t in new[] { "部署", "人数", "所定日数", "出勤日数", "出勤率", "普通残業", "深夜残業", "休日出勤", "時間外合計", "1人あたり累計", "1人あたり月平均" })
                        HeaderCell(h.Cell(), t, t == "部署");
                });
                foreach (var d in data.Departments)
                    SummaryRow(table, d.Name, d.Total, monthsCount, false);
                SummaryRow(table, "全社", total, monthsCount, true);
            });

            col.Item().Text(t =>
            {
                t.DefaultTextStyle(s => s.FontSize(7).FontColor(InkMuted));
                t.Line("・時間は「時:分」。時間外合計 ＝ 普通残業 ＋ 深夜残業（22:00〜翌6:00の時間外） ＋ 休日出勤（深夜帯を除く）。");
                t.Line($"・所定時間 {config.Schedule.Default.Start}〜{config.Schedule.Default.End}" +
                       string.Concat(config.Schedule.ByDepartment.Select(kv => $"（{kv.Key}は {kv.Value.Start}〜{kv.Value.End}）")) +
                       $"。カレンダー欄が「{string.Join("・", config.Input.WorkdayCalendarValues)}」の日を所定出勤日とします。");
                t.Line("・出勤率 ＝ 所定出勤日のうち出勤した日数 ÷ 在籍期間内の所定出勤日数。人数は期間内に勤怠データがある社員数（最終所属部署で計上）。");
                if (data.ExcludedCount > 0) t.Line($"・設定により {data.ExcludedCount}名 を集計から除外しています。");
            });
        });
    }

    private void SummaryRow(TableDescriptor table, string name, Stats s, int months, bool isTotal)
    {
        var bg = isTotal ? TotalBg : White;
        BodyCell(table.Cell(), name, bg, left: true, bold: isTotal);
        BodyCell(table.Cell(), $"{s.Headcount}", bg, bold: isTotal);
        BodyCell(table.Cell(), $"{s.ScheduledDays:N0}", bg, bold: isTotal);
        BodyCell(table.Cell(), $"{s.AttendedWorkdays:N0}", bg, bold: isTotal);
        BodyCell(table.Cell(), Pct(s.AttendanceRate), bg, bold: isTotal);
        BodyCell(table.Cell(), H(s.NormalOvertime), bg, bold: isTotal);
        BodyCell(table.Cell(), H(s.LateNightOvertime), bg, bold: isTotal);
        BodyCell(table.Cell(), H(s.HolidayWork), bg, bold: isTotal);
        BodyCell(table.Cell(), H(s.TotalOvertime), bg, bold: true);
        BodyCell(table.Cell(), H(PerHead(s.TotalOvertime, s.Headcount)), bg, bold: isTotal);
        BodyCell(table.Cell(), H(PerHead(s.TotalOvertime, s.Headcount) / Math.Max(1, months)), bg, bold: isTotal);
    }

    private static void Tile(IContainer c, string label, string value, string sub)
    {
        c.Border(0.75f).BorderColor(Rule).Padding(10).Column(col =>
        {
            col.Item().Text(label).FontSize(8).FontColor(InkMuted);
            col.Item().PaddingTop(2).Text(value).FontSize(20).Bold();
            col.Item().Text(sub).FontSize(7).FontColor(InkMuted);
        });
    }

    // ------------------------------------------------------------ 月次推移

    private void ComposeMonthlyTrend(IContainer c)
    {
        c.Column(col =>
        {
            col.Spacing(6);
            col.Item().Text("時間外時間（普通残業＋深夜残業＋休日出勤）：上段＝当月、下段＝年度累計").FontSize(10).Bold();
            col.Item().Table(table =>
            {
                table.ColumnsDefinition(cd =>
                {
                    cd.RelativeColumn(2.2f);
                    foreach (var _ in Months) cd.RelativeColumn();
                    cd.RelativeColumn(1.1f);
                });
                table.Header(h =>
                {
                    HeaderCell(h.Cell(), "部署", true);
                    foreach (var m in Months) HeaderCell(h.Cell(), MonthLabel(m));
                    HeaderCell(h.Cell(), "1人あたり累計");
                });

                void Row(string name, Func<DateTime, Stats> monthly, Stats total, bool isTotal)
                {
                    var bg = isTotal ? TotalBg : White;
                    BodyCell(table.Cell(), name, bg, left: true, bold: isTotal);
                    var cum = 0;
                    foreach (var m in Months)
                    {
                        var v = monthly(m).TotalOvertime;
                        cum += v;
                        table.Cell().Background(bg).BorderBottom(0.5f).BorderColor(Rule).PaddingVertical(3).PaddingHorizontal(4).AlignRight().Column(cc =>
                        {
                            cc.Item().AlignRight().Text(H(v)).Bold();
                            cc.Item().AlignRight().Text(H(cum)).FontSize(6.5f).FontColor(InkMuted);
                        });
                    }
                    BodyCell(table.Cell(), H(PerHead(total.TotalOvertime, total.Headcount)), bg, bold: isTotal);
                }

                foreach (var d in data.Departments) Row(d.Name, m => d.Monthly[m], d.Total, false);
                Row("全社", m => data.CompanyMonthly[m], data.CompanyTotal, true);
            });

            col.Item().PaddingTop(10).Text("出勤日数（所定出勤日の出勤＋休日出勤）：上段＝当月、下段＝年度累計").FontSize(10).Bold();
            col.Item().Table(table =>
            {
                table.ColumnsDefinition(cd =>
                {
                    cd.RelativeColumn(2.2f);
                    foreach (var _ in Months) cd.RelativeColumn();
                    cd.RelativeColumn(1.1f);
                });
                table.Header(h =>
                {
                    HeaderCell(h.Cell(), "部署", true);
                    foreach (var m in Months) HeaderCell(h.Cell(), MonthLabel(m));
                    HeaderCell(h.Cell(), "うち休日出勤");
                });
                void Row(string name, Func<DateTime, Stats> monthly, Stats total, bool isTotal)
                {
                    var bg = isTotal ? TotalBg : White;
                    BodyCell(table.Cell(), name, bg, left: true, bold: isTotal);
                    var cum = 0;
                    foreach (var m in Months)
                    {
                        var v = monthly(m).AttendedDays;
                        cum += v;
                        table.Cell().Background(bg).BorderBottom(0.5f).BorderColor(Rule).PaddingVertical(3).PaddingHorizontal(4).Column(cc =>
                        {
                            cc.Item().AlignRight().Text($"{v:N0}").Bold();
                            cc.Item().AlignRight().Text($"{cum:N0}").FontSize(6.5f).FontColor(InkMuted);
                        });
                    }
                    BodyCell(table.Cell(), $"{total.HolidayWorkDays:N0}", bg, bold: isTotal);
                }
                foreach (var d in data.Departments) Row(d.Name, m => d.Monthly[m], d.Total, false);
                Row("全社", m => data.CompanyMonthly[m], data.CompanyTotal, true);
            });
        });
    }

    // ------------------------------------------------------------ ヒートマップ

    private void ComposeHeatmap(IContainer c)
    {
        c.Column(col =>
        {
            col.Spacing(8);
            col.Item().Text("所定出勤日に対する出勤率（色が濃いほど出勤率が低い）").FontSize(10).Bold();
            col.Item().Table(table =>
            {
                table.ColumnsDefinition(cd =>
                {
                    cd.RelativeColumn(2.2f);
                    foreach (var _ in Months) cd.RelativeColumn();
                    cd.ConstantColumn(6);
                    cd.RelativeColumn(1.1f);
                });
                table.Header(h =>
                {
                    HeaderCell(h.Cell(), "部署", true);
                    foreach (var m in Months) HeaderCell(h.Cell(), MonthLabel(m), center: true);
                    h.Cell();
                    HeaderCell(h.Cell(), "期間累計", center: true);
                });

                void Row(string name, Func<DateTime, Stats> monthly, Stats total, bool isTotal)
                {
                    BodyCell(table.Cell(), name, isTotal ? TotalBg : White, left: true, bold: isTotal);
                    foreach (var m in Months) HeatCell(table.Cell(), monthly(m));
                    table.Cell();
                    HeatCell(table.Cell(), total);
                }
                foreach (var d in data.Departments) Row(d.Name, m => d.Monthly[m], d.Total, false);
                Row("全社", m => data.CompanyMonthly[m], data.CompanyTotal, true);
            });

            // 凡例
            col.Item().PaddingTop(6).Row(row =>
            {
                row.AutoItem().AlignMiddle().PaddingRight(6).Text("凡例").FontSize(7).FontColor(InkMuted);
                row.AutoItem().AlignMiddle().PaddingRight(4).Text($"{config.Heatmap.MaxRate * 100:0}%以上").FontSize(7).FontColor(InkMuted);
                foreach (var color in BlueRamp)
                    row.ConstantItem(18).Height(10).Background(color);
                row.AutoItem().AlignMiddle().PaddingLeft(4).Text($"{config.Heatmap.MinRate * 100:0}%以下").FontSize(7).FontColor(InkMuted);
                row.AutoItem().AlignMiddle().PaddingLeft(12).Text("「－」は所定出勤日なし").FontSize(7).FontColor(InkMuted);
            });
            col.Item().Text("各セルの下段は 出勤日数／所定出勤日数。色の範囲は settings.yaml の heatmap で変更できます。").FontSize(7).FontColor(InkMuted);
        });
    }

    private void HeatCell(IContainer cell, Stats s)
    {
        var rate = s.AttendanceRate;
        if (rate is null)
        {
            cell.Border(1).BorderColor(White).Background("#fafaf8").MinHeight(30).AlignCenter().AlignMiddle().Text("－").FontColor(InkMuted);
            return;
        }
        var t = Math.Clamp((config.Heatmap.MaxRate - rate.Value) / (config.Heatmap.MaxRate - config.Heatmap.MinRate), 0, 1);
        var idx = (int)Math.Round(t * (BlueRamp.Length - 1));
        var fg = idx >= 7 ? White : Ink;
        var fgSub = idx >= 7 ? "#e6eefa" : InkMuted;
        cell.Border(1).BorderColor(White).Background(BlueRamp[idx]).MinHeight(30).AlignCenter().AlignMiddle().Column(cc =>
        {
            cc.Item().AlignCenter().Text(Pct(rate)).Bold().FontColor(fg);
            cc.Item().AlignCenter().Text($"{s.AttendedWorkdays}/{s.ScheduledDays}").FontSize(6).FontColor(fgSub);
        });
    }

    // ------------------------------------------------------------ ランキング

    private void ComposeTopOvertime(IContainer c)
    {
        if (data.TopOvertime.Count == 0) { c.Text("時間外時間のある社員はいません。"); return; }
        c.Column(col =>
        {
            col.Spacing(6);
            col.Item().Text("期間累計の時間外時間（普通残業＋深夜残業＋休日出勤）が多い順").FontSize(10).Bold();
            col.Item().Table(table =>
            {
                table.ColumnsDefinition(cd =>
                {
                    cd.ConstantColumn(28);
                    cd.RelativeColumn(1.6f);
                    cd.RelativeColumn(1.1f);
                    cd.RelativeColumn(1.6f);
                    for (var i = 0; i < 6; i++) cd.RelativeColumn();
                    cd.RelativeColumn(2.2f);
                });
                table.Header(h =>
                {
                    foreach (var t in new[] { "順位", "部署", "社員番号", "氏名", "普通残業", "深夜残業", "休日出勤", "時間外合計", "月平均", "最大月", "月別推移" })
                        HeaderCell(h.Cell(), t, t is "部署" or "社員番号" or "氏名" or "月別推移");
                });
                var max = data.TopOvertime.Max(e => e.Monthly.Values.Max(s => s.TotalOvertime));
                var rank = 0;
                foreach (var e in data.TopOvertime)
                {
                    rank++;
                    var bg = rank % 2 == 0 ? TotalBg : White;
                    var peak = e.Monthly.OrderByDescending(kv => kv.Value.TotalOvertime).First();
                    BodyCell(table.Cell(), $"{rank}", bg);
                    BodyCell(table.Cell(), e.Department, bg, left: true);
                    BodyCell(table.Cell(), e.Id, bg, left: true);
                    BodyCell(table.Cell(), e.Name, bg, left: true);
                    BodyCell(table.Cell(), H(e.Total.NormalOvertime), bg);
                    BodyCell(table.Cell(), H(e.Total.LateNightOvertime), bg);
                    BodyCell(table.Cell(), H(e.Total.HolidayWork), bg);
                    BodyCell(table.Cell(), H(e.Total.TotalOvertime), bg, bold: true);
                    BodyCell(table.Cell(), H(e.Total.TotalOvertime / Math.Max(1, Months.Count)), bg);
                    BodyCell(table.Cell(), $"{MonthLabel(peak.Key)} {H(peak.Value.TotalOvertime)}", bg);
                    Sparkbars(table.Cell().Background(bg).BorderBottom(0.5f).BorderColor(Rule).PaddingVertical(2).PaddingHorizontal(4), e, max);
                }
            });
            col.Item().Text($"月別推移：{MonthLabel(Months[0])}〜{MonthLabel(Months[^1])} の各月の時間外時間（棒の高さは本表内の最大月 {H(data.TopOvertime.Max(e => e.Monthly.Values.Max(s => s.TotalOvertime)))} を基準）。")
                .FontSize(7).FontColor(InkMuted);
        });
    }

    private void Sparkbars(IContainer c, Calc.EmployeeSummary e, int max)
    {
        const float height = 14;
        c.Height(height).Row(row =>
        {
            row.Spacing(1.5f);
            foreach (var m in Months)
            {
                var v = e.Monthly[m].TotalOvertime;
                var h = max == 0 ? 0 : (float)v / max * height;
                row.RelativeItem().AlignBottom().Height(Math.Max(h, v > 0 ? 1 : 0.4f)).Background(v > 0 ? Accent : Rule);
            }
        });
    }

    private void ComposeBottomAttendance(IContainer c)
    {
        c.Column(col =>
        {
            col.Spacing(6);
            col.Item().Text("期間累計の出勤日数（所定出勤日の出勤＋休日出勤）が少ない順。同数の場合は出勤率の低い順").FontSize(10).Bold();
            col.Item().Table(table =>
            {
                table.ColumnsDefinition(cd =>
                {
                    cd.ConstantColumn(28);
                    cd.RelativeColumn(1.6f);
                    cd.RelativeColumn(1.1f);
                    cd.RelativeColumn(1.6f);
                    for (var i = 0; i < 5; i++) cd.RelativeColumn();
                    cd.RelativeColumn(1.4f);
                    cd.RelativeColumn(1.4f);
                });
                table.Header(h =>
                {
                    foreach (var t in new[] { "順位", "部署", "社員番号", "氏名", "出勤日数", "うち休日出勤", "所定日数", "欠勤等日数", "出勤率", "入社日", "退職日" })
                        HeaderCell(h.Cell(), t, t is "部署" or "社員番号" or "氏名");
                });
                var rank = 0;
                foreach (var e in data.BottomAttendance)
                {
                    rank++;
                    var bg = rank % 2 == 0 ? TotalBg : White;
                    BodyCell(table.Cell(), $"{rank}", bg);
                    BodyCell(table.Cell(), e.Department, bg, left: true);
                    BodyCell(table.Cell(), e.Id, bg, left: true);
                    BodyCell(table.Cell(), e.Name, bg, left: true);
                    BodyCell(table.Cell(), $"{e.Total.AttendedDays}", bg, bold: true);
                    BodyCell(table.Cell(), $"{e.Total.HolidayWorkDays}", bg);
                    BodyCell(table.Cell(), $"{e.Total.ScheduledDays}", bg);
                    BodyCell(table.Cell(), $"{e.Total.ScheduledDays - e.Total.AttendedWorkdays}", bg);
                    BodyCell(table.Cell(), Pct(e.Total.AttendanceRate), bg);
                    BodyCell(table.Cell(), e.HireDate?.ToString("yyyy/MM/dd") ?? "", bg);
                    BodyCell(table.Cell(), e.RetireDate?.ToString("yyyy/MM/dd") ?? "", bg);
                }
            });
            col.Item().Text("欠勤等日数 ＝ 所定日数 − 所定出勤日の出勤日数（有給休暇・特別休暇等を含む）。入社日・退職日は settings.yaml の employees で設定した値です。")
                .FontSize(7).FontColor(InkMuted);
        });
    }

    // ------------------------------------------------------------ 未承認

    private void ComposeUnapproved(IContainer c)
    {
        if (data.Unapproved.Count == 0) { c.Text("未承認の勤怠はありません。").FontSize(10); return; }
        c.Column(col =>
        {
            col.Spacing(6);
            col.Item().Text($"承認欄が「{string.Join("・", config.Input.ApprovedValues)}」以外の勤怠（打刻のある日、または承認欄に値のある日）：{data.Unapproved.Count}名 / {data.Unapproved.Sum(u => u.Count)}件").FontSize(10).Bold();
            col.Item().Table(table =>
            {
                table.ColumnsDefinition(cd =>
                {
                    cd.RelativeColumn(1.6f);
                    cd.RelativeColumn(1.1f);
                    cd.RelativeColumn(1.6f);
                    cd.RelativeColumn(0.8f);
                    cd.RelativeColumn(8);
                });
                table.Header(h =>
                {
                    HeaderCell(h.Cell(), "部署", true);
                    HeaderCell(h.Cell(), "社員番号", true);
                    HeaderCell(h.Cell(), "氏名", true);
                    HeaderCell(h.Cell(), "件数");
                    HeaderCell(h.Cell(), "対象日", true);
                });
                var i = 0;
                foreach (var u in data.Unapproved)
                {
                    var bg = ++i % 2 == 0 ? TotalBg : White;
                    BodyCell(table.Cell(), u.Department, bg, left: true);
                    BodyCell(table.Cell(), u.EmployeeId, bg, left: true);
                    BodyCell(table.Cell(), u.Name, bg, left: true);
                    BodyCell(table.Cell(), $"{u.Count}", bg, bold: true);
                    BodyCell(table.Cell(), string.Join("、", u.Dates.Select(d => d.ToString("M/d"))), bg, left: true);
                }
            });
        });
    }

    // ------------------------------------------------------------ 部署別明細

    private void ComposeDepartmentDetail(IContainer c, DepartmentSummary dept)
    {
        c.Column(col =>
        {
            col.Spacing(6);
            var schedule = config.Schedule.ByDepartment.GetValueOrDefault(dept.Name) ?? config.Schedule.Default;
            col.Item().Text($"所定時間 {schedule.Start}〜{schedule.End}　在籍 {dept.Members.Count}名　時間外合計 {H(dept.Total.TotalOvertime)}　出勤率 {Pct(dept.Total.AttendanceRate)}").FontSize(10).Bold();
            col.Item().Table(table =>
            {
                table.ColumnsDefinition(cd =>
                {
                    cd.RelativeColumn(1.2f);
                    cd.RelativeColumn(1.7f);
                    foreach (var _ in Months) cd.RelativeColumn();
                    cd.RelativeColumn(1.1f);
                    cd.RelativeColumn();
                    cd.RelativeColumn();
                    cd.RelativeColumn(0.9f);
                    cd.RelativeColumn(0.9f);
                });
                table.Header(h =>
                {
                    HeaderCell(h.Cell(), "社員番号", true);
                    HeaderCell(h.Cell(), "氏名", true);
                    foreach (var m in Months) HeaderCell(h.Cell(), MonthLabel(m));
                    HeaderCell(h.Cell(), "時間外累計");
                    HeaderCell(h.Cell(), "うち深夜");
                    HeaderCell(h.Cell(), "うち休日");
                    HeaderCell(h.Cell(), "出勤日数");
                    HeaderCell(h.Cell(), "出勤率");
                });
                var i = 0;
                foreach (var m in dept.Members)
                {
                    var bg = ++i % 2 == 0 ? TotalBg : White;
                    BodyCell(table.Cell(), m.Id, bg, left: true);
                    BodyCell(table.Cell(), m.Name, bg, left: true);
                    foreach (var month in Months)
                    {
                        var ms = m.Monthly[month];
                        var text = ms.ScheduledDays == 0 && ms.AttendedDays == 0 ? "－" : H(ms.TotalOvertime); // 在籍期間外
                        BodyCell(table.Cell(), text, bg, muted: ms.TotalOvertime == 0);
                    }
                    BodyCell(table.Cell(), H(m.Total.TotalOvertime), bg, bold: true);
                    BodyCell(table.Cell(), H(m.Total.LateNightOvertime), bg);
                    BodyCell(table.Cell(), H(m.Total.HolidayWork), bg);
                    BodyCell(table.Cell(), $"{m.Total.AttendedDays}", bg);
                    BodyCell(table.Cell(), Pct(m.Total.AttendanceRate), bg);
                }
            });
            col.Item().Text("月別欄は各月の時間外時間（普通残業＋深夜残業＋休日出勤）。期間途中で異動した社員は最終所属部署に表示します（部署別の月次集計は各月の所属で計上）。")
                .FontSize(7).FontColor(InkMuted);
        });
    }

    // ------------------------------------------------------------ 共通部品

    private static void HeaderCell(IContainer cell, string text, bool left = false, bool center = false)
    {
        var c = cell.Background(HeaderBg).BorderBottom(0.75f).BorderColor(Rule).PaddingVertical(4).PaddingHorizontal(4);
        (center ? c.AlignCenter() : left ? c.AlignLeft() : c.AlignRight()).AlignMiddle().Text(text).FontSize(7.5f).Bold().FontColor(InkMuted);
    }

    private static void BodyCell(IContainer cell, string text, string bg, bool left = false, bool bold = false, bool muted = false)
    {
        var c = cell.Background(bg).BorderBottom(0.5f).BorderColor(Rule).PaddingVertical(3.5f).PaddingHorizontal(4);
        var t = (left ? c.AlignLeft() : c.AlignRight()).AlignMiddle().Text(text);
        if (bold) t.Bold();
        if (muted) t.FontColor(InkMuted);
    }

    private static string H(int minutes) => TimeParser.FormatHours(minutes);
    private static int PerHead(int value, int headcount) => headcount == 0 ? 0 : (int)Math.Round((double)value / headcount);
    private static string Pct(double? rate) => rate is null ? "－" : $"{rate.Value * 100:0.0}%";
    private static string MonthLabel(DateTime m) => $"{m.Month}月";
}
