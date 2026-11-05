using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using APIHandler.Constants;
using APIHandler.Constants.Orders;
using APIHandler.Entities;
using APIHandler.Exceptions;
using APIHandler.Handlers.GroupReservations;
using APIHandler.Handlers.GuestSessions;
using APIHandler.Records;
using APIHandler.Repositories.Auth;
using APIHandler.Repositories.Dishes;
using APIHandler.Repositories.GroupReservations;
using APIHandler.Repositories.GuestSessions;
using APIHandler.Repositories.Orders;
using APIHandler.Repositories.Reservations;
using APIHandler.Repositories.Tables;
using APIHandler.Routing;
using APIHandler.Services.Auth;
using APIHandler.Services.GroupReservations;
using APIHandler.Services.GuestSessions;
using APIHandler.Services.Locations;
using Moq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace APIHandler.Tests.Services;

/// <summary>
/// Contract of the group pre-order endpoints (docs/features/group-preorder.md, docs/ApiRoutes.md):
/// real handler + real service + mocked repositories, so the JSON that leaves the lambda is checked —
/// exact field names, error body formats ({ error } vs { message, errorCode }) and documented decisions.
/// </summary>
public class GroupPreOrderContractTests
{
    private const string OwnerId = "owner-user";
    private const string OtherUserId = "other-user";
    private const string ReservationId = "reservation-1";
    private const string GroupId = "group-1";

    private readonly Mock<IGroupReservationRepository> _groupRepo = new();
    private readonly Mock<IGuestSessionRepository> _sessionRepo = new();
    private readonly Mock<IDishRepository> _dishRepo = new();
    private readonly Mock<IReservationRepository> _reservationRepo = new();
    private readonly Mock<IUserRepository> _userRepo = new();
    private readonly Mock<ITableRepository> _tableRepo = new();
    private readonly Mock<IUserService> _userService = new();
    private readonly Mock<ILocationService> _locationService = new();
    private readonly Mock<IOrderRepository> _orderRepo = new();

    private readonly GuestSessionService _guestSessionService;
    private readonly GroupReservationService _groupReservationService;
    private readonly ILambdaContext _context;

    private readonly Reservation _reservation = new() { Id = ReservationId, TableId = "table-1", WaiterId = "waiter-1", CustomerId = OwnerId, Status = ReservationStatus.Reserved, GuestCount = 4 };
    private readonly GroupReservation _group = new() { GroupReservationId = GroupId, ReservationId = ReservationId, Status = GroupReservationStatus.Open, ReservationToken = "token" };
    private readonly Order _order = new() { OrderId = "order-1", ReservationId = ReservationId, CustomerId = OwnerId, ReservationAddress = "address", ReservationTime = "18:00", Status = OrderStatus.Created, Dishes = [] };

    private static readonly Dish CaesarSalad = new() { Id = "dish1", Name = "Caesar Salad", Price = 180, Description = "d", Weight = "200 g" };
    private static readonly Dish TomYum = new() { Id = "dish2", Name = "Tom Yum", Price = 220, Description = "d", Weight = "300 g" };

