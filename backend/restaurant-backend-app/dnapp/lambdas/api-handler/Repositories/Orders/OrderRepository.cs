using Amazon.DynamoDBv2.DataModel;
using APIHandler.Constants;
using APIHandler.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace APIHandler.Repositories.Orders;

public class OrderRepository : IOrderRepository
{
    private readonly string OrdersTableName = ResourceNames.OrdersTableName;
    private readonly string CustomerIdGlobalIndex = ResourceNames.OrdersTableCustomerIdGlobalIndex;
    private readonly string ReservationIdGlobalIndex = ResourceNames.OrdersTableReservationIdGlobalIndex;

    private readonly IDynamoDBContext _dynamoDbContext;

    public OrderRepository(IDynamoDBContext dynamoDbContext)
    {
        _dynamoDbContext = dynamoDbContext ?? throw new ArgumentNullException(nameof(dynamoDbContext));
    }

    public async Task<Order?> GetOrderByIdAsync(string orderId, string customerId)
    {
        return await _dynamoDbContext.LoadAsync<Order>(orderId, customerId, DynamoConfig.LoadConfig(OrdersTableName));
    }

    public async Task<IEnumerable<Order>> GetOrdersByCustomerIdAsync(string customerId)
    {
        var config = new QueryConfig
        {
            OverrideTableName = OrdersTableName,
            IndexName = CustomerIdGlobalIndex,
        };

        var query = _dynamoDbContext.QueryAsync<Order>(customerId, config);

        return await query.GetRemainingAsync();
    }

    public async Task<Order?> GetOrderByReservationIdAsync(string reservationId)
    {
        var config = new QueryConfig
        {
            OverrideTableName = OrdersTableName,
            IndexName = ReservationIdGlobalIndex,
        };

        var results = await _dynamoDbContext.QueryAsync<Order>(reservationId, config).GetNextSetAsync();

        // The GSI is eventually consistent. Every save is version-checked (Order.Version), so the order is re-read by its
        // primary key with ConsistentRead: callers then modify the latest data and only a truly concurrent write conflicts.
        foreach (var candidate in results)
        {
            var order = await GetOrderByIdAsync(candidate.OrderId, candidate.CustomerId);
            if (order is not null && order.Status != OrderStatus.Cancelled)
                return order;
        }

        return null;
    }

    /// <summary>
    /// Saves the order with an optimistic-locking check on <see cref="Order.Version"/>.
    /// Throws <see cref="Amazon.DynamoDBv2.Model.ConditionalCheckFailedException"/> if the order was changed since it was read.
    /// </summary>
    public async Task SaveOrderAsync(Order order)
    {
        await _dynamoDbContext.SaveAsync(order, DynamoConfig.SaveConfig(OrdersTableName));
    }
}