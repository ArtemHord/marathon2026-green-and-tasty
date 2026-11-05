using System.Text.Json.Serialization;

namespace APIHandler.Models.GroupReservation;

/// <summary>One consolidated dish of a submitted group pre-order (quantities merged by <c>dishId</c>).</summary>
public record SubmittedGroupDish(
    [property: JsonPropertyName("dishId")] string DishId,
    [property: JsonPropertyName("quantity")] int Quantity);
