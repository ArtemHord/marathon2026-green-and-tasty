using APIHandler.Constants;
using APIHandler.Constants.Orders;
using APIHandler.Entities;
using APIHandler.Models.GuestSessions;
using APIHandler.Repositories.Auth;
using APIHandler.Repositories.Dishes;
using APIHandler.Repositories.GroupReservations;
using APIHandler.Repositories.GuestSessions;
using APIHandler.Repositories.Orders;
using APIHandler.Repositories.Reservations;
using APIHandler.Repositories.Tables;
using APIHandler.Services.Auth;
using APIHandler.Services.GroupReservations;
using APIHandler.Services.GuestSessions;
using APIHandler.Services.Locations;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace APIHandler.Tests.Services;

/// <summary>
/// Owner participant = the reservation order (security stage, change (b)):
/// boundaries of the order lines, consistency of the snapshot with what the restaurant actually gets, DI wiring.
/// </summary>
public class GroupPreOrderOwnerItemsTests
{
    private const string OwnerId = "owner-user";
    private const string ReservationId = "reservation-1";
    private const string GroupId = "group-1";
    private const string OwnerSessionId = "owner-group-1";

    private readonly Mock<IGroupReservationRepository> _groupRepo = new();
    private readonly Mock<IGuestSessionRepository> _sessionRepo = new();
    private readonly Mock<IDishRepository> _dishRepo = new();
    private readonly Mock<IReservationRepository> _reservationRepo = new();
    private readonly Mock<IUserRepository> _userRepo = new();
    private readonly Mock<ITableRepository> _tableRepo = new();
    private readonly Mock<IOrderRepository> _orderRepo = new();

    private readonly Reservation _reservation = new() { Id = ReservationId, TableId = "t-1", WaiterId = "w-1", CustomerId = OwnerId, Status = ReservationStatus.Reserved, GuestCount = 5 };
    private readonly GroupReservation _group = new() { GroupReservationId = GroupId, ReservationId = ReservationId, ReservationToken = "tok", Status = GroupReservationStatus.Open };
    private readonly Order _order = new() { OrderId = "order-1", CustomerId = OwnerId, ReservationId = ReservationId, ReservationAddress = "a", ReservationTime = "t", Status = OrderStatus.Created, Dishes = [] };

    private static readonly Dish Caesar = new() { Id = "dishA", Name = "Caesar Salad", Price = 180 };
    private static readonly Dish TomYum = new() { Id = "dishB", Name = "Tom Yum", Price = 220 };

