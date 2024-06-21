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
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Write(" [ ]");
            Console.ResetColor();
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
            Console.ForegroundColor = select[ind] ? ConsoleColor.Green : ConsoleColor.Red;
            Console.Write(select[ind] ? "[X]" : "[ ]");
            Console.ResetColor();
            Console.CursorLeft = endLeft;
            Console.CursorTop = endLine;
        }
        Console.WriteLine();
        return select;
    }
}
