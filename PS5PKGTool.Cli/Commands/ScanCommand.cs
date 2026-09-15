using System.Text.Json;
using PS5PKGTool.Core.Models;
using PS5PKGTool.Core.Services;

namespace PS5PKGTool.Cli;

internal static class ScanCommand
{
    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        var folders = new List<string>();
        bool recursive = false;
        bool json = false;

        foreach (string arg in args)
        {
            switch (arg)
            {
                case "-r" or "--recursive": recursive = true; break;
                case "--json": json = true; break;
                case "-h" or "--help":
                    Console.WriteLine("Usage: ps5pkgtool scan <path>... [--recursive] [--json]");
                    return 0;
                default:
                    if (arg.StartsWith('-'))
                    {
                        Console.Error.WriteLine($"error: unknown option '{arg}'");
                        return 1;
                    }
                    folders.Add(arg);
                    break;
            }
        }

        if (folders.Count == 0)
        {
            Console.Error.WriteLine("error: scan requires at least one path");
            return 1;
        }

        foreach (string folder in folders)
        {
            if (!Directory.Exists(folder) && !File.Exists(folder))
            {
                Console.Error.WriteLine($"error: path not found: {folder}");
                return 1;
            }
        }

        // Progress goes to stderr so that --json stdout stays machine readable.
        IProgress<Ps5ScanProgress>? progress = json || Console.IsOutputRedirected
            ? null
            : new Progress<Ps5ScanProgress>(p =>
                Console.Error.Write($"\rScanning {p.Processed}/{p.Total}...".PadRight(60)));

        Ps5ScanResult result = await new Ps5LibraryScanner()
            .ScanAsync(folders, recursive, cached: null, progress, cancellationToken);

        if (progress is not null) Console.Error.Write("\r".PadRight(61) + "\r");

        foreach (Ps5GameInfo game in result.Games) Locate.ResolveSize(game, cancellationToken);

        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                games = result.Games.Select(Summarize),
                errors = result.Errors,
            }, new JsonSerializerOptions { WriteIndented = true }));
            return result.Games.Count == 0 && result.Errors.Count > 0 ? 1 : 0;
        }

        if (result.Games.Count == 0)
        {
            Console.WriteLine("No PS5 content found.");
        }
        else
        {
            var rows = result.Games
                .OrderBy(g => g.Title, StringComparer.OrdinalIgnoreCase)
                .Select(g => new[]
                {
                    string.IsNullOrWhiteSpace(g.Title) ? "(untitled)" : g.Title,
                    g.TitleId,
                    Describe(g.SourceKind),
                    g.ContentVersion,
                    Format.Size(g.SourceSize),
                })
                .ToList();

            Format.Table(["TITLE", "TITLE ID", "FORMAT", "VERSION", "SIZE"], rows);
            Console.WriteLine();
            Console.WriteLine($"{result.Games.Count} item(s).");
        }

        foreach (string error in result.Errors)
            Console.Error.WriteLine($"warning: {error}");

        return 0;
    }

    private static object Summarize(Ps5GameInfo game) => new
    {
        title = game.Title,
        titleId = game.TitleId,
        contentId = game.ContentId,
        format = Describe(game.SourceKind),
        contentVersion = game.ContentVersion,
        requiredSystemSoftware = game.RequiredSystemSoftware,
        sizeBytes = game.SourceSize,
        path = game.RootPath,
    };

    public static string Describe(Ps5SourceKind kind) => kind switch
    {
        Ps5SourceKind.LooseDump => "dump",
        Ps5SourceKind.SonyPackage => "pkg",
        Ps5SourceKind.Ffpfsc => "ffpfsc",
        Ps5SourceKind.FilesystemImage => "exfat",
        Ps5SourceKind.Ffpkg => "ffpkg",
        _ => kind.ToString().ToLowerInvariant(),
    };
}
