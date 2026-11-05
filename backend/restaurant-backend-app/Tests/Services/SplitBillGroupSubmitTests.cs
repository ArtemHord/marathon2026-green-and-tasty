using APIHandler.Constants;
using APIHandler.Constants.Orders;
using APIHandler.Entities;
using APIHandler.Helpers;
using APIHandler.Models.Orders;
using APIHandler.Repositories.GroupReservations;
using APIHandler.Repositories.GuestSessions;
using APIHandler.Repositories.Orders;
using APIHandler.Repositories.Reservations;
using APIHandler.Repositories.Tables;
using APIHandler.Services.Auth;
using APIHandler.Services.GroupReservations;
using APIHandler.Services.Locations;
using Moq;
using System.Text.Json;

namespace APIHandler.Tests.Services;

/// <summary>
/// Split bill, data side: the group submit (POST /group-reservations/{id}/submit) records who ordered what in
/// <see cref="OrderItem.Guests"/>. Invariant: Σ Guests[].Quantity == Quantity for every line with Guests.
/// The submit response itself does not change.
/// </summary>
public class SplitBillGroupSubmitTests
{
    private const string OwnerId = "owner-user";
    private const string ReservationId = "reservation-1";
    private const string GroupId = "group-1";
    private static readonly string OwnerShareId = GuestSessionIds.ForOwner(GroupId);

    private readonly Mock<IGroupReservationRepository> _groupRepo = new();
    private readonly Mock<IGuestSessionRepository> _sessionRepo = new();
    private readonly Mock<IReservationRepository> _reservationRepo = new();
    private readonly Mock<IOrderRepository> _orderRepo = new();
    private readonly Mock<IUserService> _userService = new();

    private readonly Reservation _reservation = new() { Id = ReservationId, TableId = "t-1", WaiterId = "w-1", CustomerId = OwnerId, Status = ReservationStatus.Reserved, GuestCount = 6 };
    private readonly GroupReservation _group = new() { GroupReservationId = GroupId, ReservationId = ReservationId, ReservationToken = "tok", Status = GroupReservationStatus.Open };
    private readonly Order _order = new() { OrderId = "order-1", CustomerId = OwnerId, ReservationId = ReservationId, ReservationAddress = "a", ReservationTime = "t", Status = OrderStatus.Created, Dishes = [] };

    private Order? _saved;

    public SplitBillGroupSubmitTests()
    {
        _reservationRepo.Setup(r => r.GetReservationByIdAsync(ReservationId)).ReturnsAsync(_reservation);
        _groupRepo.Setup(r => r.GetGroupReservationByIdAsync(GroupId)).ReturnsAsync(_group);
        _orderRepo.Setup(r => r.GetOrderByReservationIdAsync(ReservationId)).ReturnsAsync(_order);
        _orderRepo.Setup(r => r.SaveOrderAsync(It.IsAny<Order>())).Callback<Order>(o => _saved = o).Returns(Task.CompletedTask);
    }

    private GroupReservationService Service() => new(
        _groupRepo.Object, new Mock<ITableRepository>().Object, _reservationRepo.Object, _sessionRepo.Object,
        _userService.Object, new Mock<ILocationService>().Object, _orderRepo.Object);

    private Task<IReadOnlyList<Models.GroupReservation.SubmittedGroupDish>> Submit() =>
        Service().SubmitGroupPreOrderAsync(OwnerId, GroupId, ReservationId);

    // ---------- one dish from several guests ----------

    [Fact]
    public async Task OneDishFromThreeGuests_LineHasThreeSharesInSessionOrder()
    {
        SetupSessions(
            Guest("s-andrii", "Andrii", true, ("caesar", 2)),
            Guest("s-olena", "Olena", true, ("caesar", 1)),
            Guest("s-ivan", "Ivan", true, ("caesar", 3)));

        await Submit();

        var line = Assert.Single(_saved!.Dishes);
        Assert.Equal(6, line.Quantity);
        Assert.Equal(
            [("s-andrii", "Andrii", 2), ("s-olena", "Olena", 1), ("s-ivan", "Ivan", 3)],
            line.Guests!.Select(g => (g.GuestSessionId, g.GuestName, g.Quantity)).ToList());
    }

