using System.Collections.ObjectModel;
using System.Windows.Threading;
using Nyri.Win10.Models;

namespace Nyri.Win10.Services;

public sealed class ActivityHub
{
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private volatile bool _sealed;
    public ObservableCollection<IslandActivity> Activities { get; } = new();
    public event EventHandler? Changed;
    public bool IsSealed => _sealed;

    // Freeze the snapshot before cancelling providers. Already queued callbacks
    // still execute, but cannot change state after the runtime has shut down.
    public void Seal() => _sealed = true;

    public void Upsert(IslandActivity activity)
    {
        if (_sealed || _dispatcher.HasShutdownStarted) return;
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.BeginInvoke(() => Upsert(activity));
            return;
        }
        var existing = Activities.FirstOrDefault(x => x.Id == activity.Id);
        // Providers may reconcile periodically. Unchanged state must not reorder priorities.
        if (existing is not null && existing with { UpdatedAt = activity.UpdatedAt } == activity) return;
        if (existing is not null) Activities[Activities.IndexOf(existing)] = activity;
        else Activities.Insert(0, activity);
        Trim();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public IslandActivity? Primary => Activities.Where(x => x.IsActive)
        .OrderBy(x => x.Ambient)
        .ThenByDescending(x => x.Priority)
        .ThenByDescending(x => x.UpdatedAt).FirstOrDefault();

    public void Remove(string id)
    {
        if (_sealed || _dispatcher.HasShutdownStarted) return;
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.BeginInvoke(() => Remove(id));
            return;
        }
        var existing = Activities.FirstOrDefault(x => x.Id == id);
        if (existing is null) return;
        Activities.Remove(existing);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Trim()
    {
        // Never evict active providers: otherwise a clipboard event can hide capture usage.
        foreach (var item in Activities.Where(x => !x.IsActive).Skip(8).ToArray())
            Activities.Remove(item);
    }
}
