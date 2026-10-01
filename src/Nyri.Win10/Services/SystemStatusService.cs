using System.Net.NetworkInformation;
using System.Windows;
using Nyri.Win10.Models;

namespace Nyri.Win10.Services;

public sealed class SystemStatusService : IDisposable
{
    private readonly ActivityHub _hub;

    public SystemStatusService(ActivityHub hub)
    {
        _hub = hub;
        NetworkChange.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;
        PublishNetwork(NetworkInterface.GetIsNetworkAvailable());
    }

    private void OnNetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e)
    {
        PublishNetwork(e.IsAvailable);
    }

    private void PublishNetwork(bool available)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(() => PublishNetwork(available));
            return;
        }
        if (available)
        {
            _hub.Remove("network");
            return;
        }

        _hub.Upsert(new IslandActivity(
            "network",
            IslandActivityKind.Network,
            "Нет сети",
            "Проверь подключение",
            "!",
            DateTimeOffset.Now));
    }

    public void Dispose()
    {
        NetworkChange.NetworkAvailabilityChanged -= OnNetworkAvailabilityChanged;
    }
}