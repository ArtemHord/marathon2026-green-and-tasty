using APIHandler.Entities;
using APIHandler.Models.Dishes;
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace APIHandler.Models.Orders;

public class GetOrderResponse
{
    [JsonPropertyName("orderId")]
    public required string OrderId { get; set; }

    [JsonPropertyName("reservationId")]
    public required string ReservationId { get; set; }

    [JsonPropertyName("reservationAddress")]
    public required string ReservationAddress { get; set; }

    [JsonPropertyName("reservationTime")]
    public required string ReservationTime { get; set; }

    [JsonPropertyName("orderItems")]
    public required List<OrderItemResponse> OrderItems { get; set; }

    [JsonPropertyName("status")]
    public OrderStatus Status { get; set; }

    /// <summary>
    /// Current start of the reservation, ISO 8601 in UTC (<c>…Z</c>) — format it in the viewer's time zone, as reservation
    /// cards do with <c>startTime</c>. <c>reservationTime</c> is a text snapshot taken at order creation and formatted in
    /// the server's time zone (UTC). Omitted if the reservation could not be loaded.
    /// </summary>
    [JsonPropertyName("reservationStartTime")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTime? ReservationStartTime { get; set; }

    /// <summary>Current end of the reservation, ISO 8601 in UTC. Omitted if the reservation could not be loaded.</summary>
    [JsonPropertyName("reservationEndTime")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTime? ReservationEndTime { get; set; }

    /// <summary>Number of the reserved table. Omitted if the table could not be loaded.</summary>
    [JsonPropertyName("tableNumber")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? TableNumber { get; set; }

    /// <summary>Display label of the table, <c>"Table {tableNumber}"</c>. Omitted if the table could not be loaded.</summary>
    [JsonPropertyName("tableName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TableName { get; set; }
}