using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using APIHandler.Exceptions;
using APIHandler.Helpers;
using APIHandler.Services.Orders;
using System;
using System.Threading.Tasks;

namespace APIHandler.Handlers.Reservations;

/// <summary>
/// <c>GET /reservations/{reservationId}/bill-split</c> — the order split by participant (who owes what).
/// For the customer who owns the reservation or its assigned waiter.
/// </summary>
public class GetBillSplitHandler : IApiRequestHandler
{
    private readonly IBillSplitService _billSplitService;

    public GetBillSplitHandler(IBillSplitService billSplitService)
    {
        _billSplitService = billSplitService ?? throw new ArgumentNullException(nameof(billSplitService));
    }

    public async Task<APIGatewayProxyResponse> HandleRequest(APIGatewayProxyRequest request, ILambdaContext context)
    {
        if (!request.TryGetPathValue("reservationId", out var reservationId))
        {
            return ResponseBuilder.CreateErrorResponse(400, "Missing 'reservationId' in path parameters.", context.Logger);
        }

        if (request.RequestContext?.Authorizer?.Claims == null ||
            !request.RequestContext.Authorizer.Claims.TryGetValue("sub", out var userId) ||
            string.IsNullOrEmpty(userId))
        {
            return ResponseBuilder.CreateErrorResponse(401, "Unauthorized.", context.Logger);
        }

        try
        {
            var billSplit = await _billSplitService.GetBillSplitAsync(userId, reservationId);
            return ResponseBuilder.CreateSuccessResponse(billSplit);
        }
        catch (ReservationNotFoundException ex)
        {
            return ResponseBuilder.CreateErrorResponse(404, ex.Message, context.Logger);
        }
        catch (OrderNotFoundException ex)
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
}
