using Amazon.DynamoDBv2.DataModel;

namespace APIHandler.Entities;

/// <summary>
/// Who ordered (part of) an order line: one share per guest session that the group submit merged into the line.
/// The Owner's share has <c>GuestSessionId = owner-&lt;groupReservationId&gt;</c>.
/// </summary>
public class OrderItemGuestShare
{
    [DynamoDBProperty("guestSessionId")]
    public required string GuestSessionId { get; set; }

    [DynamoDBProperty("guestName")]
    public required string GuestName { get; set; }

    [DynamoDBProperty("quantity")]
    public int Quantity { get; set; }
}
