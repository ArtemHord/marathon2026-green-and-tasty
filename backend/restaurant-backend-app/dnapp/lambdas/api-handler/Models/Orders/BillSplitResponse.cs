using APIHandler.Models.GuestSessions;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace APIHandler.Models.Orders;

/// <summary>
/// <c>GET /reservations/{reservationId}/bill-split</c>: the order split by participant, at current dish prices.
/// <c>Total</c> = Σ participant totals; <c>ExcludedCanceled</c> = number of order lines canceled by the waiter (not billed).
/// </summary>
public record BillSplitResponse(
    [property: JsonPropertyName("participants")] IReadOnlyList<BillSplitParticipant> Participants,
    [property: JsonPropertyName("total")] decimal Total,
    [property: JsonPropertyName("excludedCanceled")] int ExcludedCanceled);

/// <param name="GuestSessionId"><c>owner-&lt;groupReservationId&gt;</c> for the Owner (null if the reservation has no group), the guest session id for a guest.</param>
public record BillSplitParticipant(
    [property: JsonPropertyName("guestSessionId")] string? GuestSessionId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("type"), JsonConverter(typeof(JsonStringEnumConverter<ParticipantType>))] ParticipantType Type,
    [property: JsonPropertyName("items")] IReadOnlyList<BillSplitItem> Items,
    [property: JsonPropertyName("total")] decimal Total);

public record BillSplitItem(
    [property: JsonPropertyName("dishId")] string DishId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("quantity")] int Quantity,
    [property: JsonPropertyName("price")] decimal Price,
    [property: JsonPropertyName("subtotal")] decimal Subtotal);
