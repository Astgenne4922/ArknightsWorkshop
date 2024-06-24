using System.Net.Http.Json;
using KiDev.StreamCopy;

namespace ArknightsWorkshop.Tools;

public class DownloadResources(Config config) : Tool
{
    private static readonly string ApkFileName = "game.apk";
    private static readonly string ChinaApkUrl = "https://ak.hypergryph.com/downloads/android_lastest";
    private static readonly string ApkAssetsPrefix = "assets/AB/Android/";

    public override string Name => "Download and unzip all resources";

    private string folder = Path.Combine(config.WorkingDirectory, Paths.Assets);
    private bool useGlobalServer, simplerProgress;
    private PackInfoData[] processes = null!;
    private bool finishedDownloading = false;
    private Semaphore httpLimit = new(config.MaxConcurrentDownloads, config.MaxConcurrentDownloads);
    private CancellationToken cancel;

    private bool UserInput()
    {
        var serv = CLIArgs.ParamRaw("server");
        if(serv is not null)
        {
            if (serv == "Global")
                useGlobalServer = true;
            else if (serv == "China")
                useGlobalServer = false;
            else
            {
                ConsoleUI.WriteLineColor(ConsoleColor.Red, $"Invalid server: {serv}");
                return false;
            }
        }
        else
            useGlobalServer = ConsoleUI.ChooseOne("Select server", ["Global", "China"]) == 0;
        simplerProgress = CLIArgs.HasKey("simple_progress");
        return true;
    }

    public override async ValueTask Run(CancellationToken cancel)
    {
        this.cancel = cancel;
        if (!UserInput()) return;
        if(cancel.IsCancellationRequested) return;

        // Fetch game info, create directories for downloaded assets
        var info = await GameServerInfo.Fetch(useGlobalServer ? GameServerInfo.Global : GameServerInfo.China);
        Console.WriteLine($"Current version: {info.ResourceVersion}");
        folder = Path.Combine(folder, $"{(useGlobalServer ? "GL" : "CN")}_{info.ResourceVersion}");
        Directory.CreateDirectory(Path.Combine(folder, Paths.RawResources));
        Directory.CreateDirectory(Path.Combine(folder, Paths.Resources));
        if (cancel.IsCancellationRequested) return;

        // Fetch available packs
        using var http = Util.MakeAkClient();
        var url = $"{info.AssetsUrl}/hot_update_list.json";
        var list = (await http.GetFromJsonAsync(url, SourceGenerationContext.Default.HotUpdateList, CancellationToken.None)).NotNullJson();
        Console.WriteLine($"Found {list.PackInfos.Length} packs");
        if (cancel.IsCancellationRequested) return;

        // Start downloading them
        processes = new PackInfoData[list.PackInfos.Length];
        for (var i = 0; i < processes.Length; i++)
        {
            var pos = new DownloadProgressStorage(Path.Combine(folder, Paths.DownloadProgress, list.PackInfos[i].Name));
            processes[i] = new(list.PackInfos[i].Name, list.PackInfos[i].TotalSize, pos);
            var ii = i;
            processes[i].Process = Task.Run(() => DownloadDat(processes[ii], info.AssetsUrl), CancellationToken.None);
        }

        // Print status continuously until all packs are downloaded
        var console = Task.Run(PrintStatus, CancellationToken.None);
        foreach (var d in processes) await d.Process;
        finishedDownloading = true;
        await console;
        if(cancel.IsCancellationRequested) return;

        FetchApk();

        if (cancel.IsCancellationRequested) return;
        Util.TouchFile(Path.Combine(folder, Paths.DownloadFinishTag));
        Directory.Delete(Path.Combine(folder, Paths.DownloadProgress), true);
        if (!config.KeepIntermediateData)
            Directory.Delete(Path.Combine(folder, Paths.RawResources));
    }

    private void FetchApk()
    {
        var apkPath = Path.Combine(folder, Paths.RawResources, ApkFileName);
        DownloadProgressStorage process = null!;
        if (useGlobalServer)
        {
            ConsoleUI.WriteLineColor(ConsoleColor.Red, "Downloading global server's APK is not implemented yet.");
            return;
        }
        else
        {
            Util.TouchFile(apkPath);
            using var http = Util.MakeAkClient();
            process = new(Path.Combine(folder, Paths.DownloadProgress, ApkFileName));
            if(!process.Done)
            {
                Task.Run(() =>
                {
                    static HttpRequestMessage request() => new(HttpMethod.Get, ChinaApkUrl);
                    return HttpUtils.Download(http, request, Util.WriteSeeker(apkPath), process, null, cancel);
                });
                while (!process.Done && !cancel.IsCancellationRequested)
                {
                    Console.Write("[apk file] ");
                    Util.Dump(process);
                    Console.WriteLine("       ");
                    Console.CursorTop--;
                }
            }
        }
        if (cancel.IsCancellationRequested) return;

        Console.Write("[apk file] Unpacking... ");
        if(!process.Unpacked)
        {
            Util.UnpackZip(apkPath, Path.Combine(folder, Paths.Resources), ApkAssetsPrefix, cancel);
            if (cancel.IsCancellationRequested) return;
            process.Unpacked = true;
        }
        Console.WriteLine("Done.");

        if (!config.KeepIntermediateData) File.Delete(apkPath);
    }

