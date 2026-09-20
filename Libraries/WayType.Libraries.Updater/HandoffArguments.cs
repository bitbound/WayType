using WayType.Libraries.Core.Updates;

namespace WayType.Libraries.Updater;

/// <summary>
/// Removes the update handoff arguments so a relaunched binary starts normally.
/// </summary>
internal static class HandoffArguments
{
    public static string[] Strip(string[] args)
    {
        if (args.Length == 0)
        {
            return [];
        }

        var remaining = new List<string>(args.Length);

        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];

            if (string.Equals(arg, UpdateHandoff.TargetArgument, StringComparison.Ordinal)
                || string.Equals(arg, UpdateHandoff.ProcessIdArgument, StringComparison.Ordinal))
            {
                // Skip the flag and its value.
                index++;
                continue;
            }

            remaining.Add(arg);
        }

        return [.. remaining];
    }
}
