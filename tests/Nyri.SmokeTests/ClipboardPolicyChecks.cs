using Nyri.Win10.Services;

internal static class ClipboardPolicyChecks
{
    public static void Run(Action<bool, string> check)
    {
        // Pure decisions only. Never call TryRead, IsCurrentSequence or real Clipboard.
        var unmarked = ClipboardPolicy.FromMarkers(false, false, null);
        check(unmarked.AllowMonitoring && unmarked.AllowHistory,
            "An unmarked item permits preview and opt-in local history");
        check(ClipboardPolicy.FromMarkers(false, false, 0) == unmarked,
            "An absent history marker does not accidentally apply an unrelated DWORD");

        var historyDenied = ClipboardPolicy.FromMarkers(false, true, 0);
        check(historyDenied.AllowMonitoring && !historyDenied.AllowHistory,
            "A zero history DWORD suppresses recording while preserving current preview");
        var historyAllowed = ClipboardPolicy.FromMarkers(false, true, 1);
        check(historyAllowed.AllowMonitoring && historyAllowed.AllowHistory,
            "A one history DWORD permits recording without bypassing user opt-in");

        check(new uint?[] { null, 2, uint.MaxValue }
                .Select(value => ClipboardPolicy.FromMarkers(false, true, value))
                .All(policy => policy.AllowMonitoring && !policy.AllowHistory),
            "Missing or unknown payload of a present history marker fails closed for history");
        check(new uint?[] { null, 0, 1, 2, uint.MaxValue }
                .Select(value => ClipboardPolicy.FromMarkers(true, true, value))
                .All(policy => !policy.AllowMonitoring && !policy.AllowHistory),
            "Monitoring-exclusion presence overrides every history payload including explicit inclusion");
        var excludedOnly = ClipboardPolicy.FromMarkers(true, false, null);
        check(!excludedOnly.AllowMonitoring && !excludedOnly.AllowHistory,
            "Monitoring-exclusion presence needs no history marker or payload");
    }
}
