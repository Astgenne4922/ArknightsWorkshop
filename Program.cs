using ArknightsWorkshop;
using ArknightsWorkshop.Tools;

var config = Config.Read();
Tool[] tools = [
    new DownloadResources(config)
    // more tools here...
];

Console.WriteLine("Actions:");
for (var i = 0; i < tools.Length; i++)
    Console.WriteLine($"{i+1}) {tools[i].Name}");
Console.Write("Select action: ");

var rawIndex = Console.ReadLine();
if (!int.TryParse(rawIndex, out int index) || index <= 0 || index > tools.Length)
    Console.WriteLine($"'{rawIndex}' is not a valid index");
else
{
    var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) =>
    {
        cts.Cancel();
        e.Cancel = true;
    };
    await tools[index - 1].Run(cts.Token);
    Console.WriteLine($"{(cts.IsCancellationRequested ? "Canceled" : "Done")}. Press any key to exit.");
    _ = Console.ReadKey(true);
}
