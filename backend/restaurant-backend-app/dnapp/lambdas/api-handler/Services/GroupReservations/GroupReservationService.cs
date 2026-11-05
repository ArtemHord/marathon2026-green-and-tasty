using APIHandler.Constants;
using Amazon.DynamoDBv2.Model;
using APIHandler.Constants.Orders;
using APIHandler.Entities;
using APIHandler.Exceptions;
using APIHandler.Helpers;
using APIHandler.Models.GroupReservation;
using APIHandler.Models.GuestReservation;
using APIHandler.Repositories.GroupReservations;
using APIHandler.Repositories.GuestSessions;
using APIHandler.Repositories.Orders;
using APIHandler.Repositories.Reservations;
using APIHandler.Repositories.Tables;
using APIHandler.Services.Auth;
using APIHandler.Services.Locations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace APIHandler.Services.GroupReservations;

public class GroupReservationService(
    IGroupReservationRepository groupReservationRepository,
    ITableRepository tableRepository,
    IReservationRepository reservationRepository,
    IGuestSessionRepository guestSessionRepository,
    IUserService userService,
    ILocationService locationService,
    IOrderRepository orderRepository) : IGroupReservationService
{
    private const string DefaultRestaurantName = "Green & Tasty";
    private const string DefaultOwnerName = "Owner";

    public const string AlreadySubmittedMessage = "Group pre-order has already been submitted.";
    public const string ClosedMessage = "Group reservation is closed.";
    public const string OrderChangedMessage = "The pre-order was changed while submitting. Please try again.";

    public async Task<GetGroupReservationResponse> GetGroupReservationByTokenAsync(string token)
    {
        GroupReservation groupReservation = await groupReservationRepository.GetGroupReservationByTokenAsync(token)
            ?? throw new GroupReservationNotFoundException();

        // Details are returned in any group state (the Owner screen resolves its group through this endpoint even when
        // the group is full, submitted or closed). Joining is guarded by POST /guest/sessions; CanJoin mirrors those checks.
        Task<Reservation?> reservationTask = reservationRepository.GetReservationByIdAsync(groupReservation.ReservationId);
        Task<int> countTask = guestSessionRepository.CountByGroupReservationId(groupReservation.GroupReservationId);

        Reservation reservation = await reservationTask ?? throw new ValidationException("Associated reservation not found.");
        int joinedGuestsCount = await countTask;

        bool canJoin = groupReservation.Status == GroupReservationStatus.Open
            && reservation.Status == ReservationStatus.Reserved
            && joinedGuestsCount + 1 < reservation.GuestCount;

        Table table = await tableRepository.GetTableAsync(reservation.TableId)
            ?? throw new InvalidOperationException("Associated table not found.");

        Location? location = await locationService.GetLocationByIdAsync(table.LocationId);
        string restaurantName = DefaultRestaurantName;
        string address = location?.Address ?? "Unknown Address";

        string inviterName = "Unknown";
        if (!string.IsNullOrWhiteSpace(reservation.CustomerId))
        {
            inviterName = await userService.GetUserName(reservation.CustomerId) ?? "Unknown";
        }
        return new GetGroupReservationResponse(
            groupReservation.GroupReservationId,
            table.LocationId,
            restaurantName,
            address,
            reservation.StartTime,
            reservation.EndTime,
            reservation.GuestCount,
            table.TableNumber,
            inviterName,
            groupReservation.Status,
            canJoin);
    }

    public async Task<GroupReservation> GetGroupReservationAsync(string groupReservationId)
    {
        GroupReservation? groupReservation = await groupReservationRepository.GetGroupReservationByIdAsync(groupReservationId);
        return groupReservation ?? throw new InvalidOperationException($"Group reservation with ID {groupReservationId} not found.");
    }

    public async Task<GroupReservation> CreateGroupReservationAsync(string reservationId)
    {
        var groupReservation = new GroupReservation
        {
            GroupReservationId = Guid.NewGuid().ToString(),
            ReservationId = reservationId,
            ReservationToken = Guid.NewGuid().ToString("N"),
            Status = GroupReservationStatus.Open,
        };

        await groupReservationRepository.SaveGroupReservationAsync(groupReservation);
        return groupReservation;
    }

    public async Task CreateGroupReservationIfDoesNotExistAsync(string userId, string reservationId)
    {
        GroupReservation? group = await groupReservationRepository.GetByReservationIdAsync(reservationId);
        if (group is not null)
            return;

        await CreateGroupReservationAsync(reservationId);
    }

    public async Task UpdateGroupReservationStatusAsync(string groupReservationId, GroupReservationStatus status)
    {
        var groupReservation = await GetGroupReservationAsync(groupReservationId);

        groupReservation.Status = status;

        await groupReservationRepository.SaveGroupReservationAsync(groupReservation);
    }

    public async Task DeleteGroupReservationAsync(string groupReservationId)
    {
        var groupReservation = await GetGroupReservationAsync(groupReservationId);

        await groupReservationRepository.DeleteGroupReservationAsync(groupReservation);
    }

    public async Task CloseGroupReservationAsync(string reservationId)
    {
        GroupReservation? groupReservation = await groupReservationRepository.GetByReservationIdAsync(reservationId);
        if (groupReservation is null)
            return;

        // The reservationId index is eventually consistent and saves are version-checked (GroupReservation.Version):
        // re-read by key, and retry if a concurrent write (e.g. a group submit) changed the item in between.
        for (int attempt = 1; ; attempt++)
        {
            var current = await groupReservationRepository.GetGroupReservationByIdAsync(groupReservation.GroupReservationId) ?? groupReservation;
            current.Status = GroupReservationStatus.Closed;

            try
            {
                await groupReservationRepository.SaveGroupReservationAsync(current);
                return;
            }
            catch (ConditionalCheckFailedException) when (attempt < 3)
            {
            }
        }
    }

    public async Task<GetGroupInviteTokenResponse> GetGroupReservationInviteTokenAsync(string reservationId)
    {
        var reservation = await reservationRepository.GetReservationByIdAsync(reservationId)
            ?? throw new ReservationNotFoundException($"Reservation {reservationId} was not found.");

        if (reservation.GuestCount < 2)
            throw new ValidationException("Retrieving invitation token for the reservation with only one guest is not permitted.");

        // The token is returned in any reservation/group state: the Owner screen needs it to resolve the group
        // (full, Submitted or Closed). Guests cannot join a non-open group anyway — POST /guest/sessions rejects it.
        GroupReservation groupReservation = await groupReservationRepository.GetByReservationIdAsync(reservationId)
            ?? throw new GroupReservationNotFoundException();

        return new GetGroupInviteTokenResponse(groupReservation.ReservationToken);
    }

    public async Task<OwnerGuestSessionIdResponse> GetOwnerGuestSessionIdAsync(string userId, string reservationId)
    {
        var reservation = await reservationRepository.GetReservationByIdAsync(reservationId)
            ?? throw new ReservationNotFoundException($"Reservation {reservationId} was not found.");

        EnsureOwner(reservation, userId);

        var group = await groupReservationRepository.GetByReservationIdAsync(reservationId)
            ?? throw new GroupReservationNotFoundException();

        var sessions = await guestSessionRepository.GetAllByGroupReservationIdAsync(group.GroupReservationId) ?? [];
        var ownerSession = sessions.FirstOrDefault(s => s.IsOwnerSession);

        if (ownerSession is null)
        {
            // The GroupReservationId index is eventually consistent: a just-created owner session may be missing there.
            var byId = await guestSessionRepository.GetByGuestSessionId(GuestSessionIds.ForOwner(group.GroupReservationId));
            if (byId is { IsOwnerSession: true } && byId.GroupReservationId == group.GroupReservationId)
                ownerSession = byId;
        }

        if (ownerSession is null)
        {
            // Owner sessions are not created when a reservation is made, so the first call for an open group
            // creates it (deterministic id => repeated/concurrent calls resolve to the same session).
            if (group.Status != GroupReservationStatus.Open)
                throw new GuestSessionNotFoundException($"Owner session for reservation {reservationId} was not found.");

            ownerSession = new GuestSession
            {
                Id = GuestSessionIds.ForOwner(group.GroupReservationId),
                GroupReservationId = group.GroupReservationId,
                GuestName = await userService.GetUserName(userId) ?? DefaultOwnerName,
                Dishes = [],
                IsOwnerSession = true,
                OwnerUserId = userId,
            };

            await guestSessionRepository.SaveAsync(ownerSession);
        }

        return new OwnerGuestSessionIdResponse(ownerSession.Id);
    }

    public async Task<IReadOnlyList<SubmittedGroupDish>> SubmitGroupPreOrderAsync(string userId, string groupReservationId, string reservationId)
    {
        // 1. reservation (404) -> group (404) -> owner (403) -> group belongs to reservation (400) -> group is Open (400)
        var reservation = await reservationRepository.GetReservationByIdAsync(reservationId)
            ?? throw new ReservationNotFoundException($"Reservation {reservationId} was not found.");

        var group = await groupReservationRepository.GetGroupReservationByIdAsync(groupReservationId)
            ?? throw new GroupReservationNotFoundException();

        EnsureOwner(reservation, userId);

        if (group.ReservationId != reservation.Id)
            throw new ValidationException("Group reservation does not belong to this reservation.");

        if (group.Status == GroupReservationStatus.Submitted)
            throw new GroupReservationSubmittedException(AlreadySubmittedMessage);

        if (group.Status != GroupReservationStatus.Open)
            throw new GroupReservationExpiredException(ClosedMessage);

        // 2. only submitted sessions (the owner session follows the same rule)
        var sessions = await guestSessionRepository.GetAllByGroupReservationIdAsync(groupReservationId) ?? [];
        var submittedSessions = sessions.Where(s => s.IsSubmitted).ToList();

        if (submittedSessions.Count == 0)
            throw new ValidationException("There are no submitted guest sessions in this group.");

        // 3. one list, merged by dishId (first-appearance order)
        var mergedDishes = submittedSessions
            .SelectMany(s => s.Dishes ?? [])
            .Where(d => d.Quantity > 0)
            .GroupBy(d => d.DishId)
            .Select(g => new SubmittedGroupDish(g.Key, g.Sum(d => d.Quantity)))
            .ToList();

        if (mergedDishes.Count == 0)
            throw new ValidationException("Submitted guest sessions do not contain any dishes.");

        // 4. order of the reservation: add dishes, order -> SubmittedByCustomer, group -> Submitted
        var order = await orderRepository.GetOrderByReservationIdAsync(reservation.Id);

        if (order is null || order.Status == OrderStatus.Cancelled)
            throw new ValidationException($"Pre-order for reservation {reservation.Id} was not found.");

        order.Dishes ??= [];

        foreach (var dish in mergedDishes)
        {
            var existing = order.Dishes.FirstOrDefault(d => d.DishId == dish.DishId);

            if (existing is not null)
            {
                existing.Quantity += dish.Quantity;
                existing.State = OrderItemState.Pending;
            }
            else
            {
                order.Dishes.Add(new OrderItem
                {
                    DishId = dish.DishId,
                    Quantity = dish.Quantity,
                    State = OrderItemState.Pending,
                });
            }
        }

        order.Status = OrderStatus.SubmittedByCustomer;

        // 5. claim the group: fresh (consistent) read, must still be Open, then a version-checked Open -> Submitted write.
        //    Of two submits only one can make this transition, so the guests' dishes are added to the order once
        //    no matter how the requests interleave (a submit that started before another one finished is rejected here).
        var claimed = await ClaimGroupForSubmitAsync(groupReservationId);

        // 6. version-checked order write; if it fails for another reason, the group is put back to Open (compensation).
        try
        {
            await orderRepository.SaveOrderAsync(order);
        }
        catch (ConditionalCheckFailedException)
        {
            // The claim lets only one submit through, so an order version conflict means a different write to the same
            // order (owner edit/cancel, waiter confirm). Reopen the group (itself version-checked: if anyone changed the
            // group after our claim, the reopen fails and is ignored) and let the owner retry.
            await TryReopenGroupAsync(claimed);
            throw new ValidationException(OrderChangedMessage);
        }
        catch
        {
            await TryReopenGroupAsync(claimed);
            throw;
        }

        // 7. merged list
        return mergedDishes;
    }

    private async Task<GroupReservation> ClaimGroupForSubmitAsync(string groupReservationId)
    {
        var group = await groupReservationRepository.GetGroupReservationByIdAsync(groupReservationId)
            ?? throw new GroupReservationNotFoundException();

        if (group.Status == GroupReservationStatus.Submitted)
            throw new GroupReservationSubmittedException(AlreadySubmittedMessage);

        if (group.Status != GroupReservationStatus.Open)
            throw new GroupReservationExpiredException(ClosedMessage);

        group.Status = GroupReservationStatus.Submitted;

        try
        {
            await groupReservationRepository.SaveGroupReservationAsync(group); // conditional on GroupReservation.Version
        }
        catch (ConditionalCheckFailedException)
        {
            throw new GroupReservationSubmittedException(AlreadySubmittedMessage);
        }

        return group;
    }

    private async Task TryReopenGroupAsync(GroupReservation claimed)
    {
        try
        {
            claimed.Status = GroupReservationStatus.Open;
            await groupReservationRepository.SaveGroupReservationAsync(claimed);
        }
        catch (Exception)
        {
            // best effort: the original error is rethrown by the caller
        }
    }

    private static void EnsureOwner(Reservation reservation, string userId)
    {
        if (string.IsNullOrEmpty(reservation.CustomerId) || reservation.CustomerId != userId)
            throw new ActionForbiddenException("Only the reservation owner can perform this action.");
    }
}
