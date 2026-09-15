using System.Text.Json;
using PS5PKGTool.Core.Builders;
using PS5PKGTool.Core.Models;
using PS5PKGTool.Core.Services;
using PS5PKGTool.Ffpfsc;

namespace PS5PKGTool.Gui.ViewModels;

/// <summary>The operation an <see cref="OperationDialogViewModel"/> runs.</summary>
public enum OperationKind
{
    Convert,
    Extract,
    Build,
}

/// <summary>
/// Drives a single long-running operation (convert / extract / build) for <c>OperationDialog</c>.
/// There is no MVVM framework in this project, so this follows the same
/// <see cref="ViewModelBase.SetProperty{T}"/> pattern as <see cref="MainWindowViewModel"/>.
/// </summary>
public sealed class OperationDialogViewModel : ViewModelBase
{
    private readonly Ps5GameInfo? _game;
    private CancellationTokenSource? _cancellation;

    private string _sourcePath = string.Empty;
    private string _outputPath = string.Empty;
    private string _contentId = string.Empty;
    private Ps5ImageConversionTarget _target = Ps5ImageConversionTarget.Exfat;
    private bool _isRunning;
    private bool _isIndeterminate;
    private double _progressPercent;
    private string _statusText = string.Empty;
    private bool _hasResult;
    private bool _succeeded;
    private string _resultText = string.Empty;

    public OperationDialogViewModel(OperationKind kind, Ps5GameInfo? game)
    {
        Kind = kind;
        _game = game;

        if (game is not null) _sourcePath = game.RootPath;

        if (kind == OperationKind.Build)
        {
            string? paramContentId = TryReadContentIdFromParam(_sourcePath);
            if (paramContentId is not null) _contentId = paramContentId;
        }
    }

    public OperationKind Kind { get; }

    public string Title => Kind switch
    {
        OperationKind.Convert => "Convert image",
        OperationKind.Extract => "Extract",
        OperationKind.Build => "Build package",
        _ => "Operation",
    };

    public bool IsConvert => Kind == OperationKind.Convert;
    public bool IsExtract => Kind == OperationKind.Extract;
    public bool IsBuild => Kind == OperationKind.Build;

    /// <summary>For Extract/Build the source is fixed to the selected item and not editable.</summary>
    public bool SourceIsEditable => Kind == OperationKind.Convert;

    public string SourcePath
    {
        get => _sourcePath;
        set
        {
            if (!SetProperty(ref _sourcePath, value)) return;
            RaisePropertyChanged(nameof(CanStart));
        }
    }

    public string OutputPath
    {
        get => _outputPath;
        set
        {
            if (!SetProperty(ref _outputPath, value)) return;
            RaisePropertyChanged(nameof(CanStart));
        }
    }

    public string ContentId
    {
        get => _contentId;
        set
        {
            if (!SetProperty(ref _contentId, value)) return;
            RaisePropertyChanged(nameof(CanStart));
        }
    }

    public IReadOnlyList<Ps5ImageConversionTarget> ConversionTargets { get; } =
        [Ps5ImageConversionTarget.Exfat, Ps5ImageConversionTarget.Ffpkg, Ps5ImageConversionTarget.Ffpfsc];

