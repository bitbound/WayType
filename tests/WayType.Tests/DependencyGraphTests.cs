using Microsoft.Extensions.DependencyInjection;
using WayType.Startup;

namespace WayType.Tests;

public class DependencyGraphTests
{
    [Fact]
    public void AddWayType_BuildsTheProviderWithoutCircularDependencies()
    {
        var services = new ServiceCollection();

        services.AddWayType();

        // Validating on build walks every constructor, so a registration cycle fails here instead of on first resolve.
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });

        Assert.NotNull(provider);
    }
}
