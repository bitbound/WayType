using System.Diagnostics;
using Microsoft.Extensions.Logging;
using WayType.Libraries.Core.Updates;

namespace WayType.Libraries.Updater;

/// <summary>
/// Runs inside a freshly downloaded copy to stop the outdated process, replace its binary, and relaunch it.
/// </summary>
public sealed class UpdateHandoffRunner(ILogger<UpdateHandoffRunner> logger)
{
    private static readonly TimeSpan KillTimeout = TimeSpan.FromSeconds(10);

    public bool IsRequested(string[] args)
    {
        return UpdateHandoff.TryParse(args) is not null;
    }

    public async Task<bool> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        var handoff = UpdateHandoff.TryParse(args);

        if (handoff is null)
        {
            logger.LogWarning("An update handoff was requested but its arguments are incomplete.");
            return false;
        }

        var target = handoff.OutdatedExecutablePath;
        var stagedExecutable = Environment.ProcessPath;

        if (string.IsNullOrEmpty(stagedExecutable))
        {
            logger.LogError("Could not determine the path of the downloaded executable.");
            return false;
        }

        // The rename source. Renaming it over the target leaves this name gone, so it only needs cleanup on failure.
        var stagingCopy = target + ".new";

        try
        {
            await StopOutdatedProcess(handoff.OutdatedProcessId, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            // Writing over a running executable's file fails with ETXTBSY. Copy our own bytes to a sibling,
            // make it executable, then File.Move performs rename(2), which swaps the inode atomically and never
            // truncates the target. Nothing touches the original until the rename, so any earlier failure leaves
            // a working binary in place.
            File.Copy(stagedExecutable, stagingCopy, overwrite: true);
            UnixExecutablePermissions.MakeExecutable(stagingCopy);

            if (!UnixExecutablePermissions.IsExecutable(stagingCopy))
            {
                logger.LogError("The staged update at {Path} is not executable.", stagingCopy);
                return false;
            }

            File.Move(stagingCopy, target, overwrite: true);
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("The WayType update handoff was cancelled.");
            TryDelete(stagingCopy);
            return false;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to install the WayType update at {Target}.", target);
            TryDelete(stagingCopy);
            return false;
        }

        // The target now holds the new binary. Relaunch it normally so the user keeps running the app.
        if (!TryRelaunch(target, args))
        {
            TryDelete(stagedExecutable);
            return false;
        }

        TryDelete(stagedExecutable);

        return true;
    }

    private async Task StopOutdatedProcess(int processId, CancellationToken cancellationToken)
    {
        Process process;

        try
        {
            process = Process.GetProcessById(processId);
        }
        catch (ArgumentException)
        {
            logger.LogDebug("Outdated process {ProcessId} has already exited.", processId);
            return;
        }

        using (process)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: false);
                }
            }
            catch (InvalidOperationException)
            {
                logger.LogDebug("Outdated process {ProcessId} exited before it could be stopped.", processId);
                return;
            }

            try
            {
                await process.WaitForExitAsync(cancellationToken).WaitAsync(KillTimeout, CancellationToken.None);
            }
            catch (TimeoutException)
            {
                // rename(2) is safe against a still-running process, so continue regardless.
                logger.LogWarning("Outdated process {ProcessId} did not exit within {Timeout}.", processId, KillTimeout);
            }
        }
    }

    private bool TryRelaunch(string target, string[] args)
    {
        var startInfo = new ProcessStartInfo(target)
        {
            UseShellExecute = false,
        };

        foreach (var argument in HandoffArguments.Strip(args))
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            Process.Start(startInfo);

            return true;
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            logger.LogError(exception, "The update was installed but {Target} could not be relaunched.", target);

            return false;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Best-effort cleanup of staging files; the update result does not depend on it.
        }
    }
}
