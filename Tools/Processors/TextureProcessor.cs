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
        if (asset.TypeId == (int)UnityType.Texture2D)
        {
            var lp = (Dictionary<long, CachedTexture>)data!;
            if (lp.ContainsKey(asset.TypeId)) return;
            var cache = Load(asset.PathId);
            if (cache is null) return; // font texture or some error while decoding
            lp[asset.PathId] = cache;
            return;
        }
        if (asset.TypeId != (int)UnityType.Sprite) return;
        var dict = (Dictionary<long, CachedTexture>)data!;

        var field = manager.GetBaseField(assetFile, asset);

        if (field["m_RD"]["downscaleMultiplier"].AsFloat != 1f)
        {
            ConsoleUI.WriteLineColor(ConsoleColor.Yellow, $"Rescaling is not implemented yet: '{field["m_Name"].AsString}' in '{abPath}'");
            return;
        }

        var colorRaw = Resolve(field["m_RD"]["texture"]["m_PathID"].AsLong);
        if (colorRaw is null) return; // font texture or some error while decoding

        var color = colorRaw.Image ?? throw new("No texture in sprite?!");
        var alphaPathId = field["m_RD"]["alphaTexture"]["m_PathID"].AsLong;

        // alpha texture is not specified, try to find it manually
        var euristicAlpha = false;
        if (alphaPathId == 0)
        {
            euristicAlpha = true;
            var name1 = $"{field["m_Name"].AsString}[alpha]";
            var name2 = $"{field["m_Name"].AsString}a";
            var wh = assetFile.file.AssetInfos
                .Where(a => a.TypeId == (int)UnityType.Texture2D)
                .Select(a => (a, field: manager.GetBaseField(assetFile, a)))
                .Where(p =>
                {
                    var fld = p.field["m_Name"];
                    if (fld.IsDummy) return false;
                    var str = fld.AsString;
                    return str == name1 || str == name2;
                })
                .OrderByDescending(p =>
                {
                    var w = p.field["m_Width"].AsInt;
                    var h = p.field["m_Height"].AsInt;
                    if (w == color.Width && h == color.Height) return long.MaxValue;
                    return (long)w * h;
                })
                .Select(p => p.a)
                .FirstOrDefault();
            if (wh is not null) alphaPathId = wh.PathId;
        }
        Image<Bgra32>? alpha = null;
        if (alphaPathId != 0)
        {
            var alphaCache = Resolve(alphaPathId);
            if(alphaCache is not null)
            {
                alpha = alphaCache!.Image!;
                // I found a case when manually resolved alpha texture has different size
                if (euristicAlpha && alpha.Size != color.Size)
                    if (alphaCache.OtherSizes.TryGetValue(color.Size, out var alphaSz))
                        alpha = alphaSz;
                    else
                    {
                        alpha = alpha!.Clone(o => o.Resize(color.Size));
                        alphaCache.OtherSizes[color.Size] = alpha;
                    }
            }
        }

        var rect = field["m_RD"]["textureRect"];
        var rx = (int)MathF.Floor(rect["x"].AsFloat);
        var ry = (int)MathF.Floor(rect["y"].AsFloat);
        var rw = (int)MathF.Ceiling(rect["x"].AsFloat + rect["width"].AsFloat) - rx;
        var rh = (int)MathF.Ceiling(rect["y"].AsFloat + rect["height"].AsFloat) - ry;
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
                    colS.CopyTo(resS);
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


        var path = Path.Combine(folder, Paths.Processed, Path.ChangeExtension(abPath, null), $"{field["m_Name"].AsString}.png");
        Util.TouchFile(path);
        result.SaveAsPng(path);

        CachedTexture? Resolve(long pathId)
        {
            ArgumentOutOfRangeException.ThrowIfEqual(pathId, 0);
            if (!dict.TryGetValue(pathId, out var cache))
            {
                cache = Load(pathId);
                if (cache is null) return null;
                dict[pathId] = cache;
            }
            cache.Used = true;
            return cache;
        }

        CachedTexture? Load(long pathId)
        {
            ArgumentOutOfRangeException.ThrowIfEqual(pathId, 0);

            var field = manager.GetBaseField(assetFile, pathId);
            var texture = TextureFile.ReadTextureFile(field);
            var (w, h) = (field["m_Width"].AsInt, field["m_Height"].AsInt);
            if (w == 0 && h == 0) return null;
            var data = texture.GetTextureData(assetFile);
            Image<Bgra32> image;
            if (data == null)
            {
                var fmt = (TextureFormat)texture.m_TextureFormat;
                byte[] pdata;
                if (fmt == TextureFormat.RGB565)
                    pdata = PixelUtils.Rgb565ToBgra32(texture.pictureData);
                else
                {
                    ConsoleUI.WriteLineColor(ConsoleColor.Yellow, $"'{field["m_Name"].AsString}' in '{abPath}' not decoded, format not implemented: {fmt}");
                    return null;
                }
                image = Image.WrapMemory<Bgra32>(pdata, w, h);
            }
            else if(data.Length == 0)
                image = Image.WrapMemory<Bgra32>(texture.pictureData, w, h);
            else
                image = Image.WrapMemory<Bgra32>(data, w, h);
            return new(folder, abPath, image, $"{field["m_Name"].AsString}.{Util.Hex(pathId)}");
        }
    }

    public void ExitBundle(AssetsManager manager, BundleFileInstance bundle, object? data)
    {
        var dict = (Dictionary<long, CachedTexture>)data!;
        foreach (var texture in dict.Values)
        {
            if (!texture.Used && texture.Image is not null)
            {
                texture.Image.Mutate(o => o.Flip(FlipMode.Vertical));
                var filename = $"{texture.Name}.png";
                var path = Path.Combine(texture.Folder, Paths.Processed, Path.ChangeExtension(texture.ABPath, null), filename);
                Util.TouchFile(path);
                texture.Image.SaveAsPng(path);
            }
            texture.Image?.Dispose();
            foreach (var img in texture.OtherSizes) img.Value.Dispose();
            // help GC a bit
            texture.Image = null!;
            texture.Folder = null!;
            texture.ABPath = null!;
            texture.Name = null!;
        }
        dict.Clear();
    }

    delegate void PixelRowProc<T>(PixelAccessor<T> p1, PixelAccessor<T> p2, PixelAccessor<T> p3, bool hasAlpha) where T : unmanaged, IPixel<T>;

    static unsafe void ProcessMultiRow<T>(Image<T> color, Image<T>? alpha, Image<T> result, PixelRowProc<T> proc) where T : unmanaged, IPixel<T> =>
        color.ProcessPixelRows(colorProc =>
        {
            var colorPtr = &colorProc;
            if (alpha is null)
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
        public Dictionary<Size, Image<Bgra32>> OtherSizes = [];
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