    public GroupPreOrderContractTests()
    {
        _guestSessionService = new GuestSessionService(
            _groupRepo.Object, _sessionRepo.Object, _dishRepo.Object, _reservationRepo.Object, _userRepo.Object, _tableRepo.Object);
        _groupReservationService = new GroupReservationService(
            _groupRepo.Object, _tableRepo.Object, _reservationRepo.Object, _sessionRepo.Object,
            _userService.Object, _locationService.Object, _orderRepo.Object);

        var context = new Mock<ILambdaContext>();
        context.SetupGet(c => c.Logger).Returns(new Mock<ILambdaLogger>().Object);
        _context = context.Object;

        _reservationRepo.Setup(r => r.GetReservationByIdAsync(ReservationId)).ReturnsAsync(_reservation);
        _groupRepo.Setup(r => r.GetGroupReservationByIdAsync(GroupId)).ReturnsAsync(_group);
        _groupRepo.Setup(r => r.GetByReservationIdAsync(ReservationId)).ReturnsAsync(_group);
        _orderRepo.Setup(r => r.GetOrderByReservationIdAsync(ReservationId)).ReturnsAsync(_order);
        _dishRepo
            .Setup(r => r.GetDishesByIdsAsync(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync((IEnumerable<string> ids) => new[] { CaesarSalad, TomYum }.Where(d => ids.Contains(d.Id)).ToList());
        SetupSessions();
    }

    // =====================================================================
    // GET /guest/groups/{groupReservationId}/sessions — snapshot JSON shape
    // =====================================================================

    [Fact]
    public async Task GetSessions_HomeworkExample_HasExactlyTheContractFields()
    {
        var owner = Session("owner-group-1", "Andrii Shevchenko", isSubmitted: false);
        owner.IsOwnerSession = true;
        SetupSessions(
            Session("session1", "Andrii", isSubmitted: true, ("dish1", 2)),
            Session("session2", "Olena", isSubmitted: false, ("dish2", 1)),
            owner);

        var response = await GetSessions(OwnerId);

        Assert.Equal(200, response.StatusCode);
        using var json = JsonDocument.Parse(response.Body);
        var root = json.RootElement;
        Assert.Equal(["participants", "total"], Keys(root));
        Assert.Equal(580, root.GetProperty("total").GetDecimal()); // 360 + 220 + 0, as in the homework

        var participants = root.GetProperty("participants").EnumerateArray().ToList();
        Assert.Equal(3, participants.Count);
        foreach (var participant in participants)
        {
            Assert.Equal(["id", "isSubmitted", "items", "name", "total", "type"], Keys(participant));
            Assert.Equal(JsonValueKind.String, participant.GetProperty("type").ValueKind);
            Assert.Contains(participant.GetProperty("type").GetString(), new[] { "Owner", "Guest" });
        }

        Assert.Equal("Owner", participants[0].GetProperty("type").GetString()); // owner first
        Assert.Equal("owner-group-1", participants[0].GetProperty("id").GetString());

        var andrii = participants.Single(p => p.GetProperty("id").GetString() == "session1");
        Assert.Equal("Guest", andrii.GetProperty("type").GetString());
        Assert.Equal(360, andrii.GetProperty("total").GetDecimal());
        var item = andrii.GetProperty("items")[0];
        Assert.Equal(["dish", "quantity"], Keys(item));
        Assert.Equal(2, item.GetProperty("quantity").GetInt32());
        var dish = item.GetProperty("dish");
        Assert.Equal("dish1", dish.GetProperty("id").GetString());
        Assert.Equal("Caesar Salad", dish.GetProperty("name").GetString());
        Assert.Equal(180, dish.GetProperty("price").GetDecimal());
    }

    [Fact]
    public async Task GetSessions_EmptyGroup_IsEmptyParticipantsAndZeroTotal()
    {
        SetupSessions();

        var response = await GetSessions(OwnerId);

        Assert.Equal(200, response.StatusCode);
        using var json = JsonDocument.Parse(response.Body);
        Assert.Equal(0, json.RootElement.GetProperty("participants").GetArrayLength());
        Assert.Equal(0, json.RootElement.GetProperty("total").GetDecimal());
    }

    /// <summary>The frontend reloads the snapshot after submit (read-only screen) — GET must keep working.</summary>
    [Theory]
    [InlineData(GroupReservationStatus.Submitted)]
    [InlineData(GroupReservationStatus.Closed)]
    public async Task GetSessions_GroupNotOpen_StillReturnsSnapshot(GroupReservationStatus status)
    {
        _group.Status = status;
        SetupSessions(Session("session1", "Andrii", isSubmitted: true, ("dish1", 1)));

        var response = await GetSessions(OwnerId);

        Assert.Equal(200, response.StatusCode);
        using var json = JsonDocument.Parse(response.Body);
        Assert.Equal(180, json.RootElement.GetProperty("total").GetDecimal());
    }

    [Fact]
    public async Task GetSessions_NotOwner_Is403WithErrorBody()
    {
        var response = await GetSessions(OtherUserId);

        AssertErrorBody(response, 403);
    }

    [Fact]
    public async Task GetSessions_ReservationWithoutCustomer_Is403()
    {
        _reservation.CustomerId = null;

        var response = await GetSessions(OwnerId);

        AssertErrorBody(response, 403);
    }

    [Fact]
    public async Task GetSessions_GroupNotFound_Is404WithErrorBody()
    {
        _groupRepo.Setup(r => r.GetGroupReservationByIdAsync(GroupId)).ReturnsAsync((GroupReservation?)null);

        var response = await GetSessions(OwnerId);

        AssertErrorBody(response, 404);
    }

    [Fact]
    public async Task GetSessions_ParentReservationNotFound_Is404WithErrorBody()
    {
        _reservationRepo.Setup(r => r.GetReservationByIdAsync(ReservationId)).ReturnsAsync((Reservation?)null);

        var response = await GetSessions(OwnerId);

        AssertErrorBody(response, 404);
    }

    [Fact]
    public async Task GetSessions_WithoutToken_Is401WithErrorBody()
    {
        var response = await new GetGroupSessionsHandler(_guestSessionService)
            .HandleRequest(Request(new() { ["groupReservationId"] = GroupId }, userId: null), _context);

        AssertErrorBody(response, 401);
    }

    // =====================================================================
    // DELETE /guest/groups/{groupReservationId}/sessions/{guestSessionId}
    // =====================================================================

    [Fact]
    public async Task Delete_OwnerSession_Is400WithMessageAndErrorCode()
    {
        var owner = Session("owner-group-1", "Owner", isSubmitted: false);
        owner.IsOwnerSession = true;
        _sessionRepo.Setup(r => r.GetByGuestSessionId(owner.Id)).ReturnsAsync(owner);

        var response = await Delete(OwnerId, owner.Id);

        AssertErrorCodeBody(response, "Unknown");
        _sessionRepo.Verify(r => r.DeleteAsync(It.IsAny<GuestSession>()), Times.Never);
    }

    [Fact]
    public async Task Delete_SessionFromAnotherGroup_Is400WithMessageAndErrorCode()
    {
        var foreign = Session("foreign", "Ivan", isSubmitted: false);
        foreign.GroupReservationId = "another-group";
        _sessionRepo.Setup(r => r.GetByGuestSessionId(foreign.Id)).ReturnsAsync(foreign);

        var response = await Delete(OwnerId, foreign.Id);

        AssertErrorCodeBody(response, "Unknown");
        _sessionRepo.Verify(r => r.DeleteAsync(It.IsAny<GuestSession>()), Times.Never);
    }

    [Fact]
    public async Task Delete_SessionNotFound_Is404WithErrorBody()
    {
        _sessionRepo.Setup(r => r.GetByGuestSessionId("missing")).ReturnsAsync((GuestSession?)null);

        var response = await Delete(OwnerId, "missing");

        AssertErrorBody(response, 404);
    }

    [Fact]
    public async Task Delete_NotOwner_Is403AndNothingIsDeleted()
    {
        var guest = Session("session1", "Andrii", isSubmitted: false);
        _sessionRepo.Setup(r => r.GetByGuestSessionId(guest.Id)).ReturnsAsync(guest);

        var response = await Delete(OtherUserId, guest.Id);

        AssertErrorBody(response, 403);
        _sessionRepo.Verify(r => r.DeleteAsync(It.IsAny<GuestSession>()), Times.Never);
    }

    /// <summary>Documented decision: DELETE is allowed in any group status.</summary>
    [Theory]
    [InlineData(GroupReservationStatus.Submitted)]
    [InlineData(GroupReservationStatus.Closed)]
    public async Task Delete_GroupNotOpen_IsAllowed(GroupReservationStatus status)
    {
        _group.Status = status;
        var guest = Session("session1", "Andrii", isSubmitted: true, ("dish1", 1));
        _sessionRepo.Setup(r => r.GetByGuestSessionId(guest.Id)).ReturnsAsync(guest);
        SetupSessions();

        var response = await Delete(OwnerId, guest.Id);

        Assert.Equal(200, response.StatusCode);
        _sessionRepo.Verify(r => r.DeleteAsync(guest), Times.Once);
    }

    /// <summary>
    /// The group snapshot is read from the GroupReservationId GSI, which is eventually consistent:
    /// right after DeleteAsync the index can still return the removed session. The "updated snapshot"
    /// returned by DELETE must not contain the guest that was just removed.
    /// </summary>
    [Fact]
    public async Task Delete_IndexStillReturnsRemovedSession_SnapshotDoesNotContainIt()
    {
        var andrii = Session("session1", "Andrii", isSubmitted: true, ("dish1", 2));
        var olena = Session("session2", "Olena", isSubmitted: false, ("dish2", 1));
        _sessionRepo.Setup(r => r.GetByGuestSessionId(andrii.Id)).ReturnsAsync(andrii);
        SetupSessions(andrii, olena); // GSI lag: deleted item still visible

        var response = await Delete(OwnerId, andrii.Id);

        Assert.Equal(200, response.StatusCode);
        using var json = JsonDocument.Parse(response.Body);
        var ids = json.RootElement.GetProperty("participants").EnumerateArray().Select(p => p.GetProperty("id").GetString()).ToList();
        Assert.DoesNotContain("session1", ids);
        Assert.Equal(220, json.RootElement.GetProperty("total").GetDecimal());
    }

    [Fact]
    public async Task Delete_Success_ReturnsSnapshotWithContractFields()
    {
        var andrii = Session("session1", "Andrii", isSubmitted: true, ("dish1", 2));
        _sessionRepo.Setup(r => r.GetByGuestSessionId(andrii.Id)).ReturnsAsync(andrii);
        SetupSessions(Session("session2", "Olena", isSubmitted: false, ("dish2", 1)));

        var response = await Delete(OwnerId, andrii.Id);

        Assert.Equal(200, response.StatusCode);
        using var json = JsonDocument.Parse(response.Body);
        Assert.Equal(["participants", "total"], Keys(json.RootElement));
        Assert.Equal(["id", "isSubmitted", "items", "name", "total", "type"], Keys(json.RootElement.GetProperty("participants")[0]));
    }

    // =====================================================================
    // GET /reservations/{reservationId}/guest-session-id
    // =====================================================================

    [Fact]
    public async Task OwnerSessionId_Success_HasOnlyGuestSessionIdField()
    {
        var response = await OwnerSessionId(OwnerId);

        Assert.Equal(200, response.StatusCode);
        using var json = JsonDocument.Parse(response.Body);
        Assert.Equal(["guestSessionId"], Keys(json.RootElement));
        Assert.Equal($"owner-{GroupId}", json.RootElement.GetProperty("guestSessionId").GetString());
    }

    /// <summary>After submit the owner session already exists → its id is returned (no 404, nothing written).</summary>
    [Theory]
    [InlineData(GroupReservationStatus.Submitted)]
    [InlineData(GroupReservationStatus.Closed)]
    public async Task OwnerSessionId_GroupNotOpenButOwnerSessionExists_ReturnsIt(GroupReservationStatus status)
    {
        _group.Status = status;
        var owner = Session($"owner-{GroupId}", "Owner", isSubmitted: false);
        owner.IsOwnerSession = true;
        SetupSessions(Session("session1", "Andrii", isSubmitted: true), owner);

        var response = await OwnerSessionId(OwnerId);

        Assert.Equal(200, response.StatusCode);
        Assert.Contains($"owner-{GroupId}", response.Body);
        _sessionRepo.Verify(r => r.SaveAsync(It.IsAny<GuestSession>()), Times.Never);
    }

    [Theory]
    [InlineData(GroupReservationStatus.Submitted)]
    [InlineData(GroupReservationStatus.Closed)]
    public async Task OwnerSessionId_GroupNotOpenAndNoOwnerSession_Is404WithErrorBody(GroupReservationStatus status)
    {
        _group.Status = status;

        var response = await OwnerSessionId(OwnerId);

        AssertErrorBody(response, 404);
        _sessionRepo.Verify(r => r.SaveAsync(It.IsAny<GuestSession>()), Times.Never);
    }

    [Fact]
    public async Task OwnerSessionId_NotOwner_Is403AndNoSessionIsCreated()
    {
        var response = await OwnerSessionId(OtherUserId);

        AssertErrorBody(response, 403);
        _sessionRepo.Verify(r => r.SaveAsync(It.IsAny<GuestSession>()), Times.Never);
    }

    [Fact]
    public async Task OwnerSessionId_ReservationNotFound_Is404WithErrorBody()
    {
        _reservationRepo.Setup(r => r.GetReservationByIdAsync(ReservationId)).ReturnsAsync((Reservation?)null);

        var response = await OwnerSessionId(OwnerId);

        AssertErrorBody(response, 404);
    }

    [Fact]
    public async Task OwnerSessionId_GroupNotFound_Is404WithErrorBody()
    {
        _groupRepo.Setup(r => r.GetByReservationIdAsync(ReservationId)).ReturnsAsync((GroupReservation?)null);

        var response = await OwnerSessionId(OwnerId);

        AssertErrorBody(response, 404);
    }

    /// <summary>The lazily created owner session must not take a guest seat (capacity / status count guests only).</summary>
    [Fact]
    public async Task OwnerSessionId_CreatedOwnerSession_IsNotCountedInGroupStatus()
    {
        GuestSession? saved = null;
        _sessionRepo.Setup(r => r.SaveAsync(It.IsAny<GuestSession>())).Callback<GuestSession>(s => saved = s);
        var guest = Session("session1", "Andrii", isSubmitted: false);
        SetupSessions(guest);

        await OwnerSessionId(OwnerId);
        SetupSessions(guest, saved!);
        var status = await _guestSessionService.GetGroupReservationStatusAsync(GroupId);

        Assert.Equal(2, status.CurrentParticipants); // owner (+1) and one guest — not 3
    }

    // =====================================================================
    // POST /group-reservations/{groupReservationId}/submit
    // =====================================================================

    [Fact]
    public async Task Submit_Success_ResponseItemsHaveOnlyDishIdAndQuantity()
    {
        SetupSessions(
            Session("s1", "Andrii", isSubmitted: true, ("dish1", 2), ("dish2", 1)),
            Session("s2", "Olena", isSubmitted: true, ("dish1", 1), ("dish2", 1)));

        var response = await Submit(OwnerId);

        Assert.Equal(200, response.StatusCode);
        using var json = JsonDocument.Parse(response.Body);
        Assert.Equal(JsonValueKind.Array, json.RootElement.ValueKind);
        Assert.All(json.RootElement.EnumerateArray(), e => Assert.Equal(["dishId", "quantity"], Keys(e)));
        Assert.Equal("""[{"dishId":"dish1","quantity":3},{"dishId":"dish2","quantity":2}]""", response.Body);
    }

    [Fact]
    public async Task Submit_AlreadySubmitted_Is400WithContractMessageAndErrorCode()
    {
        _group.Status = GroupReservationStatus.Submitted;

        var response = await Submit(OwnerId);

        var body = AssertErrorCodeBody(response, "GroupReservationSubmitted");
        Assert.Equal("Group pre-order has already been submitted.", body.GetProperty("message").GetString());
        VerifyNothingSaved();
    }

    [Fact]
    public async Task Submit_GroupClosed_Is400WithClosedMessageAndExpiredErrorCode()
    {
        _group.Status = GroupReservationStatus.Closed;

        var response = await Submit(OwnerId);

        var body = AssertErrorCodeBody(response, "GroupReservationExpired");
        Assert.Equal("Group reservation is closed.", body.GetProperty("message").GetString());
        VerifyNothingSaved();
    }

    public static TheoryData<string, Action<GroupPreOrderContractTests>> BusinessRuleViolations() => new()
    {
        { "group belongs to another reservation", t => t._group.ReservationId = "another-reservation" },
        { "no sessions", t => t.SetupSessions() },
        { "no submitted sessions", t => t.SetupSessions(Session("s1", "Andrii", isSubmitted: false, ("dish1", 1))) },
        { "submitted sessions without dishes", t => t.SetupSessions(Session("s1", "Andrii", isSubmitted: true)) },
        { "no order", t => t._orderRepo.Setup(r => r.GetOrderByReservationIdAsync(ReservationId)).ReturnsAsync((Order?)null) },
        { "cancelled order", t => t._order.Status = OrderStatus.Cancelled },
    };

    [Theory]
    [MemberData(nameof(BusinessRuleViolations))]
    public async Task Submit_BusinessRuleViolation_Is400WithMessageAndUnknownErrorCode(string rule, Action<GroupPreOrderContractTests> arrange)
    {
        SetupSessions(Session("s1", "Andrii", isSubmitted: true, ("dish1", 1)));
        arrange(this);

        var response = await Submit(OwnerId);

        var body = AssertErrorCodeBody(response, "Unknown");
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("message").GetString()), rule);
        VerifyNothingSaved();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("{}")]
    [InlineData("not json")]
    public async Task Submit_InvalidBody_Is400WithErrorBody(string? body)
    {
        var response = await new SubmitGroupPreOrderHandler(_groupReservationService)
            .HandleRequest(Request(new() { ["groupReservationId"] = GroupId }, body), _context);

        AssertErrorBody(response, 400);
        VerifyNothingSaved();
    }

