using System.Collections.ObjectModel;
using System.Windows.Input;
using PS5PKGTool.Core.Models;
using PS5PKGTool.Core.Services;

namespace PS5PKGTool.Gui.ViewModels;

public sealed class MainWindowViewModel : ViewModelBase
{
    private readonly Ps5LibraryScanner _scanner = new();
    private CancellationTokenSource? _scanCancellation;

    private string _status = "Add a folder to scan for PS5 content.";
    private bool _isScanning;
    private GameViewModel? _selectedGame;
    private string _searchText = string.Empty;

    public ObservableCollection<string> Folders { get; } = [];
    public ObservableCollection<GameViewModel> Games { get; } = [];
    public ObservableCollection<FileEntryViewModel> SelectedGameFiles { get; } = [];

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

    private void ApplyFilter()
    {
        string query = _searchText.Trim();
        IEnumerable<GameViewModel> filtered = string.IsNullOrEmpty(query)
            ? _allGames
            : _allGames.Where(g =>
                g.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                g.TitleId.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                g.ContentId.Contains(query, StringComparison.OrdinalIgnoreCase));

        Games.Clear();
        foreach (GameViewModel game in filtered) Games.Add(game);
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
