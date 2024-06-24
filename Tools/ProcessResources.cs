using ArknightsWorkshop.Tools.Processors;
using AssetsTools.NET.Extra;

namespace ArknightsWorkshop.Tools;

public class ProcessResources : Tool
{
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
        resFolder = Path.Combine(config.WorkingDirectory, Paths.Assets);
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
        System.Diagnostics.Process.GetCurrentProcess().PriorityClass = System.Diagnostics.ProcessPriorityClass.High;
#endif
        var threads = Enumerable
            .Range(0, config.MaxProcessingThreads)
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
        System.Diagnostics.Process.GetCurrentProcess().PriorityClass = System.Diagnostics.ProcessPriorityClass.Normal;
#endif
        foreach (var proc in processors) proc.Cleanup();
        return ValueTask.CompletedTask;
    }

    private bool SelectVersion()
    {
        version.Value = ConsoleUI.SelectVersion(config.WorkingDirectory)!;
        if (version.Value is null) return false;
        datFolder = Path.Combine(config.WorkingDirectory, Paths.Assets, version.Value);
        resFolder = Path.Combine(datFolder, Paths.Resources);
        files = Util.GetFileTree(resFolder)
            .Select(f => Path.GetRelativePath(resFolder, f))
            .ToArray();

        return true;
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
                ConsoleUI.WriteLineColor(ConsoleColor.Red, "There are invalid processor keys.");
                return false;
            }
        }
#if RELEASE
        if(processors.Length == 0)
        {
            ConsoleUI.WriteLineColor(ConsoleColor.Red, "No processors were selected.");
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
