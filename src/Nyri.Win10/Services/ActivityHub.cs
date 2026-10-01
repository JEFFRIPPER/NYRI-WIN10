using System.Collections.ObjectModel;
using Nyri.Win10.Models;

namespace Nyri.Win10.Services;

public sealed class ActivityHub
{
    public ObservableCollection<IslandActivity> Activities { get; } = new();

    public event EventHandler? Changed;

    public void Upsert(IslandActivity activity)
    {
        var existing = Activities.FirstOrDefault(x => x.Id == activity.Id);
        if (existing is not null)
        {
            var index = Activities.IndexOf(existing);
            Activities[index] = activity;
        }
        else
        {
            Activities.Insert(0, activity);
        }

        Trim();
        Changed?.Invoke(this, EventArgs.Empty);
    }
    public IslandActivity? Primary =>
        Activities.OrderByDescending(x => x.UpdatedAt).FirstOrDefault(x => x.IsActive);

    public void Remove(string id)
    {
        var existing = Activities.FirstOrDefault(x => x.Id == id);
        if (existing is null) return;
        Activities.Remove(existing);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Trim()
    {
        while (Activities.Count > 8)
            Activities.RemoveAt(Activities.Count - 1);
    }
}