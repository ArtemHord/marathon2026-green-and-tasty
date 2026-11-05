using APIHandler.Constants;
using APIHandler.Entities;
using APIHandler.Exceptions;
using APIHandler.Models.GuestSessions;
using APIHandler.Repositories.Auth;
using APIHandler.Repositories.Dishes;
using APIHandler.Repositories.GroupReservations;
using APIHandler.Repositories.GuestSessions;
using APIHandler.Repositories.Orders;
using APIHandler.Constants.Orders;
using APIHandler.Repositories.Reservations;
using APIHandler.Repositories.Tables;
using APIHandler.Services.GuestSessions;
using AutoFixture.Xunit2;
using Moq;
using System.Text.Json;

namespace APIHandler.Tests.Services;

/// <summary>
/// GET /guest/groups/{groupReservationId}/sessions (L1) and
/// DELETE /guest/groups/{groupReservationId}/sessions/{guestSessionId} (L3).
/// </summary>
public class GuestSessionServiceGroupSnapshotTests
{
    private readonly Mock<IGuestSessionRepository> _mockGuestSessionRepository = new();
    private readonly Mock<IGroupReservationRepository> _mockGroupReservationRepository = new();
    private readonly Mock<IDishRepository> _mockDishRepository = new();
    private readonly Mock<IReservationRepository> _mockReservationRepository = new();
    private readonly Mock<IUserRepository> _mockUserRepository = new();
    private readonly Mock<ITableRepository> _mockTableRepository = new();
    private readonly Mock<IOrderRepository> _mockOrderRepository = new();
    private readonly GuestSessionService _guestSessionService;

    private static readonly Dish CaesarSalad = new() { Id = "dish1", Name = "Caesar Salad", Price = 180 };
    private static readonly Dish TomYum = new() { Id = "dish2", Name = "Tom Yum", Price = 220 };

    public GuestSessionServiceGroupSnapshotTests()
    {
        _guestSessionService = new GuestSessionService(
            _mockGroupReservationRepository.Object,
            _mockGuestSessionRepository.Object,
            _mockDishRepository.Object,
            _mockReservationRepository.Object,
            _mockUserRepository.Object,
            _mockTableRepository.Object,
            _mockOrderRepository.Object);
    }

    // ---------- GET sessions ----------

    [Theory]
    [AutoData]
    public async Task GetGroupSessionsAsync_Owner_ReturnsSnapshotWithTypesItemsAndTotals(
        GroupReservation groupReservation, Reservation reservation, string userId)
    {
        SetupOwnedGroup(groupReservation, reservation, userId);

        var andrii = Session("session1", groupReservation.GroupReservationId, "Andrii", isSubmitted: true, ("dish1", 2));
        var olena = Session("session2", groupReservation.GroupReservationId, "Olena", isSubmitted: false, ("dish2", 1));
        var owner = Session("owner-1", groupReservation.GroupReservationId, "Owner", isSubmitted: false);
        owner.IsOwnerSession = true;

        SetupSessions(groupReservation.GroupReservationId, andrii, olena, owner);
        SetupDishes(CaesarSalad, TomYum);

        GetAllSessionsResponse result = await _guestSessionService.GetGroupSessionsAsync(userId, groupReservation.GroupReservationId);

        var participants = result.Participants.ToList();
        Assert.Equal(3, participants.Count);

        // owner goes first and is typed as Owner
        Assert.Equal("owner-1", participants[0].Id);
        Assert.Equal(ParticipantType.Owner, participants[0].Type);
        Assert.Empty(participants[0].Items);
        Assert.Equal(0, participants[0].Total);

        var first = participants.Single(p => p.Id == "session1");
        Assert.Equal(ParticipantType.Guest, first.Type);
        Assert.Equal("Andrii", first.Name);
        Assert.True(first.IsSubmitted);
        var item = Assert.Single(first.Items);
        Assert.Equal("dish1", item.Dish.Id);
        Assert.Equal("Caesar Salad", item.Dish.Name);
        Assert.Equal(180, item.Dish.Price);
        Assert.Equal(2, item.Quantity);
        Assert.Equal(360, first.Total);

        var second = participants.Single(p => p.Id == "session2");
        Assert.Equal(ParticipantType.Guest, second.Type);
        Assert.False(second.IsSubmitted);
        Assert.Equal(220, second.Total);

        // total counts every participant, submitted or not (360 + 220)
        Assert.Equal(580, result.Total);
    }

    [Theory]
    [AutoData]
    public async Task GetGroupSessionsAsync_EmptyGroup_ReturnsNoParticipantsAndZeroTotal(
        GroupReservation groupReservation, Reservation reservation, string userId)
    {
        SetupOwnedGroup(groupReservation, reservation, userId);
        SetupSessions(groupReservation.GroupReservationId);

        GetAllSessionsResponse result = await _guestSessionService.GetGroupSessionsAsync(userId, groupReservation.GroupReservationId);

        Assert.Empty(result.Participants);
        Assert.Equal(0, result.Total);
        _mockDishRepository.Verify(r => r.GetDishesByIdsAsync(It.IsAny<IEnumerable<string>>()), Times.Never);
    }

    [Theory]
    [AutoData]
    public async Task GetGroupSessionsAsync_SessionsWithoutDishesOrWithDeletedDish_AreReturnedWithoutThoseItems(
        GroupReservation groupReservation, Reservation reservation, string userId)
    {
        SetupOwnedGroup(groupReservation, reservation, userId);

        var legacy = Session("legacy", groupReservation.GroupReservationId, "Legacy", isSubmitted: false);
        legacy.Dishes = null!; // record written before "dishes" existed
        var withDeletedDish = Session("s2", groupReservation.GroupReservationId, "Ivan", isSubmitted: true, ("dish1", 1), ("deleted-dish", 5));

        SetupSessions(groupReservation.GroupReservationId, legacy, withDeletedDish);
        SetupDishes(CaesarSalad);

        GetAllSessionsResponse result = await _guestSessionService.GetGroupSessionsAsync(userId, groupReservation.GroupReservationId);

        var participants = result.Participants.ToList();
        Assert.Empty(participants.Single(p => p.Id == "legacy").Items);
        var ivan = participants.Single(p => p.Id == "s2");
        Assert.Equal("dish1", Assert.Single(ivan.Items).Dish.Id);
        Assert.Equal(180, ivan.Total);
        Assert.Equal(180, result.Total);
    }

