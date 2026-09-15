using System.Collections.ObjectModel;
using Avalonia.Threading;
using PS5PKGTool.Core.Tasks;

namespace PS5PKGTool.Gui.ViewModels;

/// <summary>
/// Wraps a <see cref="PackageTaskQueue"/> for binding: exposes an <see cref="ObservableCollection{T}"/>
/// of per-task view models kept in sync with the queue. Two event sources drive the sync:
/// <see cref="PackageTaskQueue.TasksChanged"/> (fired when tasks are added/removed/reordered) and
/// each individual <see cref="QueuedPackageTask.Changed"/> (fired on every status/progress update
/// while a task runs -- the queue itself does NOT re-raise <c>TasksChanged</c> for those, so this
/// view model must listen to both or progress/completion never reaches the bound collection). Both
/// fire from background threads (the worker loop, task callbacks), so every handler here marshals
/// onto the UI thread via <see cref="Dispatcher.UIThread"/> before touching the collection.
/// </summary>
public sealed class TaskQueueViewModel : ViewModelBase, IAsyncDisposable
{
    private readonly PackageTaskQueue _queue;
    private readonly HashSet<QueuedPackageTask> _subscribed = [];
    private string _status = string.Empty;

    /// <param name="queue">Supply an existing queue (e.g. for tests); otherwise a new one is created.</param>
    /// <param name="persistencePath">
    /// Overrides where task state is persisted; pass a path under a temp directory in tests so
    /// probes never touch the real user data dir under ~/.local/share/PS5PKGTool. Defaults to the
    /// platform data dir, mirroring <c>Ps5LibraryCache</c>'s own default-location convention.
    /// </param>
    public TaskQueueViewModel(PackageTaskQueue? queue = null, string? persistencePath = null)
    {
        _queue = queue ?? new PackageTaskQueue();
        _queue.TasksChanged += OnTasksChanged;

        string path = persistencePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PS5PKGTool", "tasks.json");
        _queue.EnablePersistence(path);

        Sync();
    }

    public ObservableCollection<TaskItemViewModel> Tasks { get; } = [];

    /// <summary>True when the queue holds anything, so the view can hide the panel entirely.</summary>
    public bool HasTasks => Tasks.Count > 0;

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    /// <summary>Exposes the underlying queue for callers (e.g. MainWindowViewModel) that enqueue work.</summary>
    public PackageTaskQueue Queue => _queue;

    public bool Cancel(string id) => _queue.Cancel(id);

    public bool Retry(string id) => _queue.Retry(id);

    public bool Remove(string id)
    {
        bool removed = _queue.Remove(id);
        if (removed) SyncOnUiThread();
        return removed;
    }

    public int ClearCompleted()
    {
        int removed = _queue.ClearCompleted();
        if (removed > 0) SyncOnUiThread();
        return removed;
    }

    private void OnTasksChanged(object? sender, EventArgs e) => SyncOnUiThread();

    /// <summary>
    /// Per-task progress/status handler. Fired from the queue's worker thread on every
    /// <see cref="QueuedPackageTask.Apply"/> call (running %, completion, failure, cancellation),
    /// none of which raise the queue-level <see cref="PackageTaskQueue.TasksChanged"/> event.
    /// </summary>
    private void OnTaskChanged(object? sender, EventArgs e) => SyncOnUiThread();

