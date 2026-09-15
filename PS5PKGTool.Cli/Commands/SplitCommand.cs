using PS5PKGTool.Core.Services;

namespace PS5PKGTool.Cli;

internal static class SplitCommand
{
    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        string? source = null;
        string? destination = null;
        long pieceSize = 4L * 1024 * 1024 * 1024;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            switch (arg)
            {
                case "-h" or "--help":
                    Console.WriteLine("Usage: ps5pkgtool split <package.pkg> <output-dir> [--piece-size SIZE]");
                    Console.WriteLine();
                    Console.WriteLine("Splits a Sony .pkg into fixed-size pieces plus a manifest for later merging.");
                    Console.WriteLine();
                    Console.WriteLine("Options:");
                    Console.WriteLine("  --piece-size SIZE   Maximum bytes per piece (e.g. 4GB, 700MB, 1024). Default: 4GB");
                    return 0;
                case "--piece-size":
                    if (i + 1 >= args.Length)
                    {
                        Console.Error.WriteLine("error: --piece-size requires a value");
                        return 1;
                    }
                    string sizeText = args[++i];
                    if (!TryParseSize(sizeText, out pieceSize))
                    {
                        Console.Error.WriteLine($"error: invalid piece size '{sizeText}'");
                        return 1;
                    }
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
            Console.Error.WriteLine("error: split requires a package path and an output directory");
            return 1;
        }

        string packagePath = Path.GetFullPath(source);
        if (!File.Exists(packagePath))
        {
            Console.Error.WriteLine($"error: no such file: {packagePath}");
            return 1;
        }

        string outputDirectory = Path.GetFullPath(destination);
        bool showProgress = !Console.IsOutputRedirected;
        long lastPercent = -1;
        var progress = new Progress<SonyPackageSplitProgress>(update =>
        {
            if (!showProgress || update.TotalBytes <= 0) return;
            long percent = update.CompletedBytes * 100 / update.TotalBytes;
            if (percent == lastPercent) return;
            lastPercent = percent;
            Console.Error.Write($"\rSplitting {percent,3}%  {Format.Size(update.CompletedBytes)}  {update.CurrentPiece}".PadRight(70));
        });

        SonyPackageSplitResult result;
        try
        {
            result = await SonyPackageSplit.CreateAsync(packagePath, outputDirectory,
                new SonyPackageSplitOptions { MaximumPieceBytes = pieceSize }, progress, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or FileNotFoundException)
        {
            if (showProgress) Console.Error.Write("\r".PadRight(71) + "\r");
            Console.Error.WriteLine($"error: {ex.Message}");
            return 1;
        }

        if (showProgress) Console.Error.Write("\r".PadRight(71) + "\r");

        Console.WriteLine($"Split into {result.Manifest.Pieces.Count} piece(s), {Format.Size(result.Manifest.SourceBytes)}");
        Console.WriteLine($"Manifest: {result.ManifestPath}");
        return 0;
    }

    /// <summary>Parses human-readable sizes such as "4GB", "700MB" or a plain byte count.</summary>
    internal static bool TryParseSize(string text, out long bytes)
    {
        bytes = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        text = text.Trim();

        int splitIndex = text.Length;
        while (splitIndex > 0 && !char.IsDigit(text[splitIndex - 1]) && text[splitIndex - 1] != '.') splitIndex--;
        string numberPart = text[..splitIndex];
        string unitPart = text[splitIndex..].Trim().ToUpperInvariant();

        if (!double.TryParse(numberPart, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double value) || value <= 0)
            return false;

        double multiplier = unitPart switch
        {
            "" or "B" => 1,
            "K" or "KB" or "KIB" => 1024,
            "M" or "MB" or "MIB" => 1024L * 1024,
            "G" or "GB" or "GIB" => 1024L * 1024 * 1024,
            "T" or "TB" or "TIB" => 1024L * 1024 * 1024 * 1024,
            _ => -1,
        };
        if (multiplier < 0) return false;

        double result = value * multiplier;
        if (result <= 0 || result > long.MaxValue) return false;
        bytes = (long)result;
        return true;
    }
}
