using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using APIHandler.Helpers;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace APIHandler.Services.GuestSessions;

public class GuestAuthorizationService(IGuestSessionService guestSessionService) : IGuestAuthorizationService
{
    public async ValueTask<APIGatewayProxyResponse?> ValidateAsync(APIGatewayProxyRequest request, ILambdaContext context, string guestSessionid)
    {
        // header names are case-insensitive (HTTP/2 clients send "authorization"); no headers at all → 401, not 500
        string? authorizationValue = request.Headers?
            .FirstOrDefault(h => string.Equals(h.Key, "Authorization", StringComparison.OrdinalIgnoreCase)).Value;

        if (string.IsNullOrEmpty(authorizationValue))
            return CreateErrorResponse(context);

        const string BearerString = "Bearer ";
        if (!authorizationValue.StartsWith(BearerString) || authorizationValue.Length <= BearerString.Length)
            return CreateErrorResponse(context);

        string token = authorizationValue[BearerString.Length..];
        bool authorized = await guestSessionService.IsAuthorizedAsync(token, guestSessionid);

        return !authorized ? CreateErrorResponse(context) : null;
    }

    private static APIGatewayProxyResponse CreateErrorResponse(ILambdaContext context)
        => ResponseBuilder.CreateErrorResponse(401, "Invalid guest session token", context.Logger);
}