    private void SyncOnUiThread()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            Sync();
        }
        else
        {
            Dispatcher.UIThread.Post(Sync);
        }
    }

    private void Sync()
    {
        IReadOnlyList<QueuedPackageTask> current = _queue.Tasks;

        // Reconcile in place instead of clear+rebuild so selection/scroll position in a bound
        // list is not disturbed on every progress tick.
        var currentIds = new HashSet<string>(current.Select(t => t.Id), StringComparer.Ordinal);

        for (int i = Tasks.Count - 1; i >= 0; i--)
            if (!currentIds.Contains(Tasks[i].Id))
                Tasks.RemoveAt(i);

        // Drop subscriptions for tasks the queue no longer holds (removed/cleared).
        _subscribed.RemoveWhere(task =>
        {
            if (currentIds.Contains(task.Id)) return false;
            task.Changed -= OnTaskChanged;
            return true;
        });

        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < Tasks.Count; i++) index[Tasks[i].Id] = i;

        for (int i = 0; i < current.Count; i++)
        {
            QueuedPackageTask task = current[i];

            // Subscribe once per task so every status/progress tick calls back into Sync.
            if (_subscribed.Add(task)) task.Changed += OnTaskChanged;

            if (index.TryGetValue(task.Id, out int existingPosition))
            {
                Tasks[existingPosition].Update(task);
            }
            else
            {
                var item = new TaskItemViewModel(task);
                if (i >= Tasks.Count) Tasks.Add(item);
                else Tasks.Insert(i, item);
            }
        }

        RaisePropertyChanged(nameof(HasTasks));

        Status = Tasks.Count == 0
            ? "No tasks."
            : $"{Tasks.Count} task(s): "
              + $"{Tasks.Count(t => t.Status == PackageTaskStatus.Running)} running, "
              + $"{Tasks.Count(t => t.Status == PackageTaskStatus.Queued)} queued, "
              + $"{Tasks.Count(t => t.Status == PackageTaskStatus.Failed)} failed.";
    }

    public ValueTask DisposeAsync()
    {
        _queue.TasksChanged -= OnTasksChanged;
        foreach (QueuedPackageTask task in _subscribed) task.Changed -= OnTaskChanged;
        _subscribed.Clear();
        return _queue.DisposeAsync();
    }
}

/// <summary>Display projection of a <see cref="QueuedPackageTask"/> for binding in a list/grid.</summary>
public sealed class TaskItemViewModel : ViewModelBase
{
    private PackageTaskStatus _status;
    private string _statusText = string.Empty;
    private string _message = string.Empty;
    private double _percent;
    private bool _canCancel;
    private bool _canRetry;
    private bool _canRemove;

    public TaskItemViewModel(QueuedPackageTask task)
    {
        Id = task.Id;
        Type = task.Type;
        DisplayName = task.DisplayName;
        SourcePath = task.SourcePath;
        OutputPath = task.OutputPath;
        Operation = task.Operation;
        FormatRoute = task.FormatRoute;
        Update(task);
    }

    public string Id { get; }
    public string Type { get; }
    public string DisplayName { get; }
    public string SourcePath { get; }
    public string OutputPath { get; }
    public string Operation { get; }
    public string FormatRoute { get; }

    public PackageTaskStatus Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string Message
    {
        get => _message;
        private set => SetProperty(ref _message, value);
    }

    /// <summary>Overall task progress in the 0-100 range, suitable for a ProgressBar.</summary>
    public double Percent
    {
        get => _percent;
        private set => SetProperty(ref _percent, value);
    }

    public bool CanCancel
    {
        get => _canCancel;
        private set => SetProperty(ref _canCancel, value);
    }

    public bool CanRetry
    {
        get => _canRetry;
        private set => SetProperty(ref _canRetry, value);
    }

    public bool CanRemove
    {
        get => _canRemove;
        private set => SetProperty(ref _canRemove, value);
    }

    internal void Update(QueuedPackageTask task)
    {
        Status = task.Status;
        StatusText = task.Status.ToString();
        Message = task.Message;
        Percent = Math.Clamp(task.Progress.TaskPercent, 0d, 1d) * 100d;
        CanCancel = task.Status is PackageTaskStatus.Queued or PackageTaskStatus.Running;
        CanRetry = task.Status is PackageTaskStatus.Failed or PackageTaskStatus.Cancelled or PackageTaskStatus.Interrupted;
        CanRemove = task.Status is not (PackageTaskStatus.Running or PackageTaskStatus.Cancelling);
    }
}
