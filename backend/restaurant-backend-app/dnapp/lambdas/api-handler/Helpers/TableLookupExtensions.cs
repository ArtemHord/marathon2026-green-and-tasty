using APIHandler.Entities;
using APIHandler.Repositories.Reservations;
using APIHandler.Repositories.Tables;
using System;
using System.Threading.Tasks;

namespace APIHandler.Helpers;

/// <summary>
/// Best-effort lookups for display-only response fields (table number, reservation times): a missing item or a failed
/// read yields null and the field is omitted, the request itself never fails because of it.
/// </summary>
public static class TableLookupExtensions
{
    public static async Task<Table?> TryGetTableAsync(this ITableRepository tableRepository, string? tableId)
    {
        if (string.IsNullOrEmpty(tableId))
            return null;

        try
        {
            return await tableRepository.GetTableByIdAsync(tableId);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static async Task<Reservation?> TryGetReservationAsync(this IReservationRepository reservationRepository, string? reservationId)
    {
        if (string.IsNullOrEmpty(reservationId))
            return null;

        try
        {
            return await reservationRepository.GetReservationByIdAsync(reservationId);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
