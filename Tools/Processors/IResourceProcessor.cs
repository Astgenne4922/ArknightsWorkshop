using AssetsTools.NET;
using AssetsTools.NET.Extra;

namespace ArknightsWorkshop.Tools.Processors;

public interface IResourceProcessor
{
    public string Key { get; }
    public string Description { get; }
    public void Initialize();
    public void Cleanup();
    public object? EnterBundle(AssetsManager manager, BundleFileInstance bundle);
    void Process(AssetsManager manager, BundleFileInstance bundle, AssetsFileInstance assetFile, AssetFileInfo asset, string folder, string abPath, object? bundleData);
    public void ExitBundle(AssetsManager manager, BundleFileInstance bundle, object? bundleData);
}
