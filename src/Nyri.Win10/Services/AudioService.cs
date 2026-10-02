using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace Nyri.Win10.Services;

/// <summary>Controls the Windows default playback and recording endpoints.</summary>
public sealed class AudioService : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly Guid _eventContext = Guid.NewGuid();
    private readonly DeviceNotificationClient _deviceNotifications;
    private IMMDeviceEnumerator? _enumerator;
    private Endpoint? _output;
    private Endpoint? _microphone;
    private AudioState _state = new();
    private int _generation;
    private int _refreshQueued;
    private long _sequence;
    private long _lastOutputSequence;
    private long _lastMicrophoneSequence;
    private bool _notificationsRegistered;
    private volatile bool _disposed;

    public bool OutputAvailable => _state.OutputAvailable;
    public bool MicrophoneAvailable => _state.MicrophoneAvailable;
    public double Volume => _state.Volume;
    public bool OutputMuted => _state.OutputMuted;
    public bool MicrophoneMuted => _state.MicrophoneMuted;
    public string? OutputName => _state.OutputName;
    public string? MicrophoneName => _state.MicrophoneName;
    public event EventHandler? Changed;

    public AudioService(Dispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        _dispatcher = dispatcher;
        _deviceNotifications = new DeviceNotificationClient(QueueRefresh);
        if (_dispatcher.CheckAccess()) Initialize();
        else _dispatcher.Invoke(Initialize);
    }

    private void Initialize()
    {
        try
        {
            var type = Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"), true)!;
            _enumerator = (IMMDeviceEnumerator)Activator.CreateInstance(type)!;
            Check(_enumerator.RegisterEndpointNotificationCallback(_deviceNotifications));
            _notificationsRegistered = true;
            RebindEndpoints();
        }
        catch (Exception ex)
        {
            AppDiagnostics.Write($"Audio service unavailable: {ex.GetType().Name}: {ex.Message}");
            // A failure after callback registration must still unregister before
            // releasing the enumerator; otherwise Windows can retain this service.
            DisposeCore();
            _state = new AudioState();
        }
    }

    private void RebindEndpoints()
    {
        if (_disposed || _enumerator is null) return;
        _generation++;
        Unbind(ref _output);
        Unbind(ref _microphone);
        _output = BindEndpoint(DataFlow.Render);
        _microphone = BindEndpoint(DataFlow.Capture);
        var next = new AudioState(
            _output is not null, _microphone is not null,
            _output?.Volume ?? 0, _output?.Muted ?? false, _microphone?.Muted ?? false,
            _output?.Name, _microphone?.Name);
        Publish(next);
    }

    private Endpoint? BindEndpoint(DataFlow flow)
    {
        IMMDevice? device = null;
        IAudioEndpointVolume? volume = null;
        EndpointVolumeCallback? callback = null;
        var registered = false;
        try
        {
            // eConsole follows the system default endpoint; it does not mute every
            // microphone or override applications using another recording device.
            Check(_enumerator!.GetDefaultAudioEndpoint(flow, Role.Console, out device));
            var iid = typeof(IAudioEndpointVolume).GUID;
            Check(device.Activate(ref iid, 23 /* CLSCTX_ALL */, IntPtr.Zero, out var activated));
            volume = (IAudioEndpointVolume)activated;
            var generation = _generation;
            callback = new EndpointVolumeCallback(pointer => QueueVolume(flow, generation,
                Marshal.PtrToStructure<VolumeNotification>(pointer)));
            Check(volume.RegisterControlChangeNotify(callback));
            registered = true;
            Check(volume.GetMasterVolumeLevelScalar(out var level));
            Check(volume.GetMute(out var muted));
            if (!float.IsFinite(level)) throw new InvalidOperationException("Invalid endpoint volume.");
            return new Endpoint(volume, callback, ReadFriendlyName(device), Math.Clamp(level, 0, 1), muted);
        }
        catch (Exception ex)
        {
            AppDiagnostics.Write($"Audio {flow} endpoint unavailable: {ex.GetType().Name}: {ex.Message}");
            if (registered && volume is not null && callback is not null)
                volume.UnregisterControlChangeNotify(callback);
            Release(volume);
            return null;
        }
        finally { Release(device); }
    }

    // Core Audio callbacks must be nonblocking. Capture values while the native
    // notification pointer is valid, then leave all COM operations to the UI thread.
    private void QueueVolume(DataFlow flow, int generation, VolumeNotification data)
    {
        if (_disposed || data.Context == _eventContext || !float.IsFinite(data.Volume)) return;
        var snapshot = new VolumeSnapshot(flow, generation, Interlocked.Increment(ref _sequence),
            Math.Clamp(data.Volume, 0, 1), data.Muted != 0);
        Queue(() => ApplySnapshot(snapshot));
    }

    private void ApplySnapshot(VolumeSnapshot snapshot)
    {
        if (_disposed || snapshot.Generation != _generation) return;
        if (snapshot.Flow == DataFlow.Render)
        {
            if (_output is null || snapshot.Sequence <= _lastOutputSequence) return;
            _lastOutputSequence = snapshot.Sequence;
            Publish(_state with { Volume = snapshot.Volume, OutputMuted = snapshot.Muted });
        }
        else
        {
            if (_microphone is null || snapshot.Sequence <= _lastMicrophoneSequence) return;
            _lastMicrophoneSequence = snapshot.Sequence;
            Publish(_state with { MicrophoneMuted = snapshot.Muted });
        }
    }

    private void QueueRefresh()
    {
        if (_disposed || _dispatcher.HasShutdownStarted || Interlocked.Exchange(ref _refreshQueued, 1) != 0) return;
        Queue(() =>
        {
            Interlocked.Exchange(ref _refreshQueued, 0);
            RebindEndpoints();
        });
    }

    private void Queue(Action action)
    {
        if (_disposed || _dispatcher.HasShutdownStarted) return;
        try { _dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => { if (!_disposed) action(); })); }
        catch (InvalidOperationException) { /* The dispatcher can shut down during a native callback. */ }
    }

    public void SetVolume(double value)
    {
        if (!double.IsFinite(value) || value is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(value), "Volume must be between 0 and 1.");
        var normalized = (float)value;
        OnDispatcher(() => ChangeEndpoint(DataFlow.Render,
            endpoint => endpoint.SetMasterVolumeLevelScalar(normalized, _eventContext)));
    }

    public void SetOutputMuted(bool muted)
        => OnDispatcher(() => ChangeEndpoint(DataFlow.Render, endpoint => endpoint.SetMute(muted, _eventContext)));

    public void SetMicrophoneMuted(bool muted)
        => OnDispatcher(() => ChangeEndpoint(DataFlow.Capture, endpoint => endpoint.SetMute(muted, _eventContext)));

    private void ChangeEndpoint(DataFlow flow, Func<IAudioEndpointVolume, int> change)
    {
        var endpoint = flow == DataFlow.Render ? _output : _microphone;
        if (_disposed || endpoint is null) return;
        try
        {
            Check(change(endpoint.Control));
            // Read the actual result. Own-context notifications are ignored so an
            // older queued callback cannot undo a later slider movement in the UI.
            Check(endpoint.Control.GetMasterVolumeLevelScalar(out var level));
            Check(endpoint.Control.GetMute(out var muted));
            if (!float.IsFinite(level)) throw new InvalidOperationException("Invalid endpoint volume.");
            ApplySnapshot(new VolumeSnapshot(flow, _generation, Interlocked.Increment(ref _sequence),
                Math.Clamp(level, 0, 1), muted));
        }
        catch (Exception ex)
        {
            AppDiagnostics.Write($"Audio {flow} control failed: {ex.GetType().Name}: {ex.Message}");
            if (flow == DataFlow.Render)
            {
                Unbind(ref _output);
                Publish(_state with { OutputAvailable = false, Volume = 0, OutputMuted = false, OutputName = null });
            }
            else
            {
                Unbind(ref _microphone);
                Publish(_state with { MicrophoneAvailable = false, MicrophoneMuted = false, MicrophoneName = null });
            }
        }
    }

    private void OnDispatcher(Action action)
    {
        if (_disposed || _dispatcher.HasShutdownStarted) return;
        if (_dispatcher.CheckAccess()) action();
        else Queue(action);
    }

    private void Publish(AudioState next)
    {
        if (_disposed || _state == next) return;
        _state = next;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static string? ReadFriendlyName(IMMDevice device)
    {
        IPropertyStore? store = null;
        var value = new PropVariant();
        try
        {
            Check(device.OpenPropertyStore(0 /* STGM_READ */, out store));
            var key = new PropertyKey(new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), 14);
            Check(store.GetValue(ref key, out value));
            return value.Type == 31 /* VT_LPWSTR */ ? Marshal.PtrToStringUni(value.Pointer) : null;
        }
        catch { return null; }
        finally
        {
            PropVariantClear(ref value);
            Release(store);
        }
    }

    private static void Unbind(ref Endpoint? endpoint)
    {
        var old = endpoint;
        endpoint = null;
        if (old is null) return;
        try { old.Control.UnregisterControlChangeNotify(old.Callback); }
        catch (COMException) { }
        Release(old.Control);
    }

    private static void Release(object? instance)
    {
        if (instance is not null && Marshal.IsComObject(instance))
        {
            try { Marshal.ReleaseComObject(instance); }
            catch (InvalidComObjectException) { }
        }
    }

    private static void Check(int result) { if (result < 0) Marshal.ThrowExceptionForHR(result); }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; // Suppress native and dispatcher callbacks before unregistration.
        if (!_dispatcher.CheckAccess() && !_dispatcher.HasShutdownStarted) _dispatcher.Invoke(DisposeCore);
        else DisposeCore();
    }

    private void DisposeCore()
    {
        Changed = null;
        Unbind(ref _output);
        Unbind(ref _microphone);
        if (_enumerator is not null && _notificationsRegistered)
        {
            try { _enumerator.UnregisterEndpointNotificationCallback(_deviceNotifications); }
            catch (COMException) { }
        }
        Release(_enumerator);
        _enumerator = null;
        _notificationsRegistered = false;
    }

    private sealed record AudioState(bool OutputAvailable = false, bool MicrophoneAvailable = false,
        double Volume = 0, bool OutputMuted = false, bool MicrophoneMuted = false,
        string? OutputName = null, string? MicrophoneName = null);
    private sealed record Endpoint(IAudioEndpointVolume Control, EndpointVolumeCallback Callback,
        string? Name, double Volume, bool Muted);
    private readonly record struct VolumeSnapshot(DataFlow Flow, int Generation, long Sequence, double Volume, bool Muted);

    [StructLayout(LayoutKind.Sequential)]
    public struct PropertyKey(Guid formatId, uint propertyId)
    {
        public Guid FormatId = formatId;
        public uint PropertyId = propertyId;
    }

    // The native PROPVARIANT union is 16 bytes on x64 and 8 bytes on x86.
    [StructLayout(LayoutKind.Sequential)]
    private struct PropVariant
    {
        public ushort Type;
        public ushort Reserved1;
        public ushort Reserved2;
        public ushort Reserved3;
        public IntPtr Pointer;
        public IntPtr UnionPadding;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct VolumeNotification
    {
        public Guid Context;
        public int Muted; // Windows BOOL is four bytes.
        public float Volume;
        public uint Channels;
        // Variable-length channel data follows; the shell needs only master volume.
    }

    public enum DataFlow { Render, Capture, All }
    public enum Role { Console, Multimedia, Communications }

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant value);

    // Native layouts, GUIDs and vtable order verified against Microsoft's SDK headers:
    // https://github.com/microsoft/win32metadata/blob/main/generation/WinSDK/RecompiledIdlHeaders/um/mmdeviceapi.h
    // https://github.com/microsoft/win32metadata/blob/main/generation/WinSDK/RecompiledIdlHeaders/um/endpointvolume.h
    // https://github.com/microsoft/win32metadata/blob/main/generation/WinSDK/RecompiledIdlHeaders/um/propsys.h
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(DataFlow flow, uint states, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(DataFlow flow, Role role, out IMMDevice device);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        [PreserveSig] int RegisterEndpointNotificationCallback(IMMNotificationClient callback);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IMMNotificationClient callback);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, uint context, IntPtr activation, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
        [PreserveSig] int OpenPropertyStore(uint access, out IPropertyStore store);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out uint state);
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
        [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value);
        [PreserveSig] int Commit();
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IAudioEndpointVolumeCallback callback);
        [PreserveSig] int UnregisterControlChangeNotify(IAudioEndpointVolumeCallback callback);
        [PreserveSig] int GetChannelCount(out uint channels);
        [PreserveSig] int SetMasterVolumeLevel(float level, [In, MarshalAs(UnmanagedType.LPStruct)] Guid context);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, [In, MarshalAs(UnmanagedType.LPStruct)] Guid context);
        [PreserveSig] int GetMasterVolumeLevel(out float level);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint channel, float level, [In, MarshalAs(UnmanagedType.LPStruct)] Guid context);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, [In, MarshalAs(UnmanagedType.LPStruct)] Guid context);
        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float level);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, [In, MarshalAs(UnmanagedType.LPStruct)] Guid context);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
        [PreserveSig] int GetVolumeStepInfo(out uint step, out uint steps);
        [PreserveSig] int VolumeStepUp([In, MarshalAs(UnmanagedType.LPStruct)] Guid context);
        [PreserveSig] int VolumeStepDown([In, MarshalAs(UnmanagedType.LPStruct)] Guid context);
        [PreserveSig] int QueryHardwareSupport(out uint support);
        [PreserveSig] int GetVolumeRange(out float minimum, out float maximum, out float increment);
    }

    [ComVisible(true), Guid("657804FA-D6AD-4496-8A60-352752AF4F89"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAudioEndpointVolumeCallback
    {
        [PreserveSig] int OnNotify(IntPtr notification);
    }

    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    public sealed class EndpointVolumeCallback : IAudioEndpointVolumeCallback
    {
        private readonly Action<IntPtr> _notify;
        internal EndpointVolumeCallback(Action<IntPtr> notify) => _notify = notify;
        public int OnNotify(IntPtr notification)
        {
            if (notification == IntPtr.Zero) return 0;
            try { _notify(notification); }
            catch { /* Never unwind managed exceptions into an audio callback. */ }
            return 0;
        }
    }

    [ComVisible(true), Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMMNotificationClient
    {
        [PreserveSig] int OnDeviceStateChanged([MarshalAs(UnmanagedType.LPWStr)] string id, uint state);
        [PreserveSig] int OnDeviceAdded([MarshalAs(UnmanagedType.LPWStr)] string id);
        [PreserveSig] int OnDeviceRemoved([MarshalAs(UnmanagedType.LPWStr)] string id);
        [PreserveSig] int OnDefaultDeviceChanged(DataFlow flow, Role role, [MarshalAs(UnmanagedType.LPWStr)] string? id);
        [PreserveSig] int OnPropertyValueChanged([MarshalAs(UnmanagedType.LPWStr)] string id, PropertyKey key);
    }

    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    public sealed class DeviceNotificationClient : IMMNotificationClient
    {
        private readonly Action _changed;
        internal DeviceNotificationClient(Action changed) => _changed = changed;
        private int Notify() { try { _changed(); } catch { } return 0; }
        public int OnDeviceStateChanged(string id, uint state) => Notify();
        public int OnDeviceAdded(string id) => Notify();
        public int OnDeviceRemoved(string id) => Notify();
        public int OnDefaultDeviceChanged(DataFlow flow, Role role, string? id) => role == Role.Console ? Notify() : 0;
        public int OnPropertyValueChanged(string id, PropertyKey key) => Notify();
    }
}
