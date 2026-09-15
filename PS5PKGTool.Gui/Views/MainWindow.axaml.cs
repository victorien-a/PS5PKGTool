using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using PS5PKGTool.Gui.ViewModels;

namespace PS5PKGTool.Gui.Views;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    private MainWindowViewModel? ViewModel => DataContext as MainWindowViewModel;

    private async void OnAddFolder(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is null) return;

        IReadOnlyList<IStorageFolder> folders = await StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions
            {
                Title = "Select a library folder",
                AllowMultiple = true,
            });

        foreach (IStorageFolder folder in folders)
        {
            string? path = folder.TryGetLocalPath();
            if (!string.IsNullOrEmpty(path)) ViewModel.AddFolder(path);
        }
    }

    private void OnRemoveFolder(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is null) return;
        if (FolderList.SelectedItem is string folder) ViewModel.RemoveFolder(folder);
    }

    private async void OnScan(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not null) await ViewModel.ScanAsync();
    }

    private void OnCancelScan(object? sender, RoutedEventArgs e) => ViewModel?.CancelScan();

    private void OnCancelTask(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { Tag: string id }) ViewModel?.TaskQueue.Cancel(id);
    }

    private void OnRetryTask(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { Tag: string id }) ViewModel?.TaskQueue.Retry(id);
    }

    private void OnClearCompletedTasks(object? sender, RoutedEventArgs e) =>
        ViewModel?.TaskQueue.ClearCompleted();

    private async void OnConvert(object? sender, RoutedEventArgs e)
    {
        GameViewModel? game = ViewModel?.SelectedGame;
        if (game is null) return;
        var dialogVm = new OperationDialogViewModel(OperationKind.Convert, game.Model);
        var dialog = new OperationDialog { DataContext = dialogVm };
        await dialog.ShowDialog(this);
    }

    private async void OnExtract(object? sender, RoutedEventArgs e)
    {
        GameViewModel? game = ViewModel?.SelectedGame;
        if (game is null) return;
        var dialogVm = new OperationDialogViewModel(OperationKind.Extract, game.Model);
        var dialog = new OperationDialog { DataContext = dialogVm };
        await dialog.ShowDialog(this);
    }

    private async void OnBuild(object? sender, RoutedEventArgs e)
    {
        var dialogVm = new OperationDialogViewModel(OperationKind.Build, ViewModel?.SelectedGame?.Model);
        var dialog = new OperationDialog { DataContext = dialogVm };
        await dialog.ShowDialog(this);
    }
}
