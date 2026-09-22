using System.Runtime.CompilerServices;
using Xunit;

namespace WayType.Tests;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class SkipOnCiFactAttribute : FactAttribute
{
    public SkipOnCiFactAttribute(
        [CallerFilePath] string? sourceFilePath = null,
        [CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber)
    {
        if (IsCiEnvironment())
        {
            Skip = "Skipped in GitHub Actions because the test requires a live desktop service.";
        }
    }

    private static bool IsCiEnvironment()
    {
        return IsTrue(Environment.GetEnvironmentVariable("CI"))
            || IsTrue(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"));
    }

    private static bool IsTrue(string? value)
    {
        return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }
}