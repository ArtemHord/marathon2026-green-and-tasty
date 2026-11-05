using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using APIHandler.Helpers;
using APIHandler.Mappers;
using APIHandler.Models.Users;
using APIHandler.Repositories.Reservations;
using APIHandler.Repositories.Tables;
using APIHandler.Services.Dishes;
using APIHandler.Services.Orders;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace APIHandler.Handlers.Order;

public class GetOrderByReservationHandler : IApiRequestHandler
{
    private readonly IDishService _dishService;
    private readonly IOrderService _orderService;
    private readonly IReservationRepository _reservationRepository;
    private readonly ITableRepository _tableRepository;

    public GetOrderByReservationHandler(
        IDishService dishService,
        IOrderService orderService,
        IReservationRepository reservationRepository,
        ITableRepository tableRepository)
    {
        _reservationRepository = reservationRepository ?? throw new ArgumentNullException(nameof(reservationRepository));
        _tableRepository = tableRepository ?? throw new ArgumentNullException(nameof(tableRepository));
        _dishService = dishService ?? throw new ArgumentNullException(nameof(dishService));
        _orderService = orderService ?? throw new ArgumentNullException(nameof(orderService));
    }

    public async Task<APIGatewayProxyResponse> HandleRequest(APIGatewayProxyRequest request, ILambdaContext context)
    {
        var reservationId = request.PathParameters?["reservationId"];
        if (string.IsNullOrEmpty(reservationId))
        {
            return ResponseBuilder.CreateErrorResponse(400, "Reservation ID is required.", context.Logger);
        }

        var order = await _orderService.GetOrderByReservationIdAsync(reservationId);
        if (order == null)
        {
            return ResponseBuilder.CreateErrorResponse(404, "Order not found.", context.Logger);
        }

        if (request.RequestContext.Authorizer?.Claims is not { } claims)
        {
            return ResponseBuilder.CreateErrorResponse(403, "Forbidden: Missing claims.", context.Logger);
        }

        var isWaiter = claims.TryGetValue("cognito:groups", out var groups) && groups.Contains(Roles.Waiter.ToString());
        var isOrderCustomer = claims.TryGetValue("sub", out var sub) && sub == order.CustomerId;

        if (!isWaiter && !isOrderCustomer)
        {
            return ResponseBuilder.CreateErrorResponse(403, "Forbidden: Only waiters or the customer can access this order.", context.Logger);
        }

        var dishIds = order.Dishes.Select(d => d.DishId).ToList();
        var dishes = await _dishService.GetDishesByIdsAsync(dishIds);

        // current reservation times (UTC) and table number for the waiter pre-order page header
        var reservation = await _reservationRepository.TryGetReservationAsync(order.ReservationId);
        var table = await _tableRepository.TryGetTableAsync(reservation?.TableId);

        var orderResponse = OrderMapper.ToOrderResponse(order, dishes, reservation, table);
        return ResponseBuilder.CreateSuccessResponse(orderResponse);
    }
}