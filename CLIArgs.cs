public static class CLIArgs
{
    private static readonly string? _actIndex;
    private static readonly Dictionary<string, string> _params = [];
    private static readonly HashSet<string> _keys = [];

    static CLIArgs()
    {
#if DEBUG
        Console.Write("Args: ");
        var rawArgs = Console.ReadLine()!.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
#else
        var rawArgs = Environment.GetCommandLineArgs().Skip(1);
#endif
        var args = rawArgs
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrEmpty(s))
            .ToArray();
        if (args.Length == 0) return;
        _actIndex = args[0];
        for (int i = 1; i < args.Length; i++)
        {
            var sep = args[i].IndexOf(':');
            if (sep == -1)
            {
                _keys.Add(args[i]);
                continue;
            }
            _params[args[i][..sep]] = args[i][(sep + 1)..];
        }
    }

    public static string? ActionIndex => _actIndex;

    public static bool HasKey(string key) => _keys.Contains(key);

    public static string? ParamRaw(string key) => _params.GetValueOrDefault(key);

    public static List<string>? ParamList(string key)
    {
        if (!_params.TryGetValue(key, out var val)) return null;
        return [.. val.Split(',', StringSplitOptions.RemoveEmptyEntries)];
    }
}