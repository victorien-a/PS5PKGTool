using PS5PKGTool.Core.Services;

namespace PS5PKGTool.Cli;

internal static class MergeCommand
{
    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        string? manifest = null;
        string? destination = null;

        foreach (string arg in args)
        {
            switch (arg)
            {
                case "-h" or "--help":
                    Console.WriteLine("Usage: ps5pkgtool merge <manifest.json> <output.pkg>");
                    Console.WriteLine();
                    Console.WriteLine("Validates split pieces against their manifest and merges them into a single .pkg.");
                    return 0;
                default:
                    if (arg.StartsWith('-'))
                    {
                        Console.Error.WriteLine($"error: unknown option '{arg}'");
                        return 1;
                    }
                    if (manifest is null) manifest = arg;
                    else destination ??= arg;
                    break;
            }
        }

        if (manifest is null || destination is null)
        {
            Console.Error.WriteLine("error: merge requires a manifest path and an output package path");
            return 1;
        }

        string manifestPath = Path.GetFullPath(manifest);
        if (!File.Exists(manifestPath))
        {
            Console.Error.WriteLine($"error: no such file: {manifestPath}");
            return 1;
        }

        string outputPath = Path.GetFullPath(destination);
        bool showProgress = !Console.IsOutputRedirected;

        IReadOnlyList<string> pieces;
        try
        {
            if (showProgress) Console.Error.Write("Validating split pieces...");
            pieces = await SonyPackageSplit.GetValidatedPiecePathsAsync(manifestPath, cancellationToken);
            if (showProgress) Console.Error.Write("\r".PadRight(30) + "\r");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or FileNotFoundException)
        {
            if (showProgress) Console.Error.Write("\r".PadRight(30) + "\r");
            Console.Error.WriteLine($"error: {ex.Message}");
            return 1;
        }

        long totalBytes = pieces.Sum(path => new FileInfo(path).Length);

        try
        {
            if (showProgress) Console.Error.Write($"Merging {pieces.Count} piece(s), {Format.Size(totalBytes)}...");
            await SonyPackageMerge.MergeAtomicAsync(pieces, outputPath, cancellationToken);
            if (showProgress) Console.Error.Write("\r".PadRight(60) + "\r");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException)
        {
            if (showProgress) Console.Error.Write("\r".PadRight(60) + "\r");
            Console.Error.WriteLine($"error: {ex.Message}");
            return 1;
        }

        Console.WriteLine($"Verified {pieces.Count} piece(s), {Format.Size(totalBytes)}");
        Console.WriteLine($"Merged: {outputPath}");
        return 0;
    }
}
