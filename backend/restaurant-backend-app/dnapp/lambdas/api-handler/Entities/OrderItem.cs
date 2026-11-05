using Amazon.DynamoDBv2.DataModel;
using APIHandler.Constants.Orders;
using APIHandler.Converters;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace APIHandler.Entities;

public class OrderItem
{
    [DynamoDBProperty("dishId")]
    [JsonPropertyName("dishId")]
    public required string DishId { get; set; }

    [DynamoDBProperty("quantity")]
    [JsonPropertyName("quantity")]
    public int Quantity { get; set; }

    [DynamoDBProperty("state", typeof(EnumAsIntConverter<OrderItemState>))]
    [JsonPropertyName("state")]
    public OrderItemState State { get; set; }

    /// <summary>
    /// Who ordered this line (split bill). Filled only by the group submit (POST /group-reservations/{id}/submit) for the
    /// lines it touches; if set, Σ <c>Guests[].Quantity</c> == <see cref="Quantity"/>. Nullable: old orders, regular
    /// pre-orders and lines the submit did not touch have none (the whole line is the Owner's).
    /// [JsonIgnore]: never read from request bodies (POST/PUT /orders) — responses map it explicitly (OrderMapper).
    /// </summary>
    [DynamoDBProperty("guests")]
    [JsonIgnore]
    public List<OrderItemGuestShare>? Guests { get; set; }
}