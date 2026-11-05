using APIHandler.Models.Orders;
using System.Threading.Tasks;

namespace APIHandler.Services.Orders;

public interface IBillSplitService
{
    /// <summary>
    /// Split bill of the reservation's order. Access: the customer who owns the reservation or its assigned waiter
    /// (<see cref="APIHandler.Exceptions.ActionForbiddenException"/> otherwise).
    /// </summary>
    /// <exception cref="APIHandler.Exceptions.ReservationNotFoundException">No reservation.</exception>
    /// <exception cref="APIHandler.Exceptions.OrderNotFoundException">No order, or the order is Cancelled.</exception>
    Task<BillSplitResponse> GetBillSplitAsync(string callerId, string reservationId);
}
