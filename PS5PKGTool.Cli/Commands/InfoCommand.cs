using System.Text.Json;
using PS5PKGTool.Core.Models;
using PS5PKGTool.Core.Services;

namespace PS5PKGTool.Cli;

internal static class InfoCommand
{
    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        string? target = null;
        bool json = false;

        foreach (string arg in args)
        {
            switch (arg)
            {
                case "--json": json = true; break;
                case "-h" or "--help":
                    Console.WriteLine("Usage: ps5pkgtool info <path> [--json]");
                    return 0;
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
            Console.Error.WriteLine("error: info requires a path");
            return 1;
        }

        (Ps5GameInfo? game, IReadOnlyList<string> errors) = await Locate.LocateAsync(target, cancellationToken);
        if (game is null)
        {
            Console.Error.WriteLine($"error: no PS5 content found at: {target}");
            // The scanner explains a container it could parse but had to reject, which is far
            // more useful than the generic message above.
            foreach (string detail in errors) Console.Error.WriteLine(detail);
            return 1;
        }

        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(game,
                new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }

        Format.KeyValue("Title", game.Title);
        Format.KeyValue("Title ID", game.TitleId);
        Format.KeyValue("Content ID", game.ContentId);
        Format.KeyValue("Concept ID", game.ConceptId);
        Format.KeyValue("Format", ScanCommand.Describe(game.SourceKind));
        Format.KeyValue("Category", game.ApplicationCategory);
        Format.KeyValue("Content version", game.ContentVersion);
        Format.KeyValue("Master version", game.MasterVersion);
        Format.KeyValue("Required firmware", game.RequiredSystemSoftware);
        Format.KeyValue("SDK version", game.SdkVersion);
        Format.KeyValue("DRM type", game.DrmType);
        Format.KeyValue("Default language", game.DefaultLanguage);
        Format.KeyValue("Size", Format.Size(game.SourceSize));
        if (game.DownloadDataSize > 0)
            Format.KeyValue("Download data size", Format.Size(game.DownloadDataSize));
        Format.KeyValue("Creation date", game.CreationDate);
        Format.KeyValue("Path", game.RootPath);

        if (game.DeclaredFeatures.Count > 0)
            Format.KeyValue("Features", string.Join(", ", game.DeclaredFeatures));

        if (game.LocalizedTitles.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Localized titles:");
            foreach ((string language, string title) in game.LocalizedTitles.OrderBy(p => p.Key))
                Console.WriteLine($"  {language,-10}{title}");
        }

        return 0;
    }
}
