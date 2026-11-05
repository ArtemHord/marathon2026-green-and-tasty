using Amazon.DynamoDBv2.DataModel;
using Amazon.DynamoDBv2.Model;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using APIHandler.Constants;
using APIHandler.Constants.Dishes;
using APIHandler.Constants.Orders;
using APIHandler.Entities;
using APIHandler.Exceptions;
using APIHandler.Handlers.GroupReservations;
using APIHandler.Handlers.GuestReservations;
using APIHandler.Models.GuestSessions;
using APIHandler.Repositories.Auth;
using APIHandler.Repositories.Dishes;
using APIHandler.Repositories.GroupReservations;
using APIHandler.Repositories.GuestSessions;
using APIHandler.Repositories.Orders;
using APIHandler.Repositories.Reservations;
using APIHandler.Repositories.Tables;
using APIHandler.Services.Auth;
using APIHandler.Services.Dishes;
using APIHandler.Services.GroupReservations;
using APIHandler.Services.GuestSessions;
using APIHandler.Services.Locations;
using APIHandler.Services.Notifications;
using APIHandler.Services.Orders;
using Moq;
using System.Text.Json;

namespace APIHandler.Tests.Services;

/// <summary>
/// Round 2 fixes: optimistic locking on Order.Version (concurrent and sequential paths),
/// ConsistentRead re-read in OrderRepository, owner-session read by id, CanJoin vs. POST /guest/sessions,
/// GET /guest/invites/{token} and GET invite-token in any group state.
/// </summary>
public class GroupPreOrderRound2Tests
{
    private const string OwnerId = "owner-user";
    private const string WaiterId = "waiter-1";
    private const string ReservationId = "reservation-1";
    private const string GroupId = "group-1";

    private readonly Mock<IGroupReservationRepository> _groupRepo = new();
    private readonly Mock<IGuestSessionRepository> _sessionRepo = new();
    private readonly Mock<IReservationRepository> _reservationRepo = new();
    private readonly Mock<ITableRepository> _tableRepo = new();
    private readonly Mock<IUserService> _userService = new();
    private readonly Mock<ILocationService> _locationService = new();
    private readonly Mock<IDishRepository> _dishRepo = new();
    private readonly Mock<IUserRepository> _userRepo = new();
    private readonly Mock<IDishService> _dishService = new();
    private readonly Mock<INotificationService> _notificationService = new();
    private readonly FakeVersionedOrderRepository _orders = new();
    private readonly ILambdaContext _context;

    private readonly Reservation _reservation = new()
    {
        Id = ReservationId, TableId = "table-1", WaiterId = WaiterId, CustomerId = OwnerId,
        Status = ReservationStatus.Reserved, GuestCount = 4,
        StartTime = DateTime.UtcNow.AddDays(3), EndTime = DateTime.UtcNow.AddDays(3).AddMinutes(90),
    };

    private readonly object _groupLock = new();
    private GroupReservation _storedGroup = new() { GroupReservationId = GroupId, ReservationId = ReservationId, ReservationToken = "tok", Status = GroupReservationStatus.Open };

    static GroupPreOrderRound2Tests()
    {
        // repositories read table names from the lambda environment
        Environment.SetEnvironmentVariable("ordersTable", Environment.GetEnvironmentVariable("ordersTable") ?? "Orders-test");
        Environment.SetEnvironmentVariable("guestSessionTable", Environment.GetEnvironmentVariable("guestSessionTable") ?? "GuestSessions-test");
    }

