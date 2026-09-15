using Avalonia.Media.Imaging;
using PS5PKGTool.Core.Models;

namespace PS5PKGTool.Gui.ViewModels;

/// <summary>A single scanned item, shaped for display.</summary>
public sealed class GameViewModel(Ps5GameInfo game) : ViewModelBase
{
    private Bitmap? _icon;
    private bool _iconRequested;

    public Ps5GameInfo Model { get; } = game;

    public string Title => string.IsNullOrWhiteSpace(Model.Title) ? "(untitled)" : Model.Title;
    public string TitleId => Model.TitleId;
    public string ContentId => Model.ContentId;
    public string Format => Model.SourceKind switch
    {
        Ps5SourceKind.LooseDump => "dump",
        Ps5SourceKind.SonyPackage => "pkg",
        Ps5SourceKind.Ffpfsc => "ffpfsc",
        Ps5SourceKind.FilesystemImage => "exfat",
        Ps5SourceKind.Ffpkg => "ffpkg",
        _ => Model.SourceKind.ToString().ToLowerInvariant(),
    };
    public string ContentVersion => Model.ContentVersion;
    public string RequiredFirmware => Model.RequiredSystemSoftware;
    public string Category => Model.ApplicationCategory;
    public string Path => Model.RootPath;
    public string SizeText => FormatSize(Model.SourceSize);

    /// <summary>
    /// Decoded icon bitmap, or null until loaded (or if none/corrupt). Set by
    /// <see cref="Services.GameArtworkService"/>; the view falls back to a placeholder while null.
    /// </summary>
    public Bitmap? Icon
    {
        get => _icon;
        internal set => SetProperty(ref _icon, value);
    }

    /// <summary>Guards against re-requesting the same icon load from multiple UI touch points.</summary>
    internal bool IconRequested
    {
        get => _iconRequested;
        set => _iconRequested = value;
    }

    public static string FormatSize(long bytes)
    {
        if (bytes <= 0) return "0 B";
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return unit == 0 ? $"{bytes} B" : $"{value:0.##} {units[unit]}";
    }
}