    [Theory]
    [AutoData]
    public async Task GetGroupSessionsAsync_GroupNotFound_ThrowsGroupReservationNotFoundException(string groupReservationId, string userId)
    {
        _mockGroupReservationRepository
            .Setup(r => r.GetGroupReservationByIdAsync(groupReservationId))
            .ReturnsAsync((GroupReservation?)null);

        await Assert.ThrowsAsync<GroupReservationNotFoundException>(() => _guestSessionService.GetGroupSessionsAsync(userId, groupReservationId));
    }

    [Theory]
    [AutoData]
    public async Task GetGroupSessionsAsync_ParentReservationNotFound_ThrowsReservationNotFoundException(GroupReservation groupReservation, string userId)
    {
        _mockGroupReservationRepository
            .Setup(r => r.GetGroupReservationByIdAsync(groupReservation.GroupReservationId))
            .ReturnsAsync(groupReservation);
        _mockReservationRepository
            .Setup(r => r.GetReservationByIdAsync(groupReservation.ReservationId))
            .ReturnsAsync((Reservation?)null);

        await Assert.ThrowsAsync<ReservationNotFoundException>(() => _guestSessionService.GetGroupSessionsAsync(userId, groupReservation.GroupReservationId));
    }

    [Theory]
    [AutoData]
    public async Task GetGroupSessionsAsync_NotOwner_ThrowsActionForbiddenException(
        GroupReservation groupReservation, Reservation reservation, string ownerId, string otherUserId)
    {
        SetupOwnedGroup(groupReservation, reservation, ownerId);

        await Assert.ThrowsAsync<ActionForbiddenException>(() => _guestSessionService.GetGroupSessionsAsync(otherUserId, groupReservation.GroupReservationId));
        _mockGuestSessionRepository.Verify(r => r.GetAllByGroupReservationIdAsync(It.IsAny<string>()), Times.Never);
    }

