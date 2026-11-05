using APIHandler.Entities;
using APIHandler.Repositories.Tables;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace APIHandler.Helpers;

/// <summary>
/// Request-scoped table cache for list endpoints (GET /orders, GET /reservations): reservations of one customer/waiter
/// repeat the same tables, so each tableId is read once per request. Concurrent callers share the same pending read.
/// Best-effort like <see cref="TableLookupExtensions.TryGetTableAsync"/>: a missing table or a failed read → null.
/// Create one instance per request; never share it between requests (it would serve stale tables).
/// </summary>
public sealed class RequestTableCache(ITableRepository tableRepository)
{
    private readonly Dictionary<string, Task<Table?>> _tables = [];
    private readonly object _lock = new();

    public Task<Table?> GetAsync(string? tableId)
    {
        if (string.IsNullOrEmpty(tableId))
            return Task.FromResult<Table?>(null);

        lock (_lock)
        {
            if (!_tables.TryGetValue(tableId, out var table))
            {
                table = tableRepository.TryGetTableAsync(tableId); // never throws: failures are cached as null
                _tables[tableId] = table;
            }

            return table;
        }
    }
}
