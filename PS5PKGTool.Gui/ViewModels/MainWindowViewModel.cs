using System.Collections.ObjectModel;
using System.Windows.Input;
using Avalonia.Media.Imaging;
using PS5PKGTool.Core.Models;
using PS5PKGTool.Core.Services;
using PS5PKGTool.Gui.Services;

namespace PS5PKGTool.Gui.ViewModels;

public sealed class MainWindowViewModel : ViewModelBase
{
    private readonly Ps5LibraryScanner _scanner = new();
    private readonly Ps5LibraryCache _libraryCache;
    private readonly GameArtworkService _artwork = new();
    private CancellationTokenSource? _scanCancellation;

    private string _status = "Add a folder to scan for PS5 content.";
    private bool _isScanning;
    private GameViewModel? _selectedGame;
    private string _searchText = string.Empty;
    private string? _formatFilter = "Any format";
    private string? _categoryFilter = "Any category";

    public ObservableCollection<string> Folders { get; } = [];
    public ObservableCollection<GameViewModel> Games { get; } = [];
    public ObservableCollection<FileEntryViewModel> SelectedGameFiles { get; } = [];

    /// <summary>Task queue for long-running package operations, bound by the view's task panel.</summary>
    public TaskQueueViewModel TaskQueue { get; }

    /// <summary>Options for the quick "Format" filter dropdown; the first entry means "any".</summary>
    public ObservableCollection<string> FormatOptions { get; } =
        ["Any format", "dump", "pkg", "ffpfsc", "exfat", "ffpkg"];

    /// <summary>Options for the quick "Category" filter dropdown; the first entry means "any".</summary>
    public ObservableCollection<string> CategoryOptions { get; } =
        ["Any category", "Game", "DLC", "Patch", "App"];

    public MainWindowViewModel() : this(null, null)
    {
    }

    /// <param name="libraryCacheDirectory">
    /// Overrides the library cache location; pass a temp directory in tests so probes never touch
    /// the real user cache under ~/.local/share/PS5PKGTool.
    /// </param>
    /// <param name="taskQueuePersistencePath">
    /// Overrides the task queue's persistence file path; pass a temp path in tests for the same
    /// reason as <paramref name="libraryCacheDirectory"/>.
    /// </param>
    public MainWindowViewModel(string? libraryCacheDirectory, string? taskQueuePersistencePath = null)
    {
        _libraryCache = new Ps5LibraryCache(libraryCacheDirectory);
        TaskQueue = new TaskQueueViewModel(persistencePath: taskQueuePersistencePath);
    }

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public bool IsScanning
    {
        get => _isScanning;
        private set
        {
            if (!SetProperty(ref _isScanning, value)) return;
            RaisePropertyChanged(nameof(CanScan));
        }
    }