    public GroupPreOrderRound2Tests()
    {
        var context = new Mock<ILambdaContext>();
        context.SetupGet(c => c.Logger).Returns(new Mock<ILambdaLogger>().Object);
        _context = context.Object;

        _reservationRepo.Setup(r => r.GetReservationByIdAsync(ReservationId)).ReturnsAsync(() => _reservation);
        // the group repository behaves like a table: every read is a fresh copy, saves replace the stored item
        _groupRepo.Setup(r => r.GetGroupReservationByIdAsync(GroupId)).ReturnsAsync(() => Clone(_storedGroup));
        _groupRepo.Setup(r => r.GetByReservationIdAsync(ReservationId)).ReturnsAsync(() => Clone(_storedGroup));
        _groupRepo.Setup(r => r.GetGroupReservationByTokenAsync("tok")).ReturnsAsync(() => Clone(_storedGroup));
        // GroupReservation is versioned ([DynamoDBVersion]) since round 2: the fake table applies the same
        // optimistic-locking semantics as FakeVersionedOrderRepository (stored version must match, then +1, null → 0).
        _groupRepo.Setup(r => r.SaveGroupReservationAsync(It.IsAny<GroupReservation>()))
            .Returns((GroupReservation g) =>
            {
                lock (_groupLock)
                {
                    if (_storedGroup.Version != g.Version)
                        throw new ConditionalCheckFailedException("The conditional request failed");
                    g.Version = (g.Version ?? -1) + 1;
                    _storedGroup = Clone(g);
                }
                return Task.CompletedTask;
            });

        _tableRepo.Setup(r => r.GetTableAsync("table-1")).ReturnsAsync(new Table { Id = "table-1", LocationId = "loc-1", TableNumber = 7 });
        _locationService.Setup(s => s.GetLocationByIdAsync("loc-1")).ReturnsAsync(new Location { Id = "loc-1", Address = "Rustaveli Avenue 48", Description = "d", AverageOccupancy = "0" });
        _userService.Setup(s => s.GetUserName(OwnerId)).ReturnsAsync("Yevhenii");
        _dishService.Setup(s => s.GetDishByIdAsync(It.IsAny<string>()))
            .ReturnsAsync((string id) => new Dish { Id = id, Name = id, Price = 100, State = DishState.Available });

        _sessionRepo.Setup(r => r.GetAllByGroupReservationIdAsync(GroupId)).ReturnsAsync(() =>
        [
            new GuestSession { Id = "s1", GroupReservationId = GroupId, GuestName = "Andrii", IsSubmitted = true, Dishes = [new OrderItem { DishId = "dish1", Quantity = 2 }] },
        ]);
    }

    private GroupReservationService GroupService(IOrderRepository? orders = null) => new(
        _groupRepo.Object, _tableRepo.Object, _reservationRepo.Object, _sessionRepo.Object,
        _userService.Object, _locationService.Object, orders ?? _orders);

    private OrderService OrderService() => new(_orders, _reservationRepo.Object, _dishService.Object, _notificationService.Object);

    private GuestSessionService GuestSessionService() => new(
        _groupRepo.Object, _sessionRepo.Object, _dishRepo.Object, _reservationRepo.Object, _userRepo.Object, _tableRepo.Object);

    // =====================================================================
    // Two simultaneous group submits
    // =====================================================================

    /// <summary>Both requests read group + order before either writes → the second write hits the version check → 400.</summary>
    [Fact]
    public async Task TwoSimultaneousSubmits_BothReadBeforeWrite_SecondGets400AndDishesAreAddedOnce()
    {
        _orders.Seed(NewOrder());
        var bothRead = new Barrier(2);
        _orders.AfterRead = () => bothRead.SignalAndWait(TimeSpan.FromSeconds(5));
        var service = GroupService();

        var first = Task.Run(() => service.SubmitGroupPreOrderAsync(OwnerId, GroupId, ReservationId));
        var second = Task.Run(() => service.SubmitGroupPreOrderAsync(OwnerId, GroupId, ReservationId));
        var outcomes = await Task.WhenAll(Outcome(first), Outcome(second));

        Assert.Single(outcomes, o => o is null);
        Assert.Single(outcomes, o => o is GroupReservationSubmittedException { Message: "Group pre-order has already been submitted." });
        Assert.Equal(2, Assert.Single(_orders.Stored.Dishes).Quantity); // not 4
        Assert.Equal(OrderStatus.SubmittedByCustomer, _orders.Stored.Status);
        Assert.Equal(GroupReservationStatus.Submitted, _storedGroup.Status);
    }

