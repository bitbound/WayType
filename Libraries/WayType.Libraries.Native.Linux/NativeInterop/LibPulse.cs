using System.Runtime.InteropServices;

namespace WayType.Libraries.Native.Linux.NativeInterop;

/// <summary>
/// Bindings for the libpulse asynchronous API used to enumerate sources.
/// </summary>
internal static class LibPulse
{
    private const string Library = "libpulse.so.0";

    public const int ContextReady = 4;
    public const int ContextFailed = 5;
    public const int ContextTerminated = 6;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void PaContextNotifyCb(IntPtr context, IntPtr userData);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void PaSourceInfoCb(IntPtr context, IntPtr sourceInfo, int eol, IntPtr userData);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void PaServerInfoCb(IntPtr context, IntPtr serverInfo, IntPtr userData);

    // pa_strerror returns a pointer to a static table string that libpulse owns. Marshalling the
    // return as LPUTF8Str makes the runtime free that static pointer and abort, so take an IntPtr.
    [DllImport(Library, EntryPoint = "pa_strerror", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr pa_strerror(int error);

    public static string StrError(int error)
    {
        var pointer = pa_strerror(error);

        return pointer == IntPtr.Zero ? "(no message)" : Marshal.PtrToStringUTF8(pointer) ?? "(no message)";
    }

    [DllImport(Library, EntryPoint = "pa_mainloop_new", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr pa_mainloop_new();

    [DllImport(Library, EntryPoint = "pa_mainloop_get_api", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr pa_mainloop_get_api(IntPtr mainloop);

    [DllImport(Library, EntryPoint = "pa_mainloop_run", CallingConvention = CallingConvention.Cdecl)]
    public static extern int pa_mainloop_run(IntPtr mainloop, out int retval);

    [DllImport(Library, EntryPoint = "pa_mainloop_quit", CallingConvention = CallingConvention.Cdecl)]
    public static extern void pa_mainloop_quit(IntPtr mainloop, int retval);

    [DllImport(Library, EntryPoint = "pa_mainloop_free", CallingConvention = CallingConvention.Cdecl)]
    public static extern void pa_mainloop_free(IntPtr mainloop);

    [DllImport(Library, EntryPoint = "pa_context_new", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr pa_context_new(IntPtr mainloopApi, [MarshalAs(UnmanagedType.LPUTF8Str)] string name);

    [DllImport(Library, EntryPoint = "pa_context_connect", CallingConvention = CallingConvention.Cdecl)]
    public static extern int pa_context_connect(IntPtr context, IntPtr server, uint flags, IntPtr sampleSpec);

    [DllImport(Library, EntryPoint = "pa_context_disconnect", CallingConvention = CallingConvention.Cdecl)]
    public static extern void pa_context_disconnect(IntPtr context);

    [DllImport(Library, EntryPoint = "pa_context_errno", CallingConvention = CallingConvention.Cdecl)]
    public static extern int pa_context_errno(IntPtr context);

    [DllImport(Library, EntryPoint = "pa_context_unref", CallingConvention = CallingConvention.Cdecl)]
    public static extern void pa_context_unref(IntPtr context);

    [DllImport(Library, EntryPoint = "pa_context_get_state", CallingConvention = CallingConvention.Cdecl)]
    public static extern int pa_context_get_state(IntPtr context);

    [DllImport(Library, EntryPoint = "pa_context_set_state_callback", CallingConvention = CallingConvention.Cdecl)]
    public static extern int pa_context_set_state_callback(IntPtr context, IntPtr callback, IntPtr userData);

    [DllImport(Library, EntryPoint = "pa_context_get_server_info", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr pa_context_get_server_info(IntPtr context, IntPtr callback, IntPtr userData);

    [DllImport(Library, EntryPoint = "pa_context_get_source_info_list", CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr pa_context_get_source_info_list(IntPtr context, IntPtr callback, IntPtr userData);

    [DllImport(Library, EntryPoint = "pa_operation_unref", CallingConvention = CallingConvention.Cdecl)]
    public static extern void pa_operation_unref(IntPtr operation);
}
