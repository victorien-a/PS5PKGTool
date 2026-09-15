using System.Text.Json;
using PS5PKGTool.Core.Models;

namespace PS5PKGTool.Core.Services;

public sealed class Ps5LibraryManifest
{
    public int Version { get; set; } = 1;
    public DateTime CreatedUtc { get; set; }
    public List<Ps5GameInfo> Games { get; set; } = [];
}

/// <summary>
/// Persists a scanned library so a front end can show it instantly instead of rescanning on
/// every launch. The location follows the platform convention: %LOCALAPPDATA% on Windows,
/// ~/.local/share on Linux and ~/Library/Application Support on macOS.
/// </summary>
public sealed class Ps5LibraryCache
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public Ps5LibraryCache(string? directory = null)
    {
        Directory = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PS5PKGTool");
        System.IO.Directory.CreateDirectory(Directory);
    }

    public string Directory { get; }

    public string ManifestPath => Path.Combine(Directory, "manifest.json");

    public Ps5LibraryManifest Load()
    {
        try
        {
            if (!File.Exists(ManifestPath)) return new Ps5LibraryManifest();
            return JsonSerializer.Deserialize<Ps5LibraryManifest>(File.ReadAllText(ManifestPath), JsonOptions)
                   ?? new Ps5LibraryManifest();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // A corrupt or unreadable cache is not fatal; the caller just rescans.
            return new Ps5LibraryManifest();
        }
    }

    public void Save(IReadOnlyCollection<Ps5GameInfo> games)
    {
        var manifest = new Ps5LibraryManifest
        {
            CreatedUtc = DateTime.UtcNow,
            Games = games.ToList(),
        };

        // Write to a temporary file first so an interrupted save cannot truncate the cache.
        string temporary = ManifestPath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(manifest, JsonOptions));
        File.Move(temporary, ManifestPath, overwrite: true);
    }

    public void Clear()
    {
        try
        {
            if (File.Exists(ManifestPath)) File.Delete(ManifestPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Nothing useful to do; the cache is best effort.
        }
    }
}