    /// <summary>
    /// Double click, realistic interleaving: request B reads the group while it is still Open, then request A
    /// completes (order saved → version 1, group → Submitted), then B reads the order. B's ConsistentRead returns the
    /// order *after* A's write, so B's version matches and its save succeeds → guests' dishes are added twice.
    /// Expected: B gets 400 "already submitted" and the order keeps dish1 × 2.
    /// </summary>
    [Fact]
    public async Task TwoSubmits_SecondReadsGroupBeforeFirstFinishes_DishesMustNotBeAddedTwice()
    {
        _orders.Seed(NewOrder());
        var bReadGroup = new TaskCompletionSource();
        var releaseB = new TaskCompletionSource();
        var sessionReads = 0;
        _sessionRepo.Setup(r => r.GetAllByGroupReservationIdAsync(GroupId)).Returns(async () =>
        {
            if (Interlocked.Increment(ref sessionReads) == 1)
            {
                bReadGroup.SetResult();   // B has already passed the "group is Open" check
                await releaseB.Task;      // ... and waits while A runs to the end
            }
            return [new GuestSession { Id = "s1", GroupReservationId = GroupId, GuestName = "Andrii", IsSubmitted = true, Dishes = [new OrderItem { DishId = "dish1", Quantity = 2 }] }];
        });
        var service = GroupService();

        var requestB = Task.Run(() => service.SubmitGroupPreOrderAsync(OwnerId, GroupId, ReservationId));
        await bReadGroup.Task;
        await service.SubmitGroupPreOrderAsync(OwnerId, GroupId, ReservationId); // request A, completes
        releaseB.SetResult();
        var outcomeB = await Outcome(requestB);

        Assert.IsType<GroupReservationSubmittedException>(outcomeB);
        Assert.Equal(2, Assert.Single(_orders.Stored.Dishes).Quantity);
    }

    /// <summary>
    /// Waiter/Owner changes the order between submit's read and write: HTTP 400 { message: OrderChanged, errorCode: Unknown },
    /// guests' dishes are not written, the claimed group is reopened, and a retry then succeeds (dishes added once).
    /// </summary>
    [Fact]
    public async Task SubmitHandler_OrderChangedConcurrently_Returns400GroupReopened_RetrySucceeds()
    {
        _orders.Seed(NewOrder(("dish1", 1)));
        var interfered = 0;
        _orders.AfterRead = () =>
        {
            if (Interlocked.Exchange(ref interfered, 1) == 1) return;
            _orders.ConcurrentWrite(o => o.Dishes.Single().State = OrderItemState.Confirmed); // concurrent waiter confirm
        };
        var handler = new SubmitGroupPreOrderHandler(GroupService());

        var conflict = await handler.HandleRequest(SubmitRequest(), _context);

        Assert.Equal(400, conflict.StatusCode);
        using (var json = JsonDocument.Parse(conflict.Body))
        {
            Assert.Equal(["errorCode", "message"], json.RootElement.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal));
            Assert.Equal(GroupReservationService.OrderChangedMessage, json.RootElement.GetProperty("message").GetString());
            Assert.Equal("Unknown", json.RootElement.GetProperty("errorCode").GetString());
        }
        Assert.Equal(GroupReservationStatus.Open, _storedGroup.Status);                       // reopened
        Assert.Equal(1, Assert.Single(_orders.Stored.Dishes).Quantity);                        // guests' dishes not written
        Assert.Equal(OrderItemState.Confirmed, _orders.Stored.Dishes.Single().State);         // waiter's write kept

        var retry = await handler.HandleRequest(SubmitRequest(), _context);

        Assert.Equal(200, retry.StatusCode);
        Assert.Equal(3, Assert.Single(_orders.Stored.Dishes).Quantity);                        // 1 own + 2 guests, once
        Assert.Equal(GroupReservationStatus.Submitted, _storedGroup.Status);

