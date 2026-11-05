using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using APIHandler.Entities;
using APIHandler.Handlers;
using APIHandler.Handlers.Locations;
using APIHandler.Handlers.Tables.Management;
using APIHandler.Records;
using APIHandler.Routing;
using APIHandler.Services.Locations;
using APIHandler.Services.Tables;
using Moq;

namespace APIHandler.Tests.Services;

/// <summary>GET /locations/{id}/tables/all and DELETE /locations/{id}/dishes/{dishId} read the location from path parameter "id".</summary>
public class LocationPathParameterHandlersTests
{
    private readonly Mock<ITableService> _tableService = new();
    private readonly Mock<ILocationService> _locationService = new();
    private readonly ILambdaContext _context;

    public LocationPathParameterHandlersTests()
    {
        var context = new Mock<ILambdaContext>();
        context.SetupGet(c => c.Logger).Returns(new Mock<ILambdaLogger>().Object);
        _context = context.Object;
    }

    [Fact]
    public void RouteTemplate_TablesAll_ProducesIdParameter()
    {
        var match = new RouteHandler(new Route("GET", "/locations/{id}/tables/all"), Mock.Of<IApiRequestHandler>()).Match("/locations/loc-1/tables/all");

        Assert.Equal(new Dictionary<string, string> { ["id"] = "loc-1" }, match);
    }

    [Fact]
    public void RouteTemplate_RemoveDish_ProducesIdAndDishIdParameters()
    {
        var match = new RouteHandler(new Route("DELETE", "/locations/{id}/dishes/{dishId}"), Mock.Of<IApiRequestHandler>()).Match("/locations/loc-1/dishes/dish-9");

        Assert.Equal(new Dictionary<string, string> { ["id"] = "loc-1", ["dishId"] = "dish-9" }, match);
    }

    [Fact]
    public async Task GetLocationTables_IdPathParameter_Returns200WithTablesOfThatLocation()
    {
        _tableService.Setup(s => s.GetAllTablesForLocationAsync("loc-1"))
            .ReturnsAsync([new Table { Id = "t1", LocationId = "loc-1", Capacity = 4, TableNumber = 1 }]);

        var response = await new GetLocationTablesHandler(_tableService.Object).HandleRequest(WaiterRequest(new() { ["id"] = "loc-1" }), _context);

        Assert.Equal(200, response.StatusCode);
        Assert.Contains("\"t1\"", response.Body);
        _tableService.Verify(s => s.GetAllTablesForLocationAsync("loc-1"), Times.Once);
    }

    [Theory]
    [InlineData("locationId")] // the old name is no longer read
    [InlineData(null)]
    public async Task GetLocationTables_WithoutIdPathParameter_Returns400(string? parameterName)
    {
        var parameters = parameterName is null ? new Dictionary<string, string>() : new Dictionary<string, string> { [parameterName] = "loc-1" };

        var response = await new GetLocationTablesHandler(_tableService.Object).HandleRequest(WaiterRequest(parameters), _context);

        Assert.Equal(400, response.StatusCode);
        _tableService.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetLocationTables_NotWaiter_Returns403()
    {
        var request = WaiterRequest(new() { ["id"] = "loc-1" });
        request.RequestContext.Authorizer.Claims["cognito:groups"] = "Customer";

        var response = await new GetLocationTablesHandler(_tableService.Object).HandleRequest(request, _context);

        Assert.Equal(403, response.StatusCode);
    }

    [Fact]
    public async Task RemoveDish_IdAndDishIdPathParameters_Returns200AndRemovesFromThatLocation()
    {
        var response = await new RemoveDishFromLocationHandler(_locationService.Object)
            .HandleRequest(WaiterRequest(new() { ["id"] = "loc-1", ["dishId"] = "dish-9" }), _context);

        Assert.Equal(200, response.StatusCode);
        _locationService.Verify(s => s.RemoveDishFromLocationAsync("loc-1", "dish-9"), Times.Once);
    }

    [Theory]
    [InlineData("locationId", "dishId")] // old name for the location
    [InlineData("id", null)]
    [InlineData(null, "dishId")]
    public async Task RemoveDish_MissingPathParameter_Returns400(string? locationKey, string? dishKey)
    {
        var parameters = new Dictionary<string, string>();
        if (locationKey is not null) parameters[locationKey] = "loc-1";
        if (dishKey is not null) parameters[dishKey] = "dish-9";

        var response = await new RemoveDishFromLocationHandler(_locationService.Object).HandleRequest(WaiterRequest(parameters), _context);

        Assert.Equal(400, response.StatusCode);
        _locationService.VerifyNoOtherCalls();
    }

    private static APIGatewayProxyRequest WaiterRequest(Dictionary<string, string> pathParameters) => new()
    {
        PathParameters = pathParameters,
        RequestContext = new APIGatewayProxyRequest.ProxyRequestContext
        {
            Authorizer = new APIGatewayCustomAuthorizerContext
            {
                Claims = new Dictionary<string, string> { ["sub"] = "waiter-1", ["cognito:groups"] = "Waiter" },
            },
        },
    };
}
