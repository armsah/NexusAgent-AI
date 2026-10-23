using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace NexusAgent.IntegrationTests;

public sealed class ProductionHardeningTests
{
    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task NonDevelopment_ShouldNotExposeBusinessEndpoints(
        string environment)
    {
        using var factory = new EnvironmentFactory(environment);
        using var client = factory.CreateClient();

        var routes = factory.Services
            .GetRequiredService<EndpointDataSource>()
            .Endpoints
            .OfType<RouteEndpoint>()
            .Select(endpoint => endpoint.RoutePattern.RawText)
            .Where(route => route is not null)
            .ToArray();

        Assert.DoesNotContain(
            routes,
            route => route!.StartsWith(
                "/api/",
                StringComparison.OrdinalIgnoreCase));

        foreach (var path in new[]
        {
            "/api/documents",
            "/api/documents/search",
            "/api/questions",
            "/api/workflows"
        })
        {
            using var response = await client.GetAsync(path);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task NonDevelopment_ShouldKeepLivenessAvailable(
        string environment)
    {
        using var factory = new EnvironmentFactory(environment);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task DatabaseUnavailable_ShouldReturnServiceUnavailable()
    {
        using var factory = new EnvironmentFactory("Production");
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/ready");

        Assert.Equal(
            HttpStatusCode.ServiceUnavailable,
            response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain(
            "Password=",
            body,
            StringComparison.OrdinalIgnoreCase);

        Assert.DoesNotContain(
            "Npgsql",
            body,
            StringComparison.OrdinalIgnoreCase);
    }

    private sealed class EnvironmentFactory(string environment)
        : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(
            IWebHostBuilder builder)
        {
            builder.UseEnvironment(environment);

            // Deliberately unreachable local endpoint.
            builder.UseSetting(
                "ConnectionStrings:NexusAgent",
                "Host=127.0.0.1;Port=1;Database=nexusagent;Username=nexusagent;Password=test_only;Timeout=1;Command Timeout=1");

            builder.UseSetting(
                "DocumentStorage:RootPath",
                Path.Combine(
                    Path.GetTempPath(),
                    "nexusagent-p9-hardening-tests",
                    Guid.NewGuid().ToString("N")));
        }
    }
}