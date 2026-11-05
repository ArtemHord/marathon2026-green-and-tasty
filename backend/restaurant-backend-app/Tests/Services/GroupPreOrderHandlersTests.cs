using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using APIHandler.Exceptions;
using APIHandler.Handlers.GroupReservations;
using APIHandler.Handlers.GuestSessions;
using APIHandler.Models.GroupReservation;
using APIHandler.Models.GuestSessions;
using APIHandler.Services.GroupReservations;
using APIHandler.Services.GuestSessions;
using Moq;
using System.Text.Json;

namespace APIHandler.Tests.Services;

/// <summary>
/// HTTP mapping of the group pre-order endpoints: 401 without a token, 400 for a bad body,
/// service exceptions → 404 / 403 / 400, JSON shape of successful responses.
/// </summary>
public class GroupPreOrderHandlersTests
{
    private const string UserId = "user-1";
    private const string GroupId = "group-1";
    private const string ReservationId = "reservation-1";

    private readonly Mock<IGroupReservationService> _groupReservationService = new();
    private readonly Mock<IGuestSessionService> _guestSessionService = new();
    private readonly ILambdaContext _context;

    public GroupPreOrderHandlersTests()
    {
        var context = new Mock<ILambdaContext>();
        context.SetupGet(c => c.Logger).Returns(new Mock<ILambdaLogger>().Object);
        _context = context.Object;
    }

    // ---------- POST /group-reservations/{groupReservationId}/submit ----------

    [Fact]
    public async Task Submit_Success_Returns200WithMergedDishes()
    {
        _groupReservationService
            .Setup(s => s.SubmitGroupPreOrderAsync(UserId, GroupId, ReservationId))
            .ReturnsAsync([new SubmittedGroupDish("dish1", 3), new SubmittedGroupDish("dish2", 2)]);

        var response = await SubmitHandler().HandleRequest(
            Request(new() { ["groupReservationId"] = GroupId }, $$"""{ "reservationId": "{{ReservationId}}" }"""), _context);

        Assert.Equal(200, response.StatusCode);
        using var json = JsonDocument.Parse(response.Body);
        Assert.Equal(JsonValueKind.Array, json.RootElement.ValueKind);
        Assert.Equal("dish1", json.RootElement[0].GetProperty("dishId").GetString());
        Assert.Equal(3, json.RootElement[0].GetProperty("quantity").GetInt32());
        Assert.Equal("dish2", json.RootElement[1].GetProperty("dishId").GetString());
        Assert.Equal(2, json.RootElement[1].GetProperty("quantity").GetInt32());
    }

    [Fact]
    public async Task Submit_WithoutToken_Returns401()
    {
        var response = await SubmitHandler().HandleRequest(
            Request(new() { ["groupReservationId"] = GroupId }, $$"""{ "reservationId": "{{ReservationId}}" }""", userId: null), _context);

        Assert.Equal(401, response.StatusCode);
        _groupReservationService.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("""{ "reservationId": "" }""")]
    [InlineData("""{ "reservationId": "   " }""")]
    [InlineData("not json")]
    [InlineData("""{ "reservationId": 42 }""")]
    public async Task Submit_BodyWithoutReservationId_Returns400(string? body)
    {
        var response = await SubmitHandler().HandleRequest(Request(new() { ["groupReservationId"] = GroupId }, body), _context);

        Assert.Equal(400, response.StatusCode);
        _groupReservationService.VerifyNoOtherCalls();
    }

    public static TheoryData<Exception, int> SubmitErrors() => new()
    {
        { new ReservationNotFoundException("x"), 404 },
        { new GroupReservationNotFoundException(), 404 },
        { new ActionForbiddenException("Only the reservation owner can perform this action."), 403 },
        { new ValidationException("Group reservation does not belong to this reservation."), 400 },
        { new GroupReservationSubmittedException(GroupReservationService.AlreadySubmittedMessage), 400 },
        { new GroupReservationExpiredException(GroupReservationService.ClosedMessage), 400 },
        { new ValidationException("There are no submitted guest sessions in this group."), 400 },
        { new ValidationException("Submitted guest sessions do not contain any dishes."), 400 },
        { new ValidationException("Pre-order for reservation reservation-1 was not found."), 400 },
    };

    [Theory]
    [MemberData(nameof(SubmitErrors))]
    public async Task Submit_ServiceError_IsMappedToStatusCode(Exception exception, int expectedStatus)
    {
        _groupReservationService
            .Setup(s => s.SubmitGroupPreOrderAsync(UserId, GroupId, ReservationId))
            .ThrowsAsync(exception);

        var response = await SubmitHandler().HandleRequest(
            Request(new() { ["groupReservationId"] = GroupId }, $$"""{ "reservationId": "{{ReservationId}}" }"""), _context);

        Assert.Equal(expectedStatus, response.StatusCode);
        Assert.Contains(exception.Message, response.Body);
    }

