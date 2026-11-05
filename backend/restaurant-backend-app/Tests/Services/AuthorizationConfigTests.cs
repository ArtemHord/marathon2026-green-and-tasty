using System.Text.Json;

namespace APIHandler.Tests.Services;

/// <summary>
/// API Gateway authorizers in deployment_resources.json for the group pre-order endpoints:
/// Owner endpoints are behind the Cognito authorizer, guest endpoints are public (guest session token is checked in code).
/// </summary>
public class AuthorizationConfigTests
{
    private static readonly JsonElement Resources = LoadResources();

    [Theory]
    [InlineData("/reservations/{reservationId}/invite-token", "GET")]
    [InlineData("/reservations/{reservationId}/guest-session-id", "GET")]
    [InlineData("/group-reservations/{groupReservationId}/submit", "POST")]
    [InlineData("/guest/groups/{groupReservationId}/sessions", "GET")]
    [InlineData("/guest/groups/{groupReservationId}/sessions/{guestSessionId}", "DELETE")]
    [InlineData("/reservations/{reservationId}/bill-split", "GET")] // owner customer or assigned waiter, checked in code
    public void OwnerEndpoints_UseCognitoAuthorizer(string path, string method) =>
        Assert.Equal("cognito_authorizer", AuthorizationType(path, method));

    [Theory]
    [InlineData("/guest/invites/{token}", "GET")]
    [InlineData("/guest/sessions", "POST")]
    [InlineData("/guest/sessions/{guestSessionId}", "GET")]
    [InlineData("/guest/sessions/{guestSessionId}/dishes", "POST")]
    [InlineData("/guest/sessions/{guestSessionId}/dishes", "DELETE")]
    [InlineData("/guest/sessions/{guestSessionId}/menu", "GET")]
    [InlineData("/guest/sessions/{guestSessionId}/submit", "POST")]
    [InlineData("/guest/groups/{groupReservationId}/status", "GET")]
    public void GuestEndpoints_ArePublic(string path, string method) =>
        Assert.Equal("NONE", AuthorizationType(path, method));

    private static string? AuthorizationType(string path, string method) =>
        Resources.GetProperty(path).GetProperty(method).GetProperty("authorization_type").GetString();

    private static JsonElement LoadResources()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "deployment_resources.json")))
            dir = dir.Parent;
        Assert.NotNull(dir);

        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir!.FullName, "deployment_resources.json")));
        foreach (var resource in doc.RootElement.EnumerateObject())
        {
            if (resource.Value.TryGetProperty("resource_type", out var type) && type.GetString() == "api_gateway")
                return resource.Value.GetProperty("resources").Clone();
        }

        throw new InvalidOperationException("api_gateway resource not found in deployment_resources.json");
    }
}
