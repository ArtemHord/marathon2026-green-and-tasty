using Amazon.DynamoDBv2.DataModel;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace APIHandler.Entities;

public class GroupReservation
{
    [DynamoDBHashKey("groupReservationId")]
    public required string GroupReservationId { get; set; }

    [DynamoDBProperty("reservationId")]
    [DynamoDBGlobalSecondaryIndexHashKey("reservationId")]
    public required string ReservationId { get; set; }

    [DynamoDBProperty("reservationToken")]
    [DynamoDBGlobalSecondaryIndexHashKey("reservationToken")]
    public required string ReservationToken { get; set; }

    [DynamoDBProperty("status")]
    public GroupReservationStatus Status { get; set; }

    /// <summary>
    /// Optimistic-locking version (every save is a conditional write). Nullable: records written before it existed
    /// load as null and the first save sets it. Makes the Open -> Submitted transition of a group submit single-winner.
    /// </summary>
    [DynamoDBVersion("version")]
    [JsonIgnore]
    public int? Version { get; set; }

    /// <summary>
    /// Guests' dishes (merged by dishId) that the group submit added to the order. Written in the same version-checked
    /// write as the Open -> Submitted claim and cleared when the claim is compensated. The snapshot subtracts them from
    /// the order to show only the Owner's own dishes after submit. Nullable: old records / open groups have none.
    /// </summary>
    [DynamoDBProperty("submittedGuestDishes")]
    [JsonIgnore]
    public List<OrderItem>? SubmittedGuestDishes { get; set; }
}
