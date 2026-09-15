using PS5PKGTool.Core.Models;
using PS5PKGTool.Core.Services;

namespace PS5PKGTool.Cli;

/// <summary>
/// Resolves a user supplied path to a single scanned item. A path may point either at a
/// container file (.pkg/.ffpfsc/.exfat) or at a directory holding a loose dump, so the
/// scanner is pointed at the parent directory when the target is a file.
/// </summary>
internal static class Locate
{
    public static async Task<Ps5GameInfo?> SingleAsync(string path, CancellationToken cancellationToken) =>
        (await LocateAsync(path, cancellationToken)).Game;

    /// <summary>
    /// Same as <see cref="SingleAsync"/> but also returns why the scanner rejected a path, so a
    /// command can explain a malformed container instead of only saying nothing was found.
    /// </summary>
    public static async Task<(Ps5GameInfo? Game, IReadOnlyList<string> Errors)> LocateAsync(
        string path, CancellationToken cancellationToken)
    {
        string full = Path.GetFullPath(path);
        bool isFile = File.Exists(full);
        if (!isFile && !Directory.Exists(full)) return (null, []);

        string searchRoot = isFile ? Path.GetDirectoryName(full) ?? full : full;

        Ps5ScanResult result = await new Ps5LibraryScanner()
            .ScanAsync([searchRoot], recursive: false, cached: null, progress: null, cancellationToken);

        Ps5GameInfo? match = result.Games.FirstOrDefault(g =>
            string.Equals(Path.GetFullPath(g.RootPath), full, StringComparison.OrdinalIgnoreCase));

        // For a directory, fall back to the first hit when nothing is rooted exactly at it.
        if (match is null && !isFile) match = result.Games.FirstOrDefault();
        if (match is not null) ResolveSize(match, cancellationToken);

        // Only surface errors that name this path; a folder scan can report unrelated files.
        string[] relevant = match is not null
            ? []
            : result.Errors
                .Where(error => !isFile || error.Contains(Path.GetFileName(full), StringComparison.OrdinalIgnoreCase))
                .ToArray();

        return (match, relevant.Length > 0 ? relevant : result.Errors.ToArray());
    }

    /// <summary>
    /// The scanner deliberately skips sizing loose dumps, because that means walking every dump
    /// folder. Fill the gap using the same file inventory the rest of the engine uses.
    /// </summary>
    public static void ResolveSize(Ps5GameInfo game, CancellationToken cancellationToken)
    {
        if (game.SourceKind != Ps5SourceKind.LooseDump || game.SourceSize > 0) return;
        try
        {
            using IReadOnlyGameFileSystem fs = GameFileSystem.Open(game, cancellationToken);
            game.SourceSize = fs.Files.Sum(file => file.Size);
        }
        catch (Exception)
        {
            // A dump we cannot enumerate still resolves; it just has no size.
        }
    }
}
