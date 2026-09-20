using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using WayType.Libraries.Core.Audio;
using WayType.Libraries.Native.Linux.NativeInterop;

namespace WayType.Libraries.Native.Linux;

/// <summary>
/// Lists capture sources through the libpulse asynchronous API, which PipeWire serves on Linux.
/// </summary>
public sealed class PulseAudioDeviceEnumerator(ILogger<PulseAudioDeviceEnumerator> logger) : IAudioCaptureDeviceEnumerator
{
    private const string MonitorSuffix = ".monitor";

    /// <summary>
    /// Returns the non-monitor sources reported by the server, marking the server default.
    /// </summary>
    public Task<IReadOnlyList<AudioDevice>> GetDevicesAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run(() => Enumerate(cancellationToken), cancellationToken);
    }

    private IReadOnlyList<AudioDevice> Enumerate(CancellationToken cancellationToken)
    {
        var mainloop = LibPulse.pa_mainloop_new();

        if (mainloop == IntPtr.Zero)
        {
            throw new InvalidOperationException("pa_mainloop_new failed: out of memory");
        }

        var state = new EnumerateState(mainloop);
        var stateHandle = GCHandle.Alloc(state);
        var userData = GCHandle.ToIntPtr(stateHandle);
        GCHandle notifyHandle = default;
        GCHandle serverHandle = default;
        GCHandle sourceHandle = default;

        try
        {
            var api = LibPulse.pa_mainloop_get_api(mainloop);
            var context = LibPulse.pa_context_new(api, "WayType");

            if (context == IntPtr.Zero)
            {
                throw new InvalidOperationException("pa_context_new failed: out of memory");
            }

            state.Context = context;

            // The main loop stores only function pointers, so every delegate target is pinned by a
            // GCHandle and the state object is reached through userData until the loop is torn down.
            // The quit flag is sticky in libpulse, so a single run drives the whole sequence: connect
            // -> server info (for the default source) -> source list, each issued from the prior callback.
            LibPulse.PaContextNotifyCb notifyCallback = (contextPointer, notifyUserData) =>
            {
                var s = StateFromUserData(notifyUserData);
                var contextState = LibPulse.pa_context_get_state(contextPointer);

                if (contextState == LibPulse.ContextReady)
                {
                    s.ServerOperation = LibPulse.pa_context_get_server_info(contextPointer, s.ServerCallbackPointer, notifyUserData);

                    if (s.ServerOperation == IntPtr.Zero)
                    {
                        s.OperationFailed = true;
                        LibPulse.pa_mainloop_quit(s.Mainloop, 1);
                    }
                }
                else if (contextState is LibPulse.ContextFailed or LibPulse.ContextTerminated)
                {
                    LibPulse.pa_mainloop_quit(s.Mainloop, 1);
                }
            };

            LibPulse.PaServerInfoCb serverCallback = (contextPointer, infoPointer, serverUserData) =>
            {
                var s = StateFromUserData(serverUserData);

                if (infoPointer != IntPtr.Zero)
                {
                    var info = Marshal.PtrToStructure<PaServerInfo>(infoPointer);
                    s.DefaultSource = PtrToUtf8(info.DefaultSourceName);
                }

                s.Operation = LibPulse.pa_context_get_source_info_list(contextPointer, s.SourceCallbackPointer, serverUserData);

                if (s.Operation == IntPtr.Zero)
                {
                    s.OperationFailed = true;
                    LibPulse.pa_mainloop_quit(s.Mainloop, 1);
                }
            };

            LibPulse.PaSourceInfoCb sourceCallback = (_, infoPointer, eol, sourceUserData) =>
            {
                var s = StateFromUserData(sourceUserData);

                if (eol != 0)
                {
                    LibPulse.pa_mainloop_quit(s.Mainloop, 0);
                    return;
                }

                var info = Marshal.PtrToStructure<PaSourceInfo>(infoPointer);
                var name = PtrToUtf8(info.Name);

                if (name is null || name.EndsWith(MonitorSuffix, StringComparison.Ordinal))
                {
                    return;
                }

                var description = PtrToUtf8(info.Description) ?? name;
                s.Devices.Add(new AudioDevice(name, description));
            };

            notifyHandle = GCHandle.Alloc(notifyCallback);
            serverHandle = GCHandle.Alloc(serverCallback);
            sourceHandle = GCHandle.Alloc(sourceCallback);
            state.ServerCallbackPointer = Marshal.GetFunctionPointerForDelegate(serverCallback);
            state.SourceCallbackPointer = Marshal.GetFunctionPointerForDelegate(sourceCallback);

            LibPulse.pa_context_set_state_callback(context, Marshal.GetFunctionPointerForDelegate(notifyCallback), userData);

            if (LibPulse.pa_context_connect(context, IntPtr.Zero, 0, IntPtr.Zero) < 0)
            {
                throw new InvalidOperationException($"pa_context_connect failed: {LibPulse.StrError(LibPulse.pa_context_errno(context))}");
            }

            LibPulse.pa_mainloop_run(mainloop, out _);

            if (LibPulse.pa_context_get_state(context) != LibPulse.ContextReady)
            {
                throw new InvalidOperationException($"PulseAudio connect failed: {LibPulse.StrError(LibPulse.pa_context_errno(context))}");
            }

            if (state.OperationFailed)
            {
                throw new InvalidOperationException($"PulseAudio source enumeration failed: {LibPulse.StrError(LibPulse.pa_context_errno(context))}");
            }

            var devices = new List<AudioDevice>(state.Devices.Count);

            foreach (var device in state.Devices)
            {
                var isDefault = string.Equals(device.Id, state.DefaultSource, StringComparison.Ordinal);
                devices.Add(device with { IsDefault = isDefault });
            }

            logger.LogDebug("Enumerated {Count} sources, default {Default}", devices.Count, state.DefaultSource ?? "<none>");

            cancellationToken.ThrowIfCancellationRequested();

            return devices;
        }
        finally
        {
            if (state.ServerOperation != IntPtr.Zero)
            {
                LibPulse.pa_operation_unref(state.ServerOperation);
            }

            if (state.Operation != IntPtr.Zero)
            {
                LibPulse.pa_operation_unref(state.Operation);
            }

            if (state.Context != IntPtr.Zero)
            {
                LibPulse.pa_context_disconnect(state.Context);
                LibPulse.pa_context_unref(state.Context);
            }

            LibPulse.pa_mainloop_free(mainloop);

            if (notifyHandle.IsAllocated)
            {
                notifyHandle.Free();
            }

            if (serverHandle.IsAllocated)
            {
                serverHandle.Free();
            }

            if (sourceHandle.IsAllocated)
            {
                sourceHandle.Free();
            }

            stateHandle.Free();
        }
    }

    private static EnumerateState StateFromUserData(IntPtr userData)
    {
        return (EnumerateState)GCHandle.FromIntPtr(userData).Target!;
    }

    private static string? PtrToUtf8(IntPtr pointer)
    {
        return pointer == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(pointer);
    }

    private sealed class EnumerateState(IntPtr mainloop)
    {
        public IntPtr Mainloop { get; } = mainloop;

        public IntPtr Context { get; set; }

        public IntPtr ServerCallbackPointer { get; set; }

        public IntPtr SourceCallbackPointer { get; set; }

        public IntPtr ServerOperation { get; set; }

        public IntPtr Operation { get; set; }

        public bool OperationFailed { get; set; }

        public string? DefaultSource { get; set; }

        public List<AudioDevice> Devices { get; } = [];
    }

    // Only the leading source fields are read, but they are placed at explicit offsets to match the
    // C pa_source_info layout, where the uint32 index sits before an aligned description pointer.
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct PaSourceInfo
    {
        [FieldOffset(0)]
        public IntPtr Name;

        [FieldOffset(8)]
        public uint Index;

        [FieldOffset(16)]
        public IntPtr Description;
    }

    // pa_server_info places four name pointers, then the 12-byte default pa_sample_spec, then the
    // default sink and source name pointers, so default_source_name lands at offset 56. Only a
    // 64-byte prefix is read, which stops before the trailing channel map.
    [StructLayout(LayoutKind.Explicit, Size = 64)]
    private struct PaServerInfo
    {
        [FieldOffset(24)]
        public IntPtr ServerName;

        [FieldOffset(48)]
        public IntPtr DefaultSinkName;

        [FieldOffset(56)]
        public IntPtr DefaultSourceName;
    }
}
