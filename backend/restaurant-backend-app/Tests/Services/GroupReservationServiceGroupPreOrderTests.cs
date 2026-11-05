using APIHandler.Constants;
using APIHandler.Constants.Orders;
using APIHandler.Entities;
using APIHandler.Exceptions;
using APIHandler.Helpers;
using APIHandler.Models.GroupReservation;
using APIHandler.Repositories.GroupReservations;
using APIHandler.Repositories.GuestSessions;
using APIHandler.Repositories.Orders;
using APIHandler.Repositories.Reservations;
using APIHandler.Repositories.Tables;
using APIHandler.Services.Auth;
using APIHandler.Services.GroupReservations;
using APIHandler.Services.Locations;
using AutoFixture.Xunit2;
using Moq;

namespace APIHandler.Tests.Services;

/// <summary>
/// GET /reservations/{reservationId}/guest-session-id and
/// POST /group-reservations/{groupReservationId}/submit (L2).
/// </summary>
public class GroupReservationServiceGroupPreOrderTests
{
    private readonly Mock<IGroupReservationRepository> _groupReservationRepositoryMock = new();
    private readonly Mock<ITableRepository> _tableRepositoryMock = new();
    private readonly Mock<IReservationRepository> _reservationRepositoryMock = new();
    private readonly Mock<IGuestSessionRepository> _guestSessionRepositoryMock = new();
    private readonly Mock<IUserService> _userServiceMock = new();
    private readonly Mock<ILocationService> _locationServiceMock = new();
    private readonly Mock<IOrderRepository> _orderRepositoryMock = new();

    private readonly GroupReservationService _service;

    public GroupReservationServiceGroupPreOrderTests()
    {
        _service = new GroupReservationService(
            _groupReservationRepositoryMock.Object,
            _tableRepositoryMock.Object,
            _reservationRepositoryMock.Object,
            _guestSessionRepositoryMock.Object,
            _userServiceMock.Object,
            _locationServiceMock.Object,
            _orderRepositoryMock.Object);
    }

    // ---------- GET guest-session-id ----------

    [Theory]
    [AutoData]
    public async Task GetOwnerGuestSessionIdAsync_OwnerSessionExists_ReturnsItsId(
        Reservation reservation, GroupReservation groupReservation, string userId)
    {
        SetupReservation(reservation, userId);
        SetupGroupByReservation(groupReservation, reservation);
        var owner = Session("owner-session-123", groupReservation.GroupReservationId, isSubmitted: false);
        owner.IsOwnerSession = true;
        var guest = Session("guest-1", groupReservation.GroupReservationId, isSubmitted: true, ("dish1", 1));
        SetupSessions(groupReservation.GroupReservationId, guest, owner);

        OwnerGuestSessionIdResponse result = await _service.GetOwnerGuestSessionIdAsync(userId, reservation.Id);

        Assert.Equal("owner-session-123", result.GuestSessionId);
        _guestSessionRepositoryMock.Verify(r => r.SaveAsync(It.IsAny<GuestSession>()), Times.Never);
    }

    [Theory]
    [AutoData]
    public async Task GetOwnerGuestSessionIdAsync_NoOwnerSessionInOpenGroup_CreatesOwnerSession(
        Reservation reservation, GroupReservation groupReservation, string userId, string ownerName)
    {
        SetupReservation(reservation, userId);
        SetupGroupByReservation(groupReservation, reservation);
        SetupSessions(groupReservation.GroupReservationId, Session("guest-1", groupReservation.GroupReservationId, isSubmitted: false));
        _userServiceMock.Setup(s => s.GetUserName(userId)).ReturnsAsync(ownerName);

        GuestSession? saved = null;
        _guestSessionRepositoryMock
            .Setup(r => r.SaveAsync(It.IsAny<GuestSession>()))
            .Callback<GuestSession>(s => saved = s);

        OwnerGuestSessionIdResponse result = await _service.GetOwnerGuestSessionIdAsync(userId, reservation.Id);

        Assert.NotNull(saved);
        Assert.Equal(GuestSessionIds.ForOwner(groupReservation.GroupReservationId), result.GuestSessionId);
        Assert.Equal(saved.Id, result.GuestSessionId);
        Assert.True(saved.IsOwnerSession);
        Assert.False(saved.IsSubmitted);
        Assert.Empty(saved.Dishes);
        Assert.Equal(userId, saved.OwnerUserId);
        Assert.Equal(groupReservation.GroupReservationId, saved.GroupReservationId);
        Assert.Equal(ownerName, saved.GuestName);
        Assert.Null(saved.SessionTokenHash);
    }

    [Theory]
    [AutoData]
    public async Task GetOwnerGuestSessionIdAsync_RepeatedCallsWithoutStoredSession_ReturnSameId(
        Reservation reservation, GroupReservation groupReservation, string userId)
    {
        SetupReservation(reservation, userId);
        SetupGroupByReservation(groupReservation, reservation);
        SetupSessions(groupReservation.GroupReservationId);

        var first = await _service.GetOwnerGuestSessionIdAsync(userId, reservation.Id);
        var second = await _service.GetOwnerGuestSessionIdAsync(userId, reservation.Id);

        Assert.Equal(first.GuestSessionId, second.GuestSessionId);
    }

    [Theory]
    [InlineAutoData(GroupReservationStatus.Open)]
    [InlineAutoData(GroupReservationStatus.Submitted)]
    [InlineAutoData(GroupReservationStatus.Closed)]
    public async Task GetOwnerGuestSessionIdAsync_OwnerSessionMissingFromIndexButExistsById_ReturnsItWithoutSaving(
        GroupReservationStatus status, Reservation reservation, GroupReservation groupReservation, string userId)
    {
        SetupReservation(reservation, userId);
        SetupGroupByReservation(groupReservation, reservation);
        groupReservation.Status = status;
        SetupSessions(groupReservation.GroupReservationId); // index lag
        var ownerId = GuestSessionIds.ForOwner(groupReservation.GroupReservationId);
        var owner = Session(ownerId, groupReservation.GroupReservationId, isSubmitted: false);
        owner.IsOwnerSession = true;
        _guestSessionRepositoryMock.Setup(r => r.GetByGuestSessionId(ownerId)).ReturnsAsync(owner);

        var result = await _service.GetOwnerGuestSessionIdAsync(userId, reservation.Id);

        Assert.Equal(ownerId, result.GuestSessionId);
        _guestSessionRepositoryMock.Verify(r => r.SaveAsync(It.IsAny<GuestSession>()), Times.Never);
    }

