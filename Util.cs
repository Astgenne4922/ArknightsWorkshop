﻿using KiDev.StreamCopy;
using System.IO.Compression;

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
}