    [Fact]
    public async Task SameGuestWithTheDishTwiceInTheCart_OneShareWithTheSum()
    {
        SetupSessions(Guest("s-andrii", "Andrii", true, ("caesar", 2), ("caesar", 1)));

        await Submit();

        var share = Assert.Single(Assert.Single(_saved!.Dishes).Guests!);
        Assert.Equal(("s-andrii", 3), (share.GuestSessionId, share.Quantity));
    }

    [Fact]
    public async Task DifferentDishes_EachLineHasOnlyItsGuests()
    {
        SetupSessions(
            Guest("s-andrii", "Andrii", true, ("caesar", 2)),
            Guest("s-olena", "Olena", true, ("caesar", 1), ("tomyum", 1)),
            Guest("s-ivan", "Ivan", true, ("steak", 1)));

        await Submit();

        var byDish = _saved!.Dishes.ToDictionary(d => d.DishId, d => d.Guests!.Select(g => (g.GuestSessionId, g.Quantity)).ToList());
        Assert.Equal([("s-andrii", 2), ("s-olena", 1)], byDish["caesar"]);
        Assert.Equal([("s-olena", 1)], byDish["tomyum"]);
        Assert.Equal([("s-ivan", 1)], byDish["steak"]);
    }

    // ---------- only submitted guest sessions ----------

    [Fact]
    public async Task UnsubmittedSession_IsNotInGuests()
    {
        SetupSessions(
            Guest("s-andrii", "Andrii", true, ("caesar", 2)),
            Guest("s-ivan", "Ivan", false, ("caesar", 5), ("steak", 1)));

        await Submit();

        var line = Assert.Single(_saved!.Dishes);
        Assert.Equal(2, line.Quantity);
        Assert.DoesNotContain(line.Guests!, g => g.GuestSessionId == "s-ivan");
        Assert.DoesNotContain(_saved.Dishes, d => d.DishId == "steak");
    }

    [Fact]
    public async Task OwnerSessionCart_IsNotAddedAsAShare()
    {
        var ownerSession = Guest(OwnerShareId, "Yevhenii", true, ("caesar", 4));
        ownerSession.IsOwnerSession = true;
        SetupSessions(ownerSession, Guest("s-andrii", "Andrii", true, ("caesar", 1)));

        await Submit();

        var line = Assert.Single(_saved!.Dishes);
        Assert.Equal(1, line.Quantity);
        Assert.Equal("s-andrii", Assert.Single(line.Guests!).GuestSessionId);
    }

    [Fact]
    public async Task ZeroQuantityCartLine_IsNotAShare()
    {
        SetupSessions(Guest("s-andrii", "Andrii", true, ("caesar", 2)), Guest("s-olena", "Olena", true, ("caesar", 0)));

        await Submit();

        Assert.Equal("s-andrii", Assert.Single(Assert.Single(_saved!.Dishes).Guests!).GuestSessionId);
    }

    // ---------- Owner share ----------

    [Fact]
    public async Task OrderAlreadyHadTheDish_OwnerShareWithThePreviousQuantityFirst()
    {
        _order.Dishes = [Line("caesar", 1)];
        var ownerSession = Guest(OwnerShareId, "Yevhenii", false);
        ownerSession.IsOwnerSession = true;
        SetupSessions(ownerSession, Guest("s-andrii", "Andrii", true, ("caesar", 2)), Guest("s-olena", "Olena", true, ("caesar", 1)));

        await Submit();

        var line = Assert.Single(_saved!.Dishes);
        Assert.Equal(4, line.Quantity);
        Assert.Equal(
            [(OwnerShareId, "Yevhenii", 1), ("s-andrii", "Andrii", 2), ("s-olena", "Olena", 1)],
            line.Guests!.Select(g => (g.GuestSessionId, g.GuestName, g.Quantity)).ToList());
    }

