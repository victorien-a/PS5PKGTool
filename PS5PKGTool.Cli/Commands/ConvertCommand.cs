using PS5PKGTool.Core.Services;
using PS5PKGTool.Ffpfsc;

namespace PS5PKGTool.Cli;

internal static class ConvertCommand
{
    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        string? source = null;
        string? destination = null;
        string? to = null;
        bool overwrite = false;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            switch (arg)
            {
                case "-h" or "--help":
                    Console.WriteLine("Usage: ps5pkgtool convert <package.pkg> <output> --to <exfat|ffpkg|ffpfsc> [--overwrite]");
                    Console.WriteLine();
                    Console.WriteLine("Converts a Sony .pkg into another PS5 image format.");
                    return 0;
                case "--to":
                    if (i + 1 >= args.Length)
                    {
                        Console.Error.WriteLine("error: --to requires a value");
                        return 1;
                    }
                    to = args[++i];
                    break;
                case "--overwrite":
                    overwrite = true;
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
            Console.Error.WriteLine("error: convert requires a source package and an output path");
            return 1;
        }

        if (to is null)
        {
            Console.Error.WriteLine("error: --to is required (valid values: exfat, ffpkg, ffpfsc)");
            return 1;
        }

        if (!TryParseTarget(to, out Ps5ImageConversionTarget target))
        {
            Console.Error.WriteLine($"error: invalid --to value '{to}' (valid values: exfat, ffpkg, ffpfsc)");
            return 1;
        }

        if (!File.Exists(source))
        {
            Console.Error.WriteLine($"error: source file not found: {source}");
            return 1;
        }

        string output = Path.GetFullPath(destination);
        if (File.Exists(output) && !overwrite)
        {
            Console.Error.WriteLine($"error: output already exists: {output} (use --overwrite)");
            return 1;
        }

        bool showProgress = !Console.IsOutputRedirected;
        long lastPercent = -1;
        var progress = new Progress<Ps5ImageConversionProgress>(p =>
        {
            if (!showProgress) return;
            if (p.Total > 0)
            {
                long percent = p.Completed * 100 / p.Total;
                if (percent == lastPercent) return;
                lastPercent = percent;
                Console.Error.Write($"\r{p.Stage} {percent,3}%  {Format.Size(p.Completed)}".PadRight(60));
            }
            else
            {
                Console.Error.Write($"\r{p.Stage}  {Format.Size(p.Completed)}".PadRight(60));
            }
        });

        Ps5ImageConversionResult result = await SonyPackageImageConversion.ConvertAsync(
            source, output, target, overwrite, progress, cancellationToken);

        if (showProgress) Console.Error.Write("\r".PadRight(61) + "\r");

        Console.WriteLine($"Converted to {result.Target} ({result.FileCount} file(s), {Format.Size(result.OutputBytes)})");
        Console.WriteLine($"Output: {result.OutputPath}");
        return 0;
    }

    private static bool TryParseTarget(string value, out Ps5ImageConversionTarget target)
    {
        switch (value.ToLowerInvariant())
        {
            case "exfat":
                target = Ps5ImageConversionTarget.Exfat;
                return true;
            case "ffpkg":
                target = Ps5ImageConversionTarget.Ffpkg;
                return true;
            case "ffpfsc":
                target = Ps5ImageConversionTarget.Ffpfsc;
                return true;
            default:
                target = default;
                return false;
        }
    }
}
