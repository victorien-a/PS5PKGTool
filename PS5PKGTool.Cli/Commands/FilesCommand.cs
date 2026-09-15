using System.Text.Json;
using PS5PKGTool.Core.Models;
using PS5PKGTool.Core.Services;

namespace PS5PKGTool.Cli;

internal static class FilesCommand
{
    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        string? target = null;
        bool json = false;
        int limit = 0;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            switch (arg)
            {
                case "--json": json = true; break;
                case "-h" or "--help":
                    Console.WriteLine("Usage: ps5pkgtool files <path> [--limit N] [--json]");
                    return 0;
                case "--limit":
                    if (i + 1 >= args.Length || !int.TryParse(args[++i], out limit) || limit < 0)
                    {
                        Console.Error.WriteLine("error: --limit requires a non-negative number");
                        return 1;
                    }
                    break;
                default:
                    if (arg.StartsWith('-'))
                    {
                        Console.Error.WriteLine($"error: unknown option '{arg}'");
                        return 1;
                    }
                    target ??= arg;
                    break;
            }
        }

        if (target is null)
        {
            Console.Error.WriteLine("error: files requires a path");
            return 1;
        }

        Ps5GameInfo? game = await Locate.SingleAsync(target, cancellationToken);
        if (game is null)
        {
            Console.Error.WriteLine($"error: no PS5 content found at: {target}");
            return 1;
        }

        using IReadOnlyGameFileSystem fs = GameFileSystem.Open(game, cancellationToken);
        IReadOnlyList<GameFileRecord> all = fs.Files;
        IEnumerable<GameFileRecord> selected = all.OrderBy(f => f.RelativePath, StringComparer.OrdinalIgnoreCase);
        if (limit > 0) selected = selected.Take(limit);
        var files = selected.ToList();

        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(
                files.Select(f => new { path = f.RelativePath, sizeBytes = f.Size }),
                new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }

        if (all.Count == 0)
        {
            Console.WriteLine("No files found.");
            return 0;
        }

        Format.Table(["SIZE", "PATH"],
            files.Select(f => new[] { Format.Size(f.Size), f.RelativePath }).ToList());

        Console.WriteLine();
        long totalBytes = all.Sum(f => f.Size);
        Console.WriteLine(files.Count < all.Count
            ? $"Showing {files.Count} of {all.Count} file(s), {Format.Size(totalBytes)} total."
            : $"{all.Count} file(s), {Format.Size(totalBytes)} total.");
        return 0;
    }
}