    [Theory]
    [AutoData]
    public async Task GetOwnerGuestSessionIdAsync_UserNameUnknown_UsesDefaultOwnerName(
        Reservation reservation, GroupReservation groupReservation, string userId)
    {
        SetupReservation(reservation, userId);
        SetupGroupByReservation(groupReservation, reservation);
        SetupSessions(groupReservation.GroupReservationId);
        _userServiceMock.Setup(s => s.GetUserName(userId)).ReturnsAsync((string?)null);

        GuestSession? saved = null;
        _guestSessionRepositoryMock
            .Setup(r => r.SaveAsync(It.IsAny<GuestSession>()))
            .Callback<GuestSession>(s => saved = s);

        await _service.GetOwnerGuestSessionIdAsync(userId, reservation.Id);

        Assert.Equal("Owner", saved!.GuestName);
    }

    [Theory]
    [InlineAutoData(GroupReservationStatus.Submitted)]
    [InlineAutoData(GroupReservationStatus.Closed)]
    public async Task GetOwnerGuestSessionIdAsync_NoOwnerSessionAndGroupNotOpen_ThrowsGuestSessionNotFoundException(
        GroupReservationStatus status, Reservation reservation, GroupReservation groupReservation, string userId)
    {
        SetupReservation(reservation, userId);
        SetupGroupByReservation(groupReservation, reservation);
        groupReservation.Status = status;
        SetupSessions(groupReservation.GroupReservationId);

        await Assert.ThrowsAsync<GuestSessionNotFoundException>(() => _service.GetOwnerGuestSessionIdAsync(userId, reservation.Id));
        _guestSessionRepositoryMock.Verify(r => r.SaveAsync(It.IsAny<GuestSession>()), Times.Never);
    }

    [Theory]
    [AutoData]
    public async Task GetOwnerGuestSessionIdAsync_ReservationNotFound_ThrowsReservationNotFoundException(string reservationId, string userId)
    {
        _reservationRepositoryMock
            .Setup(r => r.GetReservationByIdAsync(reservationId))
            .ReturnsAsync((Reservation?)null);

        await Assert.ThrowsAsync<ReservationNotFoundException>(() => _service.GetOwnerGuestSessionIdAsync(userId, reservationId));
    }

    [Theory]
    [AutoData]
    public async Task GetOwnerGuestSessionIdAsync_NotOwner_ThrowsActionForbiddenException(
        Reservation reservation, string ownerId, string otherUserId)
    {
        SetupReservation(reservation, ownerId);

        await Assert.ThrowsAsync<ActionForbiddenException>(() => _service.GetOwnerGuestSessionIdAsync(otherUserId, reservation.Id));
        _groupReservationRepositoryMock.Verify(r => r.GetByReservationIdAsync(It.IsAny<string>()), Times.Never);
    }

    [Theory]
    [AutoData]
    public async Task GetOwnerGuestSessionIdAsync_ReservationWithoutCustomer_ThrowsActionForbiddenException(
        Reservation reservation, string userId)
    {
        reservation.CustomerId = null; // created by a waiter for a walk-in visitor
        _reservationRepositoryMock
            .Setup(r => r.GetReservationByIdAsync(reservation.Id))
            .ReturnsAsync(reservation);

        await Assert.ThrowsAsync<ActionForbiddenException>(() => _service.GetOwnerGuestSessionIdAsync(userId, reservation.Id));
    }

    [Theory]
    [AutoData]
    public async Task GetOwnerGuestSessionIdAsync_GroupNotFound_ThrowsGroupReservationNotFoundException(
        Reservation reservation, string userId)
    {
        SetupReservation(reservation, userId);
        _groupReservationRepositoryMock
            .Setup(r => r.GetByReservationIdAsync(reservation.Id))
            .ReturnsAsync((GroupReservation?)null);

        await Assert.ThrowsAsync<GroupReservationNotFoundException>(() => _service.GetOwnerGuestSessionIdAsync(userId, reservation.Id));
    }

    // ---------- POST submit: happy path ----------

    [Theory]
    [AutoData]
    public async Task SubmitGroupPreOrderAsync_Valid_MergesSubmittedDishesUpdatesOrderAndGroup(
        Reservation reservation, GroupReservation groupReservation, Order order, string userId)
    {
        SetupSubmittable(reservation, groupReservation, order, userId);
        order.Dishes = [];
        SetupSessions(groupReservation.GroupReservationId,
            Session("s1", groupReservation.GroupReservationId, isSubmitted: true, ("dish1", 2), ("dish2", 1)),
            Session("s2", groupReservation.GroupReservationId, isSubmitted: true, ("dish1", 1), ("dish2", 1)));

        Order? savedOrder = null;
        _orderRepositoryMock.Setup(r => r.SaveOrderAsync(It.IsAny<Order>())).Callback<Order>(o => savedOrder = o);
        GroupReservation? savedGroup = null;
        _groupReservationRepositoryMock.Setup(r => r.SaveGroupReservationAsync(It.IsAny<GroupReservation>())).Callback<GroupReservation>(g => savedGroup = g);

        var result = await _service.SubmitGroupPreOrderAsync(userId, groupReservation.GroupReservationId, reservation.Id);

        // response = homework example: dish1 x3, dish2 x2
        Assert.Equal([new SubmittedGroupDish("dish1", 3), new SubmittedGroupDish("dish2", 2)], result);

        Assert.NotNull(savedOrder);
        Assert.Equal(OrderStatus.SubmittedByCustomer, savedOrder.Status);
        Assert.Equal(2, savedOrder.Dishes.Count);
        Assert.Equal(3, savedOrder.Dishes.Single(d => d.DishId == "dish1").Quantity);
        Assert.Equal(2, savedOrder.Dishes.Single(d => d.DishId == "dish2").Quantity);
        Assert.All(savedOrder.Dishes, d => Assert.Equal(OrderItemState.Pending, d.State));

        Assert.NotNull(savedGroup);
        Assert.Equal(GroupReservationStatus.Submitted, savedGroup.Status);
    }

    [Theory]
    [AutoData]
    public async Task SubmitGroupPreOrderAsync_SameDishInTwoSessions_IsMergedIntoOneLine(
        Reservation reservation, GroupReservation groupReservation, Order order, string userId)
    {
        SetupSubmittable(reservation, groupReservation, order, userId);
        order.Dishes = [];
        SetupSessions(groupReservation.GroupReservationId,
            Session("s1", groupReservation.GroupReservationId, isSubmitted: true, ("dish1", 1)),
            Session("s2", groupReservation.GroupReservationId, isSubmitted: true, ("dish1", 4)));

        var result = await _service.SubmitGroupPreOrderAsync(userId, groupReservation.GroupReservationId, reservation.Id);

        var line = Assert.Single(result);
        Assert.Equal("dish1", line.DishId);
        Assert.Equal(5, line.Quantity);
        Assert.Equal(5, Assert.Single(order.Dishes).Quantity);
    }

