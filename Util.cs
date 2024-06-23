using KiDev.StreamCopy;
using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace ArknightsWorkshop;

public static class Util
{
    public static readonly string? ExecutableFolder =
        Environment.ProcessPath is null ? null : Path.GetDirectoryName(Environment.ProcessPath);

    public static string SizeString(double size)
    {
        if (size < 1000) return $"{size}B";
        size /= 1024;
        if (size < 1000) return $"{size:F2}KB";
        size /= 1024;
        if (size < 1000) return $"{size:F2}MB";
        size /= 1024;
        return $"{size:F2}GB";
    }

    public static T NotNullJson<T>(this T? obj) => obj ?? throw new("null in JSON?!");

    public static HttpClient MakeAkClient()
    {
        var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.Add(new("BestHTTP", null));
        return http;
    }

    public static void TouchFile(string path)
    {
        if (File.Exists(path)) return;
        var dir = Path.GetDirectoryName(path);
        if (dir is not null && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
        File.Create(path).Dispose();
    }

    public static Func<long, Stream> WriteSeeker(string path) => pos =>
    {
        var file = File.OpenWrite(path);
        file.Position = pos;
        return file;
    };

    public static void UnpackZip(string path, string folder, string prefix, CancellationToken cancel = default)
    {
        using var file = File.OpenRead(path);
        using var zip = new ZipArchive(file, ZipArchiveMode.Read);
        foreach (var entry in zip.Entries)
        {
            if (cancel.IsCancellationRequested) return;
            if (!entry.FullName.StartsWith(prefix)) continue;
            var destPath = Path.Combine(folder, entry.FullName[prefix.Length..]);
            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
            if (File.Exists(destPath)) continue;
            entry.ExtractToFile(destPath);
        }
    }

    public static void Dump(IPositionStorage data)
    {
        if (data.Done)
        {
            Console.Write("Done. ");
            return;
        }
        if (data.Position == 0)
        {
            Console.Write("Waiting... ");
            return;
        }
        Console.Write("Downloading... ");
        Console.Write(SizeString(data.Position));
        if (data.Length != 0)
            Console.Write($"/{SizeString(data.Length)} ({data.Position * 100 / data.Length}%)");
    }

    public static void ReadExactlyOldStream(Stream s, byte[] b)
    {
        int total = 0;
        while (total < b.Length)
        {
            var read = s.Read(b, total, b.Length - total);
            if (read == 0) throw new("Reached end of the stream");
            total += read;
        }
    }

    public static string Hex<T>(T value) where T : unmanaged
    {
        var bytes = MemoryMarshal.AsBytes(new ReadOnlySpan<T>(ref value));
        return Convert.ToHexString(bytes);
    }

    public static IEnumerable<string> GetFileTree(string folder)
    {
        return GetFiles(new DirectoryInfo(folder)).Select(f => f.FullName);

        static IEnumerable<FileInfo> GetFiles(FileSystemInfo fs) => fs switch
        {
            FileInfo fi => [fi],
            DirectoryInfo di => di.EnumerateFileSystemInfos().SelectMany(GetFiles),
            _ => throw new("Neither file nor folder?!"),
        };
    }

    public static string? GetStringPropOr(this JsonElement e, string name, string? or = null) =>
        e.TryGetProperty(name, out var prop) ? prop.GetString() : or;

    [return: NotNullIfNotNull(nameof(src))]
    public static string? Unescape(string? src)
    {
        if (src is null) return null;

        var srcS = src.AsSpan();
        var sb = new StringBuilder();
        while(!srcS.IsEmpty)
        {
            var to = srcS.IndexOf('\\');
            if(to == -1)
            {
                sb.Append(srcS);
                srcS = default;
                continue;
            }
            if(to > 0)
            {
                sb.Append(srcS[..to]);
                srcS = srcS[to..];
                continue;
            }
            if (srcS[1] == '\\')
            {
                srcS = srcS[2..];
                sb.Append('\\');
                continue;
            }
            if (srcS[1] == 'r')
            {
                srcS = srcS[2..];
                sb.Append('\r');
                continue;
            }
            if (srcS[1] == 'n')
            {
                srcS = srcS[2..];
                sb.Append('\n');
                continue;
            }
            if (srcS[1] == 't')
            {
                srcS = srcS[2..];
                sb.Append('\t');
                continue;
            }
            if (srcS[1] == 'u')
            {
                var code = srcS[2..6];
                srcS = srcS[6..];                
                sb.Append((char)int.Parse(code, System.Globalization.NumberStyles.HexNumber));
                continue;
            }
            throw new($"Unknown escape sequence: {srcS}");
        }
        return sb.ToString();
    }

    public class Ref<T> where T : class
    { 
        public T Value = null!; 
    }
}
