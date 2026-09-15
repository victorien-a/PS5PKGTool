using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PS5PKGTool.Gui.ViewModels;

/// <summary>
/// Minimal INotifyPropertyChanged base. The app needs change notification but not a full
/// MVVM framework, so this avoids taking a dependency on one.
/// </summary>
public abstract class ViewModelBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void RaisePropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        RaisePropertyChanged(propertyName);
        return true;
    }
}
