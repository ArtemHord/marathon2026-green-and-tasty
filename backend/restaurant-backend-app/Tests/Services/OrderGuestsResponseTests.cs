using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using APIHandler.Constants.Dishes;
using APIHandler.Constants.Orders;
using APIHandler.Entities;
using APIHandler.Handlers.Order;
using APIHandler.Mappers;
using APIHandler.Services.Dishes;
using APIHandler.Services.Orders;
using Moq;
using System.Text.Json;

namespace APIHandler.Tests.Services;

/// <summary>
/// GET /orders/{reservationId} (and GET /orders, POST /orders responses — same mapper): each order item gets
/// <c>guests: [{ guestSessionId, guestName, quantity }]</c> only when the line has Guests; the existing fields are unchanged.
/// </summary>
public class OrderGuestsResponseTests
{
    private const string CustomerId = "customer-1";
    private const string ReservationId = "reservation-1";

    private static readonly Dish Caesar = new() { Id = "caesar", Name = "Caesar Salad", Description = "d", Price = 180, Weight = "w", Calories = "c", Proteins = "p", Fats = "f", Carbohydrates = "c", Vitamins = "v", DishType = DishType.MainCourse, State = DishState.Available };
    private static readonly Dish Steak = new() { Id = "steak", Name = "Steak", Description = "d", Price = 300, Weight = "w", Calories = "c", Proteins = "p", Fats = "f", Carbohydrates = "c", Vitamins = "v", DishType = DishType.MainCourse, State = DishState.Available };

    private static readonly string[] OrderFields = ["orderId", "reservationId", "reservationAddress", "reservationTime", "orderItems", "status"];
    private static readonly string[] ItemFieldsWithoutGuests = ["dish", "quantity"];

    [Fact]
    public async Task OldOrderWithoutGuests_JsonIsExactlyAsBefore()
    {
        var body = await GetOrderJson(Order(Line("caesar", 2), Line("steak", 1)));

        using var json = JsonDocument.Parse(body);
        Assert.Equal(OrderFields, json.RootElement.EnumerateObject().Select(p => p.Name).ToArray());
        foreach (var item in json.RootElement.GetProperty("orderItems").EnumerateArray())
            Assert.Equal(ItemFieldsWithoutGuests, item.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.DoesNotContain("guests", body);
    }

    [Fact]
    public async Task LineWithGuests_HasGuestsArray_OtherLinesHaveNoField()
    {
        var caesar = Line("caesar", 3);
        caesar.Guests =
        [
            new OrderItemGuestShare { GuestSessionId = "s-andrii", GuestName = "Andrii", Quantity = 2 },
            new OrderItemGuestShare { GuestSessionId = "s-olena", GuestName = "Olena", Quantity = 1 },
        ];

        var body = await GetOrderJson(Order(caesar, Line("steak", 1)));

        using var json = JsonDocument.Parse(body);
        var items = json.RootElement.GetProperty("orderItems");
        Assert.Equal(OrderFields, json.RootElement.EnumerateObject().Select(p => p.Name).ToArray());

        var withGuests = items[0];
        Assert.Equal(["dish", "quantity", "guests"], withGuests.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Equal("caesar", withGuests.GetProperty("dish").GetProperty("id").GetString());
        Assert.Equal(3, withGuests.GetProperty("quantity").GetInt32());
        var guests = withGuests.GetProperty("guests").EnumerateArray().ToList();
        Assert.Equal(["guestSessionId", "guestName", "quantity"], guests[0].EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Equal([("s-andrii", "Andrii", 2), ("s-olena", "Olena", 1)],
            guests.Select(g => (g.GetProperty("guestSessionId").GetString()!, g.GetProperty("guestName").GetString()!, g.GetProperty("quantity").GetInt32())).ToList());

        Assert.Equal(ItemFieldsWithoutGuests, items[1].EnumerateObject().Select(p => p.Name).ToArray());
    }

    [Fact]
    public void Mapper_GuestsNull_ResponseGuestsNull()
    {
        var response = OrderMapper.ToOrderResponse(Order(Line("caesar", 1)), [Caesar]);

        Assert.Null(Assert.Single(response.OrderItems).Guests);
    }

    [Fact]
    public void Mapper_CopiesEveryShare()
    {
        var line = Line("caesar", 2);
        line.Guests = [new OrderItemGuestShare { GuestSessionId = "owner-g1", GuestName = "Yevhenii", Quantity = 2 }];

        var guest = Assert.Single(Assert.Single(OrderMapper.ToOrderResponse(Order(line), [Caesar]).OrderItems).Guests!);

        Assert.Equal(("owner-g1", "Yevhenii", 2), (guest.GuestSessionId, guest.GuestName, guest.Quantity));
    }

    [Fact]
    public async Task CustomerOrdersList_GuestsOnlyWhereSet()
    {
        var caesar = Line("caesar", 1);
        caesar.Guests = [new OrderItemGuestShare { GuestSessionId = "s-andrii", GuestName = "Andrii", Quantity = 1 }];
        var orderService = new Mock<IOrderService>();
        orderService.Setup(s => s.GetOrdersByCustomerIdAsync(CustomerId)).ReturnsAsync([Order(caesar, Line("steak", 1))]);

        var response = await new GetOrdersByCustomerHandler(DishService().Object, orderService.Object).HandleRequest(Request(), Context());

        Assert.Equal(200, response.StatusCode);
        using var json = JsonDocument.Parse(response.Body);
        var items = json.RootElement[0].GetProperty("orderItems");
        Assert.True(items[0].TryGetProperty("guests", out _));
        Assert.False(items[1].TryGetProperty("guests", out _));
    }

    // ---------- helpers ----------

    private static async Task<string> GetOrderJson(Order order)
    {
        var orderService = new Mock<IOrderService>();
        orderService.Setup(s => s.GetOrderByReservationIdAsync(ReservationId)).ReturnsAsync(order);

        var response = await new GetOrderByReservationHandler(DishService().Object, orderService.Object).HandleRequest(Request(), Context());

        Assert.Equal(200, response.StatusCode);
        return response.Body;
    }

    private static Mock<IDishService> DishService()
    {
        var dishService = new Mock<IDishService>();
        dishService.Setup(s => s.GetDishesByIdsAsync(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync((IEnumerable<string> ids) => new[] { Caesar, Steak }.Where(d => ids.Contains(d.Id)).ToList());
        return dishService;
    }

    private static Order Order(params OrderItem[] lines) => new()
    {
        OrderId = "order-1", CustomerId = CustomerId, ReservationId = ReservationId, ReservationAddress = "a", ReservationTime = "t",
        Status = OrderStatus.SubmittedByCustomer, Dishes = lines.ToList(),
    };

    private static OrderItem Line(string dishId, int quantity) => new() { DishId = dishId, Quantity = quantity, State = OrderItemState.Pending };

    private static APIGatewayProxyRequest Request() => new()
    {
        PathParameters = new Dictionary<string, string> { ["reservationId"] = ReservationId },
        RequestContext = new APIGatewayProxyRequest.ProxyRequestContext
        {
            Authorizer = new APIGatewayCustomAuthorizerContext { Claims = new Dictionary<string, string> { ["sub"] = CustomerId } },
        },
    };

    private static ILambdaContext Context()
    {
        var context = new Mock<ILambdaContext>();
        context.SetupGet(c => c.Logger).Returns(new Mock<ILambdaLogger>().Object);
        return context.Object;
    }
}
