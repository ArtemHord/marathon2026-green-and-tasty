using Amazon.DynamoDBv2.Model;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using APIHandler.Helpers;
using System;

namespace Function.Services;

public class ExceptionHandler
{
    public const string ConcurrentChangeMessage = "The resource was changed concurrently. Please retry.";

    public static APIGatewayProxyResponse Handle(Exception exception, ILambdaContext context)
    {
        // Optimistic-locking conflict (Order / GroupReservation [DynamoDBVersion]) that a handler did not handle itself.
        if (exception is ConditionalCheckFailedException)
        {
            context.Logger.LogWarning($"Concurrent modification: {exception.Message}");
            return ResponseBuilder.CreateErrorResponse(409, ConcurrentChangeMessage, context.Logger);
        }

        context.Logger.LogError($"An error occurred while processing the request: {exception}");
        return ResponseBuilder.CreateErrorResponse(500, "Internal Server Error", context.Logger);
    }
}
