namespace WayType.Libraries.Updater;

/// <summary>
/// Sets the executable permission bits a downloaded or copied binary needs to run.
/// </summary>
internal static class UnixExecutablePermissions
{
    /// <summary>
    /// The 0755 mode: owner read/write/execute, group and other read/execute.
    /// </summary>
    public const UnixFileMode Mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
        | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
        | UnixFileMode.OtherRead | UnixFileMode.OtherExecute;

    public static void MakeExecutable(string path)
    {
        File.SetUnixFileMode(path, Mode);
    }

    public static bool IsExecutable(string path)
    {
        return (File.GetUnixFileMode(path) & UnixFileMode.UserExecute) != 0;
    }
}
