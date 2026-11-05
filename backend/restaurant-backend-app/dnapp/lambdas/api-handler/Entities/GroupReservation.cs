using Amazon.DynamoDBv2.DataModel;
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
}