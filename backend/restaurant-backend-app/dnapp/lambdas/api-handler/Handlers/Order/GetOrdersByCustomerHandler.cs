using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using APIHandler.Entities;
using APIHandler.Helpers;
using APIHandler.Mappers;
using APIHandler.Models.Orders;
using APIHandler.Repositories.Reservations;
using APIHandler.Repositories.Tables;
using APIHandler.Services.Dishes;
using APIHandler.Services.Orders;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace APIHandler.Handlers.Order;

public class GetOrdersByCustomerHandler : IApiRequestHandler
{
    private readonly IDishService _dishService;
    private readonly IOrderService _orderService;
    private readonly IReservationRepository _reservationRepository;
    private readonly ITableRepository _tableRepository;

    public GetOrdersByCustomerHandler(
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
        if (request.RequestContext.Authorizer?.Claims is null ||
            !request.RequestContext.Authorizer.Claims.TryGetValue("sub", out var userId))
        {
            return ResponseBuilder.CreateErrorResponse(401, "You should be signed in to access orders.", context.Logger);
        }

        var orders = await _orderService.GetOrdersByCustomerIdAsync(userId);
        orders = orders.Where(o => o.Status != OrderStatus.Cancelled).ToList();

        var dishIds = orders.SelectMany(o => o.Dishes.Select(d => d.DishId)).Distinct().ToList();
        var dishes = await _dishService.GetDishesByIdsAsync(dishIds);

        // Reservation + table of every order are read in parallel (best effort: a failed read just omits the fields);
        // tables repeat between orders, so each tableId is read once per request.
        var tables = new RequestTableCache(_tableRepository);
        var orderResponses = await Task.WhenAll(orders.Select(order => ToResponseAsync(order, dishes, tables)));

        return ResponseBuilder.CreateSuccessResponse(orderResponses);
    }

    private async Task<GetOrderResponse> ToResponseAsync(Entities.Order order, IEnumerable<Entities.Dish> dishes, RequestTableCache tables)
    {
        var reservation = await _reservationRepository.TryGetReservationAsync(order.ReservationId);
        var table = await tables.GetAsync(reservation?.TableId);
        return OrderMapper.ToOrderResponse(order, dishes, reservation, table);
    }
}