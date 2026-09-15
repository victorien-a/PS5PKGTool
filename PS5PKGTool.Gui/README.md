# PS5 PKG Tool GUI (Avalonia)

A cross-platform desktop UI for the PS5 PKG Tool engine, built on
[Avalonia](https://avaloniaui.net/) (MIT licensed). It runs on Linux, macOS and
Windows from a single codebase, and depends only on the portable engine
projects rather than on the WinForms UI.

This is an early shell: it scans library folders, lists what it finds, and shows
per-item metadata and the file listing. It does not yet cover the conversion and
building features of the Windows app.

## Build and run

Requires the .NET 10 SDK.

```bash
dotnet run --project PS5PKGTool.Gui/PS5PKGTool.Gui.csproj
```

Self-contained binary:

```bash
dotnet publish PS5PKGTool.Gui/PS5PKGTool.Gui.csproj \
  -c Release -r linux-x64 --self-contained \
  -p:PublishSingleFile=true
```

Replace `linux-x64` with `osx-arm64`, `osx-x64` or `win-x64` as needed.

## Architecture

- `ViewModels/` — plain `INotifyPropertyChanged` view models. There is no MVVM
  framework dependency; `ViewModelBase` provides change notification.
- `Views/` — Avalonia XAML with compiled bindings enabled.
- Scanning and file enumeration run on background threads, so the UI stays
  responsive on large libraries.

## Notes

- Loose dumps are sized by walking their file inventory, because the scanner
  deliberately skips that during the scan itself.
- macOS builds are unsigned. Gatekeeper will block them until you either sign
  them with an Apple Developer certificate or clear the quarantine attribute:
  `xattr -dr com.apple.quarantine /path/to/app`
