using System.Windows.Threading;
using Nyri.Win10.Models;

namespace Nyri.Win10.Services;

public sealed class TimerService : IDisposable
{
    private readonly ActivityHub _hub;
    private readonly DispatcherTimer _ticker = new() { Interval = TimeSpan.FromSeconds(1) };
    private DateTimeOffset? _countdownEnd;
    private DateTimeOffset? _stopwatchStart;
    private CancellationTokenSource? _completionCts;

    public TimerService(ActivityHub hub)
    {
        _hub = hub;
        _ticker.Tick += OnTick;
    }

    public void StartCountdown(TimeSpan duration)
    {
        _completionCts?.Cancel();
        _stopwatchStart = null;
        _countdownEnd = DateTimeOffset.Now.Add(duration);
        _ticker.Start();
        PublishCountdown();
    }

    public void ToggleStopwatch()
    {
        if (_stopwatchStart is not null)
        {
            Stop();
            return;
        }

        _completionCts?.Cancel();
        _countdownEnd = null;
        _stopwatchStart = DateTimeOffset.Now;
        _ticker.Start();
        PublishStopwatch();
    }

    public void Stop()
    {
        _countdownEnd = null;
        _stopwatchStart = null;
        _ticker.Stop();
        _completionCts?.Cancel();
        _hub.Remove("timer");
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (_countdownEnd is not null)
            PublishCountdown();
        else if (_stopwatchStart is not null)
            PublishStopwatch();
        else
            _ticker.Stop();
    }

    private void PublishCountdown()
    {
        if (_countdownEnd is null) return;

        var remaining = _countdownEnd.Value - DateTimeOffset.Now;
        if (remaining <= TimeSpan.Zero)
        {
            _ticker.Stop();
            _countdownEnd = null;
            PublishCompleted();
            return;
        }

        var text = remaining.TotalHours >= 1
            ? remaining.ToString(@"hh\:mm\:ss")
            : remaining.ToString(@"mm\:ss");

        _hub.Upsert(new IslandActivity(
            "timer", IslandActivityKind.Timer, "Таймер", text, "⏱",
            DateTimeOffset.Now, true, 75));
    }

    private void PublishStopwatch()
    {
        if (_stopwatchStart is null) return;

        var elapsed = DateTimeOffset.Now - _stopwatchStart.Value;
        var text = elapsed.TotalHours >= 1
            ? elapsed.ToString(@"hh\:mm\:ss")
            : elapsed.ToString(@"mm\:ss");

        _hub.Upsert(new IslandActivity(
            "timer", IslandActivityKind.Timer, "Секундомер", text, "⏱",
            DateTimeOffset.Now, true, 75));
    }

    private async void PublishCompleted()
    {
        _completionCts?.Cancel();
        _completionCts?.Dispose();
        _completionCts = new CancellationTokenSource();
        var token = _completionCts.Token;

        _hub.Upsert(new IslandActivity(
            "timer", IslandActivityKind.Timer, "Таймер завершён", "Время вышло", "⏱",
            DateTimeOffset.Now, true, 95));

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(8), token);
            _hub.Remove("timer");
        }
        catch (TaskCanceledException)
        {
        }
    }

    public void Dispose()
    {
        _ticker.Stop();
        _ticker.Tick -= OnTick;
        _completionCts?.Cancel();
        _completionCts?.Dispose();
    }
}
