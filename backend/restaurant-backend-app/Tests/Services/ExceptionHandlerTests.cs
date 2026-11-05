using Amazon.DynamoDBv2.Model;
using Amazon.Lambda.Core;
using APIHandler.Entities;
using APIHandler.Handlers.GroupReservations;
using APIHandler.Services.GroupReservations;
using Function.Services;
using Moq;
using System.Text.Json;

namespace APIHandler.Tests.Services;

/// <summary>Function-level exception mapping: version conflicts → 409, everything else → 500.</summary>
public class ExceptionHandlerTests
{
    private readonly ILambdaContext _context;

    public ExceptionHandlerTests()
    {
        var context = new Mock<ILambdaContext>();
        context.SetupGet(c => c.Logger).Returns(new Mock<ILambdaLogger>().Object);
        _context = context.Object;
    }

    [Fact]
    public void Handle_ConditionalCheckFailed_Returns409WithRetryMessage()
    {
        var response = ExceptionHandler.Handle(new ConditionalCheckFailedException("The conditional request failed"), _context);

        Assert.Equal(409, response.StatusCode);
        using var json = JsonDocument.Parse(response.Body);
        Assert.Equal("The resource was changed concurrently. Please retry.", json.RootElement.GetProperty("error").GetString());
    }

    [Theory]
    [InlineData(typeof(InvalidOperationException))]
    [InlineData(typeof(TimeoutException))]
    [InlineData(typeof(AmazonDynamoDBExceptionStub))]
    public void Handle_OtherExceptions_Return500(Type exceptionType)
    {
        var exception = (Exception)Activator.CreateInstance(exceptionType, "boom")!;

        var response = ExceptionHandler.Handle(exception, _context);

        Assert.Equal(500, response.StatusCode);
        Assert.Contains("Internal Server Error", response.Body);
    }

    /// <summary>Group submit handles the version conflict itself: still 400, not the generic 409.</summary>
    [Fact]
    public async Task SubmitHandler_ServiceMapsConflictToValidation_StillReturns400()
    {
        var service = new Mock<IGroupReservationService>();
        service.Setup(s => s.SubmitGroupPreOrderAsync("u1", "g1", "r1"))
            .ThrowsAsync(new APIHandler.Exceptions.ValidationException(GroupReservationService.OrderChangedMessage));
        var request = new Amazon.Lambda.APIGatewayEvents.APIGatewayProxyRequest
        {
            PathParameters = new Dictionary<string, string> { ["groupReservationId"] = "g1" },
            Body = """{ "reservationId": "r1" }""",
            RequestContext = new() { Authorizer = new() { Claims = new Dictionary<string, string> { ["sub"] = "u1" } } },
        };

        var response = await new SubmitGroupPreOrderHandler(service.Object).HandleRequest(request, _context);

        Assert.Equal(400, response.StatusCode);
        Assert.Contains(GroupReservationService.OrderChangedMessage, response.Body);
    }

    public sealed class AmazonDynamoDBExceptionStub(string message) : Amazon.DynamoDBv2.AmazonDynamoDBException(message);
}