    [Fact]
    public async Task OwnerShareName_WithoutOwnerSession_IsTheCognitoName()
    {
        _order.Dishes = [Line("caesar", 1)];
        _userService.Setup(s => s.GetUserName(OwnerId)).ReturnsAsync("Yevhenii Kovalenko");
        SetupSessions(Guest("s-andrii", "Andrii", true, ("caesar", 2)));

        await Submit();

        var owner = Assert.Single(_saved!.Dishes).Guests!.Single(g => g.GuestSessionId == OwnerShareId);
        Assert.Equal("Yevhenii Kovalenko", owner.GuestName);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("  ", false)]
    [InlineData(null, true)]
    public async Task OwnerShareName_NoNameOrLookupFails_IsOwner(string? cognitoName, bool throws)
    {
        _order.Dishes = [Line("caesar", 1)];
        if (throws)
            _userService.Setup(s => s.GetUserName(OwnerId)).ThrowsAsync(new InvalidOperationException("cognito down"));
        else
            _userService.Setup(s => s.GetUserName(OwnerId)).ReturnsAsync(cognitoName);
        SetupSessions(Guest("s-andrii", "Andrii", true, ("caesar", 2)));

        await Submit();

        Assert.Equal("Owner", Assert.Single(_saved!.Dishes).Guests!.Single(g => g.GuestSessionId == OwnerShareId).GuestName);
    }

    [Fact]
    public async Task OwnerOnlyLines_SubmitDidNotTouch_StayWithoutGuests()
    {
        _order.Dishes = [Line("steak", 2, OrderItemState.Confirmed)];
        SetupSessions(Guest("s-andrii", "Andrii", true, ("caesar", 2)));

        await Submit();

        var steak = _saved!.Dishes.Single(d => d.DishId == "steak");
        Assert.Null(steak.Guests);
        Assert.Equal((2, OrderItemState.Confirmed), (steak.Quantity, steak.State));
        _userService.Verify(s => s.GetUserName(It.IsAny<string>()), Times.Never); // no Owner share → no name lookup
    }

    [Fact]
    public async Task OwnerLineWithZeroQuantity_NoOwnerShare()
    {
        _order.Dishes = [Line("caesar", 0), Line("steak", 1)];
        SetupSessions(Guest("s-andrii", "Andrii", true, ("caesar", 2)));

        await Submit();

        var line = _saved!.Dishes.Single(d => d.DishId == "caesar");
        Assert.Equal(2, line.Quantity);
        Assert.Equal("s-andrii", Assert.Single(line.Guests!).GuestSessionId);
    }

    [Fact]
    public async Task CanceledOwnerLine_OwnerShareIsThePreviousQuantity_LineBackToPending()
    {
        _order.Dishes = [Line("caesar", 2, OrderItemState.Canceled), Line("steak", 1)];
        SetupSessions(Guest("s-andrii", "Andrii", true, ("caesar", 1)));

        await Submit();

        var line = _saved!.Dishes.Single(d => d.DishId == "caesar");
        Assert.Equal((3, OrderItemState.Pending), (line.Quantity, line.State));
        Assert.Equal([(OwnerShareId, 2), ("s-andrii", 1)], line.Guests!.Select(g => (g.GuestSessionId, g.Quantity)).ToList());
    }

    [Fact]
    public async Task LineAlreadyHasGuestsCoveringPartOfIt_RemainderBecomesTheOwnerShare()
    {
        var caesar = Line("caesar", 3);
        caesar.Guests = [new OrderItemGuestShare { GuestSessionId = "s-old", GuestName = "Old", Quantity = 1 }];
        _order.Dishes = [caesar];
        SetupSessions(Guest("s-andrii", "Andrii", true, ("caesar", 2)));

        await Submit();

        var line = Assert.Single(_saved!.Dishes);
        Assert.Equal(5, line.Quantity);
        Assert.Equal([("s-old", 1), (OwnerShareId, 2), ("s-andrii", 2)], line.Guests!.Select(g => (g.GuestSessionId, g.Quantity)).ToList());
    }

    // ---------- invariant ----------

