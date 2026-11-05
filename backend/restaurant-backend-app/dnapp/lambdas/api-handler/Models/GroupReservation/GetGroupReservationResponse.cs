using APIHandler.Entities;
using System;
using System.Text.Json.Serialization;

namespace APIHandler.Models.GuestReservation;

/// <summary>
/// Response of <c>GET /guest/invites/{token}</c>. Returned in any group state; <c>Status</c> and <c>CanJoin</c>
/// tell a guest whether <c>POST /guest/sessions</c> will accept a new session.
/// </summary>
public record GetGroupReservationResponse(
    string GroupReservationId,
    string LocationId,
    string RestaurantName,
    string LocationAddress,
    DateTime StartTime,
    DateTime EndTime,
    int MaxGuestCount,
    int TableNumber,
    string InviterName,
    [property: JsonConverter(typeof(JsonStringEnumConverter<GroupReservationStatus>))] GroupReservationStatus Status,
    bool CanJoin
);