        var again = await handler.HandleRequest(SubmitRequest(), _context);
        Assert.Equal(400, again.StatusCode);
        Assert.Contains("GroupReservationSubmitted", again.Body);
        Assert.Equal(3, _orders.Stored.Dishes.Single().Quantity);
    }

    private static APIGatewayProxyRequest SubmitRequest() => new()
    {
        PathParameters = new Dictionary<string, string> { ["groupReservationId"] = GroupId },
        Body = $$"""{ "reservationId": "{{ReservationId}}" }""",
        RequestContext = new APIGatewayProxyRequest.ProxyRequestContext
        {
            Authorizer = new APIGatewayCustomAuthorizerContext { Claims = new Dictionary<string, string> { ["sub"] = OwnerId } },
        },
    };

    // =====================================================================
    // Version on the regular sequential OrderService path
    // =====================================================================

    [Fact]
    public async Task SequentialOrderFlows_WithVersionCheck_NeverConflict()
    {
        var orders = OrderService();
        var order = NewOrder();
        order.Version = null;

        await orders.AddOrderAsync(order);                                                   // v0 (new item)
        await orders.EditPreOrderAsync(OwnerId, ReservationId, [new OrderItem { DishId = "dish1", Quantity = 1 }], _context);
        await orders.SubmitPreOrderAsync(OwnerId, ReservationId, _context);
        await orders.UnSubmitPreOrderAsync(OwnerId, ReservationId, _context);
        await GroupService().SubmitGroupPreOrderAsync(OwnerId, GroupId, ReservationId);    // group submit on the same order
        await orders.ConfirmPreOrderDishAsync(WaiterId, ReservationId, "dish1", _context.Logger);

        Assert.Equal(0, _orders.Conflicts);
        Assert.Equal(5, _orders.Stored.Version);   // 6 writes: 0..5
        Assert.Equal(3, _orders.Stored.Dishes.Single(d => d.DishId == "dish1").Quantity); // 1 own + 2 from the group
        Assert.Equal(OrderItemState.Confirmed, _orders.Stored.Dishes.Single().State);
        Assert.Equal(OrderStatus.Submitted, _orders.Stored.Status);

        await orders.CancelPreOrderAsync(OwnerId, ReservationId, _context);
        Assert.Equal(0, _orders.Conflicts);
        Assert.Null(await _orders.GetOrderByReservationIdAsync(ReservationId)); // cancelled = no active order
    }

    /// <summary>Records written before Version existed load with Version = null; the first save must still succeed.</summary>
    [Fact]
    public async Task LegacyOrderWithoutVersion_SequentialEditAndGroupSubmit_Succeed()
    {
        var legacy = NewOrder();
        legacy.Version = null;
        _orders.Seed(legacy);

        await OrderService().EditPreOrderAsync(OwnerId, ReservationId, [new OrderItem { DishId = "dish9", Quantity = 1 }], _context);
        await GroupService().SubmitGroupPreOrderAsync(OwnerId, GroupId, ReservationId);

        Assert.Equal(0, _orders.Conflicts);
        Assert.Equal(1, _orders.Stored.Version);
    }

    /// <summary>A waiter write that raced with a group submit is rejected (no lost update) — it is not silently applied.</summary>
    [Fact]
    public async Task StaleOrderServiceWrite_IsRejectedByVersionCheck()
    {
        _orders.Seed(NewOrder(("dish1", 1)));
        var stale = await _orders.GetOrderByReservationIdAsync(ReservationId);
        await GroupService().SubmitGroupPreOrderAsync(OwnerId, GroupId, ReservationId);   // bumps the version

        stale!.Status = OrderStatus.Cancelled;
        await Assert.ThrowsAsync<ConditionalCheckFailedException>(() => _orders.SaveOrderAsync(stale));
        Assert.Equal(OrderStatus.SubmittedByCustomer, _orders.Stored.Status);
    }

    // =====================================================================
    // OrderRepository.GetOrderByReservationIdAsync — GSI candidates re-read with ConsistentRead
    // =====================================================================

    [Fact]
    public async Task OrderRepository_NoCandidates_ReturnsNullWithoutLoading()
    {
        var context = DynamoWithCandidates();

        Assert.Null(await new OrderRepository(context.Object).GetOrderByReservationIdAsync(ReservationId));
        context.Verify(c => c.LoadAsync<Order>(It.IsAny<object>(), It.IsAny<object>(), It.IsAny<LoadConfig>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task OrderRepository_IndexSaysActiveButConsistentReadSaysCancelled_ReturnsNull()
    {
        var stale = NewOrder();
        var fresh = NewOrder();
        fresh.Status = OrderStatus.Cancelled;
        var context = DynamoWithCandidates(stale);
        SetupLoad(context, fresh);

        Assert.Null(await new OrderRepository(context.Object).GetOrderByReservationIdAsync(ReservationId));
    }

    [Fact]
    public async Task OrderRepository_ConsistentReadReturnsNull_ReturnsNull()
    {
        var context = DynamoWithCandidates(NewOrder());
        context.Setup(c => c.LoadAsync<Order>(It.IsAny<object>(), It.IsAny<object>(), It.IsAny<LoadConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Order)null!);

        Assert.Null(await new OrderRepository(context.Object).GetOrderByReservationIdAsync(ReservationId));
    }

    [Fact]
    public async Task OrderRepository_ReturnsFreshCopyOfFirstNotCancelledCandidate_LoadedConsistentlyByKey()
    {
        var cancelled = NewOrder();
        cancelled.OrderId = "old-order";
        var activeStale = NewOrder();
        activeStale.OrderId = "new-order";
        activeStale.Version = 1;

        var cancelledFresh = NewOrder();
        cancelledFresh.OrderId = "old-order";
        cancelledFresh.Status = OrderStatus.Cancelled;
        var activeFresh = NewOrder(("dish1", 3));
        activeFresh.OrderId = "new-order";
        activeFresh.Version = 4;

        var context = DynamoWithCandidates(cancelled, activeStale);
        var loadConfigs = new List<LoadConfig>();
        context.Setup(c => c.LoadAsync<Order>(It.IsAny<object>(), It.IsAny<object>(), It.IsAny<LoadConfig>(), It.IsAny<CancellationToken>()))
            .Callback((object _, object _, LoadConfig config, CancellationToken _) => loadConfigs.Add(config))
            .ReturnsAsync((object hash, object range, LoadConfig _, CancellationToken _) =>
                (string)hash == "old-order" ? cancelledFresh : activeFresh);

        var result = await new OrderRepository(context.Object).GetOrderByReservationIdAsync(ReservationId);

        Assert.Same(activeFresh, result);
        Assert.Equal(4, result!.Version);
        Assert.All(loadConfigs, c => Assert.True(c.ConsistentRead));
        Assert.All(loadConfigs, c => Assert.Equal(ResourceNames.OrdersTableName, c.OverrideTableName));
        context.Verify(c => c.LoadAsync<Order>("new-order", OwnerId, It.IsAny<LoadConfig>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task OrderRepository_IndexSaysCancelledButConsistentReadSaysActive_ReturnsActive()
    {
        var staleCancelled = NewOrder();
        staleCancelled.Status = OrderStatus.Cancelled;
        var fresh = NewOrder();
        var context = DynamoWithCandidates(staleCancelled);
        SetupLoad(context, fresh);

        Assert.Same(fresh, await new OrderRepository(context.Object).GetOrderByReservationIdAsync(ReservationId));
    }

    // =====================================================================
    // GuestSessionRepository
    // =====================================================================

    [Fact]
    public async Task GuestSessionRepository_Count_DoesNotCountOwnerSession()
    {
        var context = new Mock<IDynamoDBContext>();
        var search = new Mock<IAsyncSearch<GuestSession>>();
        search.Setup(s => s.GetRemainingAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
        [
            new GuestSession { Id = "owner-" + GroupId, GuestName = "Owner", Dishes = [], IsOwnerSession = true },
            new GuestSession { Id = "g1", GuestName = "Andrii", Dishes = [] },
            new GuestSession { Id = "g2", GuestName = "Olena", Dishes = [] },
        ]);
        context.Setup(c => c.QueryAsync<GuestSession>(GroupId, It.IsAny<QueryConfig>())).Returns(search.Object);

        Assert.Equal(2, await new GuestSessionRepository(context.Object).CountByGroupReservationId(GroupId));
    }

    /// <summary>
    /// The owner-session fallback ("read by its deterministic id") exists because the GSI lags; it only helps if the
    /// read by id is strongly consistent — LoadAsync without ConsistentRead is eventually consistent too.
    /// </summary>
    [Fact]
    public async Task GuestSessionRepository_GetById_UsesConsistentRead()
    {
        var context = new Mock<IDynamoDBContext>();
        LoadConfig? used = null;
        context.Setup(c => c.LoadAsync<GuestSession?>(It.IsAny<object>(), It.IsAny<LoadConfig>(), It.IsAny<CancellationToken>()))
            .Callback((object _, LoadConfig config, CancellationToken _) => used = config)
            .ReturnsAsync((GuestSession?)null);

        await new GuestSessionRepository(context.Object).GetByGuestSessionId("owner-" + GroupId);

        Assert.NotNull(used);
        Assert.True(used!.ConsistentRead);
    }

    // =====================================================================
    // CanJoin == POST /guest/sessions accepts a new guest
    // =====================================================================

    public static IEnumerable<object[]> JoinMatrix()
    {
        foreach (var guestCount in new[] { 1, 2, 3, 4 })
        foreach (var joined in new[] { 0, 1, 2, 3, 4 })
        foreach (var groupStatus in Enum.GetValues<GroupReservationStatus>())
        foreach (var reservationStatus in new[] { ReservationStatus.Reserved, ReservationStatus.InProgress, ReservationStatus.Cancelled })
            yield return [guestCount, joined, groupStatus, reservationStatus];
    }

    [Theory]
    [MemberData(nameof(JoinMatrix))]
    public async Task CanJoin_MatchesWhetherPostGuestSessionsAcceptsANewGuest(
        int guestCount, int joined, GroupReservationStatus groupStatus, ReservationStatus reservationStatus)
    {
        _reservation.GuestCount = guestCount;
        _reservation.Status = reservationStatus;
        _storedGroup.Status = groupStatus;
        _sessionRepo.Setup(r => r.CountByGroupReservationId(GroupId)).ReturnsAsync(joined);

        var invite = await GroupService().GetGroupReservationByTokenAsync("tok");
        var accepted = await Outcome(GuestSessionService().CreateGuestSessionAsync(new CreateGuestSessionRequest(GroupId, "New guest"))) is null;

        Assert.Equal(accepted, invite.CanJoin);
        Assert.Equal(groupStatus, invite.Status);
    }

    [Theory]
    [InlineData(4, 2, true)]  // N−1 joined: one seat left (owner + 2 guests + 1)
    [InlineData(4, 3, false)] // N: full
    [InlineData(4, 4, false)] // N+1: over capacity (e.g. after a race)
    public async Task CanJoin_CapacityBoundary(int guestCount, int joined, bool expected)
    {
        _reservation.GuestCount = guestCount;
        _sessionRepo.Setup(r => r.CountByGroupReservationId(GroupId)).ReturnsAsync(joined);

        Assert.Equal(expected, (await GroupService().GetGroupReservationByTokenAsync("tok")).CanJoin);
    }

    // =====================================================================
    // GET /guest/invites/{token} and GET /reservations/{id}/invite-token over HTTP
    // =====================================================================

    [Theory]
    [InlineData(GroupReservationStatus.Open, 3, "Open", false)]       // full
    [InlineData(GroupReservationStatus.Submitted, 0, "Submitted", false)]
    [InlineData(GroupReservationStatus.Closed, 0, "Closed", false)]
    [InlineData(GroupReservationStatus.Open, 0, "Open", true)]
    public async Task InviteHandler_AnyGroupState_Returns200WithStringStatusAndCanJoin(
        GroupReservationStatus status, int joined, string expectedStatus, bool expectedCanJoin)
    {
        _storedGroup.Status = status;
        _sessionRepo.Setup(r => r.CountByGroupReservationId(GroupId)).ReturnsAsync(joined);

        var response = await new GetGroupReservationHandler(GroupService())
            .HandleRequest(new APIGatewayProxyRequest { PathParameters = new Dictionary<string, string> { ["token"] = "tok" } }, _context);

        Assert.Equal(200, response.StatusCode);
        using var json = JsonDocument.Parse(response.Body);
        Assert.Equal(GroupId, json.RootElement.GetProperty("GroupReservationId").GetString()); // frontend model is PascalCase
        Assert.Equal(expectedStatus, json.RootElement.GetProperty("Status").GetString());
        Assert.Equal(expectedCanJoin, json.RootElement.GetProperty("CanJoin").GetBoolean());
    }

    [Fact]
    public async Task InviteHandler_UnknownToken_Is404()
    {
        _groupRepo.Setup(r => r.GetGroupReservationByTokenAsync("nope")).ReturnsAsync((GroupReservation?)null);

        var response = await new GetGroupReservationHandler(GroupService())
            .HandleRequest(new APIGatewayProxyRequest { PathParameters = new Dictionary<string, string> { ["token"] = "nope" } }, _context);

        Assert.Equal(404, response.StatusCode);
    }

    [Theory]
    [InlineData(GroupReservationStatus.Submitted)]
    [InlineData(GroupReservationStatus.Closed)]
    public async Task InviteTokenHandler_GroupNotOpen_Returns200Token(GroupReservationStatus status)
    {
        _storedGroup.Status = status;

        var response = await new GetInviteTokenHandler(GroupService())
            .HandleRequest(new APIGatewayProxyRequest { PathParameters = new Dictionary<string, string> { ["reservationId"] = ReservationId } }, _context);

        Assert.Equal(200, response.StatusCode);
        Assert.Equal("""{"token":"tok"}""", response.Body);
    }

    [Fact]
    public async Task InviteTokenHandler_NoGroup_Is404WithErrorBody()
    {
        _groupRepo.Setup(r => r.GetByReservationIdAsync(ReservationId)).ReturnsAsync((GroupReservation?)null);

        var response = await new GetInviteTokenHandler(GroupService())
            .HandleRequest(new APIGatewayProxyRequest { PathParameters = new Dictionary<string, string> { ["reservationId"] = ReservationId } }, _context);

        Assert.Equal(404, response.StatusCode);
        using var json = JsonDocument.Parse(response.Body);
        Assert.True(json.RootElement.TryGetProperty("error", out _));
    }

    [Theory]
    [InlineData(1, 400)] // N−1: a single-guest reservation has no group invite
    [InlineData(2, 200)] // N
    public async Task InviteTokenHandler_GuestCountBoundary(int guestCount, int expectedStatus)
    {
        _reservation.GuestCount = guestCount;

        var response = await new GetInviteTokenHandler(GroupService())
            .HandleRequest(new APIGatewayProxyRequest { PathParameters = new Dictionary<string, string> { ["reservationId"] = ReservationId } }, _context);

        Assert.Equal(expectedStatus, response.StatusCode);
    }

    // =====================================================================
    // helpers
    // =====================================================================

    private static async Task<Exception?> Outcome(Task task)
    {
        try { await task; return null; }
        catch (Exception ex) { return ex; }
    }

    private Order NewOrder(params (string DishId, int Quantity)[] dishes) => new()
    {
        OrderId = "order-1", CustomerId = OwnerId, ReservationId = ReservationId,
        ReservationAddress = "address", ReservationTime = "18:00", Status = OrderStatus.Created, Version = 0,
        Dishes = dishes.Select(d => new OrderItem { DishId = d.DishId, Quantity = d.Quantity, State = OrderItemState.Pending }).ToList(),
    };

    private static Mock<IDynamoDBContext> DynamoWithCandidates(params Order[] candidates)
    {
        var context = new Mock<IDynamoDBContext>();
        var search = new Mock<IAsyncSearch<Order>>();
        search.Setup(s => s.GetNextSetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(candidates.ToList());
        context.Setup(c => c.QueryAsync<Order>(ReservationId, It.IsAny<QueryConfig>())).Returns(search.Object);
        return context;
    }

    private static void SetupLoad(Mock<IDynamoDBContext> context, Order fresh) =>
        context.Setup(c => c.LoadAsync<Order>(It.IsAny<object>(), It.IsAny<object>(), It.IsAny<LoadConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(fresh);

    private static GroupReservation Clone(GroupReservation g) => new()
    {
        GroupReservationId = g.GroupReservationId, ReservationId = g.ReservationId, ReservationToken = g.ReservationToken, Status = g.Status,
        Version = g.Version,
    };

    /// <summary>
    /// In-memory Orders table with DynamoDBContext optimistic-locking semantics: a save succeeds only if the stored
    /// version equals the version of the object being saved (null = attribute absent); on success the version is
    /// incremented (null → 0) both in the table and on the saved object. Reads return fresh copies (ConsistentRead).
    /// </summary>
    private sealed class FakeVersionedOrderRepository : IOrderRepository
    {
        private readonly object _lock = new();
        private Order? _stored;

        public Order Stored { get { lock (_lock) return Copy(_stored!); } }
        public int Conflicts { get; private set; }
        public Action? AfterRead { get; set; }

        public void Seed(Order order) { lock (_lock) _stored = Copy(order); }

        /// <summary>Another request's successful write (read latest → change → version + 1).</summary>
        public void ConcurrentWrite(Action<Order> change)
        {
            lock (_lock)
            {
                var copy = Copy(_stored!);
                change(copy);
                copy.Version = (copy.Version ?? -1) + 1;
                _stored = copy;
            }
        }

        public Task<Order?> GetOrderByReservationIdAsync(string reservationId)
        {
            Order? copy;
            lock (_lock)
                copy = _stored is { Status: not OrderStatus.Cancelled } s && s.ReservationId == reservationId ? Copy(s) : null;
            AfterRead?.Invoke();
            return Task.FromResult(copy);
        }

        public Task SaveOrderAsync(Order order)
        {
            lock (_lock)
            {
                var exists = _stored is not null && _stored.OrderId == order.OrderId;
                var storedVersion = exists ? _stored!.Version : null;
                if (exists ? storedVersion != order.Version : order.Version is not null)
                {
                    Conflicts++;
                    throw new ConditionalCheckFailedException("The conditional request failed");
                }

                order.Version = (order.Version ?? -1) + 1;
                _stored = Copy(order);
            }
            return Task.CompletedTask;
        }

        public Task<Order?> GetOrderByIdAsync(string orderId, string customerId)
        {
            lock (_lock) return Task.FromResult(_stored?.OrderId == orderId ? Copy(_stored) : null);
        }

        public Task<IEnumerable<Order>> GetOrdersByCustomerIdAsync(string customerId)
        {
            lock (_lock) return Task.FromResult<IEnumerable<Order>>(_stored is null ? [] : [Copy(_stored)]);
        }

        private static Order Copy(Order o) => new()
        {
            OrderId = o.OrderId, CustomerId = o.CustomerId, ReservationId = o.ReservationId,
            ReservationAddress = o.ReservationAddress, ReservationTime = o.ReservationTime,
            Status = o.Status, Version = o.Version,
            Dishes = o.Dishes.Select(d => new OrderItem { DishId = d.DishId, Quantity = d.Quantity, State = d.State }).ToList(),
        };
    }
}
