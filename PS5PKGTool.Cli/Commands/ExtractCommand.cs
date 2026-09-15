using PS5PKGTool.Core.Models;
using PS5PKGTool.Core.Services;

namespace PS5PKGTool.Cli;

internal static class ExtractCommand
{
    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        string? source = null;
        string? destination = null;

        foreach (string arg in args)
        {
            switch (arg)
            {
                case "-h" or "--help":
                    Console.WriteLine("Usage: ps5pkgtool extract <path> <destination>");
                    Console.WriteLine();
                    Console.WriteLine("Extracts a package, image or dump into a directory.");
                    return 0;
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
            Console.Error.WriteLine("error: extract requires a source path and a destination directory");
            return 1;
        }

        Ps5GameInfo? game = await Locate.SingleAsync(source, cancellationToken);
        if (game is null)
        {
            Console.Error.WriteLine($"error: no PS5 content found at: {source}");
            return 1;
        }

        string output = Path.GetFullPath(destination);
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
        {
            Console.Error.WriteLine($"error: destination already exists and is not empty: {output}");
            return 1;
        }

        // Use the engine's merged view of the container. For a Sony package this unions the inner
        // PFS with the CNT container, so metadata such as sce_sys/param.json is included; reading
        // only the inner PFS would silently drop it.
        using IReadOnlyGameFileSystem files = GameFileSystem.Open(game, cancellationToken);
        IReadOnlyList<GameFileRecord> entries = files.Files;
        if (entries.Count == 0)
        {
            Console.Error.WriteLine("error: the source contains no readable files");
            return 1;
        }

        long totalBytes = entries.Sum(entry => entry.Size);
        long completed = 0;
        int written = 0;
        bool showProgress = !Console.IsOutputRedirected;
        long lastPercent = -1;

        foreach (GameFileRecord entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string target = ResolveContainedPath(output, entry.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);

            await using (Stream input = files.OpenRead(entry.RelativePath))
            await using (var file = new FileStream(target, FileMode.Create, FileAccess.Write,
                FileShare.None, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                byte[] buffer = new byte[1024 * 1024];
                int read;
                while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    completed += read;

                    if (!showProgress || totalBytes <= 0) continue;
                    long percent = completed * 100 / totalBytes;
                    if (percent == lastPercent) continue;
                    lastPercent = percent;
                    Console.Error.Write($"\rExtracting {percent,3}%  {Format.Size(completed)}".PadRight(60));
                }
            }

            written++;
        }

        if (showProgress) Console.Error.Write("\r".PadRight(61) + "\r");

        Console.WriteLine($"Extracted {written} file(s), {Format.Size(completed)}");
        Console.WriteLine($"Destination: {output}");
        return 0;
    }

    /// <summary>Guards against path traversal from a crafted container.</summary>
    private static string ResolveContainedPath(string root, string relativePath)
    {
        string full = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        string prefix = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.Ordinal))
            throw new IOException($"The container entry '{relativePath}' escapes the destination directory.");
        return full;
    }
}
