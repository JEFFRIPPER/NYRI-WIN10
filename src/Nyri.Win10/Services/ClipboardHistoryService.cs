using System.Collections.ObjectModel;
using System.Windows.Threading;
using Nyri.Win10.Models;

namespace Nyri.Win10.Services;

/// <summary>
/// Optional, bounded clipboard history. This service never reads the Windows clipboard,
/// writes files, or logs copied text. Mutations and notifications use the UI dispatcher.
/// </summary>
public sealed class ClipboardHistoryService : IDisposable
{
    public const int MaximumEntries = 40;
    public const int MaximumTextLength = 65_536;

    private static readonly ReadOnlyCollection<ClipboardEntry> EmptyEntries = Array.AsReadOnly(Array.Empty<ClipboardEntry>());
    private readonly Dispatcher _dispatcher;
    private readonly object _gate = new();
    private IReadOnlyList<ClipboardEntry> _entries = EmptyEntries;
    private int _enabled;
    private bool _disposed;

    public ClipboardHistoryService(Dispatcher? dispatcher = null)
        => _dispatcher = dispatcher ?? Dispatcher.CurrentDispatcher;

    public bool Enabled
    {
        get => Volatile.Read(ref _enabled) != 0;
        private set => Volatile.Write(ref _enabled, value ? 1 : 0);
    }

    /// <summary>An immutable snapshot, ordered from most recently copied to oldest.</summary>
    public IReadOnlyList<ClipboardEntry> Entries => Volatile.Read(ref _entries);
    public event EventHandler? Changed;

    public void SetEnabled(bool enabled)
        => OnDispatcher(() =>
        {
            lock (_gate)
            {
                if (_disposed || Enabled == enabled) return false;
                Enabled = enabled;
                if (!enabled) Volatile.Write(ref _entries, EmptyEntries);
            }
            NotifyChanged();
            return true;
        });

    /// <summary>Accepts a full text value or rejects it entirely; stored text is never truncated.</summary>
    public bool Add(string text)
    {
        if (string.IsNullOrEmpty(text) || text.Length > MaximumTextLength) return false;
        return OnDispatcher(() =>
        {
            lock (_gate)
            {
                if (_disposed || !Enabled) return false;
                var entries = _entries.ToList();
                var duplicate = entries.FindIndex(entry => string.Equals(entry.Text, text, StringComparison.Ordinal));
                var copiedAt = DateTimeOffset.UtcNow;
                ClipboardEntry entry;
                if (duplicate >= 0)
                {
                    var existing = entries[duplicate];
                    if (copiedAt <= existing.CopiedAt) copiedAt = existing.CopiedAt.AddTicks(1);
                    entry = existing with { CopiedAt = copiedAt };
                    entries.RemoveAt(duplicate);
                }
                else entry = new ClipboardEntry(Guid.NewGuid(), text, copiedAt);

                entries.Insert(0, entry);
                if (entries.Count > MaximumEntries) entries.RemoveRange(MaximumEntries, entries.Count - MaximumEntries);
                Volatile.Write(ref _entries, Array.AsReadOnly(entries.ToArray()));
            }
            NotifyChanged();
            return true;
        });
    }

    public bool Remove(Guid id)
        => OnDispatcher(() =>
        {
            lock (_gate)
            {
                if (_disposed) return false;
                var retained = _entries.Where(entry => entry.Id != id).ToArray();
                if (retained.Length == _entries.Count) return false;
                Volatile.Write(ref _entries, Array.AsReadOnly(retained));
            }
            NotifyChanged();
            return true;
        });

    public void Clear()
        => OnDispatcher(() =>
        {
            lock (_gate)
            {
                if (_disposed || _entries.Count == 0) return false;
                Volatile.Write(ref _entries, EmptyEntries);
            }
            NotifyChanged();
            return true;
        });

    private bool OnDispatcher(Func<bool> action)
    {
        lock (_gate)
            if (_disposed || _dispatcher.HasShutdownStarted || _dispatcher.HasShutdownFinished) return false;
        if (_dispatcher.CheckAccess()) return action();
        try
        {
            return _dispatcher.Invoke(() =>
            {
                lock (_gate)
                    if (_disposed || _dispatcher.HasShutdownStarted || _dispatcher.HasShutdownFinished) return false;
                return action();
            });
        }
        catch (TaskCanceledException) { return false; }
        catch (InvalidOperationException) when (_dispatcher.HasShutdownStarted || _dispatcher.HasShutdownFinished)
        {
            return false;
        }
    }

    private void NotifyChanged()
    {
        EventHandler? handler;
        lock (_gate) handler = _disposed ? null : Changed;
        handler?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        // The state contains only immutable managed values, so disposal can release it
        // immediately from any thread without queuing a late UI notification.
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            Enabled = false;
            Volatile.Write(ref _entries, EmptyEntries);
            Changed = null;
        }
    }
}
