using AssetsTools.NET;
using AssetsTools.NET.Extra;
using Fmod5Sharp;

namespace ArknightsWorkshop.Tools.Processors;

public class SoundProcessor : IResourceProcessor
{
    public string Key => "sound";
    public string Description => "Sounds ('AudioClip', in '.ogg' format)";

    public void Cleanup() { }
    public void Initialize() { }

    public object? EnterBundle(AssetsManager _, BundleFileInstance __) => null;
    public void ExitBundle(AssetsManager _, BundleFileInstance __, object? ___) { }

    public void Process(AssetsManager manager, BundleFileInstance bundle, AssetsFileInstance assetFile, AssetFileInfo asset, string folder, string abPath, object? _)
    {
        if (asset.TypeId != (int)UnityType.AudioClip) return;
        var field = manager.GetBaseField(assetFile, asset);
        var src = field["m_Resource"]["m_Source"].AsString;
        var ind = bundle.file.GetFileIndex(src.Substring(src.LastIndexOf('/') + 1));
        bundle.file.GetFileRange(ind, out var ofs, out var len);
        var resource = new SegmentStream(bundle.DataStream, ofs, len);
        resource.Position = field["m_Resource"]["m_Offset"].AsLong;
        var buff = new byte[field["m_Resource"]["m_Size"].AsLong];
        Util.ReadExactlyOldStream(resource, buff);
        if(!FsbLoader.TryLoadFsbFromByteArray(buff, out var bank))
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"Warning: couldn't load '{field["m_Name"].AsString}' in '{abPath}' as an FSB bank");
            Console.ResetColor();
            return;
        }
        for (int i = 0; i < bank!.Samples.Count; i++)
        {
            bank.Samples[i].RebuildAsStandardFileFormat(out var data, out var ext);
            var filename = field["m_Name"].AsString + ((bank!.Samples.Count == 1) ? "" : $"sample_{i}");
            var path = Path.Combine(folder, Folders.Processed, Path.ChangeExtension(abPath, null), filename);
            path = Path.ChangeExtension(path, ext);
            Util.TouchFile(path);
            File.WriteAllBytes(path, data!);
        }
    }
}