    [Fact]
    public async Task Submit_AlreadySubmitted_Returns400WithMessageAndErrorCode()
    {
        _groupReservationService
            .Setup(s => s.SubmitGroupPreOrderAsync(UserId, GroupId, ReservationId))
            .ThrowsAsync(new GroupReservationSubmittedException(GroupReservationService.AlreadySubmittedMessage));

        var response = await SubmitHandler().HandleRequest(
            Request(new() { ["groupReservationId"] = GroupId }, $$"""{ "reservationId": "{{ReservationId}}" }"""), _context);

        Assert.Equal(400, response.StatusCode);
        using var json = JsonDocument.Parse(response.Body);
        Assert.Equal("Group pre-order has already been submitted.", json.RootElement.GetProperty("message").GetString());
        Assert.Equal("GroupReservationSubmitted", json.RootElement.GetProperty("errorCode").GetString());
    }

    // ---------- GET /reservations/{reservationId}/guest-session-id ----------

    [Fact]
    public async Task GetOwnerSessionId_Success_Returns200WithGuestSessionId()
    {
        _groupReservationService
            .Setup(s => s.GetOwnerGuestSessionIdAsync(UserId, ReservationId))
            .ReturnsAsync(new OwnerGuestSessionIdResponse("owner-session-123"));

        var response = await OwnerSessionHandler().HandleRequest(Request(new() { ["reservationId"] = ReservationId }), _context);

        Assert.Equal(200, response.StatusCode);
        using var json = JsonDocument.Parse(response.Body);
        Assert.Equal("owner-session-123", json.RootElement.GetProperty("guestSessionId").GetString());
    }

    [Fact]
    public async Task GetOwnerSessionId_WithoutToken_Returns401()
    {
        var response = await OwnerSessionHandler().HandleRequest(Request(new() { ["reservationId"] = ReservationId }, userId: null), _context);

        Assert.Equal(401, response.StatusCode);
        _groupReservationService.VerifyNoOtherCalls();
    }

    public static TheoryData<Exception, int> OwnerSessionErrors() => new()
    {
        { new ReservationNotFoundException("x"), 404 },
        { new GroupReservationNotFoundException(), 404 },
        { new GuestSessionNotFoundException("Owner session for reservation reservation-1 was not found."), 404 },
        { new ActionForbiddenException("Only the reservation owner can perform this action."), 403 },
    };

    [Theory]
    [MemberData(nameof(OwnerSessionErrors))]
    public async Task GetOwnerSessionId_ServiceError_IsMappedToStatusCode(Exception exception, int expectedStatus)
    {
        _groupReservationService
            .Setup(s => s.GetOwnerGuestSessionIdAsync(UserId, ReservationId))
            .ThrowsAsync(exception);

        var response = await OwnerSessionHandler().HandleRequest(Request(new() { ["reservationId"] = ReservationId }), _context);

        Assert.Equal(expectedStatus, response.StatusCode);
    }

    // ---------- GET /guest/groups/{groupReservationId}/sessions ----------

    [Fact]
    public async Task GetSessions_Success_Returns200WithCamelCaseSnapshot()
    {
        _guestSessionService
            .Setup(s => s.GetGroupSessionsAsync(UserId, GroupId))
            .ReturnsAsync(new GetAllSessionsResponse(
                [new Participant("owner-1", ParticipantType.Owner, "Andrii", [], 0, false)], 0));

        var response = await SessionsHandler().HandleRequest(Request(new() { ["groupReservationId"] = GroupId }), _context);

        Assert.Equal(200, response.StatusCode);
        using var json = JsonDocument.Parse(response.Body);
        var participant = json.RootElement.GetProperty("participants")[0];
        Assert.Equal("Owner", participant.GetProperty("type").GetString());
        Assert.Equal("owner-1", participant.GetProperty("id").GetString());
        Assert.False(participant.GetProperty("isSubmitted").GetBoolean());
        Assert.Equal(0, json.RootElement.GetProperty("total").GetDecimal());
    }

    [Fact]
    public async Task GetSessions_WithoutToken_Returns401()
    {
        var response = await SessionsHandler().HandleRequest(Request(new() { ["groupReservationId"] = GroupId }, userId: null), _context);

        Assert.Equal(401, response.StatusCode);
        _guestSessionService.VerifyNoOtherCalls();
    }

