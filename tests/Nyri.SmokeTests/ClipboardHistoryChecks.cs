using System.Windows.Threading;
using Nyri.Win10.Models;
using Nyri.Win10.Services;

internal static class ClipboardHistoryChecks
{
    public static void Run(Action<bool, string> check)
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        using var history = new ClipboardHistoryService(dispatcher);
        var changes = 0;
        var notificationsUseDispatcher = true;
        history.Changed += (_, _) =>
        {
            changes++;
            notificationsUseDispatcher &= dispatcher.CheckAccess();
        };

        check(!history.Enabled && !history.Add("not retained") && history.Entries.Count == 0 && changes == 0,
            "Clipboard history starts disabled and retains no copied text");
        history.SetEnabled(true);
        const string multiline = "  first\r\nsecond\nthird\rfourth\t\ud83d\ude42  ";
        check(history.Add(multiline) && history.Entries[0].Text == multiline,
            "Clipboard history preserves complete text, whitespace, Unicode and mixed line endings");
        var original = history.Entries[0];
        var originalSnapshot = history.Entries;
        history.Add("second value");
        history.Add(multiline);
        var duplicate = history.Entries[0];
        check(history.Entries.Count == 2 && duplicate.Id == original.Id && duplicate.CopiedAt > original.CopiedAt &&
                history.Entries[1].Text == "second value",
            "Copying an exact duplicate moves its existing ID to the front and refreshes its timestamp");
        check(originalSnapshot.Count == 1 && originalSnapshot[0] == original,
            "An existing clipboard snapshot stays unchanged after later copies");
        var readOnly = originalSnapshot is IList<ClipboardEntry> list && list.IsReadOnly;
        check(readOnly, "Clipboard snapshots expose a read-only collection");
        check(history.Add(multiline.ToUpperInvariant()) && history.Entries.Count == 3,
            "Clipboard deduplication compares the exact value rather than display previews or letter case");

        history.Clear();
        for (var index = 0; index < 41; index++) history.Add($"entry {index}");
        check(history.Entries.Count == 40 && history.Entries[0].Text == "entry 40" && history.Entries[^1].Text == "entry 1",
            "Clipboard capacity evicts the oldest complete entry and retains the forty newest values");
        var beforeOversize = history.Entries;
        var changesBeforeOversize = changes;
        check(!history.Add(new string('x', 65_537)) && ReferenceEquals(beforeOversize, history.Entries) && changes == changesBeforeOversize,
            "Clipboard history rejects oversized values without truncating them or changing existing entries");
        var boundary = new string('x', 65_536);
        check(history.Add(boundary) && history.Entries[0].Text == boundary && history.Entries[0].Text.Length == 65_536,
            "Clipboard history retains the full maximum-length UTF-16 value");
        var target = history.Entries[7];
        var changesBeforeMissingRemoval = changes;
        var missingRemoved = history.Remove(Guid.NewGuid());
        check(!missingRemoved && changes == changesBeforeMissingRemoval && history.Remove(target.Id) &&
                history.Entries.All(entry => entry.Id != target.Id) && history.Entries.Count == 39,
            "Deleting a clipboard entry removes only its ID; an unknown ID makes no changes");
        history.Clear();
        check(history.Enabled && history.Entries.Count == 0, "Clearing clipboard history removes values while collection stays enabled");
        history.Add("sensitive session text");
        history.SetEnabled(false);
        check(!history.Enabled && history.Entries.Count == 0 && !history.Add("later copy"),
            "Disabling clipboard history immediately clears retained values and refuses later copies");
        history.SetEnabled(true);
        check(history.Entries.Count == 0, "Re-enabling clipboard history does not recover cleared text");

        var unicodePreview = new ClipboardEntry(Guid.NewGuid(), new string('x', 118) + "\ud83d\ude42tail\nend", DateTimeOffset.UtcNow);
        check(unicodePreview.Preview.Length <= 120 && !unicodePreview.Preview.Contains('\n') &&
                !char.IsHighSurrogate(unicodePreview.Preview[^2]) && original.Preview.IndexOfAny(['\r', '\n']) < 0,
            "Clipboard display previews stay on one line, fit 120 characters and preserve surrogate pairs");

        var backgroundCopy = Task.Run(() => history.Add("background copy"));
        PumpUntil(backgroundCopy, dispatcher);
        check(backgroundCopy.GetAwaiter().GetResult() && history.Entries[0].Text == "background copy" && notificationsUseDispatcher,
            "Background clipboard mutations and change notifications marshal to the UI dispatcher");

        // Queue a dispatcher callback before disposal, then execute it afterwards.
        var changesBeforeDispose = changes;
        var lateCallback = dispatcher.InvokeAsync(() =>
        {
            history.SetEnabled(true);
            history.Add("late callback");
            history.Remove(original.Id);
            history.Clear();
        });
        history.Dispose();
        PumpUntil(lateCallback.Task, dispatcher);
        history.Dispose();
        check(!history.Enabled && history.Entries.Count == 0 && !history.Add("after disposal") && changes == changesBeforeDispose,
            "Disposal releases copied text and ignores queued or later mutations without raising notifications");
    }

    private static void PumpUntil(Task task, Dispatcher dispatcher)
    {
        var frame = new DispatcherFrame();
        var timeout = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = TimeSpan.FromSeconds(5) };
        timeout.Tick += (_, _) => frame.Continue = false;
        _ = task.ContinueWith(_ => dispatcher.BeginInvoke(() => frame.Continue = false), TaskScheduler.Default);
        timeout.Start();
        try { Dispatcher.PushFrame(frame); }
        finally { timeout.Stop(); }
        if (!task.IsCompleted) throw new TimeoutException("Clipboard dispatcher check did not complete.");
        task.GetAwaiter().GetResult();
    }
}
