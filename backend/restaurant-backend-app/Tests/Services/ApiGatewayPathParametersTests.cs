using System.Text.Json;
using System.Text.RegularExpressions;

namespace APIHandler.Tests.Services;

/// <summary>
/// API Gateway (REST) does not allow sibling path variables with different names under the same parent
/// (e.g. /locations/{id} and /locations/{locationId}/…): Syndicate then fails to create the resource and the route
/// is missing after deploy. These tests pin the names in deployment_resources.json and keep Routing/RouteRegistry.cs
/// in sync with it (the lambda passes the RouteRegistry names to the handlers as PathParameters).
/// </summary>
public partial class ApiGatewayPathParametersTests
{
    private static readonly string BackendRoot = FindBackendRoot();
    private static readonly Dictionary<string, HashSet<string>> Resources = LoadResources();

    [Fact]
    public void UnderTheSameParent_AllPathParametersHaveOneName()
    {
        var conflicts = Resources.Keys
            .SelectMany(path =>
            {
                var segments = path.Trim('/').Split('/');
                return segments.Select((segment, i) => (Parent: "/" + string.Join('/', segments.Take(i)), Segment: segment));
            })
            .Where(x => x.Segment.StartsWith('{'))
            .GroupBy(x => x.Parent)
            .Select(g => (Parent: g.Key, Names: g.Select(x => x.Segment).Distinct().OrderBy(n => n).ToList()))
            .Where(g => g.Names.Count > 1)
            .Select(g => $"{g.Parent}: {string.Join(", ", g.Names)}")
            .ToList();

        Assert.Empty(conflicts);
    }

    [Theory]
    [InlineData("/locations/{id}", "GET")]
    [InlineData("/locations/{id}/dishes", "GET")]
    [InlineData("/locations/{id}/tables/all", "GET")]
    [InlineData("/locations/{id}/dishes/{dishId}", "DELETE")]
    public void LocationResources_UseId(string path, string method) =>
        Assert.Contains(method, Resources[path]);

    [Fact]
    public void EveryLambdaRoute_ExistsInApiGatewayWithTheSamePathAndMethod()
    {
        var source = File.ReadAllText(Path.Combine(BackendRoot, "dnapp", "lambdas", "api-handler", "Routing", "RouteRegistry.cs"));
        var routes = RouteRegex().Matches(source).Select(m => (Method: m.Groups[1].Value, Path: m.Groups[2].Value)).ToList();

        Assert.NotEmpty(routes);
        var missing = routes
            .Where(r => !Resources.TryGetValue(r.Path, out var methods) || !methods.Contains(r.Method))
            .Select(r => $"{r.Method} {r.Path}")
            .ToList();

        Assert.Empty(missing);
    }

    [GeneratedRegex("""new Route\(HttpMethods\.(\w+), "([^"]+)"\)""")]
    private static partial Regex RouteRegex();

    private static string FindBackendRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "deployment_resources.json")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("deployment_resources.json not found");
    }

    private static Dictionary<string, HashSet<string>> LoadResources()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(BackendRoot, "deployment_resources.json")));
        foreach (var resource in doc.RootElement.EnumerateObject())
        {
            if (resource.Value.TryGetProperty("resource_type", out var type) && type.GetString() == "api_gateway")
            {
                return resource.Value.GetProperty("resources").EnumerateObject().ToDictionary(
                    r => r.Name,
                    r => r.Value.EnumerateObject().Select(m => m.Name).Where(m => m != "enable_cors").ToHashSet());
            }
        }

        throw new InvalidOperationException("api_gateway resource not found in deployment_resources.json");
    }
}
