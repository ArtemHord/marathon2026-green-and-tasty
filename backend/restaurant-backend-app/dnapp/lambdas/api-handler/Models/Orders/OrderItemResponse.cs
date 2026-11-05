using APIHandler.Models.Dishes;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace APIHandler.Models.Orders;

public class OrderItemResponse
{
    [JsonPropertyName("dish")]
    public required DishResponse Dish { get; set; }

    [JsonPropertyName("quantity")]
    public int Quantity { get; set; }

    /// <summary>Who ordered the line (split bill). Only for lines the group submit filled; otherwise the field is omitted.</summary>
    [JsonPropertyName("guests")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<OrderItemGuestShareResponse>? Guests { get; set; }
}

public class OrderItemGuestShareResponse
{
    [JsonPropertyName("guestSessionId")]
    public required string GuestSessionId { get; set; }

    [JsonPropertyName("guestName")]
    public required string GuestName { get; set; }

    [JsonPropertyName("quantity")]
    public int Quantity { get; set; }
}
