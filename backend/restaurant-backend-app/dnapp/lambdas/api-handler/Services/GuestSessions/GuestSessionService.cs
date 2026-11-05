using Amazon.CognitoIdentityProvider.Model;
using APIHandler.Constants;
using APIHandler.Constants.Dishes;
using APIHandler.Constants.Orders;
using APIHandler.Entities;
using APIHandler.Exceptions;
using APIHandler.Helpers;
using APIHandler.Mappers;
using APIHandler.Models.GuestSessions;
using APIHandler.Models.Orders;
using APIHandler.Repositories.Auth;
using APIHandler.Repositories.Dishes;
using APIHandler.Repositories.GroupReservations;
using APIHandler.Repositories.GuestSessions;
using APIHandler.Repositories.Orders;
using APIHandler.Repositories.Reservations;
using APIHandler.Repositories.Tables;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using APIHandler.Services.Notifications;
using APIHandler.Services.GroupReservations;

namespace APIHandler.Services.GuestSessions;

public class GuestSessionService(
    IGroupReservationRepository groupReservationRepository,
    IGuestSessionRepository guestSessionRepository,
    IDishRepository dishRepository,
    IReservationRepository reservationRepository,
    IUserRepository userRepository,
    ITableRepository tableRepository,
    IOrderRepository orderRepository) : IGuestSessionService
{
    public async Task<bool> IsAuthorizedAsync(string guestSessionToken, string guestSessionId)
    {
        if (string.IsNullOrWhiteSpace(guestSessionToken))
            return false;

        byte[] tokenBytes;
        try
        {
            tokenBytes = Convert.FromBase64String(guestSessionToken);
        }
        catch (FormatException)
        {
            return false; // not a guest session token (e.g. a Cognito JWT) → 401, not 500
        }

        string hash = Convert.ToBase64String(SHA256.HashData(tokenBytes));
        GuestSession? guestSession = await guestSessionRepository.GetByTokenHash(hash);
        if (guestSession is null)
            return false;

        // the token is bound to exactly one session: it cannot read or modify another guest's session
        return guestSession.Id == guestSessionId;
    }

    public async Task<CreatGuestSessionResponse> CreateGuestSessionAsync(CreateGuestSessionRequest request)
    {
        GroupReservation group = await GetGroupReservationAndValidateAsync(request.GroupReservationId);

        Task<Reservation?> reservationTask = reservationRepository.GetReservationByIdAsync(group.ReservationId);
        Task<int> countTask = guestSessionRepository.CountByGroupReservationId(group.GroupReservationId);

        Reservation reservation = await reservationTask ?? throw new GroupReservationExpiredException($"Associated reservation for group '{request.GroupReservationId}' not found.");

        if (reservation.Status != ReservationStatus.Reserved)
        {
            throw new GuestSessionExpiredException($"This pre-order session has expired or is no longer active. Current reservation status: '{reservation.Status}'.");
        }

        int joinedGuestsCount = await countTask;

        if (joinedGuestsCount + 1 >= reservation.GuestCount)
            throw new GuestLimitExceededException($"Guest limit exceeded. Maximum capacity is {reservation.GuestCount}, but {joinedGuestsCount} guests have already joined.");

        byte[] tokenBytes = RandomNumberGenerator.GetBytes(32);

        var session = new GuestSession()
        {
            Id = Guid.NewGuid().ToString(),
            GuestName = request.GuestName,
            GroupReservationId = group.GroupReservationId,
            SessionTokenHash = Convert.ToBase64String(SHA256.HashData(tokenBytes)),
            Dishes = [],
        };

        await guestSessionRepository.SaveAsync(session);

        return new CreatGuestSessionResponse(session.Id, Convert.ToBase64String(tokenBytes));
    }

    public async Task<GuestMenuResponse> GetGuestMenuAsync(string guestSessionId)
    {
        var session = await guestSessionRepository.GetByGuestSessionId(guestSessionId)
            ?? throw new ValidationException($"Guest session '{guestSessionId}' not found.");

        await ValidateGroupReservationId(session.GroupReservationId);

        string locationId = await GetLocationIdForGroupAsync(session.GroupReservationId);
        var dishes = await dishRepository.GetDishesByLocationIdAsync(locationId);

        var dishesMapped = dishes
            .GroupBy(d => d.DishType)
            .ToDictionary(k => k.Key, GuestMenuDishMapper.Map);

        return new GuestMenuResponse(dishesMapped);
    }

    public async Task AddDishAsync(string guestSessionId, string dishId, int quantity)
    {
        if (quantity <= 0)
            throw new ValidationException($"Quantity ({quantity}) must be greater than zero.");

        GuestSession session = await GetGuestSessionAsync(guestSessionId);

        string locationId = await GetLocationIdForGroupAsync(session.GroupReservationId);
        var locationDishes = await dishRepository.GetDishesByLocationIdAsync(locationId);

        var dish = locationDishes.FirstOrDefault(d => d.Id == dishId)
            ?? throw new ValidationException($"Dish '{dishId}' does not exist or is not available at this location.");

        if (dish.State != DishState.Available)
            throw new DishIsNotAvailableException($"Dish '{dishId}' is currently unavailable. State: '{dish.State}'.");

        session.Dishes ??= [];

        var existingDish = session.Dishes.FirstOrDefault(d => d.DishId == dishId);

        if (existingDish != null)
        {
            existingDish.Quantity += quantity;
        }
        else
        {
            session.Dishes.Add(new OrderItem
            {
                DishId = dishId,
                Quantity = quantity,
                State = OrderItemState.Pending
            });
        }

        await guestSessionRepository.SaveAsync(session);
    }

    public async Task RemoveDishAsync(string guestSessionId, string dishId, int quantity)
    {
        if (quantity <= 0)
            throw new ValidationException("Quantity must be greater than zero.");

        GuestSession session = await GetGuestSessionAsync(guestSessionId);

        if (session.Dishes == null || session.Dishes.Count == 0)
            throw new DishNotFoundException($"Dish {dishId} not found in guest session.");

        var existingDish = session.Dishes.FirstOrDefault(d => d.DishId == dishId)
            ?? throw new DishNotFoundException($"Dish {dishId} not found in guest session.");

        existingDish.Quantity -= quantity;

        if (existingDish.Quantity <= 0)
        {
            session.Dishes.Remove(existingDish);
        }

        await guestSessionRepository.SaveAsync(session);
    }

    public async Task<GroupReservationStatusResponse> GetGroupReservationStatusAsync(string groupReservationId)
    {
        var group = await groupReservationRepository.GetGroupReservationByIdAsync(groupReservationId)
            ?? throw new GroupReservationNotFoundException();

        var reservation = await reservationRepository.GetReservationByIdAsync(group.ReservationId)
            ?? throw new InvalidOperationException("Associated reservation not found.");

        int currentParticipants = await GetCurrentParticipantCountAsync(groupReservationId);

        return new GroupReservationStatusResponse
        {
            CurrentParticipants = currentParticipants,
            MaxCapacity = reservation.GuestCount,
            Status = group.Status
        };
    }


    public async Task<GetAllSessionsResponse> GetGroupSessionsAsync(string userId, string groupReservationId)
    {
        var group = await ValidateReservationOwnerAsync(groupReservationId, userId);

        return await BuildSessionsSnapshotAsync(group, userId);
    }

    public async Task<GetAllSessionsResponse> DeleteGuestSessionAsync(string userId, string groupReservationId, string guestSessionId)
    {
        var group = await ValidateReservationOwnerAsync(groupReservationId, userId);

        var session = await guestSessionRepository.GetByGuestSessionId(guestSessionId)
            ?? throw new GuestSessionNotFoundException($"Guest session with id {guestSessionId} not found.");

        if (session.GroupReservationId != groupReservationId)
        {
            throw new ValidationException("Guest session does not belong to this group reservation.");
        }

        if (session.IsOwnerSession)
        {
            throw new ValidationException("The owner session cannot be removed from the group.");
        }

        await guestSessionRepository.DeleteAsync(session);

        // The GroupReservationId index is eventually consistent and may still return the deleted session.
        return await BuildSessionsSnapshotAsync(group, userId, excludedSessionId: session.Id);
    }

    /// <summary>
    /// Builds the group snapshot: one participant per session (owner first, then guests in storage order),
    /// <c>type</c> = Owner for the owner session, otherwise Guest; group total = sum over all participants.
    /// Items whose dish no longer exists are skipped (they cannot be priced).
    /// The GroupReservationId index is eventually consistent, so <paramref name="excludedSessionId"/> (just deleted) is
    /// filtered out and a just-created owner session missing from the index is added by its deterministic id.
    /// </summary>
    private async Task<GetAllSessionsResponse> BuildSessionsSnapshotAsync(GroupReservation group, string ownerUserId, string? excludedSessionId = null)
    {
        string groupReservationId = group.GroupReservationId;

        var sessions = (await guestSessionRepository.GetAllByGroupReservationIdAsync(groupReservationId) ?? [])
            .Where(s => s.Id != excludedSessionId)
            .ToList();

        if (!sessions.Any(s => s.IsOwnerSession))
        {
            var owner = await guestSessionRepository.GetByGuestSessionId(GuestSessionIds.ForOwner(groupReservationId));
            if (owner is { IsOwnerSession: true } && owner.GroupReservationId == groupReservationId)
                sessions.Add(owner);
        }

        sessions = sessions.OrderByDescending(s => s.IsOwnerSession).ToList();

        // The Owner's own dishes are the reservation order ("My Pre-Order" = what the restaurant gets),
        // not the owner session's cart: only order lines that are not Canceled and have a positive quantity.
        // The order is read once, also when there is no owner session yet (see below).
        List<OrderItem> ownerOrderLines = [];
        {
            var order = await orderRepository.GetOrderByReservationIdAsync(group.ReservationId);
            if (order is not null && order.Status != OrderStatus.Cancelled)
            {
                var billable = (order.Dishes ?? []).Where(GroupReservationService.IsBillable);
                // After a group submit the order also holds the guests' dishes: subtract what that submit added,
                // so "My Pre-Order" shows only the Owner's own dishes and nothing is counted twice.
                ownerOrderLines = group.Status != GroupReservationStatus.Open && group.SubmittedGuestDishes is { Count: > 0 } added
                    ? SubtractByDishId(billable, added)
                    : billable.ToList();
            }
        }

        // The frontend asks for the snapshot in parallel with GET guest-session-id (which creates the owner session):
        // if the Owner has dishes in the order but no session yet, show the Owner with the id that endpoint will return.
        if (!sessions.Any(s => s.IsOwnerSession) && ownerOrderLines.Count > 0)
        {
            sessions.Insert(0, new GuestSession
            {
                Id = GuestSessionIds.ForOwner(groupReservationId),
                GroupReservationId = groupReservationId,
                GuestName = await GetOwnerNameAsync(ownerUserId),
                Dishes = [],
                IsOwnerSession = true,
                OwnerUserId = ownerUserId,
            });
        }

        List<OrderItem> LinesOf(GuestSession s) => s.IsOwnerSession ? ownerOrderLines : (s.Dishes ?? []);

        var dishIds = sessions
            .SelectMany(LinesOf)
            .Select(d => d.DishId)
            .Distinct()
            .ToArray();

        var dishesById = dishIds.Length == 0
            ? new Dictionary<string, Dish>()
            : (await dishRepository.GetDishesByIdsAsync(dishIds))
                .GroupBy(d => d.Id)
                .ToDictionary(g => g.Key, g => g.First());

        var participants = new List<Participant>();

        foreach (var guestSession in sessions)
        {
            var items = LinesOf(guestSession)
                .Where(d => dishesById.ContainsKey(d.DishId))
                .Select(d => new OrderItemResponse
                {
                    Dish = DishMapper.ToDishResponse(dishesById[d.DishId]),
                    Quantity = d.Quantity
                })
                .ToArray();

            decimal total = items.Sum(i => i.Quantity * i.Dish.Price);
            var type = guestSession.IsOwnerSession ? ParticipantType.Owner : ParticipantType.Guest;
            // Owner: "submitted" = the order exists and has dishes; guests: their own cart submit.
            bool isSubmitted = guestSession.IsOwnerSession ? items.Length > 0 : guestSession.IsSubmitted;
            participants.Add(new Participant(guestSession.Id, type, guestSession.GuestName, items, total, isSubmitted));
        }

        return new GetAllSessionsResponse(participants, participants.Sum(p => p.Total));
    }

    public async Task SubmitCartAsync(string guestSessionId)
    {
        GuestSession guestSession = await GetGuestSessionAsync(guestSessionId);

        if (!guestSession.Dishes.Any())
            throw new CartIsEmptyException();

        await ValidateDishesAsync(guestSession.Dishes);

        guestSession.IsSubmitted = true;
        await guestSessionRepository.SaveAsync(guestSession);
    }

    private async Task<GroupReservation> GetGroupReservationAndValidateAsync(string groupReservationId)
    {
        GroupReservation? group = await groupReservationRepository.GetGroupReservationByIdAsync(groupReservationId);
        ValidateGroupReservation(group);
        return group;
    }

    private async Task<int> GetCurrentParticipantCountAsync(string groupReservationId)
    {
        var sessions = await guestSessionRepository.GetAllByGroupReservationIdAsync(groupReservationId);

        // Owner is always 1 active participant (an owner session, if it exists, is not counted twice).
        // If sessions is null/empty, it safely evaluates to 0 joined guests + 1 owner = 1.
        return (sessions?.Count(s => !s.IsOwnerSession) ?? 0) + 1;
    }

    private async Task ValidateGroupReservationId(string groupReservationId) => await GetGroupReservationAndValidateAsync(groupReservationId);

    private static void ValidateGroupReservation([NotNull] GroupReservation? group)
    {
        if (group is null)
            throw new GroupReservationNotFoundException();

        if (group.Status == GroupReservationStatus.Closed)
            throw new GroupReservationExpiredException();

        if (group.Status == GroupReservationStatus.Submitted)
            throw new GroupReservationSubmittedException();
    }

    private async Task<GuestSession> GetGuestSessionAsync(string sessionId)
    {
        var session = await guestSessionRepository.GetByGuestSessionId(sessionId)
            ?? throw new GuestSessionNotFoundException($"Guest session {sessionId} not found.");

        if (session.IsSubmitted)
            throw new GuestSessionSubmittedException();

        await ValidateGroupReservationId(session.GroupReservationId);

        return session;
    }

    private async Task ValidateDishesAsync(IEnumerable<OrderItem> orders)
    {
        IEnumerable<string> dishIds = orders.Select(o => o.DishId).ToArray();
        IEnumerable<Dish> dishes = await dishRepository.GetDishesByIdsAsync(dishIds);

        if (dishes.FirstOrDefault(d => d.State != DishState.Available) is Dish dish)
            throw new DishIsNotAvailableException($"The dish '{dish.Name}' ({dish.Id}) is not available.");
    }

    private async Task<string> GetLocationIdForGroupAsync(string groupReservationId)
    {
        var group = await groupReservationRepository.GetGroupReservationByIdAsync(groupReservationId)
            ?? throw new GroupReservationNotFoundException();

        var reservation = await reservationRepository.GetReservationByIdAsync(group.ReservationId)
            ?? throw new ReservationNotFoundException("Parent reservation not found.");

        var table = await tableRepository.GetTableAsync(reservation.TableId)
            ?? throw new ValidationException("Associated table not found.");

        return table.LocationId;
    }

    
    /// <summary>Same name as the lazily created owner session (Cognito given + family name), "Owner" if unknown.</summary>
    private async Task<string> GetOwnerNameAsync(string userId)
    {
        try
        {
            var user = await userRepository.GetUserByIdAsync(userId);
            var given = user?.Attributes?.FirstOrDefault(a => a.Name == "given_name")?.Value;
            var family = user?.Attributes?.FirstOrDefault(a => a.Name == "family_name")?.Value;
            var name = $"{given} {family}".Trim();
            return string.IsNullOrEmpty(name) ? "Owner" : name;
        }
        catch (Exception)
        {
            return "Owner";
        }
    }

    private static List<OrderItem> SubtractByDishId(IEnumerable<OrderItem> lines, IEnumerable<OrderItem> toSubtract)
    {
        var remaining = toSubtract
            .GroupBy(d => d.DishId)
            .ToDictionary(g => g.Key, g => g.Sum(d => d.Quantity));

        var result = new List<OrderItem>();
        foreach (var line in lines)
        {
            int take = remaining.TryGetValue(line.DishId, out var left) ? Math.Min(left, line.Quantity) : 0;
            if (take > 0)
                remaining[line.DishId] = left - take;

            int quantity = line.Quantity - take;
            if (quantity > 0)
                result.Add(new OrderItem { DishId = line.DishId, Quantity = quantity, State = line.State });
        }

        return result;
    }

    /// <returns>The group reservation (its reservation belongs to the caller).</returns>
    private async Task<GroupReservation> ValidateReservationOwnerAsync(string groupReservationId, string userId)
    {
        var group = await groupReservationRepository.GetGroupReservationByIdAsync(groupReservationId)
            ?? throw new GroupReservationNotFoundException();

        var reservation = await reservationRepository.GetReservationByIdAsync(group.ReservationId)
            ?? throw new ReservationNotFoundException("Parent reservation not found.");

        if (string.IsNullOrEmpty(reservation.CustomerId) || reservation.CustomerId != userId)
        {
            throw new ActionForbiddenException("Only the reservation owner can perform this action.");
        }

        return group;
    }

    public async Task<GetGuestSessionResponse> GetGuestSessionInfoAsync(string guestSessionId)
    {
        var session = await guestSessionRepository.GetByGuestSessionId(guestSessionId)
            ?? throw new GuestSessionNotFoundException("Guest session was not found.");

        var dishIds = session.Dishes
            .Select(s => s.DishId)
            .Distinct()
            .ToArray();

        var allDishes = await dishRepository.GetDishesByIdsAsync(dishIds);
        var dishesById = allDishes.ToDictionary(d => d.Id);

        var items = session.Dishes.Select(d => new OrderItemResponse
        {
            Dish = DishMapper.ToDishResponse(dishesById[d.DishId]),
            Quantity = d.Quantity
        }).ToArray();

        decimal total = items.Sum(i => i.Quantity * i.Dish.Price);

        string name = session.GuestName;
        if (session.IsOwnerSession)
        {
            var groupReservation = await groupReservationRepository.GetGroupReservationByIdAsync(session.GroupReservationId);
            var reservation = await reservationRepository.GetReservationByIdAsync(groupReservation!.ReservationId);
            var user = await userRepository.GetUserByIdAsync(reservation!.CustomerId!);
            name = user!.Attributes.First(a => a.Name == "given_name").Value;
        }

        return new GetGuestSessionResponse(name, items, total, session.IsSubmitted);
    }

}




