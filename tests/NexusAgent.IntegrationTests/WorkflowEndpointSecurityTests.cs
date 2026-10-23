using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace NexusAgent.IntegrationTests;

public sealed class WorkflowEndpointSecurityTests
{
    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task NonDevelopment_ShouldNotExposeWorkflowEndpoints(
        string environment)
    {
        using var factory = new EnvironmentFactory(environment);
        using var client = factory.CreateClient();

        foreach (var path in new[]
        {
            "/api/workflows",
            "/api/workflows/",
            $"/api/workflows/{Guid.NewGuid()}",
            $"/api/workflows/{Guid.NewGuid()}/start",
            $"/api/workflows/{Guid.NewGuid()}/complete"
        })
        {
            using var response = await client.GetAsync(path);

            Assert.Equal(
                HttpStatusCode.NotFound,
                response.StatusCode);
        }

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
                "/api/workflows",
                StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Development_ShouldRegisterWorkflowEndpoints()
    {
        using var factory = new EnvironmentFactory("Development");
        using var client = factory.CreateClient();

        var routes = factory.Services
            .GetRequiredService<EndpointDataSource>()
            .Endpoints
            .OfType<RouteEndpoint>()
            .Select(endpoint => endpoint.RoutePattern.RawText)
            .Where(route => route is not null)
            .ToArray();

        Assert.Contains(
            routes,
            route => route!.Contains(
                "/api/workflows",
                StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Development")]
    public async Task Health_ShouldRemainAvailable(
        string environment)
    {
        using var factory = new EnvironmentFactory(environment);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private sealed class EnvironmentFactory(string environment)
        : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(
            IWebHostBuilder builder)
        {
            builder.UseEnvironment(environment);

            builder.UseSetting(
                "ConnectionStrings:NexusAgent",
                "Host=127.0.0.1;Port=15432;Database=nexusagent;Username=nexusagent;Password=nexusagent_dev_only");

            builder.UseSetting(
                "DocumentStorage:RootPath",
                Path.Combine(
                    Path.GetTempPath(),
                    "nexusagent-workflow-security-tests",
                    Guid.NewGuid().ToString("N")));
        }
    }
}