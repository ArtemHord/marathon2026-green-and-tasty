using Amazon.DynamoDBv2.DataModel;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using APIHandler.Constants;
using APIHandler.Constants.Dishes;
using APIHandler.Constants.Orders;
using APIHandler.Entities;
using APIHandler.Handlers.Order;
using APIHandler.Handlers.Reservations;
using APIHandler.Mappers;
using APIHandler.Models.Reservations;
using APIHandler.Models.WaiterList;
using APIHandler.Repositories.Auth;
using APIHandler.Repositories.GroupReservations;
using APIHandler.Repositories.Locations;
using APIHandler.Repositories.Reservations;
using APIHandler.Repositories.Tables;
using APIHandler.Services.Dishes;
using APIHandler.Services.GroupReservations;
using APIHandler.Services.Locations;
using APIHandler.Services.Orders;
using APIHandler.Services.Reservations;
using APIHandler.Services.Waiters;
using Moq;
using System.Text.Json;

namespace APIHandler.Tests.Services;

/// <summary>
/// Bugs from manual QA on the deployed app:
/// (3) waiter pre-order page: "Unknown table" (no tableName/tableNumber in reservation responses) and the time an hour off
///     (order.reservationTime is a UTC text snapshot; now the order also carries reservationStartTime/EndTime in UTC);
/// (5) Customer → Cart: "(Unknown)" next to dishes (order items had no "state");
/// (2) after editing a reservation the modal showed the tableId (UUID): PUT /reservations now returns tableNumber.
/// </summary>
public class ManualQaFixesTests
{
    private const string CustomerId = "customer-1";
    private const string WaiterId = "waiter-1";
    private const string ReservationId = "reservation-1";
    private const string TableId = "6f1c2a9e-0000-4000-8000-000000000001";