    [Theory]
    [AutoData]
    public async Task SubmitGroupPreOrderAsync_NotSubmittedSessions_AreNotIncluded(
        Reservation reservation, GroupReservation groupReservation, Order order, string userId)
    {
        SetupSubmittable(reservation, groupReservation, order, userId);
        order.Dishes = [];
        var notSubmittedOwner = Session("owner", groupReservation.GroupReservationId, isSubmitted: false, ("owner-dish", 7));
        notSubmittedOwner.IsOwnerSession = true;
        SetupSessions(groupReservation.GroupReservationId,
            Session("s1", groupReservation.GroupReservationId, isSubmitted: true, ("dish1", 2)),
            Session("s2", groupReservation.GroupReservationId, isSubmitted: false, ("dish1", 10), ("dish3", 1)),
            notSubmittedOwner);

        var result = await _service.SubmitGroupPreOrderAsync(userId, groupReservation.GroupReservationId, reservation.Id);

        var line = Assert.Single(result);
        Assert.Equal(new SubmittedGroupDish("dish1", 2), line);
        Assert.DoesNotContain(order.Dishes, d => d.DishId is "dish3" or "owner-dish");
        Assert.Equal(2, order.Dishes.Single(d => d.DishId == "dish1").Quantity);
    }

    // Changed in the security/demo stage: the Owner's dishes are already in the order, so the owner session is skipped
    // by the merge (it used to follow the "submitted" rule like any session) — nothing is added twice.
    [Theory]
    [AutoData]
    public async Task SubmitGroupPreOrderAsync_OwnerSession_IsSkippedAndOwnerDishesAreNotDuplicated(
        Reservation reservation, GroupReservation groupReservation, Order order, string userId)
    {
        SetupSubmittable(reservation, groupReservation, order, userId);
        order.Dishes = [new OrderItem { DishId = "dish9", Quantity = 1, State = OrderItemState.Pending }]; // owner's own pre-order
        var owner = Session("owner", groupReservation.GroupReservationId, isSubmitted: true, ("dish9", 1));
        owner.IsOwnerSession = true;
        SetupSessions(groupReservation.GroupReservationId, owner,
            Session("s1", groupReservation.GroupReservationId, isSubmitted: true, ("dish1", 2)));

        var result = await _service.SubmitGroupPreOrderAsync(userId, groupReservation.GroupReservationId, reservation.Id);

        Assert.Equal(new SubmittedGroupDish("dish1", 2), Assert.Single(result)); // response = guests' dishes only
        Assert.Equal(1, order.Dishes.Single(d => d.DishId == "dish9").Quantity); // not 2
        Assert.Equal(2, order.Dishes.Single(d => d.DishId == "dish1").Quantity);
    }

    // Changed with B2 (was ..._OnlyOwnerSessionSubmitted_ThrowsNoSubmittedSessions → 400): the Owner with dishes in the order
    // counts as a submitted participant, so a group where only the Owner ordered can be submitted; the response is [].
    [Theory]
    [AutoData]
    public async Task SubmitGroupPreOrderAsync_OnlyOwnerHasDishes_SubmitsWithEmptyResponse(
        Reservation reservation, GroupReservation groupReservation, Order order, string userId)
    {
        SetupSubmittable(reservation, groupReservation, order, userId);
        order.Dishes = [new OrderItem { DishId = "dish9", Quantity = 1, State = OrderItemState.Pending }];
        var owner = Session("owner", groupReservation.GroupReservationId, isSubmitted: true, ("dish9", 1));
        owner.IsOwnerSession = true;
        SetupSessions(groupReservation.GroupReservationId, owner,
            Session("s1", groupReservation.GroupReservationId, isSubmitted: false, ("dish1", 3)));
        GroupReservation? savedGroup = null;
        _groupReservationRepositoryMock.Setup(r => r.SaveGroupReservationAsync(It.IsAny<GroupReservation>())).Callback<GroupReservation>(g => savedGroup = g);

        var result = await _service.SubmitGroupPreOrderAsync(userId, groupReservation.GroupReservationId, reservation.Id);

        Assert.Empty(result);
        Assert.Equal(OrderStatus.SubmittedByCustomer, order.Status);
        Assert.Equal(1, Assert.Single(order.Dishes).Quantity); // nothing added
        Assert.Equal(GroupReservationStatus.Submitted, savedGroup!.Status);
        Assert.Empty(savedGroup.SubmittedGuestDishes!);
        _orderRepositoryMock.Verify(r => r.SaveOrderAsync(order), Times.Once);
    }

    [Theory]
    [AutoData]
    public async Task SubmitGroupPreOrderAsync_SubmittedGuestsWithoutDishesButOwnerHasDishes_Submits(
        Reservation reservation, GroupReservation groupReservation, Order order, string userId)
    {
        SetupSubmittable(reservation, groupReservation, order, userId);
        order.Dishes = [new OrderItem { DishId = "dish9", Quantity = 2, State = OrderItemState.Confirmed }];
        SetupSessions(groupReservation.GroupReservationId, Session("s1", groupReservation.GroupReservationId, isSubmitted: true));

        var result = await _service.SubmitGroupPreOrderAsync(userId, groupReservation.GroupReservationId, reservation.Id);

        Assert.Empty(result);
        Assert.Equal(OrderStatus.SubmittedByCustomer, order.Status);
    }

