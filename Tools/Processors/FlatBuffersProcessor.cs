using AssetsTools.NET;
using AssetsTools.NET.Extra;
using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using static System.Runtime.InteropServices.RuntimeInformation;

namespace ArknightsWorkshop.Tools.Processors;

public class FlatBuffersProcessor(Config config, Util.Ref<string> version) : IResourceProcessor
{
    public string Key => "flatc";
    public string Description => "FlatBuffers (character table, module table, etc)";

    private string flatcPath;
    private GitHubClient github = new();
    private string fbsFolder;
    private List<(string Name, string Path)> fbsFiles = [];

    public void Cleanup() => github.Dispose();

    public void Initialize()
    {
        if (!CheckFlatc()) return;
        var branch = version.Value.StartsWith("GL") ? "YoStar" : "main";
        using var jsonTree = github.FileTree("MooncellWiki", "OpenArknightsFBS", branch);
        fbsFolder = Path.Combine(config.WorkingDirectory, Folders.FBS, branch);
        Directory.CreateDirectory(fbsFolder);

        foreach (var file in jsonTree.RootElement.GetProperty("tree").EnumerateArray())
        {
            var path = file.GetProperty("path").GetString();
            if (path is null || !path.EndsWith(".fbs")) continue;
            var sha = Convert.FromHexString(file.GetProperty("sha").GetString()!);

            var fbsPath = Path.Combine(fbsFolder, path);
            fbsFiles.Add((Path.GetFileNameWithoutExtension(fbsPath), fbsPath));

            var shaPath = fbsPath + "._sha1_hash";
            if (File.Exists(fbsPath) && File.ReadAllBytes(shaPath).AsSpan().SequenceEqual(sha)) continue;
            Util.TouchFile(shaPath);
            File.WriteAllBytes(shaPath, sha);

            using var fbsStream = File.Create(fbsPath);
            github.RawFile("MooncellWiki", "OpenArknightsFBS", branch, path, fbsStream);
        }
    }

    public object? EnterBundle(AssetsManager _, BundleFileInstance __) => null;
    public void ExitBundle(AssetsManager _, BundleFileInstance __, object? ___) { }

    public void Process(AssetsManager manager, BundleFileInstance bundle, AssetsFileInstance assetFile, AssetFileInfo asset, string folder, string abPath, object? _)
    {
        if (flatcPath is null) return;
        if (asset.TypeId != (int)UnityType.TextAsset) return;
        var field = manager.GetBaseField(assetFile, asset);
        var name = field["m_Name"].AsString;

        // some eurisic to find appropriate 'fbs' file
        var onlyOne = false;
        var fbsPath = "";
        foreach(var pair in fbsFiles)
        {
            if (!name.StartsWith(pair.Name)) continue;
            if (name.Length - pair.Name.Length is not (0 or 6)) continue;

            if (fbsPath == "")
            {
                onlyOne = true;
                fbsPath = pair.Path;
            }
            else onlyOne = false;
        }
        if (!onlyOne) return;

        var data = field["m_Script"].AsByteArray;

        var tmpPath = Path.Combine(Path.GetTempPath(), $"fbs_bin_{Stopwatch.GetTimestamp()}", $"{name}.bin");
        Util.TouchFile(tmpPath);
        using (var file = File.Create(tmpPath))
            file.Write(data.AsSpan(128));

        var destDir = Path.Combine(folder, Folders.Processed, Path.ChangeExtension(abPath, null)) + Path.DirectorySeparatorChar;
        Directory.CreateDirectory(destDir);
        var proc = new Process()
        {
            StartInfo = new(flatcPath, ["--raw-binary", "--strict-json", "-o", destDir, "-t", fbsPath, "--", tmpPath])
            {
                CreateNoWindow = true,
                RedirectStandardOutput = true,
            },
        };
        proc.Start();
        proc.WaitForExit();
        if (proc.ExitCode != 0)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"Warning: couldn't decode '{name}' in '{abPath}' as an FlatBuffer");
            Console.ResetColor();
        }
        Directory.Delete(Path.GetDirectoryName(tmpPath)!, true);
    }

    private bool CheckFlatc()
    {
        var flatc = new DirectoryInfo(config.WorkingDirectory)
            .EnumerateFiles()
            .SingleOrDefault(f => Path.ChangeExtension(f.Name, null) == "flatc");
        if (flatc is not null)
        {
            flatcPath = flatc.FullName;
            return true;
        }

        using var json = github.Releases("google", "flatbuffers");
        var links = new List<(string Name, string Url)>();
        foreach (var item in json.RootElement[0].GetProperty("assets").EnumerateArray())
            links.Add((
                item.GetProperty("name").GetString()!,
                item.GetProperty("browser_download_url").GetString()!
            ));


        string? link = null;
        if (IsOSPlatform(OSPlatform.OSX))
            link = links.Single(i => i.Name.Contains("Mac") && !i.Name.Contains("Intel")).Url;
        else if (OSArchitecture == Architecture.X64 && IsOSPlatform(OSPlatform.Windows))
            link = links.Single(i => i.Name.Contains("Windows")).Url;
        else if (OSArchitecture == Architecture.X64 && IsOSPlatform(OSPlatform.Linux))
            link = links.Single(i => i.Name.Contains("Linux") && i.Name.Contains("g++")).Url;
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("""
                There is no official 'flatc' build for your machine. Download it into resource 
                directory of this app. Name (without extension) should be exactly 'flatc'.
                """);
            Console.ResetColor();
            return false;
        }

        using var flatcReq = github.Http.Send(new(HttpMethod.Get, link), HttpCompletionOption.ResponseContentRead);
        ZipFile.ExtractToDirectory(flatcReq.Content.ReadAsStream(), config.WorkingDirectory);
        return true;
    }
}