    public GroupPreOrderOwnerItemsTests()
    {
        _reservationRepo.Setup(r => r.GetReservationByIdAsync(ReservationId)).ReturnsAsync(_reservation);
        _groupRepo.Setup(r => r.GetGroupReservationByIdAsync(GroupId)).ReturnsAsync(_group);
        _groupRepo.Setup(r => r.GetByReservationIdAsync(ReservationId)).ReturnsAsync(_group);
        _orderRepo.Setup(r => r.GetOrderByReservationIdAsync(ReservationId))
            .ReturnsAsync(() => _order.Status == OrderStatus.Cancelled ? null : _order);
        _dishRepo.Setup(r => r.GetDishesByIdsAsync(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync((IEnumerable<string> ids) => new[] { Caesar, TomYum }.Where(d => ids.Contains(d.Id)).ToList());
    }

    private GuestSessionService Snapshots() => new(
        _groupRepo.Object, _sessionRepo.Object, _dishRepo.Object, _reservationRepo.Object, _userRepo.Object, _tableRepo.Object, _orderRepo.Object);

    private GroupReservationService Groups() => new(
        _groupRepo.Object, _tableRepo.Object, _reservationRepo.Object, _sessionRepo.Object,
        new Mock<IUserService>().Object, new Mock<ILocationService>().Object, _orderRepo.Object);

    /// <summary>
    /// What the Owner sees before submit (consolidated = submitted participants) is what the restaurant gets, and the
    /// read-only screen after submit must show the same thing. After submit the order already contains the guests' dishes,
    /// so taking Owner.items = all order lines counts every guest dish twice (Grand Total too high, "My Pre-Order" shows guests' dishes).
    /// </summary>
    [Fact]
    public async Task SnapshotAfterSubmit_ConsolidatedEqualsOrder_GuestDishesNotCountedTwice()
    {
        _order.Dishes = [Line("dishA", 1)];                                      // Owner's own pre-order
        SetupSessions(
            OwnerSession(),
            Guest("andrii", isSubmitted: true, ("dishA", 2)),
            Guest("olena", isSubmitted: true, ("dishB", 1)),
            Guest("ivan", isSubmitted: false, ("dishB", 5)));

        var before = Consolidate(await Snapshots().GetGroupSessionsAsync(OwnerId, GroupId));
        await Groups().SubmitGroupPreOrderAsync(OwnerId, GroupId, ReservationId);
        var order = _order.Dishes.ToDictionary(d => d.DishId, d => d.Quantity);
        var after = Consolidate(await Snapshots().GetGroupSessionsAsync(OwnerId, GroupId));

        Assert.Equal(new Dictionary<string, int> { ["dishA"] = 3, ["dishB"] = 1 }, before);  // preview
        Assert.Equal(before, order);                                                          // restaurant gets the preview
        Assert.Equal(order, after);                                                           // read-only screen = order
    }

    /// <summary>Same invariant expressed as money: Grand Total after submit = order total.</summary>
    [Fact]
    public async Task SnapshotAfterSubmit_OwnerItemsAreOnlyTheOwnersOwnDishes()
    {
        _order.Dishes = [Line("dishA", 1)];
        SetupSessions(OwnerSession(), Guest("andrii", isSubmitted: true, ("dishA", 2)));

        await Groups().SubmitGroupPreOrderAsync(OwnerId, GroupId, ReservationId);
        var owner = (await Snapshots().GetGroupSessionsAsync(OwnerId, GroupId)).Participants.Single(p => p.Type == ParticipantType.Owner);

        Assert.Equal(180, owner.Total); // A × 1, not A × 3
    }

    public static TheoryData<string, OrderItem[]> OwnerHasNothingToSubmit() => new()
    {
        { "order without lines", [] },
        { "all lines Canceled", [Line("dishA", 2, OrderItemState.Canceled), Line("dishB", 1, OrderItemState.Canceled)] },
        { "only zero quantities", [Line("dishA", 0), Line("dishB", 0, OrderItemState.Confirmed)] },
        { "only deleted dishes", [Line("deleted-dish", 3)] },
        { "Canceled + zero + deleted", [Line("dishA", 1, OrderItemState.Canceled), Line("dishB", 0), Line("deleted-dish", 1)] },
    };

    [Theory]
    [MemberData(nameof(OwnerHasNothingToSubmit))]
    public async Task OwnerWithoutBillableLines_ItemsEmptyNotSubmittedZeroTotal(string _, OrderItem[] lines)
    {
        _order.Dishes = lines.ToList();
        SetupSessions(OwnerSession(), Guest("andrii", isSubmitted: true, ("dishB", 1)));

        var snapshot = await Snapshots().GetGroupSessionsAsync(OwnerId, GroupId);
        var owner = snapshot.Participants.Single(p => p.Type == ParticipantType.Owner);

        Assert.Empty(owner.Items);
        Assert.False(owner.IsSubmitted);
        Assert.Equal(0, owner.Total);
        Assert.Equal(220, snapshot.Total);
    }

    [Theory]
    [InlineData(OrderItemState.Pending)]
    [InlineData(OrderItemState.Confirmed)]
    public async Task OwnerLine_PendingOrConfirmed_QuantityOne_IsBillable(OrderItemState state)
    {
        _order.Dishes = [Line("dishA", 1, state), Line("dishB", 1, OrderItemState.Canceled)];
        SetupSessions(OwnerSession());

        var owner = Assert.Single((await Snapshots().GetGroupSessionsAsync(OwnerId, GroupId)).Participants);

        Assert.True(owner.IsSubmitted);
        Assert.Equal("dishA", Assert.Single(owner.Items).Dish.Id);
        Assert.Equal(180, owner.Total);
    }

    [Fact]
    public async Task OwnerOrderCancelled_IsNotShownAsSubmitted_AndSubmitIsRejected()
    {
        _order.Dishes = [Line("dishA", 1)];
        _order.Status = OrderStatus.Cancelled;
        SetupSessions(OwnerSession(), Guest("andrii", isSubmitted: true, ("dishA", 2)));

        var owner = (await Snapshots().GetGroupSessionsAsync(OwnerId, GroupId)).Participants.Single(p => p.Type == ParticipantType.Owner);

        Assert.False(owner.IsSubmitted);
        Assert.Empty(owner.Items);
        await Assert.ThrowsAsync<APIHandler.Exceptions.ValidationException>(() => Groups().SubmitGroupPreOrderAsync(OwnerId, GroupId, ReservationId));
    }

    /// <summary>
    /// GuestSessionService takes IOrderRepository as an optional (= null) constructor parameter. With null the Owner
    /// silently has no items. The container used by RouteRegistry (MS DI) must inject the registered repository.
    /// </summary>
    [Fact]
    public async Task DependencyInjection_InjectsOrderRepositoryIntoGuestSessionService()
    {
        var services = new ServiceCollection();
        services.AddSingleton(_groupRepo.Object);
        services.AddSingleton(_sessionRepo.Object);
        services.AddSingleton(_dishRepo.Object);
        services.AddSingleton(_reservationRepo.Object);
        services.AddSingleton(_userRepo.Object);
        services.AddSingleton(_tableRepo.Object);
        services.AddSingleton(_orderRepo.Object);
        services.AddSingleton<IGuestSessionService, GuestSessionService>(); // same registration as RouteRegistry
        _order.Dishes = [Line("dishA", 2)];
        SetupSessions(OwnerSession());

        var service = services.BuildServiceProvider().GetRequiredService<IGuestSessionService>();
        var owner = Assert.Single((await service.GetGroupSessionsAsync(OwnerId, GroupId)).Participants);

        Assert.Equal(2, Assert.Single(owner.Items).Quantity);
    }

    // ---------- helpers ----------

    private static Dictionary<string, int> Consolidate(GetAllSessionsResponse snapshot) => snapshot.Participants
        .Where(p => p.IsSubmitted)
        .SelectMany(p => p.Items)
        .GroupBy(i => i.Dish.Id)
        .ToDictionary(g => g.Key, g => g.Sum(i => i.Quantity));

    private void SetupSessions(params GuestSession[] sessions) =>
        _sessionRepo.Setup(r => r.GetAllByGroupReservationIdAsync(GroupId)).ReturnsAsync(sessions.ToList());

    private static GuestSession OwnerSession() => new()
    {
        Id = OwnerSessionId, GroupReservationId = GroupId, GuestName = "Yevhenii", Dishes = [], IsOwnerSession = true, OwnerUserId = OwnerId,
    };

    private static GuestSession Guest(string id, bool isSubmitted, params (string DishId, int Quantity)[] dishes) => new()
    {
        Id = id, GroupReservationId = GroupId, GuestName = id, IsSubmitted = isSubmitted,
        Dishes = dishes.Select(d => new OrderItem { DishId = d.DishId, Quantity = d.Quantity }).ToList(),
    };

    private static OrderItem Line(string dishId, int quantity, OrderItemState state = OrderItemState.Pending) =>
        new() { DishId = dishId, Quantity = quantity, State = state };
}
