namespace PS5PKGTool.Cli;

/// <summary>Shared output helpers so every command formats sizes and tables the same way.</summary>
internal static class Format
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB"];

    public static string Size(long bytes)
    {
        if (bytes <= 0) return "0 B";
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < Units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return unit == 0
            ? $"{bytes} {Units[unit]}"
            : $"{value:0.##} {Units[unit]}";
    }

    /// <summary>Render rows as a left-aligned table sized to the widest cell in each column.</summary>
    public static void Table(IReadOnlyList<string> headers, IReadOnlyList<string[]> rows)
    {
        int columns = headers.Count;
        int[] widths = new int[columns];
        for (int i = 0; i < columns; i++) widths[i] = headers[i].Length;

        foreach (string[] row in rows)
            for (int i = 0; i < columns && i < row.Length; i++)
                widths[i] = Math.Max(widths[i], (row[i] ?? string.Empty).Length);

        Console.WriteLine(Line(headers.ToArray(), widths));
        Console.WriteLine(string.Join("  ", widths.Select(w => new string('-', w))));
        foreach (string[] row in rows)
            Console.WriteLine(Line(row, widths));
    }

    private static string Line(string[] cells, int[] widths)
    {
        var parts = new List<string>(widths.Length);
        for (int i = 0; i < widths.Length; i++)
        {
            string cell = i < cells.Length ? cells[i] ?? string.Empty : string.Empty;
            parts.Add(i == widths.Length - 1 ? cell : cell.PadRight(widths[i]));
        }
        return string.Join("  ", parts).TrimEnd();
    }

    public static void KeyValue(string key, string? value, int pad = 24)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        Console.WriteLine($"{(key + ":").PadRight(pad)}{value}");
    }
}