    public static TheoryData<string, OrderItem[], GuestSession[]> InvariantCases() => new()
    {
        { "only guests", [], [Guest("a", "A", true, ("d1", 2)), Guest("b", "B", true, ("d1", 1), ("d2", 4))] },
        { "owner + guests on the same dish", [Line("d1", 1)], [Guest("a", "A", true, ("d1", 2)), Guest("b", "B", true, ("d1", 1))] },
        { "owner on several dishes", [Line("d1", 3), Line("d2", 1), Line("d3", 7)], [Guest("a", "A", true, ("d1", 1), ("d2", 1)), Guest("b", "B", true, ("d4", 1))] },
        { "boundary: quantity 1 everywhere", [Line("d1", 1)], [Guest("a", "A", true, ("d1", 1))] },
        { "unsubmitted guest ignored", [Line("d1", 2)], [Guest("a", "A", true, ("d1", 1)), Guest("b", "B", false, ("d1", 9))] },
        { "canceled owner line revived", [Line("d1", 2, OrderItemState.Canceled)], [Guest("a", "A", true, ("d1", 1))] },
        { "large quantities", [Line("d1", 99)], [Guest("a", "A", true, ("d1", 100)), Guest("b", "B", true, ("d1", 1))] },
    };

    [Theory]
    [MemberData(nameof(InvariantCases))]
    public async Task EveryLineWithGuests_SumOfSharesEqualsQuantity(string _, OrderItem[] orderLines, GuestSession[] sessions)
    {
        _order.Dishes = orderLines.Select(l => Line(l.DishId, l.Quantity, l.State)).ToList();
        SetupSessions(sessions);

        await Submit();

        Assert.NotEmpty(_saved!.Dishes.Where(d => d.Guests is not null));
        Assert.All(_saved.Dishes.Where(d => d.Guests is not null), d => Assert.Equal(d.Quantity, d.Guests!.Sum(g => g.Quantity)));
        Assert.All(_saved.Dishes.Where(d => d.Guests is not null), d => Assert.All(d.Guests!, g => Assert.True(g.Quantity > 0)));
    }

    // ---------- the submit response does not change ----------

    [Fact]
    public async Task Response_IsStillTheMergedGuestDishesOnly()
    {
        _order.Dishes = [Line("caesar", 1)];
        SetupSessions(Guest("s-andrii", "Andrii", true, ("caesar", 2)), Guest("s-olena", "Olena", true, ("caesar", 1), ("tomyum", 1)));

        var result = await Submit();

        Assert.Equal([("caesar", 3), ("tomyum", 1)], result.Select(d => (d.DishId, d.Quantity)).ToList());
        var json = JsonSerializer.Serialize(result);
        Assert.DoesNotContain("guest", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SubmittedGuestDishesOfTheGroup_HaveNoGuests()
    {
        SetupSessions(Guest("s-andrii", "Andrii", true, ("caesar", 2)));

        await Submit();

        Assert.All(_group.SubmittedGuestDishes!, d => Assert.Null(d.Guests)); // snapshot subtraction input unchanged
    }

    // ---------- request bodies cannot set Guests ----------

    [Fact]
    public void CreateOrderRequestBody_GuestsAreIgnored()
    {
        var request = JsonSerializer.Deserialize<CreateOrderRequest>(
            """{ "reservationId": "r1", "orderItems": [ { "dishId": "d1", "quantity": 1, "guests": [ { "guestSessionId": "x", "guestName": "X", "quantity": 1 } ] } ] }""");

        Assert.Null(Assert.Single(request!.OrderItems).Guests);
    }

    // ---------- helpers ----------

    private void SetupSessions(params GuestSession[] sessions) =>
        _sessionRepo.Setup(r => r.GetAllByGroupReservationIdAsync(GroupId)).ReturnsAsync(sessions.ToList());

    private static GuestSession Guest(string id, string name, bool isSubmitted, params (string DishId, int Quantity)[] dishes) => new()
    {
        Id = id, GroupReservationId = GroupId, GuestName = name, IsSubmitted = isSubmitted,
        Dishes = dishes.Select(d => new OrderItem { DishId = d.DishId, Quantity = d.Quantity }).ToList(),
    };

    private static OrderItem Line(string dishId, int quantity, OrderItemState state = OrderItemState.Pending) =>
        new() { DishId = dishId, Quantity = quantity, State = state };
}
