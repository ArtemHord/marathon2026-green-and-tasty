using APIHandler.Constants.Orders;
using APIHandler.Entities;
using APIHandler.Exceptions;
using APIHandler.Helpers;
using APIHandler.Models.GuestSessions;
using APIHandler.Models.Orders;
using APIHandler.Repositories.Dishes;
using APIHandler.Repositories.GroupReservations;
using APIHandler.Repositories.Orders;
using APIHandler.Repositories.Reservations;
using APIHandler.Services.Auth;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace APIHandler.Services.Orders;

/// <summary>
/// Split bill of a reservation's order (<c>GET /reservations/{reservationId}/bill-split</c>).
/// Built from <see cref="OrderItem.Guests"/> (filled by the group submit); lines without Guests are the Owner's.
/// Nothing is stored: the split is computed from the order on every request, at current prices from Dishes.
/// </summary>
public class BillSplitService(
    IReservationRepository reservationRepository,
    IOrderRepository orderRepository,
    IGroupReservationRepository groupReservationRepository,
    IDishRepository dishRepository,
    IUserService userService) : IBillSplitService
{
    public const string DefaultOwnerName = "Owner";
    public const string ForbiddenMessage = "Only the reservation owner or the assigned waiter can view the split bill.";

    public async Task<BillSplitResponse> GetBillSplitAsync(string callerId, string reservationId)
    {
        // 404 reservation -> 403 caller -> 404 order: a stranger never learns whether the reservation has an order.
        var reservation = await reservationRepository.GetReservationByIdAsync(reservationId)
            ?? throw new ReservationNotFoundException($"Reservation {reservationId} was not found.");

        if (!IsOwner(reservation, callerId) && !IsAssignedWaiter(reservation, callerId))
            throw new ActionForbiddenException(ForbiddenMessage);

        var order = await orderRepository.GetOrderByReservationIdAsync(reservationId);
        if (order is null || order.Status == OrderStatus.Cancelled)
            throw new OrderNotFoundException($"Pre-order for reservation {reservationId} was not found.");

        var group = await groupReservationRepository.GetByReservationIdAsync(reservationId);
        string? ownerShareId = group is null ? null : GuestSessionIds.ForOwner(group.GroupReservationId);

        var lines = (order.Dishes ?? []).Where(l => l.Quantity > 0).ToList();
        int excludedCanceled = (order.Dishes ?? []).Count(l => l.State == OrderItemState.Canceled);

        var billableDishIds = lines.Where(IsBillable).Select(l => l.DishId).Distinct().ToArray();
        var dishesById = billableDishIds.Length == 0
            ? new Dictionary<string, Dish>()
            : (await dishRepository.GetDishesByIdsAsync(billableDishIds))
                .GroupBy(d => d.Id)
                .ToDictionary(g => g.Key, g => g.First());

        // Participants are registered from every line (also Canceled ones), so a participant whose dishes the waiter
        // canceled stays in the split with total 0 instead of disappearing. Owner first, guests in order of first appearance.
        Accumulator? owner = null;
        var guests = new List<Accumulator>();
        var guestsById = new Dictionary<string, Accumulator>();
        string? ownerNameFromShares = null;

        foreach (var line in lines)
        {
            bool billable = IsBillable(line);
            dishesById.TryGetValue(line.DishId, out var dish);

            foreach (var (shareId, shareName, quantity) in Allocate(line, ownerShareId))
            {
                Accumulator participant;
                if (shareId is null)
                {
                    participant = owner ??= new Accumulator(ownerShareId, ParticipantType.Owner);
                    ownerNameFromShares ??= shareName;
                }
                else if (guestsById.TryGetValue(shareId, out var known))
                {
                    participant = known;
                }
                else
                {
                    participant = new Accumulator(shareId, ParticipantType.Guest) { Name = shareName ?? string.Empty };
                    guestsById[shareId] = participant;
                    guests.Add(participant);
                }

                // Canceled lines are not billed; a dish deleted from the catalog cannot be priced and is skipped.
                if (billable && dish is not null)
                    participant.Add(dish, quantity);
            }
        }

        var participants = new List<BillSplitParticipant>();
        if (owner is not null)
        {
            owner.Name = string.IsNullOrWhiteSpace(ownerNameFromShares)
                ? await ResolveOwnerNameAsync(reservation.CustomerId)
                : ownerNameFromShares;
            participants.Add(owner.ToResponse());
        }
        participants.AddRange(guests.Select(g => g.ToResponse()));

        return new BillSplitResponse(participants, participants.Sum(p => p.Total), excludedCanceled);
    }

    private static bool IsBillable(OrderItem line) => line.Quantity > 0 && line.State != OrderItemState.Canceled;

    /// <summary>
    /// Splits a line (Quantity &gt; 0) between participants: <c>(null, name, qty)</c> = Owner, <c>(sessionId, name, qty)</c> = guest.
    /// No Guests → the whole line is the Owner's. Shares are taken in order and capped at the line quantity; the part not
    /// covered by any share (Σ Guests &lt; Quantity, e.g. a line changed after submit) is the Owner's — so the split always
    /// adds up to exactly the order.
    /// </summary>
    private static IEnumerable<(string? ShareId, string? Name, int Quantity)> Allocate(OrderItem line, string? ownerShareId)
    {
        int remaining = line.Quantity;

        foreach (var share in line.Guests ?? [])
        {
            int take = Math.Min(share.Quantity, remaining);
            if (take <= 0)
                continue;

            remaining -= take;
            bool isOwner = string.IsNullOrEmpty(share.GuestSessionId) || share.GuestSessionId == ownerShareId;
            yield return (isOwner ? null : share.GuestSessionId, share.GuestName, take);
        }

        if (remaining > 0)
            yield return (null, null, remaining);
    }

    private async Task<string> ResolveOwnerNameAsync(string? customerId)
    {
        if (string.IsNullOrWhiteSpace(customerId))
            return DefaultOwnerName;

        try
        {
            var name = await userService.GetUserName(customerId);
            return string.IsNullOrWhiteSpace(name) ? DefaultOwnerName : name;
        }
        catch (Exception)
        {
            return DefaultOwnerName;
        }
    }

    private static bool IsOwner(Reservation reservation, string callerId) =>
        !string.IsNullOrEmpty(reservation.CustomerId)
        && string.Equals(reservation.CustomerId, callerId, StringComparison.OrdinalIgnoreCase);

    private static bool IsAssignedWaiter(Reservation reservation, string callerId) =>
        !string.IsNullOrEmpty(reservation.WaiterId)
        && string.Equals(reservation.WaiterId, callerId, StringComparison.OrdinalIgnoreCase);

    private sealed class Accumulator(string? guestSessionId, ParticipantType type)
    {
        private readonly List<(Dish Dish, int Quantity)> _items = [];

        public string Name { get; set; } = string.Empty;

        public void Add(Dish dish, int quantity)
        {
            int i = _items.FindIndex(x => x.Dish.Id == dish.Id);
            if (i >= 0)
                _items[i] = (dish, _items[i].Quantity + quantity);
            else
                _items.Add((dish, quantity));
        }

        public BillSplitParticipant ToResponse()
        {
            var items = _items
                .Select(x => new BillSplitItem(x.Dish.Id, x.Dish.Name, x.Quantity, x.Dish.Price, x.Dish.Price * x.Quantity))
                .ToList();
            return new BillSplitParticipant(guestSessionId, Name, type, items, items.Sum(i => i.Subtotal));
        }
    }
}
