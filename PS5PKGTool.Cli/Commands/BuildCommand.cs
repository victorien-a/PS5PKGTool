using System.Text.Json;
using PS5PKGTool.Core.Builders;

namespace PS5PKGTool.Cli;

internal static class BuildCommand
{
    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        string? source = null;
        string? destination = null;
        string? contentId = null;
        string? passcode = null;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            switch (arg)
            {
                case "-h" or "--help":
                    Console.WriteLine("Usage: ps5pkgtool build <source-dir> <output.pkg> [--content-id ID] [--passcode VALUE]");
                    Console.WriteLine();
                    Console.WriteLine("Builds a Sony debug package from a loose PS5 dump directory.");
                    return 0;
                case "--content-id":
                    if (i + 1 >= args.Length)
                    {
                        Console.Error.WriteLine("error: --content-id requires a value");
                        return 1;
                    }
                    contentId = args[++i];
                    break;
                case "--passcode":
                    if (i + 1 >= args.Length)
                    {
                        Console.Error.WriteLine("error: --passcode requires a value");
                        return 1;
                    }
                    passcode = args[++i];
                    break;
                default:
                    if (arg.StartsWith('-'))
                    {
                        Console.Error.WriteLine($"error: unknown option '{arg}'");
                        return 1;
                    }
                    if (source is null) source = arg;
                    else destination ??= arg;
                    break;
            }
        }

        if (source is null || destination is null)
        {
            Console.Error.WriteLine("error: build requires a source directory and an output package path");
            return 1;
        }

        if (!Directory.Exists(source))
        {
            Console.Error.WriteLine($"error: source directory not found: {source}");
            return 1;
        }

        if (contentId is null)
        {
            contentId = TryReadContentIdFromParam(source);
            if (contentId is null)
            {
                Console.Error.WriteLine("error: --content-id is required (sce_sys/param.json does not declare a contentId)");
                return 1;
            }
        }

        string output = Path.GetFullPath(destination);

        SonyDebugPackageBuildOptions options = passcode is not null
            ? new SonyDebugPackageBuildOptions { ContentId = contentId, Passcode = passcode }
            : new SonyDebugPackageBuildOptions { ContentId = contentId };

        bool showProgress = !Console.IsOutputRedirected;
        long lastPercent = -1;
        var progress = new Progress<SonyDebugPackageProgress>(p =>
        {
            if (!showProgress) return;
            if (p.TotalBytes > 0)
            {
                long percent = p.CompletedBytes * 100 / p.TotalBytes;
                if (percent == lastPercent) return;
                lastPercent = percent;
                Console.Error.Write($"\r{p.Stage} {percent,3}%  {Format.Size(p.CompletedBytes)}".PadRight(60));
            }
            else
            {
                Console.Error.Write($"\r{p.Stage}  {Format.Size(p.CompletedBytes)}".PadRight(60));
            }
        });

        SonyDebugPackageBuildResult result;
        try
        {
            result = await SonyDebugPackageBuilder.CreateFromDirectoryAsync(
                source, output, options, progress, cancellationToken);
        }
        catch (Exception ex)
        {
            if (showProgress) Console.Error.Write("\r".PadRight(61) + "\r");
            Console.Error.WriteLine($"error: {ex.Message}");
            return 1;
        }

        if (showProgress) Console.Error.Write("\r".PadRight(61) + "\r");

        Console.WriteLine($"Built {result.ContentId} ({result.SourceFiles} file(s), {Format.Size(result.SourceBytes)} source)");
        Console.WriteLine($"Output: {result.OutputPath} ({Format.Size(result.PackageSize)})");
        Format.KeyValue("Key fingerprint", result.KeyFingerprint);
        if (result.UsesDefaultPasscode)
            Console.WriteLine("warning: package uses the default passcode.");
        return 0;
    }

    /// <summary>Reads sce_sys/param.json's contentId to use as the default when --content-id is omitted.</summary>
    private static string? TryReadContentIdFromParam(string sourceDirectory)
    {
        string paramPath = Path.Combine(sourceDirectory, "sce_sys", "param.json");
        if (!File.Exists(paramPath)) return null;

        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(paramPath), new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
            });
            if (document.RootElement.TryGetProperty("contentId", out JsonElement value) &&
                value.ValueKind == JsonValueKind.String)
            {
                string? contentId = value.GetString();
                return string.IsNullOrWhiteSpace(contentId) ? null : contentId;
            }
        }
        catch (Exception)
        {
            // Fall through to requiring --content-id explicitly.
        }
        return null;
    }
}
