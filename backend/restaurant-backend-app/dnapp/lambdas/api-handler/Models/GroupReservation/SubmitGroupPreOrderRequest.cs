using System.Text.Json.Serialization;

namespace APIHandler.Models.GroupReservation;

/// <summary>Body of <c>POST /group-reservations/{groupReservationId}/submit</c>.</summary>
public class SubmitGroupPreOrderRequest
{
    [JsonPropertyName("reservationId")]
    public string? ReservationId { get; set; }
}
