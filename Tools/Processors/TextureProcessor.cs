using AssetsTools.NET;
using AssetsTools.NET.Extra;
using AssetsTools.NET.Texture;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace ArknightsWorkshop.Tools.Processors;

public class TextureProcessor : IResourceProcessor
{
    public string Key => "image";
    public string Description => "Textures (all 'Sprite's, 'Texture2D's that are not used in any 'Sprite's)";

    public void Initialize() {}
    public void Cleanup() {}

    public object? EnterBundle(AssetsManager manager, BundleFileInstance bundle) => new Dictionary<long, CachedTexture>();

    public void Process(AssetsManager manager, BundleFileInstance bundle, AssetsFileInstance assetFile, AssetFileInfo asset, string folder, string abPath, object? data)
    {
        if (asset.TypeId == (int)UnityType.Texture)
        {
            var lp = (Dictionary<long, CachedTexture>)data!;
            if (lp.ContainsKey(asset.TypeId)) return;
            lp[asset.PathId] = Load(asset.PathId);
            return;
        }
        if (asset.TypeId != (int)UnityType.Sprite) return;
        var dict = (Dictionary<long, CachedTexture>)data!;

        var field = manager.GetBaseField(assetFile, asset);
        var color = Resolve(field["m_RD"]["texture"]["m_PathID"].AsLong).Image ?? throw new("No texture in sprite?!");
        var alpha = Resolve(field["m_RD"]["alphaTexture"]["m_PathID"].AsLong).Image;

        if(field["m_RD"]["downscaleMultiplier"].AsFloat != 1f)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"Rescaling is not implemented yet: '{field["m_Name"].AsString}' in '{abPath}'");
            Console.ResetColor();
            return;
        }

        var rect = field["m_RD"]["textureRect"];
        var rx = IRound(rect["x"].AsFloat);
        var ry = IRound(rect["y"].AsFloat);
        var rw = IRound(rect["width"].AsFloat);
        var rh = IRound(rect["height"].AsFloat);
        var result = new Image<Bgra32>(rw, rh);

        ProcessMultiRow(color, alpha, result, (col, alp, res, hasAlpha) =>
        {
            for (var y = 0; y < res.Height; y++)
            {
                var my = ry + y;
                var colS = col.GetRowSpan(my).Slice(rx, rw);
                var resS = res.GetRowSpan(y);
                if (hasAlpha)
                {
                    var alpS = alp.GetRowSpan(my).Slice(rx, rw);
                    for (int x = 0; x < res.Width; x++)
                        resS[x] = new(colS[x].R, colS[x].G, colS[x].B, alpS[x].R);
                }
                else
                {
                    for (int x = 0; x < res.Width; x++)
                        resS[x] = new(colS[x].R, colS[x].G, colS[x].B, 255);
                }
            }
        });

        var settings = new SettingsRaw(field["m_RD"]["settingsRaw"].AsUInt);
        result.Mutate(o => o.RotateFlip(
            settings.Rotation switch
            {
                SettingsRaw.Rotations.Rotate180 => RotateMode.Rotate180,
                SettingsRaw.Rotations.Rotate90 => RotateMode.Rotate90,
                _ => RotateMode.None
            },
            settings.Rotation switch // don't forget vertical flip from texture asset
            {
                SettingsRaw.Rotations.FlipHorizontal => FlipMode.Horizontal | FlipMode.Vertical,
                SettingsRaw.Rotations.FlipVertical => FlipMode.None, // vert + vert
                _ => FlipMode.Vertical
            }));

        var path = Path.Combine(folder, Folders.Processed, Path.ChangeExtension(abPath, null), $"{field["m_Name"].AsString}.png");
        Util.TouchFile(path);
        result.SaveAsPng(path);

        CachedTexture Resolve(long pathId)
        {
            if (!dict.TryGetValue(pathId, out var cache))
                dict[pathId] = cache = Load(pathId);
            cache.Used = true;
            return cache;
        }

        CachedTexture Load(long pathid)
        {
            if(pathid == 0)
                return new(folder, abPath, null, "");

            var field = manager.GetBaseField(assetFile, pathid);
            var texture = TextureFile.ReadTextureFile(field);
            var (w, h) = (field["m_Width"].AsInt, field["m_Height"].AsInt);
            var data = texture.GetTextureData(assetFile);
            var image = Image.WrapMemory<Bgra32>(data, w, h);            
            return new(folder, abPath, image, $"{field["m_Name"].AsString}.{Util.Hex(pathid)}");
        }
    }

    public void ExitBundle(AssetsManager manager, BundleFileInstance bundle, object? data)
    {
        var dict = (Dictionary<long, CachedTexture>)data!;
        foreach (var texture in dict.Values)
        {
            if(!texture.Used && texture.Image is not null)
            {
                texture.Image.Mutate(o => o.Flip(FlipMode.Vertical));
                var filename = $"{texture.Name}.png";
                var path = Path.Combine(texture.Folder, Folders.Processed, Path.ChangeExtension(texture.ABPath, null), filename);
                Util.TouchFile(path);
                texture.Image.SaveAsPng(path);
            }
            texture.Image?.Dispose();
            // help GC a bit
            texture.Image = null!;
            texture.Folder = null!;
            texture.ABPath = null!;
            texture.Name = null!;
        }
        dict.Clear();
    }

    private static int IRound(float f) => (int)MathF.Round(f);

    delegate void PixelRowProc<T>(PixelAccessor<T> p1, PixelAccessor<T> p2, PixelAccessor<T> p3, bool hasAlpha) where T : unmanaged, IPixel<T>;

    static unsafe void ProcessMultiRow<T>(Image<T> color, Image<T>? alpha, Image<T> result, PixelRowProc<T> proc) where T : unmanaged, IPixel<T> =>
        color.ProcessPixelRows(colorProc =>
        {
            var colorPtr = &colorProc;
            if(alpha is null)
            {
                result.ProcessPixelRows(resultProc =>
                    proc(*colorPtr, default, resultProc, false));
                return;
            }
            alpha.ProcessPixelRows(alphaProc =>
            {
                var alphaPtr = &alphaProc;
                result.ProcessPixelRows(resultProc =>
                    proc(*colorPtr, *alphaPtr, resultProc, true));
            });
        });

    private class CachedTexture(string folder, string abPath, Image<Bgra32>? image, string name)
    {
        public bool Used = false;

        public string Folder = folder;
        public string ABPath = abPath;
        public string Name = name;
        public Image<Bgra32>? Image = image;
    }

    private struct SettingsRaw(uint raw)
    {
        public readonly bool Packed => (raw & 1) == 0;
        public readonly bool IsPackingTight => ((raw >> 1) & 1) == 0;
        public readonly Rotations Rotation => (Rotations)((raw >> 2) & 0xF);
        public readonly bool IsMeshTight => ((raw >> 6) & 1) == 1;

        public enum Rotations
        {
            None = 0,
            FlipHorizontal = 1,
            FlipVertical = 2,
            Rotate180 = 3,
            Rotate90 = 4
        }
    }
}