    [Fact]
    public async Task Submit_WithoutToken_Is401WithErrorBody()
    {
        var response = await new SubmitGroupPreOrderHandler(_groupReservationService)
            .HandleRequest(Request(new() { ["groupReservationId"] = GroupId }, SubmitBody(), userId: null), _context);

        AssertErrorBody(response, 401);
        VerifyNothingSaved();
    }

    [Fact]
    public async Task Submit_NotOwner_Is403WithErrorBody()
    {
        SetupSessions(Session("s1", "Andrii", isSubmitted: true, ("dish1", 1)));

        var response = await Submit(OtherUserId);

        AssertErrorBody(response, 403);
        VerifyNothingSaved();
    }

    [Fact]
    public async Task Submit_ReservationWithoutCustomer_Is403()
    {
        _reservation.CustomerId = null;
        SetupSessions(Session("s1", "Andrii", isSubmitted: true, ("dish1", 1)));

        var response = await Submit(OwnerId);

        AssertErrorBody(response, 403);
        VerifyNothingSaved();
    }

    /// <summary>Check order: 403 owner is checked before the 400 status check — a stranger never learns the group status.</summary>
    [Theory]
    [InlineData(GroupReservationStatus.Submitted)]
    [InlineData(GroupReservationStatus.Closed)]
    public async Task Submit_NotOwnerOfNotOpenGroup_Is403Not400(GroupReservationStatus status)
    {
        _group.Status = status;

        var response = await Submit(OtherUserId);

        AssertErrorBody(response, 403);
    }

