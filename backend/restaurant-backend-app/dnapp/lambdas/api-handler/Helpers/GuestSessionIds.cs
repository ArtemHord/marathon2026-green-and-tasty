namespace APIHandler.Helpers;

public static class GuestSessionIds
{
    /// <summary>Deterministic id of the owner's guest session in a group (one owner session per group).</summary>
    public static string ForOwner(string groupReservationId) => $"owner-{groupReservationId}";
}
