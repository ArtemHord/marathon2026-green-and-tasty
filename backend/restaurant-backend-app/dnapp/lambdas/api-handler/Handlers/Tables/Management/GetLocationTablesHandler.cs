using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using APIHandler.Constants;
using APIHandler.Helpers;
using APIHandler.Services.Tables;
using System.Threading.Tasks;

namespace APIHandler.Handlers.Tables.Management;

public class GetLocationTablesHandler(ITableService tableService) : IApiRequestHandler
{
    public async Task<APIGatewayProxyResponse> HandleRequest(APIGatewayProxyRequest request, ILambdaContext context)
    {
        if (!RoleHelper.IsWaiter(request)) return ResponseBuilder.CreateErrorResponse(403, "Forbidden", context.Logger);

        // path: /locations/{id}/tables/all — the location parameter is named "id" like every /locations/{…} resource
        // (API Gateway does not allow different parameter names at the same path level)
        if (!request.TryGetPathValue("id", out var locationId))
            return ResponseBuilder.CreateErrorResponse(400, "Missing location 'id' in path parameters.", context.Logger);

        var tables = await tableService.GetAllTablesForLocationAsync(locationId);
        return ResponseBuilder.CreateSuccessResponse(tables);
    }
}