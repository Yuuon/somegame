using SomeGame.Core;
using SomeGame.Core.Balance;

var battleSeeds = int.TryParse(Environment.GetCommandLineArgs().Skip(1).FirstOrDefault(), out var bs) ? bs : 3000;
var stressSeeds = int.TryParse(Environment.GetCommandLineArgs().Skip(2).FirstOrDefault(), out var ss) ? ss : 12;
var outPath = Environment.GetCommandLineArgs().Skip(3).FirstOrDefault() ?? Path.Combine("docs", "balance-report.md");

var cfg = GameConfig.Default();
var report = Report.ProduceMarkdown(cfg, battleSeeds, stressSeeds);

Console.WriteLine(report);

Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
File.WriteAllText(outPath, report, new System.Text.UTF8Encoding(false));
Console.WriteLine($"\n已写入: {Path.GetFullPath(outPath)}");