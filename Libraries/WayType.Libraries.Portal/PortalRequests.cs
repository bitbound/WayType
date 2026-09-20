using System.Collections;
using Tmds.DBus;

namespace WayType.Libraries.Portal;

/// <summary>
/// Shared plumbing for the portal: well-known ids, token/path generation, and the request/response wait.
/// </summary>
internal static class PortalRequests
{
    public const string BusName = "org.freedesktop.portal.Desktop";

    public const string ObjectPath = "/org/freedesktop/portal/desktop";

    public static string NewToken(string prefix) => $"{prefix}_{Guid.NewGuid():N}";

    // The portal derives the Request object path from the caller's unique bus name plus the handle_token it was
    // given, so the path can be computed before the call and the Response signal subscribed up front.
    public static string ExpectedPath(ConnectionInfo connectionInfo, string token)
    {
        var senderName = connectionInfo.LocalName.TrimStart(':').Replace('.', '_');
        return $"{ObjectPath}/request/{senderName}/{token}";
    }

    public static async Task<(uint Response, IDictionary<string, object> Results)> AwaitResponseAsync(
        Connection connection,
        string expectedPath,
        Func<Task> trigger,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<(uint, IDictionary<string, object>)>(TaskCreationOptions.RunContinuationsAsynchronously);
        var request = connection.CreateProxy<IRequest>(BusName, new ObjectPath(expectedPath));

        // The subscription must exist before the trigger, otherwise a fast reply is missed.
        var subscription = await request.WatchResponseAsync(
            data => completion.TrySetResult((data.response, data.results)),
            error => completion.TrySetException(error));

        try
        {
            await trigger();

            using var timeoutSource = new CancellationTokenSource(timeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);

            try
            {
                return await completion.Task.WaitAsync(linked.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException($"Timed out waiting for the portal reply at {expectedPath}.");
            }
        }
        finally
        {
            subscription.Dispose();
        }
    }

    // Tmds surfaces a D-Bus variant as its inner value when the target is object, so a color-scheme arrives boxed.
    public static uint? ToUInt32(object? value)
    {
        return value switch
        {
            uint unsigned => unsigned,
            int signed when signed >= 0 => (uint)signed,
            ushort small => small,
            byte tiny => tiny,
            long wide when wide >= 0 && wide <= uint.MaxValue => (uint)wide,
            null => null,
            _ => TryConvertUnsigned(value),
        };
    }

    private static uint? TryConvertUnsigned(object value)
    {
        try
        {
            return Convert.ToUInt32(value);
        }
        catch (Exception exception) when (exception is FormatException or InvalidCastException or OverflowException)
        {
            return null;
        }
    }

    public static IReadOnlyList<string> ExtractShortcutIds(IDictionary<string, object> results)
    {
        var ids = new List<string>();

        if (!results.TryGetValue("shortcuts", out var raw) || raw is not IEnumerable entries)
        {
            return ids;
        }

        foreach (var entry in entries)
        {
            if (entry is null)
            {
                continue;
            }

            var id = ReadShortcutId(entry);
            if (id is not null)
            {
                ids.Add(id);
            }
        }

        return ids;
    }

    private static string? ReadShortcutId(object entry)
    {
        // Each shortcut is a (s a{sv}) struct. Tmds surfaces it as a boxed ValueTuple whose Item1 is the id.
        var fields = entry.GetType().GetFields();

        foreach (var field in fields)
        {
            if (field.Name == "Item1" && field.GetValue(entry) is string id && id.Length > 0)
            {
                return id;
            }
        }

        foreach (var field in fields)
        {
            if (field.GetValue(entry) is IDictionary<string, object> vardict
                && vardict.TryGetValue("shortcut_id", out var value)
                && value is string assigned
                && assigned.Length > 0)
            {
                return assigned;
            }
        }

        if (entry is object[] array && array.Length > 0 && array[0] is string first)
        {
            return first.Length > 0 ? first : null;
        }

        return null;
    }
}
