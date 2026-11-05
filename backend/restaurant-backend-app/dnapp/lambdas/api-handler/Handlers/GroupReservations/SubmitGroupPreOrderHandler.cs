using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using APIHandler.Exceptions;
using APIHandler.Helpers;
using APIHandler.Models.GroupReservation;
using APIHandler.Services.GroupReservations;
using System;
using System.Text.Json;
using System.Threading.Tasks;

namespace APIHandler.Handlers.GroupReservations;

/// <summary><c>POST /group-reservations/{groupReservationId}/submit</c> — owner confirms the consolidated group pre-order.</summary>
public class SubmitGroupPreOrderHandler : IApiRequestHandler
{
    private static readonly JsonSerializerOptions s_jsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IGroupReservationService _groupReservationService;

    public SubmitGroupPreOrderHandler(IGroupReservationService groupReservationService)
    {
        _groupReservationService = groupReservationService ?? throw new ArgumentNullException(nameof(groupReservationService));
    }

    public async Task<APIGatewayProxyResponse> HandleRequest(APIGatewayProxyRequest request, ILambdaContext context)
    {
        if (!request.TryGetPathValue("groupReservationId", out var groupReservationId))
        {
            return ResponseBuilder.CreateErrorResponse(400, "Missing 'groupReservationId' in path parameters.", context.Logger);
        }

        if (request.RequestContext?.Authorizer?.Claims == null ||
            !request.RequestContext.Authorizer.Claims.TryGetValue("sub", out var userId))
        {
            return ResponseBuilder.CreateErrorResponse(401, "Unauthorized.", context.Logger);
        }

        if (!TryParseBody(request.Body, out var body))
        {
            return ResponseBuilder.CreateErrorResponse(400, "Invalid request body.", context.Logger);
        }

        if (string.IsNullOrWhiteSpace(body.ReservationId))
        {
            return ResponseBuilder.CreateErrorResponse(400, "'reservationId' is required.", context.Logger);
        }

        try
        {
            var dishes = await _groupReservationService.SubmitGroupPreOrderAsync(userId, groupReservationId, body.ReservationId);
            return ResponseBuilder.CreateSuccessResponse(dishes);
        }
        catch (ReservationNotFoundException ex)
        {
            return ResponseBuilder.CreateErrorResponse(404, ex.Message, context.Logger);
        }
        catch (GroupReservationNotFoundException ex)
        {
            return ResponseBuilder.CreateErrorResponse(404, ex.Message, context.Logger);
        }
        catch (ActionForbiddenException ex)
        {
            return ResponseBuilder.CreateErrorResponse(403, ex.Message, context.Logger);
        }
        catch (ValidationException ex)
        {
            return ResponseBuilder.CreateErrorCodeResponse(ex);
        }
    }

    private static bool TryParseBody(string? rawBody, out SubmitGroupPreOrderRequest body)
    {
        body = new SubmitGroupPreOrderRequest();

        if (string.IsNullOrWhiteSpace(rawBody))
            return true; // reported as missing 'reservationId'

        try
        {
            body = JsonSerializer.Deserialize<SubmitGroupPreOrderRequest>(rawBody, s_jsonOptions) ?? new SubmitGroupPreOrderRequest();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
