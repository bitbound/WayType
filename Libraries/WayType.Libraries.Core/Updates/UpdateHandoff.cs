namespace WayType.Libraries.Core.Updates;

/// <summary>
/// The command line a downloaded update is launched with so it can replace the process that started it.
/// </summary>
public sealed record UpdateHandoff(string OutdatedExecutablePath, int OutdatedProcessId)
{
    public const string TargetArgument = "--waytype-update-target";

    public const string ProcessIdArgument = "--waytype-update-pid";

    public static string[] BuildArguments(UpdateHandoff handoff)
    {
        return [TargetArgument, handoff.OutdatedExecutablePath, ProcessIdArgument, handoff.OutdatedProcessId.ToString()];
    }

    public static UpdateHandoff? TryParse(string[] args)
    {
        var target = GetValue(args, TargetArgument);
        var rawProcessId = GetValue(args, ProcessIdArgument);

        if (string.IsNullOrWhiteSpace(target) || !int.TryParse(rawProcessId, out var processId))
        {
            return null;
        }

        return new UpdateHandoff(target, processId);
    }

    private static string? GetValue(string[] args, string name)
    {
        for (var index = 0; index < args.Length - 1; index++)
        {
            if (string.Equals(args[index], name, StringComparison.Ordinal))
            {
                return args[index + 1];
            }
        }

        return null;
    }
}
