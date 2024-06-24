namespace ArknightsWorkshop;

public static class ConsoleUI
{
    public static int ChooseOne(string title, IEnumerable<string> options)
    {
        const string select = "Write index: ";

        Console.WriteLine(title);
        int count = 0;
        foreach (var v in options)
        {
            Console.WriteLine($"{count + 1}) {v}");
            count++;
        }
        Console.Write(select);
        while(true)
        {
            if (int.TryParse(Console.ReadLine(), out var sel) && sel > 0 && sel <= count)
                return sel - 1;
            Console.CursorTop--;
            Console.Write($"Wrong index. {select}");
            var (left, top) = (Console.CursorLeft, Console.CursorTop);
            Console.Write(new string(' ', Console.WindowWidth - Console.CursorLeft));
            (Console.CursorLeft, Console.CursorTop) = (left, top);
        }
    }

    public static bool[] ChooseMultiple(string title, IEnumerable<string> options)
    {
        const string keys = "QAZWSXEDCRFVTGBYHNUJMIKOLP";

        Console.WriteLine(title);
        int count = 0;
        var line = Console.CursorTop;
        foreach (var v in options)
        {
            Console.Write(keys[count]);
            WriteColor(ConsoleColor.Red, " [ ]");
            Console.WriteLine($": {v}");
            count++;
        }
        var select = new bool[count];
        Console.Write("Select options (press corresponding keys to [un]select, press Enter to confirm)");
        var endLine = Console.CursorTop;
        var endLeft = Console.CursorLeft;
        while (true)
        {
            var key = Console.ReadKey(true);
            if (key.Key == ConsoleKey.Enter) break;
            var ind = keys.IndexOf(char.ToUpper(key.KeyChar));
            if (ind == -1 || ind >= select.Length) continue;
            select[ind] = !select[ind];
            Console.CursorTop = line + ind;
            Console.CursorLeft = 2;
            if (select[ind])
                WriteColor(ConsoleColor.Green, "[X]");
            else
                WriteColor(ConsoleColor.Red, "[ ]");
            Console.CursorLeft = endLeft;
            Console.CursorTop = endLine;
        }
        Console.WriteLine();
        return select;
    }

    public static string? SelectVersion(string workingDir)
    {
        var resFolder = Path.Combine(workingDir, Paths.Assets);

        if (!Directory.Exists(resFolder))
        {
            NoResError();
            return null;
        }
        var dirs = new DirectoryInfo(resFolder).GetDirectories();
        if (dirs.Length == 0)
        {
            NoResError();
            return null;
        }
        var resNamePrefix = CLIArgs.ParamRaw("res");
        if (resNamePrefix is null)
        {
            var ind = ChooseOne("Select resource version", dirs.Select(d => d.Name));
            return dirs[ind].Name;
        }
        else
        {
            var cands = dirs.Where(d => d.Name.StartsWith(resNamePrefix)).ToArray();
            if (cands.Length != 1)
            {
                WriteLineColor(ConsoleColor.Red, cands.Length switch
                {
                    0 => $"No resource version begin with '{resNamePrefix}'",
                    _ => $"Multiple resource versions begin with '{resNamePrefix}'"
                });
                return null;
            }
            return cands[0].Name;
        }

        static void NoResError() => WriteLineColor(ConsoleColor.Red, "No resources downloaded.");
    }

    public static void WriteLineColor(ConsoleColor color, string line)
    {
        Console.ForegroundColor = color;
        Console.WriteLine(line);
        Console.ResetColor();
    }
    public static void WriteColor(ConsoleColor color, string line)
    {
        Console.ForegroundColor = color;
        Console.Write(line);
        Console.ResetColor();
    }
}
