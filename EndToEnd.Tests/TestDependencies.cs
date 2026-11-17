using Microsoft.Extensions.DependencyInjection;
using Refit;
using Reqnroll.Microsoft.Extensions.DependencyInjection;

namespace EndToEnd.Tests;

public static class TestDependencies
{
    [ScenarioDependencies]
    public static IServiceCollection CreateServices()
    {
        var services = new ServiceCollection();

        services.AddRefitClient<ITasksApi>()
            .ConfigureHttpClient(client => client.BaseAddress = BaseUrl("TASKS_API_URL", "http://localhost:5080"));
        services.AddRefitClient<INotificationsApi>()
            .ConfigureHttpClient(client => client.BaseAddress = BaseUrl("NOTIFICATIONS_API_URL", "http://localhost:5090"));

        return services;
    }

    private static Uri BaseUrl(string environmentVariable, string fallback) =>
        new(Environment.GetEnvironmentVariable(environmentVariable) ?? fallback);
}
