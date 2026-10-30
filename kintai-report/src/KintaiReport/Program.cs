using System.Globalization;
using System.Text;
using KintaiReport.Calc;
using KintaiReport.Config;
using KintaiReport.Excel;
using KintaiReport.Pdf;
using QuestPDF.Drawing;
using QuestPDF.Infrastructure;

namespace KintaiReport;

public static class Program
{
    private const string Usage = """
        使い方:
          KintaiReport <勤怠Excel.xlsx> [オプション]

        オプション:
          -c, --config <path>   設定ファイル（既定: 実行ファイルと同じフォルダの settings.yaml）
          -o, --output <path>   出力PDF（既定: Excelと同じフォルダに「勤怠分析レポート_yyyyMM.pdf」）
          -m, --month <yyyy-MM> 累計の最終月（既定: データ内の最終月）。年度開始月から この月までを集計
          -h, --help            このヘルプを表示

        Excel ファイルを KintaiReport.exe にドラッグ＆ドロップしても実行できます。
        """;

    public static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        var interactive = args.Length == 1 && !args[0].StartsWith('-'); // ドラッグ＆ドロップ起動
        try
        {
            return Run(args);
        }
        catch (UserException ex)
        {
            Console.Error.WriteLine($"エラー: {ex.Message}");
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"予期しないエラーが発生しました: {ex}");
            return 2;
        }
        finally
        {
            if (interactive && !Console.IsInputRedirected)
            {
                Console.WriteLine("Enter キーで終了します。");
                Console.ReadLine();
            }
        }
    }

    private static int Run(string[] args)
    {
        string? input = null, configPath = null, output = null;
        DateTime? month = null;
        for (var i = 0; i < args.Length; i++)
        {
            string Next() => i + 1 < args.Length ? args[++i] : throw new UserException($"{args[i]} の値がありません。");
            switch (args[i])
            {
                case "-h" or "--help" or "/?":
                    Console.WriteLine(Usage);
                    return 0;
                case "-c" or "--config": configPath = Next(); break;
                case "-o" or "--output": output = Next(); break;
                case "-m" or "--month":
                    var v = Next();
                    if (!DateTime.TryParseExact(v, ["yyyy-MM", "yyyy/MM", "yyyyMM", "yyyy-M", "yyyy/M"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var m))
                        throw new UserException($"--month は yyyy-MM 形式で指定してください: {v}");
                    month = m;
                    break;
                default:
                    if (args[i].StartsWith('-')) throw new UserException($"不明なオプションです: {args[i]}\n\n{Usage}");
                    input = args[i];
                    break;
            }
        }
        if (input == null)
        {
            Console.WriteLine(Usage);
            return 1;
        }

        configPath ??= Path.Combine(AppContext.BaseDirectory, "settings.yaml");
        var config = ConfigLoader.Load(configPath);
        Console.WriteLine($"設定ファイル: {Path.GetFullPath(configPath)}");

        var reader = new AttendanceReader(config.Input);
        var records = reader.Read(input);
        Console.WriteLine($"読み込み: {records.Count:N0}行（シート「{config.Input.SheetName}」）");

        var aggregator = new ReportAggregator(config);
        var data = aggregator.Aggregate(records, month);
        Console.WriteLine($"対象期間: {data.PeriodStart:yyyy/MM/dd}〜{data.PeriodEnd:yyyy/MM/dd}　部署 {data.Departments.Count}　社員 {data.Employees.Count}名");

        var warnings = reader.Warnings.Concat(aggregator.Warnings).ToList();
        if (warnings.Count > 0)
        {
            Console.WriteLine($"注意 {warnings.Count}件:");
            foreach (var w in warnings.Take(30)) Console.WriteLine($"  - {w}");
            if (warnings.Count > 30) Console.WriteLine($"  …ほか {warnings.Count - 30}件");
        }

        output ??= Path.Combine(Path.GetDirectoryName(Path.GetFullPath(input))!, $"勤怠分析レポート_{data.Months[^1]:yyyyMM}.pdf");
        var families = SetupFonts(config.Report.Font, Path.GetDirectoryName(Path.GetFullPath(configPath))!);
        new ReportDocument(data, config, families, DateTime.Now).GeneratePdf(output);
        Console.WriteLine($"出力しました: {Path.GetFullPath(output)}");
        return 0;
    }

    private static string[] SetupFonts(FontConfig font, string configDir)
    {
        // QuestPDF Community ライセンス（年間売上 100万USD未満の組織は無償。README 参照）
        QuestPDF.Settings.License = LicenseType.Community;

        var families = font.Families.Where(f => !string.IsNullOrWhiteSpace(f)).ToList();
        if (!string.IsNullOrWhiteSpace(font.File))
        {
            var path = Path.IsPathRooted(font.File) ? font.File : Path.Combine(configDir, font.File);
            if (!File.Exists(path)) throw new UserException($"report.font.file のフォントが見つかりません: {path}");
            using var stream = File.OpenRead(path);
            FontManager.RegisterFontWithCustomName("KintaiReportFont", stream);
            families.Insert(0, "KintaiReportFont");
        }
        return families.ToArray();
    }
}
