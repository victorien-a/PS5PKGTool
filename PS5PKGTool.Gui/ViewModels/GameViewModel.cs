using PS5PKGTool.Core.Models;

namespace PS5PKGTool.Gui.ViewModels;

/// <summary>A single scanned item, shaped for display.</summary>
public sealed class GameViewModel(Ps5GameInfo game)
{
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