    [Theory]
    [AutoData]
    public async Task GetGroupSessionsAsync_Snapshot_SerializesToHomeworkContract(
        GroupReservation groupReservation, Reservation reservation, string userId)
    {
        SetupOwnedGroup(groupReservation, reservation, userId);
        // security/demo stage: the Owner's items come from the reservation order, not from the owner session cart
        var owner = Session("owner-1", groupReservation.GroupReservationId, "Owner", isSubmitted: false);
        owner.IsOwnerSession = true;
        SetupSessions(groupReservation.GroupReservationId, owner);
        SetupOrder(reservation.Id, OrderStatus.Created, ("dish1", 1, OrderItemState.Pending));
        SetupDishes(CaesarSalad);

        var result = await _guestSessionService.GetGroupSessionsAsync(userId, groupReservation.GroupReservationId);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result));

        var participant = json.RootElement.GetProperty("participants")[0];
        Assert.Equal("Owner", participant.GetProperty("type").GetString());
        Assert.Equal("owner-1", participant.GetProperty("id").GetString());
        Assert.Equal("Owner", participant.GetProperty("name").GetString());
        Assert.True(participant.GetProperty("isSubmitted").GetBoolean());
        Assert.Equal(180, participant.GetProperty("total").GetDecimal());
        var dish = participant.GetProperty("items")[0].GetProperty("dish");
        Assert.Equal("dish1", dish.GetProperty("id").GetString());
        Assert.Equal("Caesar Salad", dish.GetProperty("name").GetString());
        Assert.Equal(180, dish.GetProperty("price").GetDecimal());
        Assert.Equal(1, participant.GetProperty("items")[0].GetProperty("quantity").GetInt32());
        Assert.Equal(180, json.RootElement.GetProperty("total").GetDecimal());
    }

    // ---------- DELETE session ----------

    [Theory]
    [AutoData]
    public async Task DeleteGuestSessionAsync_Owner_DeletesGuestAndReturnsUpdatedSnapshotWithOwnerType(
        GroupReservation groupReservation, Reservation reservation, string userId)
    {
        SetupOwnedGroup(groupReservation, reservation, userId);

        var andrii = Session("session1", groupReservation.GroupReservationId, "Andrii", isSubmitted: true, ("dish1", 2));
        var olena = Session("session2", groupReservation.GroupReservationId, "Olena", isSubmitted: false, ("dish2", 1));
        var owner = Session("owner-1", groupReservation.GroupReservationId, "Owner", isSubmitted: false);
        owner.IsOwnerSession = true;

        _mockGuestSessionRepository
            .Setup(r => r.GetByGuestSessionId(andrii.Id))
            .ReturnsAsync(andrii);
        // after deletion the repository returns the remaining sessions
        SetupSessions(groupReservation.GroupReservationId, olena, owner);
        SetupDishes(TomYum);

        GetAllSessionsResponse result = await _guestSessionService.DeleteGuestSessionAsync(userId, groupReservation.GroupReservationId, andrii.Id);

        _mockGuestSessionRepository.Verify(r => r.DeleteAsync(andrii), Times.Once);

        var participants = result.Participants.ToList();
        Assert.Equal(2, participants.Count);
        Assert.DoesNotContain(participants, p => p.Id == andrii.Id);
        // bug fix: the owner session used to be reported as Guest
        Assert.Equal(ParticipantType.Owner, participants.Single(p => p.Id == "owner-1").Type);
        Assert.Equal(ParticipantType.Guest, participants.Single(p => p.Id == "session2").Type);
        Assert.Equal(220, result.Total);
    }

    [Theory]
    [AutoData]
    public async Task DeleteGuestSessionAsync_SessionNotFound_ThrowsGuestSessionNotFoundException(
        GroupReservation groupReservation, Reservation reservation, string userId, string guestSessionId)
    {
        SetupOwnedGroup(groupReservation, reservation, userId);
        _mockGuestSessionRepository
            .Setup(r => r.GetByGuestSessionId(guestSessionId))
            .ReturnsAsync((GuestSession?)null);

        await Assert.ThrowsAsync<GuestSessionNotFoundException>(() =>
            _guestSessionService.DeleteGuestSessionAsync(userId, groupReservation.GroupReservationId, guestSessionId));
        _mockGuestSessionRepository.Verify(r => r.DeleteAsync(It.IsAny<GuestSession>()), Times.Never);
    }

    [Theory]
    [AutoData]
    public async Task DeleteGuestSessionAsync_SessionFromAnotherGroup_ThrowsValidationException(
        GroupReservation groupReservation, Reservation reservation, string userId, string otherGroupId)
    {
        SetupOwnedGroup(groupReservation, reservation, userId);
        var foreign = Session("foreign", otherGroupId, "Stranger", isSubmitted: false);
        _mockGuestSessionRepository
            .Setup(r => r.GetByGuestSessionId(foreign.Id))
            .ReturnsAsync(foreign);

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            _guestSessionService.DeleteGuestSessionAsync(userId, groupReservation.GroupReservationId, foreign.Id));
        Assert.Equal("Guest session does not belong to this group reservation.", ex.Message);
        _mockGuestSessionRepository.Verify(r => r.DeleteAsync(It.IsAny<GuestSession>()), Times.Never);
    }

    [Theory]
    [AutoData]
    public async Task DeleteGuestSessionAsync_OwnerSession_ThrowsValidationException(
        GroupReservation groupReservation, Reservation reservation, string userId)
    {
        SetupOwnedGroup(groupReservation, reservation, userId);
        var owner = Session("owner-1", groupReservation.GroupReservationId, "Owner", isSubmitted: false);
        owner.IsOwnerSession = true;
        _mockGuestSessionRepository
            .Setup(r => r.GetByGuestSessionId(owner.Id))
            .ReturnsAsync(owner);

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            _guestSessionService.DeleteGuestSessionAsync(userId, groupReservation.GroupReservationId, owner.Id));
        Assert.Equal("The owner session cannot be removed from the group.", ex.Message);
        _mockGuestSessionRepository.Verify(r => r.DeleteAsync(It.IsAny<GuestSession>()), Times.Never);
    }

    [Theory]
    [AutoData]
    public async Task DeleteGuestSessionAsync_NotOwner_ThrowsActionForbiddenException(
        GroupReservation groupReservation, Reservation reservation, string ownerId, string otherUserId, string guestSessionId)
    {
        SetupOwnedGroup(groupReservation, reservation, ownerId);

        await Assert.ThrowsAsync<ActionForbiddenException>(() =>
            _guestSessionService.DeleteGuestSessionAsync(otherUserId, groupReservation.GroupReservationId, guestSessionId));
        _mockGuestSessionRepository.Verify(r => r.DeleteAsync(It.IsAny<GuestSession>()), Times.Never);
    }

    [Theory]
    [AutoData]
    public async Task DeleteGuestSessionAsync_GroupNotFound_ThrowsGroupReservationNotFoundException(
        string groupReservationId, string userId, string guestSessionId)
    {
        _mockGroupReservationRepository
            .Setup(r => r.GetGroupReservationByIdAsync(groupReservationId))
            .ReturnsAsync((GroupReservation?)null);

        await Assert.ThrowsAsync<GroupReservationNotFoundException>(() =>
            _guestSessionService.DeleteGuestSessionAsync(userId, groupReservationId, guestSessionId));
    }

    // ---------- participant count with an owner session ----------

    [Theory]
    [AutoData]
    public async Task GetGroupReservationStatusAsync_OwnerSessionExists_OwnerIsNotCountedTwice(
        GroupReservation groupReservation, Reservation reservation)
    {
        groupReservation.ReservationId = reservation.Id;
        _mockGroupReservationRepository
            .Setup(r => r.GetGroupReservationByIdAsync(groupReservation.GroupReservationId))
            .ReturnsAsync(groupReservation);
        _mockReservationRepository
            .Setup(r => r.GetReservationByIdAsync(reservation.Id))
            .ReturnsAsync(reservation);

        var owner = Session("owner-1", groupReservation.GroupReservationId, "Owner", isSubmitted: false);
        owner.IsOwnerSession = true;
        var guest = Session("guest-1", groupReservation.GroupReservationId, "Guest", isSubmitted: false);
        SetupSessions(groupReservation.GroupReservationId, owner, guest);

        var status = await _guestSessionService.GetGroupReservationStatusAsync(groupReservation.GroupReservationId);

        Assert.Equal(2, status.CurrentParticipants); // owner + 1 guest
    }

    // ---------- eventual consistency of the GroupReservationId index (F1) ----------

    [Theory]
    [AutoData]
    public async Task DeleteGuestSessionAsync_IndexStillReturnsDeletedSession_SnapshotExcludesIt(
        GroupReservation groupReservation, Reservation reservation, string userId)
    {
        SetupOwnedGroup(groupReservation, reservation, userId);
        var andrii = Session("session1", groupReservation.GroupReservationId, "Andrii", isSubmitted: true, ("dish1", 2));
        var olena = Session("session2", groupReservation.GroupReservationId, "Olena", isSubmitted: false, ("dish2", 1));
        _mockGuestSessionRepository.Setup(r => r.GetByGuestSessionId(andrii.Id)).ReturnsAsync(andrii);
        SetupSessions(groupReservation.GroupReservationId, andrii, olena); // index lag
        SetupDishes(CaesarSalad, TomYum);

        var result = await _guestSessionService.DeleteGuestSessionAsync(userId, groupReservation.GroupReservationId, andrii.Id);

        Assert.Equal("session2", Assert.Single(result.Participants).Id);
        Assert.Equal(220, result.Total);
    }

    [Theory]
    [AutoData]
    public async Task GetGroupSessionsAsync_OwnerSessionMissingFromIndex_IsAddedById(
        GroupReservation groupReservation, Reservation reservation, string userId)
    {
        SetupOwnedGroup(groupReservation, reservation, userId);
        var guest = Session("session1", groupReservation.GroupReservationId, "Andrii", isSubmitted: true, ("dish1", 1));
        SetupSessions(groupReservation.GroupReservationId, guest);
        var owner = Session($"owner-{groupReservation.GroupReservationId}", groupReservation.GroupReservationId, "Owner", isSubmitted: false);
        owner.IsOwnerSession = true;
        _mockGuestSessionRepository.Setup(r => r.GetByGuestSessionId(owner.Id)).ReturnsAsync(owner);
        SetupDishes(CaesarSalad);

        var result = await _guestSessionService.GetGroupSessionsAsync(userId, groupReservation.GroupReservationId);

        var participants = result.Participants.ToList();
        Assert.Equal(2, participants.Count);
        Assert.Equal(owner.Id, participants[0].Id);
        Assert.Equal(ParticipantType.Owner, participants[0].Type);
        Assert.Equal(180, result.Total);
    }

    [Theory]
    [AutoData]
    public async Task GetGroupSessionsAsync_OwnerSessionInIndex_IsNotLookedUpOrDuplicated(
        GroupReservation groupReservation, Reservation reservation, string userId)
    {
        SetupOwnedGroup(groupReservation, reservation, userId);
        var owner = Session($"owner-{groupReservation.GroupReservationId}", groupReservation.GroupReservationId, "Owner", isSubmitted: false);
        owner.IsOwnerSession = true;
        SetupSessions(groupReservation.GroupReservationId, owner);
        _mockGuestSessionRepository.Setup(r => r.GetByGuestSessionId(owner.Id)).ReturnsAsync(owner);

        var result = await _guestSessionService.GetGroupSessionsAsync(userId, groupReservation.GroupReservationId);

        Assert.Single(result.Participants);
        _mockGuestSessionRepository.Verify(r => r.GetByGuestSessionId(It.IsAny<string>()), Times.Never);
    }

    [Theory]
    [AutoData]
    public async Task GetGroupSessionsAsync_SessionWithOwnerIdButFromAnotherGroupOrNotOwner_IsNotAdded(
        GroupReservation groupReservation, Reservation reservation, string userId, string otherGroupId)
    {
        SetupOwnedGroup(groupReservation, reservation, userId);
        SetupSessions(groupReservation.GroupReservationId);
        var foreign = Session($"owner-{groupReservation.GroupReservationId}", otherGroupId, "Owner", isSubmitted: false);
        foreign.IsOwnerSession = true;
        _mockGuestSessionRepository.Setup(r => r.GetByGuestSessionId(foreign.Id)).ReturnsAsync(foreign);

        var result = await _guestSessionService.GetGroupSessionsAsync(userId, groupReservation.GroupReservationId);

        Assert.Empty(result.Participants);
        Assert.Equal(0, result.Total);
    }

    // ---------- joining is guarded by POST /guest/sessions (F2/F3: GET /guest/invites no longer rejects) ----------

    [Theory]
    [InlineAutoData(GroupReservationStatus.Submitted, typeof(GroupReservationSubmittedException))]
    [InlineAutoData(GroupReservationStatus.Closed, typeof(GroupReservationExpiredException))]
    public async Task CreateGuestSessionAsync_GroupNotOpen_ThrowsAndDoesNotSave(
        GroupReservationStatus status, Type exceptionType, GroupReservation groupReservation, Reservation reservation, string guestName)
    {
        groupReservation.Status = status;
        groupReservation.ReservationId = reservation.Id;
        reservation.Status = ReservationStatus.Reserved;
        reservation.GuestCount = 10;
        _mockGroupReservationRepository.Setup(r => r.GetGroupReservationByIdAsync(groupReservation.GroupReservationId)).ReturnsAsync(groupReservation);
        _mockReservationRepository.Setup(r => r.GetReservationByIdAsync(reservation.Id)).ReturnsAsync(reservation);

        await Assert.ThrowsAsync(exceptionType, () =>
            _guestSessionService.CreateGuestSessionAsync(new CreateGuestSessionRequest(groupReservation.GroupReservationId, guestName)));
        _mockGuestSessionRepository.Verify(r => r.SaveAsync(It.IsAny<GuestSession>()), Times.Never);
    }

    [Theory]
    [InlineAutoData(ReservationStatus.InProgress)]
    [InlineAutoData(ReservationStatus.Completed)]
    [InlineAutoData(ReservationStatus.Cancelled)]
    public async Task CreateGuestSessionAsync_ReservationNotReserved_ThrowsGuestSessionExpiredException(
        ReservationStatus reservationStatus, GroupReservation groupReservation, Reservation reservation, string guestName)
    {
        groupReservation.Status = GroupReservationStatus.Open;
        groupReservation.ReservationId = reservation.Id;
        reservation.Status = reservationStatus;
        reservation.GuestCount = 10;
        _mockGroupReservationRepository.Setup(r => r.GetGroupReservationByIdAsync(groupReservation.GroupReservationId)).ReturnsAsync(groupReservation);
        _mockReservationRepository.Setup(r => r.GetReservationByIdAsync(reservation.Id)).ReturnsAsync(reservation);

        await Assert.ThrowsAsync<GuestSessionExpiredException>(() =>
            _guestSessionService.CreateGuestSessionAsync(new CreateGuestSessionRequest(groupReservation.GroupReservationId, guestName)));
        _mockGuestSessionRepository.Verify(r => r.SaveAsync(It.IsAny<GuestSession>()), Times.Never);
    }

    [Theory]
    [InlineAutoData(4, 3, true)]  // owner + 3 guests: full
    [InlineAutoData(4, 2, false)] // last free seat
    public async Task CreateGuestSessionAsync_CapacityBoundary(
        int guestCount, int joinedGuests, bool shouldThrow, GroupReservation groupReservation, Reservation reservation, string guestName)
    {
        groupReservation.Status = GroupReservationStatus.Open;
        groupReservation.ReservationId = reservation.Id;
        reservation.Status = ReservationStatus.Reserved;
        reservation.GuestCount = guestCount;
        _mockGroupReservationRepository.Setup(r => r.GetGroupReservationByIdAsync(groupReservation.GroupReservationId)).ReturnsAsync(groupReservation);
        _mockReservationRepository.Setup(r => r.GetReservationByIdAsync(reservation.Id)).ReturnsAsync(reservation);
        _mockGuestSessionRepository.Setup(r => r.CountByGroupReservationId(groupReservation.GroupReservationId)).ReturnsAsync(joinedGuests);

        var request = new CreateGuestSessionRequest(groupReservation.GroupReservationId, guestName);

        if (shouldThrow)
            await Assert.ThrowsAsync<GuestLimitExceededException>(() => _guestSessionService.CreateGuestSessionAsync(request));
        else
            Assert.NotEmpty((await _guestSessionService.CreateGuestSessionAsync(request)).SessionId);
    }

    // ---------- Owner participant = the reservation order ----------

    [Theory]
    [AutoData]
    public async Task GetGroupSessionsAsync_OwnerWithOrder_ItemsTotalAndIsSubmittedFromOrder(
        GroupReservation groupReservation, Reservation reservation, string userId)
    {
        SetupOwnedGroup(groupReservation, reservation, userId);
        var owner = Session($"owner-{groupReservation.GroupReservationId}", groupReservation.GroupReservationId, "Andrii", isSubmitted: false, ("dish2", 9)); // cart ignored
        owner.IsOwnerSession = true;
        var olena = Session("session2", groupReservation.GroupReservationId, "Olena", isSubmitted: true, ("dish2", 1));
        SetupSessions(groupReservation.GroupReservationId, owner, olena);
        SetupOrder(reservation.Id, OrderStatus.Created,
            ("dish1", 2, OrderItemState.Pending), ("dish2", 1, OrderItemState.Confirmed), ("dish1", 0, OrderItemState.Pending), ("dish2", 4, OrderItemState.Canceled));
        SetupDishes(CaesarSalad, TomYum);

        var result = await _guestSessionService.GetGroupSessionsAsync(userId, groupReservation.GroupReservationId);

        var ownerParticipant = result.Participants.First();
        Assert.Equal(ParticipantType.Owner, ownerParticipant.Type);
        Assert.True(ownerParticipant.IsSubmitted);
        Assert.Equal([("dish1", 2), ("dish2", 1)], ownerParticipant.Items.Select(i => (i.Dish.Id, i.Quantity)).ToList()); // no 0-qty / Canceled lines
        Assert.Equal(2 * 180 + 220, ownerParticipant.Total);
        Assert.Equal(580 + 220, result.Total); // owner order 580 + Olena 220
        _mockOrderRepository.Verify(r => r.GetOrderByReservationIdAsync(reservation.Id), Times.Once);
    }

    [Theory]
    [AutoData]
    public async Task GetGroupSessionsAsync_OwnerOrderCancelled_ItemsEmptyAndNotSubmitted(
        GroupReservation groupReservation, Reservation reservation, string userId)
    {
        SetupOwnedGroup(groupReservation, reservation, userId);
        var owner = Session("owner-1", groupReservation.GroupReservationId, "Owner", isSubmitted: true, ("dish1", 1));
        owner.IsOwnerSession = true;
        SetupSessions(groupReservation.GroupReservationId, owner);
        SetupOrder(reservation.Id, OrderStatus.Cancelled, ("dish1", 3, OrderItemState.Pending));
        SetupDishes(CaesarSalad);

        var result = await _guestSessionService.GetGroupSessionsAsync(userId, groupReservation.GroupReservationId);

        var ownerParticipant = Assert.Single(result.Participants);
        Assert.Empty(ownerParticipant.Items);
        Assert.False(ownerParticipant.IsSubmitted);
        Assert.Equal(0, result.Total);
    }

    [Theory]
    [AutoData]
    public async Task GetGroupSessionsAsync_OwnerWithoutOrderOrWithEmptyOrder_ItemsEmptyAndNotSubmitted(
        GroupReservation groupReservation, Reservation reservation, string userId)
    {
        SetupOwnedGroup(groupReservation, reservation, userId);
        var owner = Session("owner-1", groupReservation.GroupReservationId, "Owner", isSubmitted: true, ("dish1", 1));
        owner.IsOwnerSession = true;
        SetupSessions(groupReservation.GroupReservationId, owner);
        SetupDishes(CaesarSalad);

        _mockOrderRepository.Setup(r => r.GetOrderByReservationIdAsync(reservation.Id)).ReturnsAsync((Order?)null);
        var noOrder = Assert.Single((await _guestSessionService.GetGroupSessionsAsync(userId, groupReservation.GroupReservationId)).Participants);

        SetupOrder(reservation.Id, OrderStatus.Created);
        var emptyOrder = Assert.Single((await _guestSessionService.GetGroupSessionsAsync(userId, groupReservation.GroupReservationId)).Participants);

        Assert.All(new[] { noOrder, emptyOrder }, p =>
        {
            Assert.Empty(p.Items);
            Assert.False(p.IsSubmitted);
            Assert.Equal(0, p.Total);
        });
    }

    [Theory]
    [InlineAutoData(GroupReservationStatus.Submitted)]
    [InlineAutoData(GroupReservationStatus.Closed)]
    public async Task GetGroupSessionsAsync_GroupNotOpen_OwnerItemsStillFromOrder(
        GroupReservationStatus status, GroupReservation groupReservation, Reservation reservation, string userId)
    {
        SetupOwnedGroup(groupReservation, reservation, userId);
        groupReservation.Status = status;
        var owner = Session("owner-1", groupReservation.GroupReservationId, "Owner", isSubmitted: false);
        owner.IsOwnerSession = true;
        SetupSessions(groupReservation.GroupReservationId, owner);
        SetupOrder(reservation.Id, OrderStatus.SubmittedByCustomer, ("dish1", 3, OrderItemState.Pending));
        SetupDishes(CaesarSalad);

        var ownerParticipant = Assert.Single((await _guestSessionService.GetGroupSessionsAsync(userId, groupReservation.GroupReservationId)).Participants);

        Assert.Equal(3, Assert.Single(ownerParticipant.Items).Quantity);
        Assert.True(ownerParticipant.IsSubmitted);
    }

    // Reviewer stage 1 (was ..._NoOwnerSession_OrderIsNotRead): the order is now read once in every snapshot, so that an Owner
    // who already has dishes but no owner session yet (GET guest-session-id runs in parallel) is shown on the first load.
    [Theory]
    [AutoData]
    public async Task GetGroupSessionsAsync_NoOwnerSessionAndNoOrder_NoOwnerParticipant(
        GroupReservation groupReservation, Reservation reservation, string userId)
    {
        SetupOwnedGroup(groupReservation, reservation, userId);
        SetupSessions(groupReservation.GroupReservationId, Session("s1", groupReservation.GroupReservationId, "Andrii", isSubmitted: true, ("dish1", 1)));
        _mockOrderRepository.Setup(r => r.GetOrderByReservationIdAsync(reservation.Id)).ReturnsAsync((Order?)null);
        SetupDishes(CaesarSalad);

        var result = await _guestSessionService.GetGroupSessionsAsync(userId, groupReservation.GroupReservationId);

        Assert.Equal(ParticipantType.Guest, Assert.Single(result.Participants).Type);
        _mockOrderRepository.Verify(r => r.GetOrderByReservationIdAsync(reservation.Id), Times.Once);
    }

    [Theory]
    [AutoData]
    public async Task GetGroupSessionsAsync_NoOwnerSessionButOrderWithDishes_AddsOwnerWithOwnerSessionId(
        GroupReservation groupReservation, Reservation reservation, string userId)
    {
        SetupOwnedGroup(groupReservation, reservation, userId);
        SetupSessions(groupReservation.GroupReservationId, Session("s1", groupReservation.GroupReservationId, "Olena", isSubmitted: true, ("dish2", 1)));
        SetupOrder(reservation.Id, OrderStatus.Created, ("dish1", 2, OrderItemState.Pending));
        SetupDishes(CaesarSalad, TomYum);
        _mockUserRepository.Setup(r => r.GetUserByIdAsync(userId)).ReturnsAsync(new Amazon.CognitoIdentityProvider.Model.UserType
        {
            Attributes = [new() { Name = "given_name", Value = "Andrii" }, new() { Name = "family_name", Value = "Shevchenko" }],
        });

        var result = await _guestSessionService.GetGroupSessionsAsync(userId, groupReservation.GroupReservationId);

        var participants = result.Participants.ToList();
        Assert.Equal(2, participants.Count);
        var owner = participants[0];
        Assert.Equal($"owner-{groupReservation.GroupReservationId}", owner.Id); // = what GET guest-session-id returns
        Assert.Equal(ParticipantType.Owner, owner.Type);
        Assert.Equal("Andrii Shevchenko", owner.Name);
        Assert.True(owner.IsSubmitted);
        Assert.Equal(360, owner.Total);
        Assert.Equal(360 + 220, result.Total);
        _mockOrderRepository.Verify(r => r.GetOrderByReservationIdAsync(reservation.Id), Times.Once);
        _mockGuestSessionRepository.Verify(r => r.SaveAsync(It.IsAny<GuestSession>()), Times.Never); // nothing is written by a GET
    }

    [Theory]
    [AutoData]
    public async Task GetGroupSessionsAsync_NoOwnerSessionAndUnknownUserName_OwnerNamedOwner(
        GroupReservation groupReservation, Reservation reservation, string userId)
    {
        SetupOwnedGroup(groupReservation, reservation, userId);
        SetupSessions(groupReservation.GroupReservationId);
        SetupOrder(reservation.Id, OrderStatus.Created, ("dish1", 1, OrderItemState.Pending));
        SetupDishes(CaesarSalad);
        _mockUserRepository.Setup(r => r.GetUserByIdAsync(userId)).ThrowsAsync(new Exception("cognito down"));

        var owner = Assert.Single((await _guestSessionService.GetGroupSessionsAsync(userId, groupReservation.GroupReservationId)).Participants);

        Assert.Equal("Owner", owner.Name);
    }

    [Theory]
    [AutoData]
    public async Task GetGroupSessionsAsync_OwnerSessionExists_NoDuplicateOwner(
        GroupReservation groupReservation, Reservation reservation, string userId)
    {
        SetupOwnedGroup(groupReservation, reservation, userId);
        var owner = Session($"owner-{groupReservation.GroupReservationId}", groupReservation.GroupReservationId, "Andrii", isSubmitted: false);
        owner.IsOwnerSession = true;
        SetupSessions(groupReservation.GroupReservationId, owner);
        SetupOrder(reservation.Id, OrderStatus.Created, ("dish1", 1, OrderItemState.Pending));
        SetupDishes(CaesarSalad);

        var result = await _guestSessionService.GetGroupSessionsAsync(userId, groupReservation.GroupReservationId);

        var single = Assert.Single(result.Participants);
        Assert.Equal("Andrii", single.Name);
        _mockOrderRepository.Verify(r => r.GetOrderByReservationIdAsync(reservation.Id), Times.Once);
        _mockUserRepository.Verify(r => r.GetUserByIdAsync(It.IsAny<string>()), Times.Never);
    }

    [Theory]
    [AutoData]
    public async Task GetGroupSessionsAsync_NoOwnerSession_OrderWithoutBillableLines_NoOwner(
        GroupReservation groupReservation, Reservation reservation, string userId)
    {
        SetupOwnedGroup(groupReservation, reservation, userId);
        SetupSessions(groupReservation.GroupReservationId);
        SetupOrder(reservation.Id, OrderStatus.Created, ("dish1", 0, OrderItemState.Pending), ("dish2", 2, OrderItemState.Canceled));
        SetupDishes(CaesarSalad, TomYum);

        var result = await _guestSessionService.GetGroupSessionsAsync(userId, groupReservation.GroupReservationId);

        Assert.Empty(result.Participants);
        Assert.Equal(0, result.Total);
    }

    [Theory]
    [AutoData]
    public async Task GetGroupSessionsAsync_NoOwnerSession_SubmittedGroupWhereOrderHasOnlyGuestDishes_NoOwner(
        GroupReservation groupReservation, Reservation reservation, string userId)
    {
        SetupOwnedGroup(groupReservation, reservation, userId);
        groupReservation.Status = GroupReservationStatus.Submitted;
        groupReservation.SubmittedGuestDishes = [Item("dish1", 2)];
        SetupSessions(groupReservation.GroupReservationId, Session("andrii", groupReservation.GroupReservationId, "Andrii", isSubmitted: true, ("dish1", 2)));
        SetupOrder(reservation.Id, OrderStatus.SubmittedByCustomer, ("dish1", 2, OrderItemState.Pending));
        SetupDishes(CaesarSalad);

        var result = await _guestSessionService.GetGroupSessionsAsync(userId, groupReservation.GroupReservationId);

        Assert.Equal("andrii", Assert.Single(result.Participants).Id);
        Assert.Equal(360, result.Total);
    }

    [Theory]
    [AutoData]
    public async Task DeleteGuestSessionAsync_SnapshotAlsoTakesOwnerItemsFromOrder(
        GroupReservation groupReservation, Reservation reservation, string userId)
    {
        SetupOwnedGroup(groupReservation, reservation, userId);
        var guest = Session("session1", groupReservation.GroupReservationId, "Andrii", isSubmitted: true, ("dish2", 1));
        var owner = Session("owner-1", groupReservation.GroupReservationId, "Owner", isSubmitted: false);
        owner.IsOwnerSession = true;
        _mockGuestSessionRepository.Setup(r => r.GetByGuestSessionId(guest.Id)).ReturnsAsync(guest);
        SetupSessions(groupReservation.GroupReservationId, owner);
        SetupOrder(reservation.Id, OrderStatus.Created, ("dish1", 1, OrderItemState.Pending));
        SetupDishes(CaesarSalad, TomYum);

        var result = await _guestSessionService.DeleteGuestSessionAsync(userId, groupReservation.GroupReservationId, guest.Id);

        var ownerParticipant = Assert.Single(result.Participants);
        Assert.Equal("dish1", Assert.Single(ownerParticipant.Items).Dish.Id);
        Assert.Equal(180, result.Total);
    }

    [Theory]
    [AutoData]
    public async Task GetGroupSessionsAsync_ReservationWithoutCustomer_ThrowsActionForbidden(
        GroupReservation groupReservation, Reservation reservation)
    {
        SetupOwnedGroup(groupReservation, reservation, "unused");
        reservation.CustomerId = null;

        await Assert.ThrowsAsync<ActionForbiddenException>(() => _guestSessionService.GetGroupSessionsAsync("", groupReservation.GroupReservationId));
    }

    // ---------- B1: after submit the Owner shows only their own dishes ----------

    [Theory]
    [InlineAutoData(GroupReservationStatus.Submitted)]
    [InlineAutoData(GroupReservationStatus.Closed)]
    public async Task GetGroupSessionsAsync_AfterSubmit_SubtractsSubmittedGuestDishesFromOwnerItems(
        GroupReservationStatus status, GroupReservation groupReservation, Reservation reservation, string userId)
    {
        SetupOwnedGroup(groupReservation, reservation, userId);
        groupReservation.Status = status;
        groupReservation.SubmittedGuestDishes = [Item("dish1", 2), Item("dish2", 1)];
        var owner = Session("owner-1", groupReservation.GroupReservationId, "Owner", isSubmitted: false);
        owner.IsOwnerSession = true;
        SetupSessions(groupReservation.GroupReservationId, owner,
            Session("andrii", groupReservation.GroupReservationId, "Andrii", isSubmitted: true, ("dish1", 2), ("dish2", 1)));
        // order = owner dish1×1 + guests dish1×2 (merged line) + guests dish2×1
        SetupOrder(reservation.Id, OrderStatus.SubmittedByCustomer, ("dish1", 3, OrderItemState.Pending), ("dish2", 1, OrderItemState.Pending));
        SetupDishes(CaesarSalad, TomYum);

        var result = await _guestSessionService.GetGroupSessionsAsync(userId, groupReservation.GroupReservationId);

        var ownerParticipant = result.Participants.First();
        Assert.Equal([("dish1", 1)], ownerParticipant.Items.Select(i => (i.Dish.Id, i.Quantity)).ToList()); // dish2 line ≤ 0 → dropped
        Assert.Equal(180, ownerParticipant.Total);
        Assert.True(ownerParticipant.IsSubmitted);
        Assert.Equal(180 + 2 * 180 + 220, result.Total); // = the order total, nothing counted twice
    }

    [Theory]
    [AutoData]
    public async Task GetGroupSessionsAsync_OpenGroupWithStaleSubmittedGuestDishes_NothingSubtracted(
        GroupReservation groupReservation, Reservation reservation, string userId)
    {
        SetupOwnedGroup(groupReservation, reservation, userId); // Open
        groupReservation.SubmittedGuestDishes = [Item("dish1", 5)];
        var owner = Session("owner-1", groupReservation.GroupReservationId, "Owner", isSubmitted: false);
        owner.IsOwnerSession = true;
        SetupSessions(groupReservation.GroupReservationId, owner);
        SetupOrder(reservation.Id, OrderStatus.Created, ("dish1", 2, OrderItemState.Pending));
        SetupDishes(CaesarSalad);

        var ownerParticipant = Assert.Single((await _guestSessionService.GetGroupSessionsAsync(userId, groupReservation.GroupReservationId)).Participants);

        Assert.Equal(2, Assert.Single(ownerParticipant.Items).Quantity);
    }

    [Theory]
    [AutoData]
    public async Task GetGroupSessionsAsync_SubmittedGroupWithoutRecordedGuestDishes_NothingSubtracted(
        GroupReservation groupReservation, Reservation reservation, string userId)
    {
        SetupOwnedGroup(groupReservation, reservation, userId);
        groupReservation.Status = GroupReservationStatus.Submitted;
        groupReservation.SubmittedGuestDishes = null; // record submitted before the field existed
        var owner = Session("owner-1", groupReservation.GroupReservationId, "Owner", isSubmitted: false);
        owner.IsOwnerSession = true;
        SetupSessions(groupReservation.GroupReservationId, owner);
        SetupOrder(reservation.Id, OrderStatus.SubmittedByCustomer, ("dish1", 3, OrderItemState.Pending));
        SetupDishes(CaesarSalad);

        var ownerParticipant = Assert.Single((await _guestSessionService.GetGroupSessionsAsync(userId, groupReservation.GroupReservationId)).Participants);

        Assert.Equal(3, Assert.Single(ownerParticipant.Items).Quantity);
    }

    [Theory]
    [AutoData]
    public async Task DeleteGuestSessionAsync_AfterSubmit_DoesNotChangeOwnerItems(
        GroupReservation groupReservation, Reservation reservation, string userId)
    {
        SetupOwnedGroup(groupReservation, reservation, userId);
        groupReservation.Status = GroupReservationStatus.Submitted;
        groupReservation.SubmittedGuestDishes = [Item("dish1", 2)];
        var owner = Session("owner-1", groupReservation.GroupReservationId, "Owner", isSubmitted: false);
        owner.IsOwnerSession = true;
        var andrii = Session("andrii", groupReservation.GroupReservationId, "Andrii", isSubmitted: true, ("dish1", 2));
        SetupSessions(groupReservation.GroupReservationId, owner, andrii);
        SetupOrder(reservation.Id, OrderStatus.SubmittedByCustomer, ("dish1", 3, OrderItemState.Pending));
        SetupDishes(CaesarSalad);
        _mockGuestSessionRepository.Setup(r => r.GetByGuestSessionId(andrii.Id)).ReturnsAsync(andrii);

        var before = (await _guestSessionService.GetGroupSessionsAsync(userId, groupReservation.GroupReservationId)).Participants.First();
        var after = (await _guestSessionService.DeleteGuestSessionAsync(userId, groupReservation.GroupReservationId, andrii.Id)).Participants.Single();

        Assert.Equal(ParticipantType.Owner, after.Type);
        Assert.Equal(before.Items.Select(i => (i.Dish.Id, i.Quantity)), after.Items.Select(i => (i.Dish.Id, i.Quantity)));
        Assert.Equal(1, Assert.Single(after.Items).Quantity);
    }

    private static OrderItem Item(string dishId, int quantity) => new() { DishId = dishId, Quantity = quantity };

    // ---------- guest session token is bound to one session ----------

    [Theory]
    [InlineData("not base64 !!")]
    [InlineData("eyJhbGciOiJSUzI1NiJ9.eyJzdWIiOiJ4In0.sig")] // a Cognito JWT is not a guest token
    [InlineData("")]
    [InlineData("   ")]
    public async Task IsAuthorizedAsync_MalformedToken_ReturnsFalseInsteadOfThrowing(string token)
    {
        Assert.False(await _guestSessionService.IsAuthorizedAsync(token, "session1"));
        _mockGuestSessionRepository.Verify(r => r.GetByTokenHash(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task IsAuthorizedAsync_TokenOfAnotherSession_ReturnsFalse()
    {
        var tokenBytes = new byte[] { 1, 2, 3, 4 };
        var hash = Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(tokenBytes));
        _mockGuestSessionRepository.Setup(r => r.GetByTokenHash(hash))
            .ReturnsAsync(Session("andrii", "g1", "Andrii", isSubmitted: false));

        Assert.True(await _guestSessionService.IsAuthorizedAsync(Convert.ToBase64String(tokenBytes), "andrii"));
        Assert.False(await _guestSessionService.IsAuthorizedAsync(Convert.ToBase64String(tokenBytes), "olena"));
    }

    // ---------- helpers ----------

    private void SetupOwnedGroup(GroupReservation groupReservation, Reservation reservation, string ownerId)
    {
        groupReservation.ReservationId = reservation.Id;
        groupReservation.Status = GroupReservationStatus.Open;
        reservation.CustomerId = ownerId;
        reservation.Status = ReservationStatus.Reserved;

        _mockGroupReservationRepository
            .Setup(r => r.GetGroupReservationByIdAsync(groupReservation.GroupReservationId))
            .ReturnsAsync(groupReservation);
        _mockReservationRepository
            .Setup(r => r.GetReservationByIdAsync(reservation.Id))
            .ReturnsAsync(reservation);
    }

    private void SetupSessions(string groupReservationId, params GuestSession[] sessions)
    {
        _mockGuestSessionRepository
            .Setup(r => r.GetAllByGroupReservationIdAsync(groupReservationId))
            .ReturnsAsync(sessions.ToList());
    }

    private void SetupOrder(string reservationId, OrderStatus status, params (string DishId, int Quantity, OrderItemState State)[] lines)
    {
        _mockOrderRepository
            .Setup(r => r.GetOrderByReservationIdAsync(reservationId))
            .ReturnsAsync(new Order
            {
                OrderId = "order-1", CustomerId = "owner", ReservationId = reservationId, ReservationAddress = "a", ReservationTime = "t",
                Status = status,
                Dishes = lines.Select(l => new OrderItem { DishId = l.DishId, Quantity = l.Quantity, State = l.State }).ToList(),
            });
    }

    private void SetupDishes(params Dish[] dishes)
    {
        _mockDishRepository
            .Setup(r => r.GetDishesByIdsAsync(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync((IEnumerable<string> ids) => dishes.Where(d => ids.Contains(d.Id)).ToList());
    }

    private static GuestSession Session(string id, string groupReservationId, string name, bool isSubmitted, params (string DishId, int Quantity)[] dishes) => new()
    {
        Id = id,
        GroupReservationId = groupReservationId,
        GuestName = name,
        IsSubmitted = isSubmitted,
        Dishes = dishes.Select(d => new OrderItem { DishId = d.DishId, Quantity = d.Quantity }).ToList(),
    };
}
