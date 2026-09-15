using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using PS5PKGTool.Gui.ViewModels;

namespace PS5PKGTool.Gui.Views;

public partial class OperationDialog : Window
{
    public OperationDialog() => InitializeComponent();

    private OperationDialogViewModel? ViewModel => DataContext as OperationDialogViewModel;

    private async void OnBrowseSource(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is null) return;

        IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select a source package",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Sony package") { Patterns = ["*.pkg"] }],
        });

        if (files.Count == 0) return;
        string? path = files[0].TryGetLocalPath();
        if (!string.IsNullOrEmpty(path)) ViewModel.SourcePath = path;
    }

    private async void OnBrowseOutput(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is null) return;

        if (ViewModel.IsExtract)
        {
            IReadOnlyList<IStorageFolder> folders = await StorageProvider.OpenFolderPickerAsync(
                new FolderPickerOpenOptions { Title = "Select an output folder", AllowMultiple = false });

            if (folders.Count == 0) return;
            string? path = folders[0].TryGetLocalPath();
            if (!string.IsNullOrEmpty(path)) ViewModel.OutputPath = path;
            return;
        }

        string suggestedExtension = ViewModel.IsBuild ? "pkg" : ViewModel.Target.ToString().ToLowerInvariant();
        IStorageFile? file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Select an output file",
            DefaultExtension = suggestedExtension,
            FileTypeChoices = [new FilePickerFileType(suggestedExtension.ToUpperInvariant())
            {
                Patterns = [$"*.{suggestedExtension}"],
            }],
        });

        if (file is null) return;
        string? outputPath = file.TryGetLocalPath();
        if (!string.IsNullOrEmpty(outputPath)) ViewModel.OutputPath = outputPath;
    }

    private async void OnStart(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not null) await ViewModel.RunAsync();
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => ViewModel?.Cancel();

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
