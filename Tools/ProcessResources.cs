using ArknightsWorkshop.Tools.Processors;
using AssetsTools.NET.Extra;
using System.Diagnostics;

namespace ArknightsWorkshop.Tools;

public class ProcessResources : Tool
{
    private static readonly int ThreadCount =
#if DEBUG && false
        1;
#else
        Environment.ProcessorCount;
#endif

    public override string Name => "Process resources";

    // Need to propagate resource version into 'flatc' processor to select appropriate 'fbs' files
    private Util.Ref<string> version = new();

    private Config config;
    private string resFolder;
    private string datFolder = null!;
    private IResourceProcessor[] allProcessors;

    private string[] files = null!;
    private IResourceProcessor[] processors = null!;

    public ProcessResources(Config config)
    {
        this.config = config;
        resFolder = Path.Combine(config.WorkingDirectory, Folders.Assets);
        allProcessors = [
            new SoundProcessor(),
            new FlatBuffersProcessor(config, version),
            new TextureProcessor()
        ];
    }

    public override ValueTask Run(CancellationToken cancel)
    {
        if (!SelectVersion()) return ValueTask.CompletedTask;
        if (!SelectProcessors()) return ValueTask.CompletedTask;

        foreach (var proc in processors) proc.Initialize();
#if RELEASE
        Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.High;
#endif
        var threads = Enumerable
            .Range(0, ThreadCount)
            .Select(_ => new Thread(() => ProcessThread(cancel)))
            .ToArray();
        foreach (var thread in threads)
        {
#if RELEASE
            thread.Priority = ThreadPriority.Highest;
#endif
            thread.Start();
        }
        foreach (var thread in threads) thread.Join();
#if RELEASE
        Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.Normal;
#endif
        foreach (var proc in processors) proc.Cleanup();
        return ValueTask.CompletedTask;
    }

    private bool SelectVersion()
    {
        static void NoResError()
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("No resources downloaded.");
            Console.ResetColor();
        }

        if (!Directory.Exists(resFolder))
        {
            NoResError();
            return false;
        }
        var dirs = new DirectoryInfo(resFolder).GetDirectories();
        if (dirs.Length == 0)
        {
            NoResError();
            return false;
        }

        var resNamePrefix = CLIArgs.ParamRaw("res");
        if(resNamePrefix is null)
        { 
            var ind = ConsoleUI.ChooseOne("Select resource version", dirs.Select(d => d.Name));
            version.Value = dirs[ind].Name;
            datFolder = dirs[ind].FullName;
        }
        else
        {
            var cands = dirs.Where(d => d.Name.StartsWith(resNamePrefix)).ToArray();
            if(cands.Length != 1)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine(cands.Length switch
                {
                    0 => $"No resource version begin with '{resNamePrefix}'",
                    _ => $"Multiple resource versions begin with '{resNamePrefix}'"
                });
                Console.ResetColor();
                return false;
            }
            version.Value = cands[0].Name;
            datFolder = cands[0].FullName;
        }
        resFolder = Path.Combine(datFolder, Folders.Resources);
        files = GetFiles(new DirectoryInfo(resFolder))
            .Select(f => Path.GetRelativePath(resFolder, f.FullName))
            .ToArray();

        return true;

        static IEnumerable<FileInfo> GetFiles(FileSystemInfo fs) => fs switch
        {
            FileInfo fi => [fi],
            DirectoryInfo di => di.EnumerateFileSystemInfos().SelectMany(GetFiles),
            _ => throw new("Neither file nor folder?!"),
        };
    }

    private bool SelectProcessors()
    {
        var procs = CLIArgs.ParamList("process");
        if(procs is null)
        {
            var sel = ConsoleUI.ChooseMultiple("Select processors", allProcessors.Select(f => f.Description));
            processors = sel.Zip(allProcessors).Where(p => p.First).Select(p => p.Second).ToArray();
        }
        else
        {
            processors = procs.Select(k => allProcessors.SingleOrDefault(p => p.Key == k)!).ToArray();
            if(processors.Any(p => p is null))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("There are invalid processor keys.");
                Console.ResetColor();
                return false;
            }
        }
#if RELEASE
        if(processors.Length == 0)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("No processors were selected.");
            Console.ResetColor();
            return false;
        }
#endif
        return true;
    }

    private int _currentIndex = -1;
    private void ProcessThread(CancellationToken cancel)
    {
        var manager = new AssetsManager();
        var datas = new object?[processors.Length];
        while (true)
        {
            if (cancel.IsCancellationRequested) break;
            var i = Interlocked.Increment(ref _currentIndex);
            if (i >= files.Length) break;

            var bundle = manager.LoadBundleFile(Path.Combine(resFolder, files[i]));

            for (int pi = 0; pi < processors.Length; pi++)
                datas[pi] = processors[pi].EnterBundle(manager, bundle);
            GC.Collect(0); // collect freed processors caches

            foreach (var assetFileName in bundle.file.GetAllFileNames())
            {
                if (cancel.IsCancellationRequested) break;

                if (assetFileName.EndsWith(".resS")) continue;
                if (assetFileName.EndsWith(".resource")) continue;
                var assetFile = manager.LoadAssetsFileFromBundle(bundle, assetFileName);

                foreach (var asset in assetFile.file.AssetInfos)
                {
                    if (cancel.IsCancellationRequested) break;
                    for (int pi = 0; pi < processors.Length; pi++)
                        processors[pi].Process(manager, bundle, assetFile, asset, datFolder, files[i], datas[pi]);
                }

                manager.UnloadAssetsFile(assetFile);
            }

            for (int pi = 0; pi < processors.Length; pi++)
                processors[pi].ExitBundle(manager, bundle, datas[pi]);

            manager.UnloadBundleFile(bundle);
        }
    }
}