    [Theory]
    [InlineAutoData(0, OrderItemState.Pending)]
    [InlineAutoData(3, OrderItemState.Canceled)]
    public async Task SubmitGroupPreOrderAsync_OwnerOrderWithoutBillableLinesAndNoSubmittedGuests_ThrowsNoSubmittedSessions(
        int quantity, OrderItemState state, Reservation reservation, GroupReservation groupReservation, Order order, string userId)
    {
        SetupSubmittable(reservation, groupReservation, order, userId);
        order.Dishes = [new OrderItem { DishId = "dish9", Quantity = quantity, State = state }];
        var owner = Session("owner", groupReservation.GroupReservationId, isSubmitted: true, ("dish9", 1)); // owner cart is ignored
        owner.IsOwnerSession = true;
        SetupSessions(groupReservation.GroupReservationId, owner);

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            _service.SubmitGroupPreOrderAsync(userId, groupReservation.GroupReservationId, reservation.Id));
        Assert.Equal("There are no submitted guest sessions in this group.", ex.Message);
        VerifyNothingSaved();
    }

    [Theory]
    [AutoData]
    public async Task SubmitGroupPreOrderAsync_SubmittedGuestsWithoutDishesAndOwnerWithoutDishes_ThrowsNoDishes(
        Reservation reservation, GroupReservation groupReservation, Order order, string userId)
    {
        SetupSubmittable(reservation, groupReservation, order, userId);
        order.Dishes = [];
        SetupSessions(groupReservation.GroupReservationId, Session("s1", groupReservation.GroupReservationId, isSubmitted: true));

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            _service.SubmitGroupPreOrderAsync(userId, groupReservation.GroupReservationId, reservation.Id));
        Assert.Equal("Submitted guest sessions do not contain any dishes.", ex.Message);
        VerifyNothingSaved();
    }

    // ---------- B1: SubmittedGuestDishes on the group ----------

    [Theory]
    [AutoData]
    public async Task SubmitGroupPreOrderAsync_ClaimStoresMergedGuestDishesInTheSameWrite(
        Reservation reservation, GroupReservation groupReservation, Order order, string userId)
    {
        SetupSubmittable(reservation, groupReservation, order, userId);
        order.Dishes = [];
        SetupSessions(groupReservation.GroupReservationId,
            Session("s1", groupReservation.GroupReservationId, isSubmitted: true, ("dish1", 2)),
            Session("s2", groupReservation.GroupReservationId, isSubmitted: true, ("dish1", 1), ("dish2", 1)));
        var writes = new List<(GroupReservationStatus Status, List<(string, int)>? Dishes)>();
        _groupReservationRepositoryMock.Setup(r => r.SaveGroupReservationAsync(It.IsAny<GroupReservation>()))
            .Callback<GroupReservation>(g => writes.Add((g.Status, g.SubmittedGuestDishes?.Select(d => (d.DishId, d.Quantity)).ToList())));

        await _service.SubmitGroupPreOrderAsync(userId, groupReservation.GroupReservationId, reservation.Id);

        var claim = Assert.Single(writes);
        Assert.Equal(GroupReservationStatus.Submitted, claim.Status);
        Assert.Equal([("dish1", 3), ("dish2", 1)], claim.Dishes);
    }

    [Theory]
    [AutoData]
    public async Task SubmitGroupPreOrderAsync_Compensation_ClearsSubmittedGuestDishes(
        Reservation reservation, GroupReservation groupReservation, Order order, string userId)
    {
        SetupSubmittable(reservation, groupReservation, order, userId);
        order.Dishes = [];
        SetupSessions(groupReservation.GroupReservationId, Session("s1", groupReservation.GroupReservationId, isSubmitted: true, ("dish1", 2)));
        var writes = new List<(GroupReservationStatus Status, int? DishCount)>();
        _groupReservationRepositoryMock.Setup(r => r.SaveGroupReservationAsync(It.IsAny<GroupReservation>()))
            .Callback<GroupReservation>(g => writes.Add((g.Status, g.SubmittedGuestDishes?.Count)));
        _orderRepositoryMock.Setup(r => r.SaveOrderAsync(It.IsAny<Order>())).ThrowsAsync(new TimeoutException("dynamo down"));

        await Assert.ThrowsAsync<TimeoutException>(() => _service.SubmitGroupPreOrderAsync(userId, groupReservation.GroupReservationId, reservation.Id));

        Assert.Equal([(GroupReservationStatus.Submitted, (int?)1), (GroupReservationStatus.Open, (int?)null)], writes);
        Assert.Null(groupReservation.SubmittedGuestDishes);
    }

    [Fact]
    public void GroupReservation_SubmittedGuestDishes_IsNotSerialized()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new GroupReservation
        {
            GroupReservationId = "g1", ReservationId = "r1", ReservationToken = "t",
            SubmittedGuestDishes = [new OrderItem { DishId = "d", Quantity = 1 }],
        });

        Assert.DoesNotContain("ubmittedGuestDishes", json);
    }

    // ---------- GET invite-token: owner only ----------

    [Theory]
    [AutoData]
    public async Task GetOwnerInviteTokenAsync_Owner_ReturnsToken(Reservation reservation, GroupReservation groupReservation, string userId)
    {
        SetupReservation(reservation, userId);
        reservation.GuestCount = 3;
        SetupGroupByReservation(groupReservation, reservation);

        var result = await _service.GetOwnerInviteTokenAsync(userId, reservation.Id);

        Assert.Equal(groupReservation.ReservationToken, result.Token);
    }

    [Theory]
    [AutoData]
    public async Task GetOwnerInviteTokenAsync_AnotherCustomer_ThrowsActionForbidden(Reservation reservation, GroupReservation groupReservation, string ownerId, string otherId)
    {
        SetupReservation(reservation, ownerId);
        reservation.GuestCount = 3;
        SetupGroupByReservation(groupReservation, reservation);

        await Assert.ThrowsAsync<ActionForbiddenException>(() => _service.GetOwnerInviteTokenAsync(otherId, reservation.Id));
        _groupReservationRepositoryMock.Verify(r => r.GetByReservationIdAsync(It.IsAny<string>()), Times.Never);
    }

    [Theory]
    [AutoData]
    public async Task GetOwnerInviteTokenAsync_ReservationWithoutCustomer_ThrowsActionForbidden(Reservation reservation, string userId)
    {
        reservation.CustomerId = null;
        _reservationRepositoryMock.Setup(r => r.GetReservationByIdAsync(reservation.Id)).ReturnsAsync(reservation);

        await Assert.ThrowsAsync<ActionForbiddenException>(() => _service.GetOwnerInviteTokenAsync(userId, reservation.Id));
    }

    [Theory]
    [AutoData]
    public async Task GetOwnerInviteTokenAsync_ReservationNotFound_ThrowsReservationNotFound(string reservationId, string userId)
    {
        _reservationRepositoryMock.Setup(r => r.GetReservationByIdAsync(reservationId)).ReturnsAsync((Reservation?)null);

        await Assert.ThrowsAsync<ReservationNotFoundException>(() => _service.GetOwnerInviteTokenAsync(userId, reservationId));
    }

    [Theory]
    [AutoData]
    public async Task SubmitGroupPreOrderAsync_DishAlreadyInOrder_IncreasesQuantity(
        Reservation reservation, GroupReservation groupReservation, Order order, string userId)
    {
        SetupSubmittable(reservation, groupReservation, order, userId);
        order.Dishes =
        [
            new OrderItem { DishId = "dish1", Quantity = 2, State = OrderItemState.Confirmed },
            new OrderItem { DishId = "owner-only", Quantity = 1, State = OrderItemState.Pending },
        ];
        SetupSessions(groupReservation.GroupReservationId,
            Session("s1", groupReservation.GroupReservationId, isSubmitted: true, ("dish1", 3), ("dish2", 1)));

        var result = await _service.SubmitGroupPreOrderAsync(userId, groupReservation.GroupReservationId, reservation.Id);

        // the response contains only the merged guest dishes, not the whole order
        Assert.Equal([new SubmittedGroupDish("dish1", 3), new SubmittedGroupDish("dish2", 1)], result);

        Assert.Equal(3, order.Dishes.Count);
        var dish1 = order.Dishes.Single(d => d.DishId == "dish1");
        Assert.Equal(5, dish1.Quantity);
        Assert.Equal(OrderItemState.Pending, dish1.State); // changed quantity needs a new confirmation
        Assert.Equal(1, order.Dishes.Single(d => d.DishId == "owner-only").Quantity);
        Assert.Equal(1, order.Dishes.Single(d => d.DishId == "dish2").Quantity);
        _orderRepositoryMock.Verify(r => r.SaveOrderAsync(order), Times.Once);
    }

    [Theory]
    [AutoData]
    public async Task SubmitGroupPreOrderAsync_SingleSubmittedSessionWithSingleDish_Succeeds(
        Reservation reservation, GroupReservation groupReservation, Order order, string userId)
    {
        SetupSubmittable(reservation, groupReservation, order, userId);
        order.Dishes = [];
        SetupSessions(groupReservation.GroupReservationId,
            Session("s1", groupReservation.GroupReservationId, isSubmitted: true, ("dish1", 1)));

        var result = await _service.SubmitGroupPreOrderAsync(userId, groupReservation.GroupReservationId, reservation.Id);

        Assert.Equal(new SubmittedGroupDish("dish1", 1), Assert.Single(result));
    }

    // ---------- POST submit: errors ----------

    [Theory]
    [AutoData]
    public async Task SubmitGroupPreOrderAsync_ReservationNotFound_ThrowsReservationNotFoundException(
        string groupReservationId, string reservationId, string userId)
    {
        _reservationRepositoryMock
            .Setup(r => r.GetReservationByIdAsync(reservationId))
            .ReturnsAsync((Reservation?)null);

        await Assert.ThrowsAsync<ReservationNotFoundException>(() => _service.SubmitGroupPreOrderAsync(userId, groupReservationId, reservationId));
        VerifyNothingSaved();
    }

    [Theory]
    [AutoData]
    public async Task SubmitGroupPreOrderAsync_GroupNotFound_ThrowsGroupReservationNotFoundException(
        Reservation reservation, string groupReservationId, string userId)
    {
        SetupReservation(reservation, userId);
        _groupReservationRepositoryMock
            .Setup(r => r.GetGroupReservationByIdAsync(groupReservationId))
            .ReturnsAsync((GroupReservation?)null);

        await Assert.ThrowsAsync<GroupReservationNotFoundException>(() => _service.SubmitGroupPreOrderAsync(userId, groupReservationId, reservation.Id));
        VerifyNothingSaved();
    }

    [Theory]
    [AutoData]
    public async Task SubmitGroupPreOrderAsync_NotOwner_ThrowsActionForbiddenException(
        Reservation reservation, GroupReservation groupReservation, Order order, string ownerId, string otherUserId)
    {
        SetupSubmittable(reservation, groupReservation, order, ownerId);

        await Assert.ThrowsAsync<ActionForbiddenException>(() =>
            _service.SubmitGroupPreOrderAsync(otherUserId, groupReservation.GroupReservationId, reservation.Id));
        VerifyNothingSaved();
    }

    [Theory]
    [AutoData]
    public async Task SubmitGroupPreOrderAsync_NotOwnerOfForeignGroup_ThrowsActionForbiddenBeforeMismatchCheck(
        Reservation reservation, GroupReservation groupReservation, string ownerId, string otherUserId, string otherReservationId)
    {
        SetupReservation(reservation, ownerId);
        groupReservation.ReservationId = otherReservationId;
        groupReservation.Status = GroupReservationStatus.Open;
        SetupGroupById(groupReservation);

        await Assert.ThrowsAsync<ActionForbiddenException>(() =>
            _service.SubmitGroupPreOrderAsync(otherUserId, groupReservation.GroupReservationId, reservation.Id));
    }

    [Theory]
    [AutoData]
    public async Task SubmitGroupPreOrderAsync_GroupBelongsToAnotherReservation_ThrowsValidationException(
        Reservation reservation, GroupReservation groupReservation, string userId, string otherReservationId)
    {
        SetupReservation(reservation, userId);
        groupReservation.ReservationId = otherReservationId;
        groupReservation.Status = GroupReservationStatus.Open;
        SetupGroupById(groupReservation);

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            _service.SubmitGroupPreOrderAsync(userId, groupReservation.GroupReservationId, reservation.Id));
        Assert.Equal("Group reservation does not belong to this reservation.", ex.Message);
        VerifyNothingSaved();
    }

    [Theory]
    [AutoData]
    public async Task SubmitGroupPreOrderAsync_AlreadySubmitted_ThrowsGroupReservationSubmittedExceptionWithContractMessage(
        Reservation reservation, GroupReservation groupReservation, Order order, string userId)
    {
        SetupSubmittable(reservation, groupReservation, order, userId);
        groupReservation.Status = GroupReservationStatus.Submitted;

        var ex = await Assert.ThrowsAsync<GroupReservationSubmittedException>(() =>
            _service.SubmitGroupPreOrderAsync(userId, groupReservation.GroupReservationId, reservation.Id));
        Assert.Equal("Group pre-order has already been submitted.", ex.Message);
        VerifyNothingSaved();
    }

    [Theory]
    [AutoData]
    public async Task SubmitGroupPreOrderAsync_GroupClosed_ThrowsGroupReservationExpiredException(
        Reservation reservation, GroupReservation groupReservation, Order order, string userId)
    {
        SetupSubmittable(reservation, groupReservation, order, userId);
        groupReservation.Status = GroupReservationStatus.Closed;

        var ex = await Assert.ThrowsAsync<GroupReservationExpiredException>(() =>
            _service.SubmitGroupPreOrderAsync(userId, groupReservation.GroupReservationId, reservation.Id));
        Assert.Equal("Group reservation is closed.", ex.Message);
        VerifyNothingSaved();
    }

    [Theory]
    [AutoData]
    public async Task SubmitGroupPreOrderAsync_NoSessions_ThrowsValidationException(
        Reservation reservation, GroupReservation groupReservation, Order order, string userId)
    {
        SetupSubmittable(reservation, groupReservation, order, userId);
        order.Dishes = []; // B2: the Owner without dishes in the order is not a submitted participant
        SetupSessions(groupReservation.GroupReservationId);

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            _service.SubmitGroupPreOrderAsync(userId, groupReservation.GroupReservationId, reservation.Id));
        Assert.Equal("There are no submitted guest sessions in this group.", ex.Message);
        VerifyNothingSaved();
    }

    [Theory]
    [AutoData]
    public async Task SubmitGroupPreOrderAsync_NoSubmittedSessions_ThrowsValidationException(
        Reservation reservation, GroupReservation groupReservation, Order order, string userId)
    {
        SetupSubmittable(reservation, groupReservation, order, userId);
        order.Dishes = []; // B2: the Owner without dishes in the order is not a submitted participant
        SetupSessions(groupReservation.GroupReservationId,
            Session("s1", groupReservation.GroupReservationId, isSubmitted: false, ("dish1", 2)),
            Session("s2", groupReservation.GroupReservationId, isSubmitted: false, ("dish2", 1)));

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            _service.SubmitGroupPreOrderAsync(userId, groupReservation.GroupReservationId, reservation.Id));
        Assert.Equal("There are no submitted guest sessions in this group.", ex.Message);
        VerifyNothingSaved();
    }

    [Theory]
    [AutoData]
    public async Task SubmitGroupPreOrderAsync_SubmittedSessionsWithoutDishes_ThrowsValidationException(
        Reservation reservation, GroupReservation groupReservation, Order order, string userId)
    {
        SetupSubmittable(reservation, groupReservation, order, userId);
        order.Dishes = []; // B2: the Owner without dishes in the order is not a submitted participant
        var legacy = Session("s3", groupReservation.GroupReservationId, isSubmitted: true);
        legacy.Dishes = null!;
        SetupSessions(groupReservation.GroupReservationId,
            Session("s1", groupReservation.GroupReservationId, isSubmitted: true),
            Session("s2", groupReservation.GroupReservationId, isSubmitted: true, ("dish1", 0)),
            legacy,
            Session("s4", groupReservation.GroupReservationId, isSubmitted: false, ("dish2", 3)));

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            _service.SubmitGroupPreOrderAsync(userId, groupReservation.GroupReservationId, reservation.Id));
        Assert.Equal("Submitted guest sessions do not contain any dishes.", ex.Message);
        VerifyNothingSaved();
    }

    [Theory]
    [AutoData]
    public async Task SubmitGroupPreOrderAsync_OrderNotFound_ThrowsValidationException(
        Reservation reservation, GroupReservation groupReservation, Order order, string userId)
    {
        SetupSubmittable(reservation, groupReservation, order, userId);
        _orderRepositoryMock
            .Setup(r => r.GetOrderByReservationIdAsync(reservation.Id))
            .ReturnsAsync((Order?)null);
        SetupSessions(groupReservation.GroupReservationId,
            Session("s1", groupReservation.GroupReservationId, isSubmitted: true, ("dish1", 1)));

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            _service.SubmitGroupPreOrderAsync(userId, groupReservation.GroupReservationId, reservation.Id));
        Assert.Equal($"Pre-order for reservation {reservation.Id} was not found.", ex.Message);
        VerifyNothingSaved();
    }

    [Theory]
    [AutoData]
    public async Task SubmitGroupPreOrderAsync_OrderCancelled_ThrowsValidationException(
        Reservation reservation, GroupReservation groupReservation, Order order, string userId)
    {
        SetupSubmittable(reservation, groupReservation, order, userId);
        order.Status = OrderStatus.Cancelled;
        SetupSessions(groupReservation.GroupReservationId,
            Session("s1", groupReservation.GroupReservationId, isSubmitted: true, ("dish1", 1)));

        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.SubmitGroupPreOrderAsync(userId, groupReservation.GroupReservationId, reservation.Id));
        VerifyNothingSaved();
    }

    // ---------- concurrent submits (optimistic locking on Order.Version) ----------

    [Theory]
    [AutoData]
    public async Task SubmitGroupPreOrderAsync_GroupClaimConflict_ThrowsAlreadySubmittedAndDoesNotWriteOrder(
        Reservation reservation, GroupReservation groupReservation, Order order, string userId)
    {
        SetupSubmittable(reservation, groupReservation, order, userId);
        order.Dishes = [];
        SetupSessions(groupReservation.GroupReservationId,
            Session("s1", groupReservation.GroupReservationId, isSubmitted: true, ("dish1", 1)));
        _groupReservationRepositoryMock
            .Setup(r => r.SaveGroupReservationAsync(It.IsAny<GroupReservation>()))
            .ThrowsAsync(new Amazon.DynamoDBv2.Model.ConditionalCheckFailedException("The conditional request failed"));

        var ex = await Assert.ThrowsAsync<GroupReservationSubmittedException>(() =>
            _service.SubmitGroupPreOrderAsync(userId, groupReservation.GroupReservationId, reservation.Id));

        Assert.Equal("Group pre-order has already been submitted.", ex.Message);
        _groupReservationRepositoryMock.Verify(r => r.SaveGroupReservationAsync(It.IsAny<GroupReservation>()), Times.Once);
        _orderRepositoryMock.Verify(r => r.SaveOrderAsync(It.IsAny<Order>()), Times.Never);
    }

    // Reviewer round 2: an order version conflict after a successful claim is a DIFFERENT write to the order (owner edit,
    // waiter confirm) — the group is reopened (version-checked) and the owner is asked to retry.
    [Fact]
    public async Task SubmitGroupPreOrderAsync_OrderVersionConflictAfterClaim_ReopensGroupAndAsksToRetry()
    {
        var (storedGroup, groupWrites) = SetupVersionedGroupTable(GroupReservationStatus.Open, version: 4);
        SetupOrderConflict();

        var ex = await Assert.ThrowsAsync<ValidationException>(() => _service.SubmitGroupPreOrderAsync("u1", "g1", "r1"));

        Assert.Equal("The pre-order was changed while submitting. Please try again.", ex.Message);
        Assert.Equal([GroupReservationStatus.Submitted, GroupReservationStatus.Open], groupWrites);
        Assert.Equal(GroupReservationStatus.Open, storedGroup().Status);
        Assert.Equal(6, storedGroup().Version); // claim 4 → 5, reopen 5 → 6: the reopen used the version bumped by the claim
    }

    [Fact]
    public async Task SubmitGroupPreOrderAsync_OrderVersionConflictAndGroupChangedAfterClaim_ReopenDoesNotOverwrite()
    {
        var (storedGroup, groupWrites) = SetupVersionedGroupTable(GroupReservationStatus.Open, version: 0);
        // the order write conflicts, and meanwhile someone else closes the group (version 1 → 2)
        _orderRepositoryMock.Setup(r => r.GetOrderByReservationIdAsync("r1"))
            .ReturnsAsync(() => new Order { OrderId = "o1", CustomerId = "u1", ReservationId = "r1", ReservationAddress = "a", ReservationTime = "t", Dishes = [], Version = 3 });
        _orderRepositoryMock.Setup(r => r.SaveOrderAsync(It.IsAny<Order>())).Returns(() =>
        {
            var g = storedGroup();
            g.Status = GroupReservationStatus.Closed;
            return _groupReservationRepositoryMock.Object.SaveGroupReservationAsync(g)
                .ContinueWith(_ => throw new Amazon.DynamoDBv2.Model.ConditionalCheckFailedException("conflict"));
        });

        var ex = await Assert.ThrowsAsync<ValidationException>(() => _service.SubmitGroupPreOrderAsync("u1", "g1", "r1"));

        Assert.Equal("The pre-order was changed while submitting. Please try again.", ex.Message);
        Assert.Equal([GroupReservationStatus.Submitted, GroupReservationStatus.Closed], groupWrites); // reopen rejected
        Assert.Equal(GroupReservationStatus.Closed, storedGroup().Status);
        Assert.Equal(2, storedGroup().Version);
    }

    /// <summary>Group "table" with DynamoDB optimistic-locking semantics; returns accessors to the stored item and the successful writes.</summary>
    private (Func<GroupReservation> Stored, List<GroupReservationStatus> Writes) SetupVersionedGroupTable(GroupReservationStatus status, int? version)
    {
        var reservation = new Reservation { Id = "r1", TableId = "t1", WaiterId = "w1", CustomerId = "u1", Status = ReservationStatus.Reserved, GuestCount = 4 };
        _reservationRepositoryMock.Setup(r => r.GetReservationByIdAsync("r1")).ReturnsAsync(reservation);
        SetupSessions("g1", Session("s1", "g1", isSubmitted: true, ("dish1", 2)));

        var stored = new GroupReservation { GroupReservationId = "g1", ReservationId = "r1", ReservationToken = "tok", Status = status, Version = version };
        GroupReservation Copy() => new() { GroupReservationId = "g1", ReservationId = "r1", ReservationToken = "tok", Status = stored.Status, Version = stored.Version };
        var writes = new List<GroupReservationStatus>();
        _groupReservationRepositoryMock.Setup(r => r.GetGroupReservationByIdAsync("g1")).ReturnsAsync(() => Copy());
        _groupReservationRepositoryMock.Setup(r => r.SaveGroupReservationAsync(It.IsAny<GroupReservation>())).Returns((GroupReservation g) =>
        {
            if (g.Version != stored.Version) throw new Amazon.DynamoDBv2.Model.ConditionalCheckFailedException("conflict");
            g.Version = (g.Version ?? -1) + 1; // the SDK bumps the version on the saved object
            stored = new GroupReservation { GroupReservationId = "g1", ReservationId = "r1", ReservationToken = "tok", Status = g.Status, Version = g.Version };
            writes.Add(g.Status);
            return Task.CompletedTask;
        });
        return (Copy, writes);
    }

    private void SetupOrderConflict()
    {
        _orderRepositoryMock.Setup(r => r.GetOrderByReservationIdAsync("r1"))
            .ReturnsAsync(() => new Order { OrderId = "o1", CustomerId = "u1", ReservationId = "r1", ReservationAddress = "a", ReservationTime = "t", Dishes = [], Version = 3 });
        _orderRepositoryMock.Setup(r => r.SaveOrderAsync(It.IsAny<Order>()))
            .ThrowsAsync(new Amazon.DynamoDBv2.Model.ConditionalCheckFailedException("The conditional request failed"));
    }

    [Theory]
    [AutoData]
    public async Task SubmitGroupPreOrderAsync_OrderWriteFailsOtherwise_GroupIsReopened(
        Reservation reservation, GroupReservation groupReservation, Order order, string userId)
    {
        SetupSubmittable(reservation, groupReservation, order, userId);
        order.Dishes = [];
        SetupSessions(groupReservation.GroupReservationId,
            Session("s1", groupReservation.GroupReservationId, isSubmitted: true, ("dish1", 1)));
        var groupWrites = new List<GroupReservationStatus>();
        _groupReservationRepositoryMock
            .Setup(r => r.SaveGroupReservationAsync(It.IsAny<GroupReservation>()))
            .Callback((GroupReservation g) => groupWrites.Add(g.Status));
        _orderRepositoryMock
            .Setup(r => r.SaveOrderAsync(It.IsAny<Order>()))
            .ThrowsAsync(new TimeoutException("dynamo down"));

        await Assert.ThrowsAsync<TimeoutException>(() =>
            _service.SubmitGroupPreOrderAsync(userId, groupReservation.GroupReservationId, reservation.Id));

        Assert.Equal([GroupReservationStatus.Submitted, GroupReservationStatus.Open], groupWrites);
        Assert.Equal(GroupReservationStatus.Open, groupReservation.Status);
    }

    [Fact]
    public async Task SubmitGroupPreOrderAsync_TwoConcurrentSubmits_OnlyFirstWritesOrderAndGroup()
    {
        var reservation = new Reservation { Id = "r1", TableId = "t1", WaiterId = "w1", CustomerId = "u1", Status = ReservationStatus.Reserved, GuestCount = 4 };
        _reservationRepositoryMock.Setup(r => r.GetReservationByIdAsync("r1")).ReturnsAsync(reservation);
        SetupSessions("g1", Session("s1", "g1", isSubmitted: true, ("dish1", 2)));

        // simulated DynamoDB items with version-checked writes (null = attribute absent)
        var storedGroup = new GroupReservation { GroupReservationId = "g1", ReservationId = "r1", ReservationToken = "tok", Status = GroupReservationStatus.Open };
        var storedOrder = new Order { OrderId = "o1", CustomerId = "u1", ReservationId = "r1", ReservationAddress = "a", ReservationTime = "t", Dishes = [] };
        GroupReservation CopyGroup() => new() { GroupReservationId = "g1", ReservationId = "r1", ReservationToken = "tok", Status = storedGroup.Status, Version = storedGroup.Version };
        Order CopyOrder() => new() { OrderId = "o1", CustomerId = "u1", ReservationId = "r1", ReservationAddress = "a", ReservationTime = "t", Status = storedOrder.Status, Version = storedOrder.Version,
            Dishes = storedOrder.Dishes.Select(d => new OrderItem { DishId = d.DishId, Quantity = d.Quantity }).ToList() };

        // request B has read the group while it was still Open (stale copy), request A then runs to the end
        var staleForB = CopyGroup();
        var groupReads = 0;
        _groupReservationRepositoryMock.Setup(r => r.GetGroupReservationByIdAsync("g1"))
            .ReturnsAsync(() => ++groupReads == 3 ? staleForB : CopyGroup());
        _orderRepositoryMock.Setup(r => r.GetOrderByReservationIdAsync("r1")).ReturnsAsync(() => CopyOrder());
        _groupReservationRepositoryMock.Setup(r => r.SaveGroupReservationAsync(It.IsAny<GroupReservation>())).Returns((GroupReservation g) =>
        {
            if (g.Version != storedGroup.Version) throw new Amazon.DynamoDBv2.Model.ConditionalCheckFailedException("conflict");
            g.Version = (g.Version ?? -1) + 1;
            storedGroup = CopyGroup(); storedGroup.Status = g.Status; storedGroup.Version = g.Version;
            return Task.CompletedTask;
        });
        _orderRepositoryMock.Setup(r => r.SaveOrderAsync(It.IsAny<Order>())).Returns((Order o) =>
        {
            if (o.Version != storedOrder.Version) throw new Amazon.DynamoDBv2.Model.ConditionalCheckFailedException("conflict");
            o.Version = (o.Version ?? -1) + 1;
            storedOrder = o;
            return Task.CompletedTask;
        });

        var first = await _service.SubmitGroupPreOrderAsync("u1", "g1", "r1");   // A: reads 1 (validate) and 2 (claim)
        await Assert.ThrowsAsync<GroupReservationSubmittedException>(() =>
            _service.SubmitGroupPreOrderAsync("u1", "g1", "r1"));                // B: validates on the stale Open copy, claim re-read sees Submitted

        Assert.Equal(new SubmittedGroupDish("dish1", 2), Assert.Single(first));
        Assert.Equal(2, Assert.Single(storedOrder.Dishes).Quantity); // not doubled
        Assert.Equal(GroupReservationStatus.Submitted, storedGroup.Status);
        Assert.Equal(0, storedGroup.Version);
    }

    [Fact]
    public async Task CloseGroupReservationAsync_RereadsByKeyAndRetriesOnVersionConflict()
    {
        var fromIndex = new GroupReservation { GroupReservationId = "g1", ReservationId = "r1", ReservationToken = "tok", Status = GroupReservationStatus.Open, Version = 0 };
        _groupReservationRepositoryMock.Setup(r => r.GetByReservationIdAsync("r1")).ReturnsAsync(fromIndex);
        _groupReservationRepositoryMock.Setup(r => r.GetGroupReservationByIdAsync("g1"))
            .ReturnsAsync(() => new GroupReservation { GroupReservationId = "g1", ReservationId = "r1", ReservationToken = "tok", Status = GroupReservationStatus.Submitted, Version = 1 });
        var attempts = 0;
        var saved = new List<GroupReservation>();
        _groupReservationRepositoryMock.Setup(r => r.SaveGroupReservationAsync(It.IsAny<GroupReservation>())).Returns((GroupReservation g) =>
        {
            if (++attempts == 1) throw new Amazon.DynamoDBv2.Model.ConditionalCheckFailedException("conflict");
            saved.Add(g);
            return Task.CompletedTask;
        });

        await _service.CloseGroupReservationAsync("r1");

        var closed = Assert.Single(saved);
        Assert.Equal(GroupReservationStatus.Closed, closed.Status);
        Assert.Equal(1, closed.Version); // version of the consistent read, not of the index copy
        _groupReservationRepositoryMock.Verify(r => r.GetGroupReservationByIdAsync("g1"), Times.Exactly(2));
    }

    [Fact]
    public void GroupReservation_Version_IsNotSerialized()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new GroupReservation { GroupReservationId = "g1", ReservationId = "r1", ReservationToken = "t", Version = 3 });

        Assert.DoesNotContain("ersion", json);
    }

    [Fact]
    public void Order_Version_IsNotSerializedToApiResponses()
    {
        var order = new Order { OrderId = "o1", CustomerId = "u1", ReservationId = "r1", ReservationAddress = "a", ReservationTime = "t", Dishes = [], Version = 7 };

        var json = System.Text.Json.JsonSerializer.Serialize(order);

        Assert.DoesNotContain("ersion", json);
    }

    // ---------- helpers ----------

    private void SetupReservation(Reservation reservation, string ownerId)
    {
        reservation.CustomerId = ownerId;
        reservation.Status = ReservationStatus.Reserved;
        _reservationRepositoryMock
            .Setup(r => r.GetReservationByIdAsync(reservation.Id))
            .ReturnsAsync(reservation);
    }

    private void SetupGroupByReservation(GroupReservation groupReservation, Reservation reservation)
    {
        groupReservation.ReservationId = reservation.Id;
        groupReservation.Status = GroupReservationStatus.Open;
        _groupReservationRepositoryMock
            .Setup(r => r.GetByReservationIdAsync(reservation.Id))
            .ReturnsAsync(groupReservation);
    }

    private void SetupGroupById(GroupReservation groupReservation)
    {
        _groupReservationRepositoryMock
            .Setup(r => r.GetGroupReservationByIdAsync(groupReservation.GroupReservationId))
            .ReturnsAsync(groupReservation);
    }

    private void SetupSubmittable(Reservation reservation, GroupReservation groupReservation, Order order, string ownerId)
    {
        SetupReservation(reservation, ownerId);
        groupReservation.ReservationId = reservation.Id;
        groupReservation.Status = GroupReservationStatus.Open;
        SetupGroupById(groupReservation);

        order.ReservationId = reservation.Id;
        order.Status = OrderStatus.Created;
        _orderRepositoryMock
            .Setup(r => r.GetOrderByReservationIdAsync(reservation.Id))
            .ReturnsAsync(order);
    }

    private void SetupSessions(string groupReservationId, params GuestSession[] sessions)
    {
        _guestSessionRepositoryMock
            .Setup(r => r.GetAllByGroupReservationIdAsync(groupReservationId))
            .ReturnsAsync(sessions.ToList());
    }

    private void VerifyNothingSaved()
    {
        _orderRepositoryMock.Verify(r => r.SaveOrderAsync(It.IsAny<Order>()), Times.Never);
        _groupReservationRepositoryMock.Verify(r => r.SaveGroupReservationAsync(It.IsAny<GroupReservation>()), Times.Never);
    }

    private static GuestSession Session(string id, string groupReservationId, bool isSubmitted, params (string DishId, int Quantity)[] dishes) => new()
    {
        Id = id,
        GroupReservationId = groupReservationId,
        GuestName = id,
        IsSubmitted = isSubmitted,
        Dishes = dishes.Select(d => new OrderItem { DishId = d.DishId, Quantity = d.Quantity }).ToList(),
    };
}