    public static TheoryData<Exception, int> SessionsErrors() => new()
    {
        { new GroupReservationNotFoundException(), 404 },
        { new ReservationNotFoundException("x"), 404 },
        { new ActionForbiddenException("Only the reservation owner can perform this action."), 403 },
    };

    [Theory]
    [MemberData(nameof(SessionsErrors))]
    public async Task GetSessions_ServiceError_IsMappedToStatusCode(Exception exception, int expectedStatus)
    {
        _guestSessionService
            .Setup(s => s.GetGroupSessionsAsync(UserId, GroupId))
            .ThrowsAsync(exception);

        var response = await SessionsHandler().HandleRequest(Request(new() { ["groupReservationId"] = GroupId }), _context);

        Assert.Equal(expectedStatus, response.StatusCode);
    }

    // ---------- DELETE /guest/groups/{groupReservationId}/sessions/{guestSessionId} ----------

    public static TheoryData<Exception, int> DeleteErrors() => new()
    {
        { new GroupReservationNotFoundException(), 404 },
        { new GuestSessionNotFoundException("Guest session with id s1 not found."), 404 },
        { new ActionForbiddenException("Only the reservation owner can perform this action."), 403 },
        { new ValidationException("Guest session does not belong to this group reservation."), 400 },
        { new ValidationException("The owner session cannot be removed from the group."), 400 },
    };

    [Theory]
    [MemberData(nameof(DeleteErrors))]
    public async Task DeleteSession_ServiceError_IsMappedToStatusCode(Exception exception, int expectedStatus)
    {
        _guestSessionService
            .Setup(s => s.DeleteGuestSessionAsync(UserId, GroupId, "s1"))
            .ThrowsAsync(exception);

        var response = await DeleteHandler().HandleRequest(
            Request(new() { ["groupReservationId"] = GroupId, ["guestSessionId"] = "s1" }), _context);

        Assert.Equal(expectedStatus, response.StatusCode);
    }

    [Fact]
    public async Task DeleteSession_Success_Returns200WithCamelCaseSnapshot()
    {
        _guestSessionService
            .Setup(s => s.DeleteGuestSessionAsync(UserId, GroupId, "s1"))
            .ReturnsAsync(new GetAllSessionsResponse(
                [new Participant("session2", ParticipantType.Guest, "Olena", [], 220, false)], 220));

        var response = await DeleteHandler().HandleRequest(
            Request(new() { ["groupReservationId"] = GroupId, ["guestSessionId"] = "s1" }), _context);

        Assert.Equal(200, response.StatusCode);
        using var json = JsonDocument.Parse(response.Body);
        Assert.Equal("Guest", json.RootElement.GetProperty("participants")[0].GetProperty("type").GetString());
        Assert.Equal(220, json.RootElement.GetProperty("total").GetDecimal());
    }

    [Fact]
    public async Task DeleteSession_WithoutToken_Returns401()
    {
        var response = await DeleteHandler().HandleRequest(
            Request(new() { ["groupReservationId"] = GroupId, ["guestSessionId"] = "s1" }, userId: null), _context);

        Assert.Equal(401, response.StatusCode);
        _guestSessionService.VerifyNoOtherCalls();
    }

    // ---------- GET /reservations/{reservationId}/invite-token (owner only) ----------

    [Fact]
    public async Task InviteToken_Owner_Returns200()
    {
        _groupReservationService.Setup(s => s.GetOwnerInviteTokenAsync(UserId, ReservationId)).ReturnsAsync(new GetGroupInviteTokenResponse("tok"));

        var response = await new GetInviteTokenHandler(_groupReservationService.Object)
            .HandleRequest(Request(new() { ["reservationId"] = ReservationId }), _context);

        Assert.Equal(200, response.StatusCode);
        Assert.Equal("""{"token":"tok"}""", response.Body);
    }

    [Fact]
    public async Task InviteToken_WithoutSub_Returns401AndDoesNotCallService()
    {
        var response = await new GetInviteTokenHandler(_groupReservationService.Object)
            .HandleRequest(Request(new() { ["reservationId"] = ReservationId }, userId: null), _context);

        Assert.Equal(401, response.StatusCode);
        _groupReservationService.VerifyNoOtherCalls();
    }

    public static TheoryData<Exception, int> InviteTokenErrors() => new()
    {
        { new ActionForbiddenException("Only the reservation owner can perform this action."), 403 },
        { new ReservationNotFoundException("x"), 404 },
        { new GroupReservationNotFoundException(), 404 },
    };

    [Theory]
    [MemberData(nameof(InviteTokenErrors))]
    public async Task InviteToken_ServiceError_IsMappedToStatusCode(Exception exception, int expectedStatus)
    {
        _groupReservationService.Setup(s => s.GetOwnerInviteTokenAsync(UserId, ReservationId)).ThrowsAsync(exception);

        var response = await new GetInviteTokenHandler(_groupReservationService.Object)
            .HandleRequest(Request(new() { ["reservationId"] = ReservationId }), _context);

        Assert.Equal(expectedStatus, response.StatusCode);
    }

