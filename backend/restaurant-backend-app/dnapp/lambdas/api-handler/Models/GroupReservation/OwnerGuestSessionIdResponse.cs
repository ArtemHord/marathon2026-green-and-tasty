using System.Text.Json.Serialization;

namespace APIHandler.Models.GroupReservation;

/// <summary>Response of <c>GET /reservations/{reservationId}/guest-session-id</c>.</summary>
public record OwnerGuestSessionIdResponse(
    [property: JsonPropertyName("guestSessionId")] string GuestSessionId);