    public Ps5ImageConversionTarget Target
    {
        get => _target;
        set => SetProperty(ref _target, value);
    }

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (!SetProperty(ref _isRunning, value)) return;
            RaisePropertyChanged(nameof(CanStart));
            RaisePropertyChanged(nameof(CanClose));
        }
    }

    public bool IsIndeterminate
    {
        get => _isIndeterminate;
        private set => SetProperty(ref _isIndeterminate, value);
    }

    public double ProgressPercent
    {
        get => _progressPercent;
        private set => SetProperty(ref _progressPercent, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public bool HasResult
    {
        get => _hasResult;
        private set => SetProperty(ref _hasResult, value);
    }

    public bool Succeeded
    {
        get => _succeeded;
        private set => SetProperty(ref _succeeded, value);
    }

    public string ResultText
    {
        get => _resultText;
        private set => SetProperty(ref _resultText, value);
    }

    public bool CanClose => !IsRunning;

    public bool CanStart =>
        !IsRunning &&
        !string.IsNullOrWhiteSpace(_sourcePath) &&
        !string.IsNullOrWhiteSpace(_outputPath) &&
        (Kind != OperationKind.Build || !string.IsNullOrWhiteSpace(_contentId));

    /// <summary>Runs the selected operation. Never throws; failures are reported via <see cref="ResultText"/>.</summary>
    public async Task RunAsync()
    {
        if (!CanStart) return;

        _cancellation = new CancellationTokenSource();
        CancellationToken token = _cancellation.Token;

        IsRunning = true;
        HasResult = false;
        IsIndeterminate = true;
        ProgressPercent = 0;
        StatusText = "Starting...";

        try
        {
            switch (Kind)
            {
                case OperationKind.Convert:
                    await RunConvertAsync(token).ConfigureAwait(true);
                    break;
                case OperationKind.Extract:
                    await RunExtractAsync(token).ConfigureAwait(true);
                    break;
                case OperationKind.Build:
                    await RunBuildAsync(token).ConfigureAwait(true);
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            StatusText = "Cancelled.";
            HasResult = true;
            Succeeded = false;
            ResultText = "The operation was cancelled.";
        }
        catch (Exception ex)
        {
            StatusText = "Failed.";
            HasResult = true;
            Succeeded = false;
            ResultText = ex.Message;
        }
        finally
        {
            IsRunning = false;
            IsIndeterminate = false;
            _cancellation = null;
        }
    }

    public void Cancel() => _cancellation?.Cancel();

    private async Task RunConvertAsync(CancellationToken token)
    {
        string source = _sourcePath;
        string output = _outputPath;
        Ps5ImageConversionTarget target = _target;

        var progress = new Progress<Ps5ImageConversionProgress>(p =>
        {
            IsIndeterminate = p.Total <= 0;
            if (p.Total > 0) ProgressPercent = Math.Clamp(p.Completed * 100.0 / p.Total, 0, 100);
            StatusText = $"{p.Stage} - {FormatSize(p.Completed)}" + (p.Total > 0 ? $" / {FormatSize(p.Total)}" : string.Empty);
        });

        Ps5ImageConversionResult result = await SonyPackageImageConversion.ConvertAsync(
            source, output, target, overwrite: true, progress, token).ConfigureAwait(true);

        HasResult = true;
        Succeeded = true;
        StatusText = "Done.";
        ResultText = $"Converted to {result.Target} ({result.FileCount} file(s), {FormatSize(result.OutputBytes)}).\nOutput: {result.OutputPath}";
    }

    private async Task RunExtractAsync(CancellationToken token)
    {
        if (_game is null) throw new InvalidOperationException("No item selected to extract.");

        string output = Path.GetFullPath(_outputPath);
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
            throw new IOException($"Destination already exists and is not empty: {output}");

        Ps5GameInfo game = _game;

        (int written, long completed) = await Task.Run(async () =>
        {
            // Use the engine's merged view of the container so metadata such as sce_sys/param.json,
            // which lives only in the CNT container and not the inner PFS, is included. This mirrors
            // PS5PKGTool.Cli.ExtractCommand exactly.
            using IReadOnlyGameFileSystem fs = GameFileSystem.Open(game, token);
            IReadOnlyList<GameFileRecord> entries = fs.Files;
            if (entries.Count == 0) throw new InvalidOperationException("The source contains no readable files.");

            long totalBytes = entries.Sum(e => e.Size);
            long done = 0;
            int count = 0;

            foreach (GameFileRecord entry in entries)
            {
                token.ThrowIfCancellationRequested();

                string target = ResolveContainedPath(output, entry.RelativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);

                await using (Stream input = fs.OpenRead(entry.RelativePath))
                await using (var file = new FileStream(target, FileMode.Create, FileAccess.Write,
                    FileShare.None, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    byte[] buffer = new byte[1024 * 1024];
                    int read;
                    while ((read = await input.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
                    {
                        await file.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
                        done += read;

                        long capturedDone = done;
                        long capturedTotal = totalBytes;
                        string stage = $"Extracting {entry.RelativePath}";
                        ReportExtractProgress(stage, capturedDone, capturedTotal);
                    }
                }

                count++;
            }

            return (count, done);
        }, token).ConfigureAwait(true);

        HasResult = true;
        Succeeded = true;
        StatusText = "Done.";
        ResultText = $"Extracted {written} file(s), {FormatSize(completed)}.\nDestination: {output}";
    }

    /// <summary>Marshals a progress update raised from the background extraction loop onto this view model.</summary>
    private void ReportExtractProgress(string stage, long completed, long total)
    {
        IsIndeterminate = total <= 0;
        if (total > 0) ProgressPercent = Math.Clamp(completed * 100.0 / total, 0, 100);
        StatusText = $"{stage} - {FormatSize(completed)}" + (total > 0 ? $" / {FormatSize(total)}" : string.Empty);
    }

    private async Task RunBuildAsync(CancellationToken token)
    {
        string source = _sourcePath;
        string output = _outputPath;
        var options = new SonyDebugPackageBuildOptions { ContentId = _contentId };

        var progress = new Progress<SonyDebugPackageProgress>(p =>
        {
            IsIndeterminate = p.TotalBytes <= 0;
            if (p.TotalBytes > 0) ProgressPercent = Math.Clamp(p.CompletedBytes * 100.0 / p.TotalBytes, 0, 100);
            StatusText = $"{p.Stage} - {FormatSize(p.CompletedBytes)}" +
                          (p.TotalBytes > 0 ? $" / {FormatSize(p.TotalBytes)}" : string.Empty);
        });

        SonyDebugPackageBuildResult result = await SonyDebugPackageBuilder.CreateFromDirectoryAsync(
            source, output, options, progress, token).ConfigureAwait(true);

        HasResult = true;
        Succeeded = true;
        StatusText = "Done.";
        ResultText = $"Built {result.ContentId} ({result.SourceFiles} file(s), {FormatSize(result.SourceBytes)} source).\n" +
                     $"Output: {result.OutputPath} ({FormatSize(result.PackageSize)})" +
                     (result.UsesDefaultPasscode ? "\nWarning: package uses the default passcode." : string.Empty);
    }

    /// <summary>Guards against path traversal from a crafted container. Mirrors ExtractCommand's guard.</summary>
    private static string ResolveContainedPath(string root, string relativePath)
    {
        string full = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        string prefix = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.Ordinal))
            throw new IOException($"The container entry '{relativePath}' escapes the destination directory.");
        return full;
    }

    /// <summary>Reads sce_sys/param.json's contentId to default the Build dialog's content id field.</summary>
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
            // Fall through; the user enters a content id manually.
        }
        return null;
    }

    private static string FormatSize(long bytes) => GameViewModel.FormatSize(bytes);
}
