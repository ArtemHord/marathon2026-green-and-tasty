using APIHandler.Entities;
using APIHandler.Models.Orders;
using System;
using System.Collections.Generic;
using System.Linq;

namespace APIHandler.Mappers;

public static class OrderMapper
{
    public static GetOrderResponse ToOrderResponse(Order order, IEnumerable<Dish> dishes, Reservation? reservation = null, Table? table = null)
    {
        return new GetOrderResponse
        {
            OrderId = order.OrderId,
            ReservationId = order.ReservationId,
            ReservationAddress = order.ReservationAddress,
            ReservationTime = order.ReservationTime,
            Status = order.Status,
            ReservationStartTime = reservation is null ? null : AsUtc(reservation.StartTime),
            ReservationEndTime = reservation is null ? null : AsUtc(reservation.EndTime),
            TableNumber = table?.TableNumber,
            TableName = table is null ? null : ReservationMapper.TableLabel(table),
            OrderItems = order.Dishes
                .Where(d => dishes.Any(dish => dish.Id == d.DishId))
                .Select(d => new OrderItemResponse
                {
                    Dish = DishMapper.ToDishResponse(dishes.First(dish => dish.Id == d.DishId)),
                    Quantity = d.Quantity,
                    State = d.State,
                    Guests = d.Guests?.Select(g => new OrderItemGuestShareResponse
                    {
                        GuestSessionId = g.GuestSessionId,
                        GuestName = g.GuestName,
                        Quantity = g.Quantity,
                    }).ToList(),
                }).ToList(),
        };
    }

    /// <summary>
    /// Reservation times are stored in UTC; a value read back as Unspecified is UTC, a Local one is converted. Serialized
    /// with a <c>Z</c> suffix, so the browser never takes it for its own local time.
    /// </summary>
    public static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };

    public static Order ToOrderEntity(CreateOrderRequest request, string customerId, string reservationAddress, string reservationTime)
    {
        return new Order
        {
            OrderId = Guid.NewGuid().ToString(),
            CustomerId = customerId,
            ReservationId = request.ReservationId,
            ReservationAddress = reservationAddress,
            ReservationTime = reservationTime,
            Dishes = request.OrderItems ?? [],
            Status = OrderStatus.Created
        };
    }
}