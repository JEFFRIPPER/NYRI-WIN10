using System.Net.NetworkInformation;
using System.Windows;
using Nyri.Win10.Models;

namespace Nyri.Win10.Services;

public sealed class SystemStatusService : IDisposable
{
    private static readonly string[] VpnHints =
    [
        "vpn", "wireguard", "wintun", "openvpn", "tailscale",
        "proton", "mullvad", "nordlynx", "sing-box", "singbox",
        "clash", "happ", "incy", "v2ray", "outline", "zerotier",
        "tap-windows", "amnezia"
    ];

    private readonly ActivityHub _hub;
    private string _lastVpnKey = "";
    private CancellationTokenSource? _vpnFlashCts;

    public SystemStatusService(ActivityHub hub)
    {
        _hub = hub;
        NetworkChange.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;
        NetworkChange.NetworkAddressChanged += OnNetworkAddressChanged;
        Refresh();
    }

    private void OnNetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e)
        => Refresh();

    private void OnNetworkAddressChanged(object? sender, EventArgs e)
        => Refresh();
    private void Refresh()
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(Refresh);
            return;
        }

        if (NetworkInterface.GetIsNetworkAvailable())
        {
            _hub.Remove("network");
        }
        else
        {
            _hub.Upsert(new IslandActivity(
                "network",
                IslandActivityKind.Network,
                "Нет сети",
                "Проверь подключение",
                "!",
                DateTimeOffset.Now,
                true,
                90));
        }

        PublishVpn();
    }

    private void PublishVpn()
    {
        var names = NetworkInterface.GetAllNetworkInterfaces()
            .Where(IsActiveVpn)
            .Select(x => x.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x)
            .ToArray();
        var key = string.Join("|", names);
        var changed = !string.Equals(key, _lastVpnKey, StringComparison.Ordinal);
        var wasConnected = !string.IsNullOrEmpty(_lastVpnKey);
        _lastVpnKey = key;

        if (names.Length == 0)
        {
            _hub.Remove("vpn");
            if (changed && wasConnected)
                FlashVpn("VPN отключён", "Активных VPN-интерфейсов нет");
            return;
        }

        var detail = string.Join(", ", names);
        _hub.Upsert(new IslandActivity(
            "vpn",
            IslandActivityKind.Vpn,
            "VPN",
            detail,
            "◇",
            DateTimeOffset.Now,
            true,
            40,
            true));

        if (changed)
            FlashVpn("VPN подключён", detail);
    }

    private async void FlashVpn(string title, string detail)
    {
        _vpnFlashCts?.Cancel();
        _vpnFlashCts?.Dispose();
        _vpnFlashCts = new CancellationTokenSource();
        var token = _vpnFlashCts.Token;
        _hub.Upsert(new IslandActivity(
            "vpn:flash",
            IslandActivityKind.Vpn,
            title,
            detail,
            "◇",
            DateTimeOffset.Now,
            true,
            40));

        try
        {
            await Task.Delay(5000, token);
            _hub.Remove("vpn:flash");
        }
        catch (TaskCanceledException)
        {
        }
    }

    private static bool IsActiveVpn(NetworkInterface nic)
    {
        if (nic.OperationalStatus != OperationalStatus.Up)
            return false;

        if (nic.NetworkInterfaceType == NetworkInterfaceType.Ppp)
            return true;

        var text = $"{nic.Name} {nic.Description}".ToLowerInvariant();
        return VpnHints.Any(text.Contains);
    }
    public void Dispose()
    {
        NetworkChange.NetworkAvailabilityChanged -= OnNetworkAvailabilityChanged;
        NetworkChange.NetworkAddressChanged -= OnNetworkAddressChanged;
        _vpnFlashCts?.Cancel();
        _vpnFlashCts?.Dispose();
    }
}