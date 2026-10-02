using System.Windows.Threading;
using Nyri.Win10.Services;

internal static class AudioChecks
{
    // Opt-in local hardware check: it writes the existing values back to the
    // endpoints, so checking the native setters does not change the user's audio.
    public static void Run(Action<bool, string> check)
    {
        check(Thread.CurrentThread.GetApartmentState() == ApartmentState.STA,
            "Native audio check runs on an STA thread");
        var dispatcher = Dispatcher.CurrentDispatcher;
        using var audio = new AudioService(dispatcher);
        Console.WriteLine($"Audio: output_available={audio.OutputAvailable}, " +
            $"microphone_available={audio.MicrophoneAvailable}, volume={audio.Volume:0.000}");
        check(double.IsFinite(audio.Volume) && audio.Volume is >= 0 and <= 1,
            "Native audio reports a finite normalized output volume");

        var initial = Snapshot(audio);
        audio.SetVolume(audio.Volume);
        audio.SetOutputMuted(audio.OutputMuted);
        audio.SetMicrophoneMuted(audio.MicrophoneMuted);
        Pump(dispatcher);
        check(Snapshot(audio) == initial,
            "Native setters accept existing audio settings without changing them");

        foreach (var invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, -0.01, 1.01 })
        {
            var rejected = false;
            try { audio.SetVolume(invalid); }
            catch (ArgumentOutOfRangeException) { rejected = true; }
            check(rejected, "Native audio rejects invalid normalized volume " + invalid);
        }

        var notifications = 0;
        audio.Changed += (_, _) => notifications++;
        // Leave a dispatcher operation pending when shutdown begins. It must be
        // cancelled logically rather than calling a released native endpoint.
        Task.Run(() =>
        {
            audio.SetVolume(initial.Volume);
            audio.SetOutputMuted(initial.OutputMuted);
            audio.SetMicrophoneMuted(initial.MicrophoneMuted);
        }).GetAwaiter().GetResult();
        var beforeDispose = Snapshot(audio);
        var beforeNotifications = notifications;
        audio.Dispose();
        audio.Dispose();
        Pump(dispatcher);
        check(Snapshot(audio) == beforeDispose && notifications == beforeNotifications,
            "Queued native audio operations cannot mutate state after disposal");
    }

    private static (bool OutputAvailable, bool MicrophoneAvailable, double Volume,
        bool OutputMuted, bool MicrophoneMuted, string? OutputName, string? MicrophoneName) Snapshot(AudioService audio)
        => (audio.OutputAvailable, audio.MicrophoneAvailable, audio.Volume,
            audio.OutputMuted, audio.MicrophoneMuted, audio.OutputName, audio.MicrophoneName);

    private static void Pump(Dispatcher dispatcher)
    {
        var frame = new DispatcherFrame();
        var deadline = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(150)
        };
        deadline.Tick += (_, _) =>
        {
            deadline.Stop();
            frame.Continue = false;
        };
        deadline.Start();
        Dispatcher.PushFrame(frame);
    }
}