    [Fact]
    public async Task Submit_ReservationNotFound_Is404WithErrorBody()
    {
        _reservationRepo.Setup(r => r.GetReservationByIdAsync(ReservationId)).ReturnsAsync((Reservation?)null);

        var response = await Submit(OwnerId);

        AssertErrorBody(response, 404);
        VerifyNothingSaved();
    }

    [Fact]
    public async Task Submit_GroupNotFound_Is404WithErrorBody()
    {
        _groupRepo.Setup(r => r.GetGroupReservationByIdAsync(GroupId)).ReturnsAsync((GroupReservation?)null);

        var response = await Submit(OwnerId);

        AssertErrorBody(response, 404);
        VerifyNothingSaved();
    }

    /// <summary>Quantity boundary: a line with quantity 0 is not a dish (N−1), quantity 1 is (N).</summary>
    [Theory]
    [InlineData(0, 400)]
    [InlineData(1, 200)]
    [InlineData(2, 200)]
    public async Task Submit_QuantityBoundary(int quantity, int expectedStatus)
    {
        SetupSessions(Session("s1", "Andrii", isSubmitted: true, ("dish1", quantity)));

        var response = await Submit(OwnerId);

        Assert.Equal(expectedStatus, response.StatusCode);
    }

    // Updated in round 2 (R1, option b): the group is now claimed (version-checked Open -> Submitted) BEFORE the order
    // write, so the write order is group -> order. The invariant for a failed order write is kept and still checked:
    // the group ends up Open (compensation write) and no order write succeeded.
    [Fact]
    public async Task Submit_ClaimsGroupBeforeOrder_AndGroupEndsOpenWhenOrderWriteFails()
    {
        SetupSessions(Session("s1", "Andrii", isSubmitted: true, ("dish1", 1)));
        var writes = new List<string>();
        _orderRepo.Setup(r => r.SaveOrderAsync(It.IsAny<Order>())).Callback(() => writes.Add("order"));
        _groupRepo.Setup(r => r.SaveGroupReservationAsync(It.IsAny<GroupReservation>()))
            .Callback((GroupReservation g) => writes.Add($"group:{g.Status}"));

        await Submit(OwnerId);
        Assert.Equal(["group:Submitted", "order"], writes);

        // second scenario: the order write fails → the group must end up Open again, no order write succeeded
        _group.Status = GroupReservationStatus.Open;
        writes.Clear();
        _orderRepo.Setup(r => r.SaveOrderAsync(It.IsAny<Order>())).ThrowsAsync(new InvalidOperationException("dynamo down"));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _groupReservationService.SubmitGroupPreOrderAsync(OwnerId, GroupId, ReservationId));
        Assert.Equal(["group:Submitted", "group:Open"], writes);
        Assert.DoesNotContain("order", writes);
        Assert.Equal(GroupReservationStatus.Open, _group.Status);
    }

    [Fact]
    public async Task Submit_DoesNotTouchSessionsOrOwnerOrderLinesOutsideMergedDishes()
    {
        _order.Dishes = [new OrderItem { DishId = "owner-dish", Quantity = 1, State = OrderItemState.Confirmed }];
        SetupSessions(Session("s1", "Andrii", isSubmitted: true, ("dish1", 1)));

        var response = await Submit(OwnerId);

        Assert.Equal(200, response.StatusCode);
        var ownerLine = _order.Dishes.Single(d => d.DishId == "owner-dish");
        Assert.Equal(1, ownerLine.Quantity);
        Assert.Equal(OrderItemState.Confirmed, ownerLine.State); // untouched line keeps its state
        Assert.Equal(OrderStatus.SubmittedByCustomer, _order.Status);
        _sessionRepo.Verify(r => r.SaveAsync(It.IsAny<GuestSession>()), Times.Never);
        _sessionRepo.Verify(r => r.DeleteAsync(It.IsAny<GuestSession>()), Times.Never);
    }

    // =====================================================================
    // Routing + deployment_resources.json (the "3 places" rule)
    // =====================================================================

    public static TheoryData<string, string, string, string> GroupPreOrderRoutes() => new()
    {
        { "GET", "/guest/groups/{groupReservationId}/sessions", "/guest/groups/g-1/sessions", "groupReservationId=g-1" },
        { "GET", "/reservations/{reservationId}/guest-session-id", "/reservations/r-1/guest-session-id", "reservationId=r-1" },
        { "POST", "/group-reservations/{groupReservationId}/submit", "/group-reservations/g-1/submit", "groupReservationId=g-1" },
        { "DELETE", "/guest/groups/{groupReservationId}/sessions/{guestSessionId}", "/guest/groups/g-1/sessions/owner-g-1", "groupReservationId=g-1;guestSessionId=owner-g-1" },
    };

    [Theory]
    [MemberData(nameof(GroupPreOrderRoutes))]
    public void Route_TemplateMatchesConcretePath(string method, string template, string path, string expectedParams)
    {
        var route = new RouteHandler(new Route(method, template), Mock.Of<APIHandler.Handlers.IApiRequestHandler>());

        var parameters = route.Match(path);

        Assert.NotNull(parameters);
        var expected = expectedParams.Split(';').Select(p => p.Split('=')).ToDictionary(p => p[0], p => p[1]);
        Assert.Equal(expected, parameters);
    }

    [Fact]
    public void Route_SessionsAndSessionByIdDoNotShadowEachOther()
    {
        var list = new RouteHandler(new Route("GET", "/guest/groups/{groupReservationId}/sessions"), Mock.Of<APIHandler.Handlers.IApiRequestHandler>());
        var byId = new RouteHandler(new Route("DELETE", "/guest/groups/{groupReservationId}/sessions/{guestSessionId}"), Mock.Of<APIHandler.Handlers.IApiRequestHandler>());
        var reservationById = new RouteHandler(new Route("GET", "/reservations/{reservationId}"), Mock.Of<APIHandler.Handlers.IApiRequestHandler>());

        Assert.Null(list.Match("/guest/groups/g-1/sessions/s-1"));
        Assert.Null(byId.Match("/guest/groups/g-1/sessions"));
        Assert.Null(reservationById.Match("/reservations/r-1/guest-session-id"));
    }

    public static TheoryData<string, string> GroupPreOrderEndpoints() => new()
    {
        { "GET", "/guest/groups/{groupReservationId}/sessions" },
        { "GET", "/reservations/{reservationId}/guest-session-id" },
        { "POST", "/group-reservations/{groupReservationId}/submit" },
        { "DELETE", "/guest/groups/{groupReservationId}/sessions/{guestSessionId}" },
    };

    [Theory]
    [MemberData(nameof(GroupPreOrderEndpoints))]
    public void DeploymentResources_EndpointIsBehindCognitoAndProxiedToApiHandler(string method, string template)
    {
        using var json = JsonDocument.Parse(File.ReadAllText(FindRepoFile("deployment_resources.json")));
        var api = json.RootElement.EnumerateObject()
            .Select(p => p.Value)
            .Single(v => v.TryGetProperty("resource_type", out var t) && t.GetString() == "api_gateway");

        Assert.True(api.GetProperty("resources").TryGetProperty(template, out var resource), $"{template} is missing in deployment_resources.json");
        Assert.True(resource.GetProperty("enable_cors").GetProperty("state").GetBoolean());
        Assert.True(resource.TryGetProperty(method, out var endpoint), $"{method} {template} is missing in deployment_resources.json");
        Assert.Equal("cognito_authorizer", endpoint.GetProperty("authorization_type").GetString());
        Assert.Equal("api-handler", endpoint.GetProperty("lambda_name").GetString());
        Assert.True(endpoint.GetProperty("enable_proxy").GetBoolean());
    }

    [Theory]
    [MemberData(nameof(GroupPreOrderEndpoints))]
    public void RouteRegistry_RegistersEndpoint(string method, string template)
    {
        var source = File.ReadAllText(FindRepoFile(Path.Combine("dnapp", "lambdas", "api-handler", "Routing", "RouteRegistry.cs")));

        Assert.Matches(new Regex($@"new Route\(HttpMethods\.{method},\s*""{Regex.Escape(template)}""\)"), source);
    }

    // =====================================================================
    // helpers
    // =====================================================================

    private Task<APIGatewayProxyResponse> GetSessions(string userId) =>
        new GetGroupSessionsHandler(_guestSessionService).HandleRequest(Request(new() { ["groupReservationId"] = GroupId }, userId: userId), _context);

    private Task<APIGatewayProxyResponse> Delete(string userId, string guestSessionId) =>
        new DeleteGuestSessionHandler(_guestSessionService).HandleRequest(
            Request(new() { ["groupReservationId"] = GroupId, ["guestSessionId"] = guestSessionId }, userId: userId), _context);

    private Task<APIGatewayProxyResponse> OwnerSessionId(string userId) =>
        new GetOwnerGuestSessionIdHandler(_groupReservationService).HandleRequest(Request(new() { ["reservationId"] = ReservationId }, userId: userId), _context);

    private Task<APIGatewayProxyResponse> Submit(string userId) =>
        new SubmitGroupPreOrderHandler(_groupReservationService).HandleRequest(
            Request(new() { ["groupReservationId"] = GroupId }, SubmitBody(), userId), _context);

    private static string SubmitBody() => $$"""{ "reservationId": "{{ReservationId}}" }""";

    private void SetupSessions(params GuestSession[] sessions) =>
        _sessionRepo.Setup(r => r.GetAllByGroupReservationIdAsync(GroupId)).ReturnsAsync(sessions.ToList());

    private void VerifyNothingSaved()
    {
        _orderRepo.Verify(r => r.SaveOrderAsync(It.IsAny<Order>()), Times.Never);
        _groupRepo.Verify(r => r.SaveGroupReservationAsync(It.IsAny<GroupReservation>()), Times.Never);
    }

    /// <summary>401/403/404 and handler validation: exactly <c>{ "error": "…" }</c>.</summary>
    private static void AssertErrorBody(APIGatewayProxyResponse response, int expectedStatus)
    {
        Assert.Equal(expectedStatus, response.StatusCode);
        using var json = JsonDocument.Parse(response.Body);
        Assert.Equal(["error"], Keys(json.RootElement));
        Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("error").GetString()));
    }

    /// <summary>400 from business rules: exactly <c>{ "message": "…", "errorCode": "…" }</c>.</summary>
    private static JsonElement AssertErrorCodeBody(APIGatewayProxyResponse response, string expectedErrorCode)
    {
        Assert.Equal(400, response.StatusCode);
        var root = JsonDocument.Parse(response.Body).RootElement;
        Assert.Equal(["errorCode", "message"], Keys(root));
        Assert.Equal(expectedErrorCode, root.GetProperty("errorCode").GetString());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("message").GetString()));
        return root;
    }

    private static string[] Keys(JsonElement element) =>
        element.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();

    private static GuestSession Session(string id, string name, bool isSubmitted, params (string DishId, int Quantity)[] dishes) => new()
    {
        Id = id,
        GroupReservationId = GroupId,
        GuestName = name,
        IsSubmitted = isSubmitted,
        Dishes = dishes.Select(d => new OrderItem { DishId = d.DishId, Quantity = d.Quantity }).ToList(),
    };

    private static APIGatewayProxyRequest Request(Dictionary<string, string> pathParameters, string? body = null, string? userId = OwnerId)
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

    /// <summary>Walks up from the test bin folder to backend/restaurant-backend-app.</summary>
    private static string FindRepoFile(string relativePath)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, relativePath);
            if (File.Exists(candidate) && File.Exists(Path.Combine(dir.FullName, "deployment_resources.json")))
                return candidate;
        }

        throw new FileNotFoundException($"{relativePath} not found above {AppContext.BaseDirectory}");
    }
}