    private static readonly DateTime Start = new(2027, 3, 10, 13, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime End = Start.AddMinutes(90);

    private static readonly Dish Caesar = new() { Id = "caesar", Name = "Caesar Salad", Description = "d", Price = 180, Weight = "w", Calories = "c", Proteins = "p", Fats = "f", Carbohydrates = "c", Vitamins = "v", DishType = DishType.MainCourse, State = DishState.Available };
    private static readonly Dish Steak = new() { Id = "steak", Name = "Steak", Description = "d", Price = 300, Weight = "w", Calories = "c", Proteins = "p", Fats = "f", Carbohydrates = "c", Vitamins = "v", DishType = DishType.MainCourse, State = DishState.Available };
    private static readonly Dish Soup = new() { Id = "soup", Name = "Soup", Description = "d", Price = 90, Weight = "w", Calories = "c", Proteins = "p", Fats = "f", Carbohydrates = "c", Vitamins = "v", DishType = DishType.Appetizer, State = DishState.Available };

    private readonly Mock<IReservationRepository> _reservationRepo = new();
    private readonly Mock<ITableRepository> _tableRepo = new();
    private readonly Mock<IOrderService> _orderService = new();
    private readonly Mock<ILocationService> _locationService = new();

    private readonly Reservation _reservation = new()
    {
        Id = ReservationId, TableId = TableId, CustomerId = CustomerId, WaiterId = WaiterId,
        StartTime = Start, EndTime = End, GuestCount = 1, Status = ReservationStatus.Reserved,
    };

    private readonly Table _table = new() { Id = TableId, LocationId = "loc-1", Capacity = 4, TableNumber = 7 };
    private readonly Location _location = new() { Id = "loc-1", Address = "Rustaveli 1", Description = "d", AverageOccupancy = "0" };

    public ManualQaFixesTests()
    {
        // handlers that read ResourceNames (as in GroupPreOrderRound2Tests): keep a value if the environment already has one
        foreach (var (name, value) in new[]
        {
            ("reservationsTable", "Reservations-test"), ("tablesTable", "Tables-test"),
            ("reservationsTableGlobalIndex", "GSI_ByTableId"), ("reservationsCustomerIdGlobalIndex", "GSI_UserId"),
            ("reservationsWaiterIdGlobalIndex", "GSI_WaiterId"),
        })
            Environment.SetEnvironmentVariable(name, Environment.GetEnvironmentVariable(name) ?? value);

        _reservationRepo.Setup(r => r.GetReservationByIdAsync(ReservationId)).ReturnsAsync(_reservation);
        _tableRepo.Setup(r => r.GetTableByIdAsync(TableId)).ReturnsAsync(_table);
        _locationService.Setup(s => s.GetLocationByTableIdAsync(TableId)).ReturnsAsync(_location);
    }

    // ================= (5) Cart "(Unknown)": order items carry "state" =================

    [Theory]
    [InlineData(OrderItemState.Pending, 0)]
    [InlineData(OrderItemState.Confirmed, 1)]
    [InlineData(OrderItemState.Canceled, 2)]
    public async Task GetOrderByReservation_EveryItemHasNumericState(OrderItemState state, int expected)
    {
        var json = await GetOrderJson(Order(Line("caesar", 2, state)));

        Assert.Equal(expected, json.RootElement.GetProperty("orderItems")[0].GetProperty("state").GetInt32());
    }

    [Fact]
    public async Task CustomerOrdersList_Cart_ItemsHaveTheirOwnStates()
    {
        _orderService.Setup(s => s.GetOrdersByCustomerIdAsync(CustomerId)).ReturnsAsync(
            [Order(Line("caesar", 1, OrderItemState.Pending), Line("steak", 1, OrderItemState.Confirmed), Line("soup", 1, OrderItemState.Canceled))]);

        var response = await new GetOrdersByCustomerHandler(DishService().Object, _orderService.Object, _reservationRepo.Object, _tableRepo.Object)
            .HandleRequest(Request(CustomerId), Context());

        Assert.Equal(200, response.StatusCode);
        using var json = JsonDocument.Parse(response.Body);
        Assert.Equal([0, 1, 2], json.RootElement[0].GetProperty("orderItems").EnumerateArray().Select(i => i.GetProperty("state").GetInt32()).ToArray());
    }

    [Fact]
    public void Mapper_StateCopiedFromTheOrderLine()
    {
        var response = OrderMapper.ToOrderResponse(Order(Line("caesar", 1, OrderItemState.Confirmed)), [Caesar]);

        Assert.Equal(OrderItemState.Confirmed, Assert.Single(response.OrderItems).State);
    }

    // ================= (3) waiter pre-order page: time and table =================

    [Fact]
    public async Task GetOrderByReservation_HasReservationTimesInUtcAndTableNumber_OldFieldsUnchanged()
    {
        var order = Order(Line("caesar", 1));

        var json = await GetOrderJson(order);
        var root = json.RootElement;

        Assert.Equal("2027-03-10T13:00:00Z", root.GetProperty("reservationStartTime").GetString());
        Assert.Equal("2027-03-10T14:30:00Z", root.GetProperty("reservationEndTime").GetString());
        Assert.Equal(7, root.GetProperty("tableNumber").GetInt32());
        Assert.Equal("Table 7", root.GetProperty("tableName").GetString());
        // existing fields keep their names and values
        Assert.Equal(order.ReservationTime, root.GetProperty("reservationTime").GetString());
        Assert.Equal(order.ReservationAddress, root.GetProperty("reservationAddress").GetString());
        Assert.Equal(["orderId", "reservationId", "reservationAddress", "reservationTime", "orderItems", "status",
                      "reservationStartTime", "reservationEndTime", "tableNumber", "tableName"],
            root.EnumerateObject().Select(p => p.Name).ToArray());
    }

    [Fact]
    public async Task GetOrderByReservation_ReservationTimesAreTheCurrentOnes_NotTheCreationSnapshot()
    {
        // the reservation was moved after the order was created: reservationTime (text snapshot) is stale, the new fields are not
        _reservation.StartTime = Start.AddHours(2);
        _reservation.EndTime = End.AddHours(2);

        var json = await GetOrderJson(Order(Line("caesar", 1)));

        Assert.Equal("2027-03-10T15:00:00Z", json.RootElement.GetProperty("reservationStartTime").GetString());
    }

    [Fact]
    public async Task GetOrderByReservation_ReservationOrTableMissing_FieldsOmitted_Still200()
    {
        _reservationRepo.Setup(r => r.GetReservationByIdAsync(ReservationId)).ReturnsAsync((Reservation?)null);

        var json = await GetOrderJson(Order(Line("caesar", 1)));

        foreach (var field in new[] { "reservationStartTime", "reservationEndTime", "tableNumber", "tableName" })
            Assert.False(json.RootElement.TryGetProperty(field, out _), field);
    }

    [Fact]
    public async Task GetOrderByReservation_TableLookupThrows_TableOmitted_TimesStillThere()
    {
        _tableRepo.Setup(r => r.GetTableByIdAsync(TableId)).ThrowsAsync(new InvalidOperationException("dynamo down"));

        var json = await GetOrderJson(Order(Line("caesar", 1)));

        Assert.False(json.RootElement.TryGetProperty("tableNumber", out _));
        Assert.True(json.RootElement.TryGetProperty("reservationStartTime", out _));
    }

    public static TheoryData<DateTimeKind> Kinds() => new() { DateTimeKind.Utc, DateTimeKind.Unspecified, DateTimeKind.Local };

    [Theory]
    [MemberData(nameof(Kinds))]
    public void AsUtc_AnyKind_SerializesAsTheSameInstantWithZ(DateTimeKind kind)
    {
        var value = kind switch
        {
            DateTimeKind.Local => Start.ToLocalTime(),
            DateTimeKind.Unspecified => DateTime.SpecifyKind(Start, DateTimeKind.Unspecified),
            _ => Start,
        };

        var utc = OrderMapper.AsUtc(value);

        Assert.Equal(DateTimeKind.Utc, utc.Kind);
        Assert.Equal(Start, utc);
        Assert.Equal("\"2027-03-10T13:00:00Z\"", JsonSerializer.Serialize(utc));
    }

    [Theory]
    [InlineData("Waiter")]
    [InlineData("Customer")]
    public async Task GetReservations_WaiterAndCustomer_HaveTableNumberAndTableName(string role)
    {
        _reservationRepo.Setup(r => r.GetReservationsByWaiterIdAsync(WaiterId)).ReturnsAsync([_reservation]);
        _reservationRepo.Setup(r => r.GetReservationsByCustomerIdAsync(CustomerId)).ReturnsAsync([_reservation]);

        var response = await ReservationsHandler().HandleRequest(Request(role == "Waiter" ? WaiterId : CustomerId, role), Context());

        Assert.Equal(200, response.StatusCode);
        using var json = JsonDocument.Parse(response.Body);
        var reservation = json.RootElement[0];
        Assert.Equal(TableId, reservation.GetProperty("tableId").GetString()); // unchanged
        Assert.Equal(7, reservation.GetProperty("tableNumber").GetInt32());
        Assert.Equal("Table 7", reservation.GetProperty("tableName").GetString());
    }

    [Fact]
    public async Task GetReservations_TableLookupFails_ReservationStillListedWithoutTableFields()
    {
        _reservationRepo.Setup(r => r.GetReservationsByWaiterIdAsync(WaiterId)).ReturnsAsync([_reservation]);
        _tableRepo.Setup(r => r.GetTableByIdAsync(TableId)).ThrowsAsync(new InvalidOperationException("dynamo down"));

        var response = await ReservationsHandler().HandleRequest(Request(WaiterId, "Waiter"), Context());

        Assert.Equal(200, response.StatusCode);
        using var json = JsonDocument.Parse(response.Body);
        Assert.Equal(ReservationId, json.RootElement[0].GetProperty("id").GetString());
        Assert.False(json.RootElement[0].TryGetProperty("tableNumber", out _));
        Assert.False(json.RootElement[0].TryGetProperty("tableName", out _));
    }

    [Fact]
    public async Task GetReservationById_HasTableNumber()
    {
        var reservationService = new Mock<IReservationService>();
        reservationService.Setup(s => s.GetReservationByIdAsync(ReservationId)).ReturnsAsync(_reservation);
        var request = Request(CustomerId);
        request.PathParameters = new Dictionary<string, string> { ["reservationId"] = ReservationId };

        var response = await new GetReservationByIdHandler(_orderService.Object, _locationService.Object, reservationService.Object, _tableRepo.Object)
            .HandleRequest(request, Context());

        Assert.Equal(200, response.StatusCode);
        using var json = JsonDocument.Parse(response.Body);
        Assert.Equal(7, json.RootElement.GetProperty("tableNumber").GetInt32());
    }

    [Fact]
    public void ReservationMapper_WithoutTable_NoTableFieldsInJson()
    {
        var json = JsonSerializer.Serialize(ReservationMapper.ToReservationResponse(_reservation, "Rustaveli 1"));

        Assert.DoesNotContain("tableNumber", json);
        Assert.DoesNotContain("tableName", json);
        Assert.Contains("\"tableId\"", json);
    }

    // ================= N+1: request-scoped table cache, parallel reads in GET /orders =================

    [Fact]
    public async Task CustomerOrdersList_SameTableReadOnce_OrdersKeepTheirOrderAndFields()
    {
        var other = new Reservation { Id = "reservation-2", TableId = TableId, CustomerId = CustomerId, WaiterId = WaiterId, StartTime = Start.AddDays(1), EndTime = End.AddDays(1) };
        var third = new Reservation { Id = "reservation-3", TableId = "table-9", CustomerId = CustomerId, WaiterId = WaiterId, StartTime = Start.AddDays(2), EndTime = End.AddDays(2) };
        _reservationRepo.Setup(r => r.GetReservationByIdAsync("reservation-2")).ReturnsAsync(other);
        _reservationRepo.Setup(r => r.GetReservationByIdAsync("reservation-3")).ReturnsAsync(third);
        _tableRepo.Setup(r => r.GetTableByIdAsync("table-9")).ReturnsAsync(new Table { Id = "table-9", LocationId = "loc-1", TableNumber = 9 });
        _orderService.Setup(s => s.GetOrdersByCustomerIdAsync(CustomerId)).ReturnsAsync(
        [
            OrderFor(ReservationId, "order-1"), OrderFor("reservation-2", "order-2"), OrderFor("reservation-3", "order-3"),
            OrderFor("reservation-missing", "order-4"),
        ]);

        var response = await new GetOrdersByCustomerHandler(DishService().Object, _orderService.Object, _reservationRepo.Object, _tableRepo.Object)
            .HandleRequest(Request(CustomerId), Context());

        Assert.Equal(200, response.StatusCode);
        using var json = JsonDocument.Parse(response.Body);
        var orders = json.RootElement.EnumerateArray().ToList();
        Assert.Equal(["order-1", "order-2", "order-3", "order-4"], orders.Select(o => o.GetProperty("orderId").GetString()).ToArray());
        Assert.Equal([7, 7, 9], orders.Take(3).Select(o => o.GetProperty("tableNumber").GetInt32()).ToArray());
        Assert.Equal("2027-03-11T13:00:00Z", orders[1].GetProperty("reservationStartTime").GetString());
        Assert.False(orders[3].TryGetProperty("tableNumber", out _)); // unknown reservation: best effort, no fields
        _tableRepo.Verify(r => r.GetTableByIdAsync(TableId), Times.Once);
        _tableRepo.Verify(r => r.GetTableByIdAsync("table-9"), Times.Once);
    }

    [Fact]
    public async Task CustomerOrdersList_ReservationReadFails_OnlyThatOrderLosesTheFields()
    {
        _reservationRepo.Setup(r => r.GetReservationByIdAsync("reservation-2")).ThrowsAsync(new InvalidOperationException("dynamo down"));
        _orderService.Setup(s => s.GetOrdersByCustomerIdAsync(CustomerId)).ReturnsAsync([OrderFor(ReservationId, "order-1"), OrderFor("reservation-2", "order-2")]);

        var response = await new GetOrdersByCustomerHandler(DishService().Object, _orderService.Object, _reservationRepo.Object, _tableRepo.Object)
            .HandleRequest(Request(CustomerId), Context());

        Assert.Equal(200, response.StatusCode);
        using var json = JsonDocument.Parse(response.Body);
        Assert.Equal(7, json.RootElement[0].GetProperty("tableNumber").GetInt32());
        Assert.False(json.RootElement[1].TryGetProperty("reservationStartTime", out _));
    }

    [Theory]
    [InlineData("Waiter")]
    [InlineData("Customer")]
    public async Task GetReservations_SameTableReadOncePerRequest(string role)
    {
        var reservations = Enumerable.Range(1, 3).Select(i => new Reservation
        {
            Id = $"r-{i}", TableId = TableId, CustomerId = CustomerId, WaiterId = WaiterId, StartTime = Start.AddDays(i), EndTime = End.AddDays(i), GuestCount = 1,
        }).ToList();
        _reservationRepo.Setup(r => r.GetReservationsByWaiterIdAsync(WaiterId)).ReturnsAsync(reservations);
        _reservationRepo.Setup(r => r.GetReservationsByCustomerIdAsync(CustomerId)).ReturnsAsync(reservations);
        foreach (var r in reservations)
            _reservationRepo.Setup(x => x.GetReservationByIdAsync(r.Id)).ReturnsAsync(r);

        var response = await ReservationsHandler().HandleRequest(Request(role == "Waiter" ? WaiterId : CustomerId, role), Context());

        Assert.Equal(200, response.StatusCode);
        using var json = JsonDocument.Parse(response.Body);
        Assert.All(json.RootElement.EnumerateArray(), r => Assert.Equal(7, r.GetProperty("tableNumber").GetInt32()));
        _tableRepo.Verify(r => r.GetTableByIdAsync(TableId), Times.Once);
    }

    [Fact]
    public async Task GetReservations_FailedTableRead_CachedAsNull_NotRetriedPerReservation()
    {
        var reservations = new List<Reservation>
        {
            new() { Id = "r-1", TableId = TableId, WaiterId = WaiterId, StartTime = Start, EndTime = End, GuestCount = 1 },
            new() { Id = "r-2", TableId = TableId, WaiterId = WaiterId, StartTime = Start.AddDays(1), EndTime = End.AddDays(1), GuestCount = 1 },
        };
        _reservationRepo.Setup(r => r.GetReservationsByWaiterIdAsync(WaiterId)).ReturnsAsync(reservations);
        _tableRepo.Setup(r => r.GetTableByIdAsync(TableId)).ThrowsAsync(new InvalidOperationException("dynamo down"));

        var response = await ReservationsHandler().HandleRequest(Request(WaiterId, "Waiter"), Context());

        Assert.Equal(200, response.StatusCode);
        using var json = JsonDocument.Parse(response.Body);
        Assert.Equal(2, json.RootElement.GetArrayLength());
        Assert.All(json.RootElement.EnumerateArray(), r => Assert.False(r.TryGetProperty("tableNumber", out _)));
        _tableRepo.Verify(r => r.GetTableByIdAsync(TableId), Times.Once);
    }

    [Fact]
    public async Task RequestTableCache_ConcurrentCallsForTheSameTable_ShareOneRead()
    {
        var pending = new TaskCompletionSource<Table?>();
        _tableRepo.Setup(r => r.GetTableByIdAsync(TableId)).Returns(pending.Task);
        var cache = new APIHandler.Helpers.RequestTableCache(_tableRepo.Object);

        var calls = Enumerable.Range(0, 5).Select(_ => cache.GetAsync(TableId)).ToArray();
        pending.SetResult(_table);
        var tables = await Task.WhenAll(calls);

        Assert.All(tables, t => Assert.Same(_table, t));
        _tableRepo.Verify(r => r.GetTableByIdAsync(TableId), Times.Once);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task RequestTableCache_NoTableId_NullWithoutARead(string? tableId)
    {
        var cache = new APIHandler.Helpers.RequestTableCache(_tableRepo.Object);

        Assert.Null(await cache.GetAsync(tableId));
        _tableRepo.Verify(r => r.GetTableByIdAsync(It.IsAny<string>()), Times.Never);
    }

    // ================= (2) PUT /reservations returns the table number =================

    [Fact]
    public async Task EditReservation_ResponseHasTableNumberAndTableName_TableIdUnchanged()
    {
        var db = new Mock<IDynamoDBContext>();
        db.Setup(c => c.LoadAsync<Reservation>(ReservationId, It.IsAny<LoadConfig>(), It.IsAny<CancellationToken>())).ReturnsAsync(_reservation);
        db.Setup(c => c.LoadAsync<Table>(TableId, It.IsAny<LoadConfig>(), It.IsAny<CancellationToken>())).ReturnsAsync(_table);
        var search = new Mock<IAsyncSearch<Reservation>>();
        search.Setup(s => s.GetRemainingAsync(It.IsAny<CancellationToken>())).ReturnsAsync([_reservation]);
        db.Setup(c => c.QueryAsync<Reservation>(TableId, It.IsAny<QueryConfig>())).Returns(search.Object);

        var newStart = DateTime.UtcNow.AddDays(3).Date.AddHours(12);
        var request = Request(CustomerId, "Customer");
        request.Body = JsonSerializer.Serialize(new { reservationId = ReservationId, startTime = newStart, endTime = newStart.AddMinutes(90), guestCount = 3 });

        var response = await new EditReservationHandler(db.Object, _locationService.Object).HandleRequest(request, Context());

        Assert.Equal(200, response.StatusCode);
        using var json = JsonDocument.Parse(response.Body);
        Assert.Equal(TableId, json.RootElement.GetProperty("tableId").GetString());
        Assert.Equal(7, json.RootElement.GetProperty("tableNumber").GetInt32());
        Assert.Equal("Table 7", json.RootElement.GetProperty("tableName").GetString());
        Assert.Equal(3, json.RootElement.GetProperty("guestCount").GetInt32());
    }

    [Fact]
    public async Task CreateReservation_ResponseHasTableNumber()
    {
        var (service, locationRepo, waiterService, _) = ReservationServiceWithMocks();
        var request = new CreateReservationRequest { TableId = TableId, StartTime = DateTime.UtcNow.AddDays(2), EndTime = DateTime.UtcNow.AddDays(2).AddMinutes(90), GuestCount = 1 };
        var waiter = new WaiterListItem { UserId = WaiterId, LocationId = _table.LocationId };
        locationRepo.Setup(r => r.GetTableByIdAsync(TableId)).ReturnsAsync(_table);
        waiterService.Setup(s => s.GetLeastBusyWaiterAsync(_table.LocationId)).ReturnsAsync(waiter);
        waiterService.Setup(s => s.FindWaiterByIdAsync(WaiterId)).ReturnsAsync(waiter);
        _reservationRepo.Setup(r => r.GetReservationsByTableIdAsync(TableId)).ReturnsAsync([]);

        var response = await service.CreateReservationAsync(request, CustomerId, isWaiter: false, new Mock<ILambdaLogger>().Object);

        Assert.Equal((7, "Table 7"), (response.TableNumber, response.TableName));
    }

    [Fact]
    public async Task UpdateReservationTable_ResponseHasTheNewTableNumber()
    {
        var (service, locationRepo, waiterService, _) = ReservationServiceWithMocks();
        var newTable = new Table { Id = "table-2", LocationId = _table.LocationId, Capacity = 6, TableNumber = 12 };
        locationRepo.Setup(r => r.GetTableByIdAsync("table-2")).ReturnsAsync(newTable);
        locationRepo.Setup(r => r.GetTableByIdAsync(TableId)).ReturnsAsync(_table);
        waiterService.Setup(s => s.FindWaiterByIdAsync(WaiterId)).ReturnsAsync(new WaiterListItem { UserId = WaiterId, LocationId = _table.LocationId });
        _reservationRepo.Setup(r => r.GetReservationsByTableIdAsync("table-2")).ReturnsAsync([]);
        _locationService.Setup(s => s.GetLocationByTableIdAsync("table-2")).ReturnsAsync(_location);

        var response = await service.UpdateReservationTableAsync(WaiterId, ReservationId, "table-2", new Mock<ILambdaLogger>().Object);

        Assert.Equal(("table-2", 12, "Table 12"), (response.TableId, response.TableNumber, response.TableName));
    }

    // ---------- helpers ----------

    private (ReservationService Service, Mock<ILocationRepository> LocationRepo, Mock<IWaiterService> WaiterService, Mock<IGroupReservationService> Groups) ReservationServiceWithMocks()
    {
        var locationRepo = new Mock<ILocationRepository>();
        var waiterService = new Mock<IWaiterService>();
        var groups = new Mock<IGroupReservationService>();
        var service = new ReservationService(_reservationRepo.Object, locationRepo.Object, new Mock<IUserRepository>().Object,
            waiterService.Object, _locationService.Object, _orderService.Object, groups.Object);
        return (service, locationRepo, waiterService, groups);
    }

    private GetReservationsForUserHandler ReservationsHandler() =>
        new(_reservationRepo.Object, _locationService.Object, _orderService.Object, new Mock<IGroupReservationRepository>().Object, _tableRepo.Object);

    private async Task<JsonDocument> GetOrderJson(Order order)
    {
        _orderService.Setup(s => s.GetOrderByReservationIdAsync(ReservationId)).ReturnsAsync(order);
        var request = Request(CustomerId);
        request.PathParameters = new Dictionary<string, string> { ["reservationId"] = ReservationId };

        var response = await new GetOrderByReservationHandler(DishService().Object, _orderService.Object, _reservationRepo.Object, _tableRepo.Object)
            .HandleRequest(request, Context());

        Assert.Equal(200, response.StatusCode);
        return JsonDocument.Parse(response.Body);
    }

    private static Mock<IDishService> DishService()
    {
        var dishService = new Mock<IDishService>();
        dishService.Setup(s => s.GetDishesByIdsAsync(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync((IEnumerable<string> ids) => new[] { Caesar, Steak, Soup }.Where(d => ids.Contains(d.Id)).ToList());
        return dishService;
    }

    private static Order Order(params OrderItem[] lines) => new()
    {
        OrderId = "order-1", CustomerId = CustomerId, ReservationId = ReservationId, ReservationAddress = "Rustaveli 1, Table 7",
        ReservationTime = "Mar 10, 2027, 1:00 PM - 2:30 PM", Status = OrderStatus.SubmittedByCustomer, Dishes = lines.ToList(),
    };

    private static Order OrderFor(string reservationId, string orderId)
    {
        var order = Order(Line("caesar", 1));
        order.OrderId = orderId;
        order.ReservationId = reservationId;
        return order;
    }

    private static OrderItem Line(string dishId, int quantity, OrderItemState state = OrderItemState.Pending) =>
        new() { DishId = dishId, Quantity = quantity, State = state };

    private static APIGatewayProxyRequest Request(string sub, string? groups = null)
    {
        var claims = new Dictionary<string, string> { ["sub"] = sub };
        if (groups is not null)
            claims["cognito:groups"] = groups;

        return new APIGatewayProxyRequest
        {
            RequestContext = new APIGatewayProxyRequest.ProxyRequestContext
            {
                Authorizer = new APIGatewayCustomAuthorizerContext { Claims = claims },
            },
        };
    }

    private static ILambdaContext Context()
    {
        var context = new Mock<ILambdaContext>();
        context.SetupGet(c => c.Logger).Returns(new Mock<ILambdaLogger>().Object);
        return context.Object;
    }
}