    public bool CanScan => !IsScanning && Folders.Count > 0;

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value)) ApplyFilter();
        }
    }

    /// <summary>
    /// Quick "Format" dropdown selection ("Any format" or null clears it). Composes into
    /// <see cref="SearchText"/> as a <c>format:</c> token rather than duplicating the filter logic.
    /// </summary>
    public string? FormatFilter
    {
        get => _formatFilter;
        set
        {
            if (SetProperty(ref _formatFilter, value)) ApplyQuickFilters();
        }
    }

    /// <summary>Quick "Category" dropdown selection; composes into <see cref="SearchText"/> as <c>category:</c>.</summary>
    public string? CategoryFilter
    {
        get => _categoryFilter;
        set
        {
            if (SetProperty(ref _categoryFilter, value)) ApplyQuickFilters();
        }
    }

    public GameViewModel? SelectedGame
    {
        get => _selectedGame;
        set
        {
            if (!SetProperty(ref _selectedGame, value)) return;
            RaisePropertyChanged(nameof(HasSelection));
            _ = LoadFilesAsync(value);
        }
    }

    public bool HasSelection => SelectedGame is not null;

    /// <summary>All scanned items; <see cref="Games"/> is the filtered view bound to the list.</summary>
    private readonly List<GameViewModel> _allGames = [];

    /// <summary>
    /// Loads the previously cached library instantly (no scan), so the list is populated the
    /// moment the window opens. A missing or corrupt cache degrades to an empty list; it never
    /// throws.
    /// </summary>
    public void LoadCachedLibrary()
    {
        Ps5LibraryManifest manifest;
        try
        {
            manifest = _libraryCache.Load();
        }
        catch (Exception)
        {
            manifest = new Ps5LibraryManifest();
        }

        _allGames.Clear();
        Games.Clear();
        SelectedGameFiles.Clear();

        foreach (Ps5GameInfo game in manifest.Games.OrderBy(g => g.Title, StringComparer.OrdinalIgnoreCase))
            _allGames.Add(new GameViewModel(game));

        ApplyFilter();

        Status = manifest.Games.Count == 0
            ? "No cached library. Add a folder to scan for PS5 content."
            : $"{manifest.Games.Count} item(s) loaded from cache (saved {manifest.CreatedUtc:g} UTC).";
    }

    /// <summary>Clears the on-disk library cache and the currently displayed list.</summary>
    public void ClearCachedLibrary()
    {
        try
        {
            _libraryCache.Clear();
        }
        catch (Exception)
        {
            // Best-effort; nothing useful to surface if the delete itself fails.
        }

        _allGames.Clear();
        Games.Clear();
        SelectedGameFiles.Clear();
        Status = "Library cache cleared.";
    }

    public void AddFolder(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || Folders.Contains(folder)) return;
        Folders.Add(folder);
        RaisePropertyChanged(nameof(CanScan));
        Status = $"{Folders.Count} folder(s) queued. Press Scan.";
    }

    public void RemoveFolder(string folder)
    {
        if (!Folders.Remove(folder)) return;
        RaisePropertyChanged(nameof(CanScan));
    }

    public async Task ScanAsync()
    {
        if (IsScanning || Folders.Count == 0) return;

        _scanCancellation?.Cancel();
        _scanCancellation = new CancellationTokenSource();
        CancellationToken token = _scanCancellation.Token;

        IsScanning = true;
        Status = "Scanning...";
        _allGames.Clear();
        Games.Clear();
        SelectedGameFiles.Clear();

        try
        {
            var progress = new Progress<Ps5ScanProgress>(p =>
                Status = p.Total > 0 ? $"Scanning {p.Processed}/{p.Total}..." : "Scanning...");

            Ps5ScanResult result = await _scanner.ScanAsync(
                Folders.ToList(), recursive: true, cached: null, progress, token);

            // The scanner skips sizing loose dumps because that walks every dump folder.
            // The list shows a size column, so resolve it here, off the UI thread.
            await Task.Run(() =>
            {
                foreach (Ps5GameInfo game in result.Games)
                {
                    token.ThrowIfCancellationRequested();
                    ResolveSize(game, token);
                }
            }, token);

            foreach (Ps5GameInfo game in result.Games.OrderBy(g => g.Title, StringComparer.OrdinalIgnoreCase))
                _allGames.Add(new GameViewModel(game));

            ApplyFilter();

            try
            {
                _libraryCache.Save(result.Games);
            }
            catch (Exception)
            {
                // Caching is best-effort; a failed save should not fail the scan the user just ran.
            }

            Status = result.Games.Count == 0
                ? "No PS5 content found."
                : $"{result.Games.Count} item(s)."
                  + (result.Errors.Count > 0 ? $" {result.Errors.Count} warning(s)." : string.Empty);
        }
        catch (OperationCanceledException)
        {
            Status = "Scan cancelled.";
        }
        catch (Exception ex)
        {
            Status = $"Scan failed: {ex.Message}";
        }
        finally
        {
            IsScanning = false;
        }
    }

    public void CancelScan() => _scanCancellation?.Cancel();

    /// <summary>
    /// Rebuilds <see cref="SearchText"/> from the quick-filter dropdowns plus whatever free-text
    /// tokens the user already typed (any existing <c>format:</c>/<c>category:</c> token is replaced
    /// rather than duplicated, so toggling a dropdown twice doesn't accumulate stale tokens).
    /// </summary>
    private void ApplyQuickFilters()
    {
        IEnumerable<string> tokens = (_searchText ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(t => !t.StartsWith("format:", StringComparison.OrdinalIgnoreCase) &&
                        !t.StartsWith("category:", StringComparison.OrdinalIgnoreCase));

        var parts = new List<string>(tokens);
        if (!string.IsNullOrEmpty(_formatFilter) && _formatFilter != FormatOptions[0])
            parts.Insert(0, $"format:{_formatFilter}");
        if (!string.IsNullOrEmpty(_categoryFilter) && _categoryFilter != CategoryOptions[0])
            parts.Insert(0, $"category:{_categoryFilter}");

        SearchText = string.Join(' ', parts);
    }

    private void ApplyFilter()
    {
        string query = _searchText.Trim();
        IEnumerable<GameViewModel> filtered = string.IsNullOrEmpty(query)
            ? _allGames
            : _allGames.Where(g => Ps5LibraryQuery.Matches(g.Model, query));

        Games.Clear();
        foreach (GameViewModel game in filtered)
        {
            Games.Add(game);
            RequestIcon(game);
        }
    }

    /// <summary>
    /// Kicks off (or reuses) the icon decode for a row. Cheap to call per row on every filter
    /// refresh: the artwork service dedupes in-flight/cached loads, and the per-VM flag here avoids
    /// even that lookup once a row already has an answer (icon or confirmed none).
    /// </summary>
    private void RequestIcon(GameViewModel game)
    {
        if (game.IconRequested) return;
        Bitmap? cached = _artwork.TryGetCached(game.Model);
        if (cached is not null)
        {
            game.Icon = cached;
            game.IconRequested = true;
            return;
        }

        game.IconRequested = true;
        _artwork.RequestAsync(game.Model, bitmap => game.Icon = bitmap);
    }

    private async Task LoadFilesAsync(GameViewModel? game)
    {
        SelectedGameFiles.Clear();
        if (game is null) return;

        try
        {
            List<FileEntryViewModel> files = await Task.Run(() =>
            {
                using IReadOnlyGameFileSystem fs = GameFileSystem.Open(game.Model);
                return fs.Files
                    .OrderBy(f => f.RelativePath, StringComparer.OrdinalIgnoreCase)
                    .Select(f => new FileEntryViewModel(f.RelativePath, f.Size))
                    .ToList();
            });

            foreach (FileEntryViewModel file in files) SelectedGameFiles.Add(file);
        }
        catch (Exception ex)
        {
            Status = $"Could not read files: {ex.Message}";
        }
    }

    private static void ResolveSize(Ps5GameInfo game, CancellationToken token)
    {
        if (game.SourceKind != Ps5SourceKind.LooseDump || game.SourceSize > 0) return;
        try
        {
            using IReadOnlyGameFileSystem fs = GameFileSystem.Open(game, token);
            game.SourceSize = fs.Files.Sum(file => file.Size);
        }
        catch (Exception)
        {
            // A dump we cannot enumerate still lists; it just has no size.
        }
    }
}

public sealed class FileEntryViewModel(string path, long size)
{
    public string Path { get; } = path;
    public long Size { get; } = size;
    public string SizeText { get; } = GameViewModel.FormatSize(size);
}