    private void PrintStatus()
    {
        Console.CursorVisible = false;
        bool first = true;
        while (!finishedDownloading)
        {
            // Go to beginning to overwrite old status (only if we already printed it)
            if (first)
                first = false;
            else
            {
                Console.CursorLeft = 0;
                Console.CursorTop -= (simplerProgress ? 0 : processes.Length) + 1;
            }

            // Print all pack's info while accumulating total/downloaded size
            long totalCur = 0, totalTotal = 0;
            for (int i = 0; i < processes.Length; i++)
            {
                totalCur += processes[i].Downloading.Position;
                totalTotal += processes[i].Downloading.Length;
                if (simplerProgress) continue;

                // Name and status
                Console.Write($"[{processes[i].Name}] ");
                if (processes[i].Downloading.Unpacked)
                {
                    Console.ForegroundColor = ConsoleColor.DarkGreen;
                    Console.Write("Done.");
                }
                else if (processes[i].Downloading.Done)
                {
                    Console.ForegroundColor = ConsoleColor.DarkBlue;
                    Console.Write("Downloaded. Unpacking...");
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.DarkYellow;
                    Util.Dump(processes[i].Downloading);
                }
                Console.ResetColor();

                // Print padding
                var pad = Math.Max(0, processes[i].StatusStringLength - Console.CursorLeft);
                processes[i].StatusStringLength = Math.Max(processes[i].StatusStringLength, Console.CursorLeft);
                Console.WriteLine(new string(' ', pad));
            }
            Console.WriteLine($"Total progress: {Util.SizeString(totalCur)}/{Util.SizeString(totalTotal)} ({totalCur * 100 / totalTotal}%)   ");
        }
        Console.CursorVisible = true;
    }

    private void DownloadDat(PackInfoData data, string assetsUrl)
    {
        var datPath = Path.Combine(folder, Paths.RawResources, $"{data.Name}.dat");
        // Download pack from servers
        if(!data.Downloading.Done)
        {
            httpLimit.WaitOne();
            if (!cancel.IsCancellationRequested)
            {
                using var http = Util.MakeAkClient();
                Util.TouchFile(datPath);
                HttpRequestMessage request() => new(HttpMethod.Get, $"{assetsUrl}/{data.Name}.dat");
                HttpUtils.Download(http, request, Util.WriteSeeker(datPath), data.Downloading, null, cancel);
            }
            httpLimit.Release();
        }
        if (cancel.IsCancellationRequested) return;
        // Decompress these packs. They're compressed as .zip
        if (!data.Downloading.Unpacked)
        {
            Util.UnpackZip(datPath, Path.Combine(folder, Paths.Resources), "", cancel);
            if (cancel.IsCancellationRequested) return;
            if (!config.KeepIntermediateData) File.Delete(datPath);
            data.Downloading.Unpacked = true;
        }
    }
}

public class DownloadProgressStorage(string path) : FilePositionStorage(path)
{
    private bool _unpacked;
    public bool Unpacked 
    {
        get => _unpacked; 
        set
        {
            _unpacked = value;
            WriteFile();
        }
    }

    override protected void ReadFile()
    {
        var tokens = File.ReadAllText(_path).Split('|');
        _unpacked = tokens[0] == "1";
        _done = tokens[1] == "1";
        _position = long.Parse(tokens[2]);
        _length = long.Parse(tokens[3]);
    }

    override protected void WriteFile() =>
        File.WriteAllText(_path, $"{(_unpacked ? 1 : 0)}|{(_done ? 1 : 0)}|{_position}|{_length}");

}

public class PackInfoData
{
    public PackInfoData(string name, long size, DownloadProgressStorage position)
    {
        Name = name;
        StatusStringLength = 0;
        Downloading = position;
        Downloading.Length = size;
    }

    public string Name;
    public DownloadProgressStorage Downloading;
    public Task Process = null!;
    public int StatusStringLength;
}