    // ---------- public guest endpoints work without Cognito claims ----------

    [Fact]
    public async Task GuestInvite_WithoutClaims_Returns200()
    {
        _groupReservationService.Setup(s => s.GetGroupReservationByTokenAsync("tok")).ReturnsAsync(new APIHandler.Models.GuestReservation.GetGroupReservationResponse(
            GroupId, "loc", "Green & Tasty", "addr", DateTime.UtcNow, DateTime.UtcNow.AddHours(1), 4, 1, "Owner", APIHandler.Entities.GroupReservationStatus.Open, true));

        var response = await new APIHandler.Handlers.GuestReservations.GetGroupReservationHandler(_groupReservationService.Object)
            .HandleRequest(new APIGatewayProxyRequest { PathParameters = new Dictionary<string, string> { ["token"] = "tok" } }, _context);

        Assert.Equal(200, response.StatusCode);
    }

    [Fact]
    public async Task CreateGuestSession_WithoutClaims_ReturnsSession()
    {
        _guestSessionService.Setup(s => s.CreateGuestSessionAsync(It.IsAny<CreateGuestSessionRequest>()))
            .ReturnsAsync(new CreatGuestSessionResponse("s1", "dG9rZW4="));

        var response = await new CreateGuestSessionHandler(_guestSessionService.Object).HandleRequest(
            new APIGatewayProxyRequest { Body = $$"""{ "groupReservationId": "{{GroupId}}", "guestName": "Olena" }""" }, _context);

        Assert.Equal(200, response.StatusCode);
        Assert.Contains("\"SessionId\":\"s1\"", response.Body);
    }

    // ---------- guest session token check (GuestAuthorizationService) ----------

    [Theory]
    [InlineData("Authorization")]
    [InlineData("authorization")]
    public async Task GuestAuthorization_ValidTokenForThisSession_Passes(string headerName)
    {
        _guestSessionService.Setup(s => s.IsAuthorizedAsync("dG9rZW4=", "s1")).ReturnsAsync(true);
        var request = new APIGatewayProxyRequest { Headers = new Dictionary<string, string> { [headerName] = "Bearer dG9rZW4=" } };

        var result = await new GuestAuthorizationService(_guestSessionService.Object).ValidateAsync(request, _context, "s1");

        Assert.Null(result);
    }

    [Fact]
    public async Task GuestAuthorization_TokenOfAnotherSession_Returns401()
    {
        _guestSessionService.Setup(s => s.IsAuthorizedAsync("dG9rZW4=", "olena")).ReturnsAsync(false);
        var request = new APIGatewayProxyRequest { Headers = new Dictionary<string, string> { ["Authorization"] = "Bearer dG9rZW4=" } };

        var result = await new GuestAuthorizationService(_guestSessionService.Object).ValidateAsync(request, _context, "olena");

        Assert.Equal(401, result!.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Bearer ")]
    [InlineData("Basic dG9rZW4=")]
    public async Task GuestAuthorization_MissingOrMalformedHeader_Returns401(string? value)
    {
        var request = new APIGatewayProxyRequest
        {
            Headers = value is null ? null : new Dictionary<string, string> { ["Authorization"] = value },
        };

        var result = await new GuestAuthorizationService(_guestSessionService.Object).ValidateAsync(request, _context, "s1");

        Assert.Equal(401, result!.StatusCode);
        _guestSessionService.Verify(s => s.IsAuthorizedAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    // ---------- helpers ----------

    private SubmitGroupPreOrderHandler SubmitHandler() => new(_groupReservationService.Object);
    private GetOwnerGuestSessionIdHandler OwnerSessionHandler() => new(_groupReservationService.Object);
    private GetGroupSessionsHandler SessionsHandler() => new(_guestSessionService.Object);
    private DeleteGuestSessionHandler DeleteHandler() => new(_guestSessionService.Object);

    private static APIGatewayProxyRequest Request(Dictionary<string, string> pathParameters, string? body = null, string? userId = UserId)
    {
        var request = new APIGatewayProxyRequest
        {
            PathParameters = pathParameters,
            Body = body,
            RequestContext = new APIGatewayProxyRequest.ProxyRequestContext(),
        };

        if (userId is not null)
        {
            request.RequestContext.Authorizer = new APIGatewayCustomAuthorizerContext
            {
                Claims = new Dictionary<string, string> { ["sub"] = userId },
            };
        }

        return request;
    }
}
