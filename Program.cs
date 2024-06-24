using ArknightsWorkshop;
using ArknightsWorkshop.Tools;
using System.Diagnostics;

#if DEBUG
Console.Clear();
Console.ResetColor();
#endif
var config = Config.Read();
Tool[] tools = [
    new DownloadResources(config),
    new ProcessResources(config),
    new GenerateSummaries(config)
];

int index = 0;
if(CLIArgs.ActionIndex is not null)
{
    if(!int.TryParse(CLIArgs.ActionIndex, out index) || index <= 0 || index > tools.Length)
    {
        ConsoleUI.WriteLineColor(ConsoleColor.DarkRed, $"Invalid action index: {CLIArgs.ActionIndex}");
        return;
    }
    --index;
}
else
    index = ConsoleUI.ChooseOne("Actions", tools.Select(t => t.Name));
var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    cts.Cancel();
    e.Cancel = true;
};
var start = Stopwatch.GetTimestamp();
await tools[index].Run(cts.Token);
var time = Stopwatch.GetElapsedTime(start);
Console.Write($"{(cts.IsCancellationRequested ? "Canceled" : "Done")} after {time}.");
if (CLIArgs.HasKey("autoexit"))
    Console.WriteLine();
else
{
    Console.WriteLine(" Press any key to exit.");
    _ = Console.ReadKey(true);
}