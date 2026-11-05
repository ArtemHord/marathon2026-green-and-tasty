using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using APIHandler.Constants;
using APIHandler.Constants.Orders;
using APIHandler.Entities;
using APIHandler.Exceptions;
using APIHandler.Handlers.Reservations;
using APIHandler.Helpers;
using APIHandler.Models.GuestSessions;
using APIHandler.Models.Orders;
using APIHandler.Repositories.Dishes;
using APIHandler.Repositories.GroupReservations;
using APIHandler.Repositories.GuestSessions;
using APIHandler.Repositories.Orders;
using APIHandler.Repositories.Reservations;
using APIHandler.Repositories.Tables;
using APIHandler.Services.Auth;
using APIHandler.Services.GroupReservations;
using APIHandler.Services.Locations;
using APIHandler.Services.Orders;
using Moq;
using System.Text.Json;

namespace APIHandler.Tests.Services;

/// <summary>
/// GET /reservations/{reservationId}/bill-split: access (owner customer or assigned waiter), 401/403/404,
/// participants from OrderItem.Guests, Canceled lines excluded and counted, current prices, old orders without Guests.
/// </summary>
public class BillSplitServiceTests
{
    private const string OwnerId = "owner-user";
    private const string WaiterId = "waiter-1";
    private const string ReservationId = "reservation-1";
    private const string GroupId = "group-1";
    private static readonly string OwnerShareId = GuestSessionIds.ForOwner(GroupId);

    private readonly Mock<IReservationRepository> _reservationRepo = new();
    private readonly Mock<IOrderRepository> _orderRepo = new();
    private readonly Mock<IGroupReservationRepository> _groupRepo = new();
    private readonly Mock<IDishRepository> _dishRepo = new();
    private readonly Mock<IUserService> _userService = new();

    private readonly Reservation _reservation = new() { Id = ReservationId, TableId = "t-1", WaiterId = WaiterId, CustomerId = OwnerId, Status = ReservationStatus.Reserved, GuestCount = 4 };
    private readonly GroupReservation _group = new() { GroupReservationId = GroupId, ReservationId = ReservationId, ReservationToken = "tok", Status = GroupReservationStatus.Submitted };
    private readonly Order _order = new() { OrderId = "order-1", CustomerId = OwnerId, ReservationId = ReservationId, ReservationAddress = "a", ReservationTime = "t", Status = OrderStatus.SubmittedByCustomer, Dishes = [] };

    private static readonly Dish Caesar = new() { Id = "caesar", Name = "Caesar Salad", Price = 180 };
    private static readonly Dish TomYum = new() { Id = "tomyum", Name = "Tom Yum", Price = 220 };
    private static readonly Dish Steak = new() { Id = "steak", Name = "Steak", Price = 12.5m };

    private readonly List<Dish> _catalog = [Caesar, TomYum, Steak];

    public BillSplitServiceTests()
    {
        _reservationRepo.Setup(r => r.GetReservationByIdAsync(ReservationId)).ReturnsAsync(_reservation);
        _orderRepo.Setup(r => r.GetOrderByReservationIdAsync(ReservationId)).ReturnsAsync(_order);
        _groupRepo.Setup(r => r.GetByReservationIdAsync(ReservationId)).ReturnsAsync(_group);
        _groupRepo.Setup(r => r.GetGroupReservationByIdAsync(GroupId)).ReturnsAsync(_group);
        _dishRepo.Setup(r => r.GetDishesByIdsAsync(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync((IEnumerable<string> ids) => _catalog.Where(d => ids.Contains(d.Id)).ToList());
        _userService.Setup(s => s.GetUserName(OwnerId)).ReturnsAsync("Yevhenii");
    }

    private BillSplitService Service() => new(_reservationRepo.Object, _orderRepo.Object, _groupRepo.Object, _dishRepo.Object, _userService.Object);

    private Task<BillSplitResponse> Split(string callerId = OwnerId) => Service().GetBillSplitAsync(callerId, ReservationId);

    // ---------- access ----------

    [Fact]
    public async Task OwnerCustomer_GetsTheSplit()
    {
        _order.Dishes = [Line("caesar", 1)];

        var split = await Split(OwnerId);

        Assert.Equal(180, split.Total);
    }

    [Fact]
    public async Task AssignedWaiter_GetsTheSplit()
    {
        _order.Dishes = [Line("caesar", 1)];

        var split = await Split(WaiterId);

        Assert.Equal(180, split.Total);
    }

    [Fact]
    public async Task AssignedWaiter_IdComparedCaseInsensitively()
    {
        _order.Dishes = [Line("caesar", 1)];

        var split = await Split(WaiterId.ToUpperInvariant());

        Assert.Equal(180, split.Total);
    }

    [Theory]
    [InlineData("other-customer")]
    [InlineData("other-waiter")]
    [InlineData("")]
    public async Task NeitherOwnerNorAssignedWaiter_Forbidden(string callerId)
    {
        _order.Dishes = [Line("caesar", 1)];

        var ex = await Assert.ThrowsAsync<ActionForbiddenException>(() => Split(callerId));

        Assert.Equal(BillSplitService.ForbiddenMessage, ex.Message);
    }

    [Fact]
    public async Task Forbidden_IsCheckedBeforeTheOrder()
    {
        _orderRepo.Setup(r => r.GetOrderByReservationIdAsync(ReservationId)).ReturnsAsync((Order?)null);

        await Assert.ThrowsAsync<ActionForbiddenException>(() => Split("other-customer"));
        _orderRepo.Verify(r => r.GetOrderByReservationIdAsync(It.IsAny<string>()), Times.Never);
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("", null)]
    [InlineData(null, null)]
    public async Task ReservationWithoutCustomerOrWaiter_EmptyIdsDoNotMatchAnyone(string? customerId, string? waiterId)
    {
        _reservation.CustomerId = customerId;
        _reservation.WaiterId = waiterId!;

        await Assert.ThrowsAsync<ActionForbiddenException>(() => Split(""));
    }

    // ---------- 404 ----------

    [Fact]
    public async Task NoReservation_NotFound()
    {
        await Assert.ThrowsAsync<ReservationNotFoundException>(() => Service().GetBillSplitAsync(OwnerId, "missing"));
    }

    [Fact]
    public async Task NoOrder_NotFound()
    {
        _orderRepo.Setup(r => r.GetOrderByReservationIdAsync(ReservationId)).ReturnsAsync((Order?)null);

        await Assert.ThrowsAsync<OrderNotFoundException>(() => Split());
    }

    [Fact]
    public async Task CancelledOrder_NotFound()
    {
        _order.Status = OrderStatus.Cancelled;
        _order.Dishes = [Line("caesar", 1)];

        await Assert.ThrowsAsync<OrderNotFoundException>(() => Split(WaiterId));
    }

    // ---------- old orders / regular pre-order ----------

    [Fact]
    public async Task OldOrderWithoutGuests_OneOwnerParticipantWithAllLines()
    {
        _order.Dishes = [Line("caesar", 2), Line("tomyum", 1, OrderItemState.Confirmed)];

        var split = await Split();

        var owner = Assert.Single(split.Participants);
        Assert.Equal((OwnerShareId, "Yevhenii", ParticipantType.Owner), (owner.GuestSessionId, owner.Name, owner.Type));
        Assert.Equal([("caesar", "Caesar Salad", 2, 180m, 360m), ("tomyum", "Tom Yum", 1, 220m, 220m)],
            owner.Items.Select(i => (i.DishId, i.Name, i.Quantity, i.Price, i.Subtotal)).ToList());
        Assert.Equal(580, owner.Total);
        Assert.Equal((580m, 0), (split.Total, split.ExcludedCanceled));
    }

    [Fact]
    public async Task ReservationWithoutGroup_OwnerGuestSessionIdIsNull()
    {
        _groupRepo.Setup(r => r.GetByReservationIdAsync(ReservationId)).ReturnsAsync((GroupReservation?)null);
        _order.Dishes = [Line("caesar", 1)];

        var owner = Assert.Single((await Split()).Participants);

        Assert.Null(owner.GuestSessionId);
        Assert.Equal(ParticipantType.Owner, owner.Type);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task OwnerName_UnknownInCognito_IsOwner(string? cognitoName)
    {
        _userService.Setup(s => s.GetUserName(OwnerId)).ReturnsAsync(cognitoName);
        _order.Dishes = [Line("caesar", 1)];

        Assert.Equal("Owner", Assert.Single((await Split()).Participants).Name);
    }

    [Fact]
    public async Task OwnerName_ReservationWithoutCustomer_IsOwner_NoLookup()
    {
        _reservation.CustomerId = null;
        _order.Dishes = [Line("caesar", 1)];

        Assert.Equal("Owner", Assert.Single((await Split(WaiterId)).Participants).Name);
        _userService.Verify(s => s.GetUserName(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task OwnerName_LookupThrows_IsOwner()
    {
        _userService.Setup(s => s.GetUserName(OwnerId)).ThrowsAsync(new InvalidOperationException("cognito down"));
        _order.Dishes = [Line("caesar", 1)];

        Assert.Equal("Owner", Assert.Single((await Split()).Participants).Name);
    }

    [Fact]
    public async Task OrderWithoutLines_NoParticipantsZeroTotal()
    {
        var split = await Split();

        Assert.Empty(split.Participants);
        Assert.Equal((0m, 0), (split.Total, split.ExcludedCanceled));
        _dishRepo.Verify(r => r.GetDishesByIdsAsync(It.IsAny<IEnumerable<string>>()), Times.Never);
    }

    // ---------- participants from Guests ----------

    [Fact]
    public async Task GroupOrder_OwnerFirstThenGuestsInOrderOfFirstAppearance()
    {
        _order.Dishes =
        [
            Line("tomyum", 2, shares: [("s-ivan", "Ivan", 1), (OwnerShareId, "Yevhenii", 1)]),
            Line("caesar", 3, shares: [("s-andrii", "Andrii", 2), ("s-olena", "Olena", 1)]),
        ];

        var split = await Split();

        Assert.Equal([(OwnerShareId, "Owner"), ("s-ivan", "Guest"), ("s-andrii", "Guest"), ("s-olena", "Guest")],
            split.Participants.Select(p => (p.GuestSessionId, p.Type.ToString())).ToList());
    }

    [Fact]
    public async Task Demo_OneDishFromThreeGuestsAndOwner_TotalsAddUp()
    {
        // Owner: Steak ×1 (own line) + Caesar ×1 (owner share); Andrii Caesar ×2; Olena Caesar ×1; Ivan Tom Yum ×1
        _order.Dishes =
        [
            Line("steak", 1),
            Line("caesar", 4, shares: [(OwnerShareId, "Yevhenii", 1), ("s-andrii", "Andrii", 2), ("s-olena", "Olena", 1)]),
            Line("tomyum", 1, shares: [("s-ivan", "Ivan", 1)]),
        ];

        var split = await Split();

        var byName = split.Participants.ToDictionary(p => p.Name);
        Assert.Equal(["Yevhenii", "Andrii", "Olena", "Ivan"], split.Participants.Select(p => p.Name).ToList());
        Assert.Equal([("steak", 1, 12.5m), ("caesar", 1, 180m)], byName["Yevhenii"].Items.Select(i => (i.DishId, i.Quantity, i.Subtotal)).ToList());
        Assert.Equal(192.5m, byName["Yevhenii"].Total);
        Assert.Equal(360, byName["Andrii"].Total);
        Assert.Equal(180, byName["Olena"].Total);
        Assert.Equal(220, byName["Ivan"].Total);
        Assert.Equal(952.5m, split.Total);
        Assert.All(split.Participants, p => Assert.Equal(p.Items.Sum(i => i.Subtotal), p.Total));
        Assert.All(split.Participants.SelectMany(p => p.Items), i => Assert.Equal(i.Price * i.Quantity, i.Subtotal));
        // the split adds up to the whole order
        Assert.Equal(_order.Dishes.Sum(l => l.Quantity * _catalog.Single(d => d.Id == l.DishId).Price), split.Total);
    }

    [Fact]
    public async Task OwnerNameFromTheOwnerShare_NoCognitoLookup()
    {
        _order.Dishes = [Line("caesar", 2, shares: [(OwnerShareId, "Yevhenii K.", 1), ("s-andrii", "Andrii", 1)])];

        var owner = (await Split()).Participants[0];

        Assert.Equal("Yevhenii K.", owner.Name);
        _userService.Verify(s => s.GetUserName(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task GuestWithSeveralLinesOfTheSameDish_ItemsMergedByDish()
    {
        _order.Dishes =
        [
            Line("caesar", 1, shares: [("s-andrii", "Andrii", 1)]),
            Line("caesar", 2, shares: [("s-andrii", "Andrii", 2)]),
        ];

        var item = Assert.Single(Assert.Single((await Split()).Participants).Items);

        Assert.Equal((3, 540m), (item.Quantity, item.Subtotal));
    }

    [Fact]
    public async Task OnlyGuestsOrdered_NoOwnerParticipant()
    {
        _order.Dishes = [Line("caesar", 2, shares: [("s-andrii", "Andrii", 2)])];

        var participant = Assert.Single((await Split()).Participants);

        Assert.Equal(ParticipantType.Guest, participant.Type);
    }

    [Fact]
    public async Task SharesCoverLessThanTheLine_RemainderIsTheOwners()
    {
        _order.Dishes = [Line("caesar", 3, shares: [("s-andrii", "Andrii", 1)])];

        var split = await Split();

        Assert.Equal(360, split.Participants.Single(p => p.Type == ParticipantType.Owner).Total);
        Assert.Equal(180, split.Participants.Single(p => p.Type == ParticipantType.Guest).Total);
        Assert.Equal(540, split.Total);
    }

    [Fact]
    public async Task SharesCoverMoreThanTheLine_CappedAtTheLineQuantity()
    {
        _order.Dishes = [Line("caesar", 2, shares: [("s-andrii", "Andrii", 2), ("s-olena", "Olena", 5)])];

        var split = await Split();

        var andrii = Assert.Single(split.Participants);
        Assert.Equal(("Andrii", 360m), (andrii.Name, andrii.Total));
        Assert.Equal(360, split.Total);
    }

    [Fact]
    public async Task ZeroQuantityShares_AndLines_AreIgnored()
    {
        _order.Dishes =
        [
            Line("caesar", 1, shares: [("s-olena", "Olena", 0), ("s-andrii", "Andrii", 1)]),
            Line("tomyum", 0),
        ];

        var split = await Split();

        Assert.Equal(["Andrii"], split.Participants.Select(p => p.Name).ToList());
        Assert.Equal(180, split.Total);
    }

    // ---------- Canceled lines ----------

    [Fact]
    public async Task CanceledLine_ExcludedFromSumsAndItems_CountedInExcludedCanceled()
    {
        _order.Dishes =
        [
            Line("caesar", 3, OrderItemState.Confirmed, shares: [("s-andrii", "Andrii", 2), ("s-olena", "Olena", 1)]),
            Line("tomyum", 2, OrderItemState.Canceled, shares: [(OwnerShareId, "Yevhenii", 1), ("s-ivan", "Ivan", 1)]),
        ];

        var split = await Split();

        Assert.Equal(1, split.ExcludedCanceled);
        Assert.Equal(540, split.Total);
        Assert.DoesNotContain(split.Participants.SelectMany(p => p.Items), i => i.DishId == "tomyum");
    }

    [Fact]
    public async Task ParticipantWithOnlyCanceledDishes_StaysWithZeroTotal()
    {
        _order.Dishes =
        [
            Line("caesar", 2, shares: [("s-andrii", "Andrii", 2)]),
            Line("tomyum", 1, OrderItemState.Canceled, shares: [("s-ivan", "Ivan", 1)]),
        ];

        var split = await Split();

        var ivan = split.Participants.Single(p => p.Name == "Ivan");
        Assert.Empty(ivan.Items);
        Assert.Equal(0, ivan.Total);
    }

    [Fact]
    public async Task AllLinesCanceled_ZeroTotal_AllCounted()
    {
        _order.Dishes = [Line("caesar", 1, OrderItemState.Canceled), Line("tomyum", 2, OrderItemState.Canceled), Line("steak", 0, OrderItemState.Canceled)];

        var split = await Split();

        Assert.Equal(3, split.ExcludedCanceled); // every Canceled line of the order, also with quantity 0
        Assert.Equal(0, split.Total);
        Assert.All(split.Participants, p => Assert.Empty(p.Items));
        _dishRepo.Verify(r => r.GetDishesByIdsAsync(It.IsAny<IEnumerable<string>>()), Times.Never);
    }

    [Fact]
    public async Task NoCanceledLines_ExcludedCanceledIsZero()
    {
        _order.Dishes = [Line("caesar", 1, OrderItemState.Pending), Line("tomyum", 1, OrderItemState.Confirmed)];

        Assert.Equal(0, (await Split()).ExcludedCanceled);
    }

    // ---------- prices / catalog ----------

    [Fact]
    public async Task Prices_AreTheCurrentOnesFromDishes()
    {
        _catalog[0] = new Dish { Id = "caesar", Name = "Caesar Salad", Price = 200 };
        _order.Dishes = [Line("caesar", 2)];

        var item = Assert.Single(Assert.Single((await Split()).Participants).Items);

        Assert.Equal((200m, 400m), (item.Price, item.Subtotal));
    }

    [Fact]
    public async Task DishDeletedFromCatalog_Skipped()
    {
        _order.Dishes = [Line("caesar", 1), Line("deleted", 3, shares: [("s-andrii", "Andrii", 3)])];

        var split = await Split();

        Assert.Equal(180, split.Total);
        Assert.DoesNotContain(split.Participants.SelectMany(p => p.Items), i => i.DishId == "deleted");
    }

    // ---------- end to end: group submit -> bill split ----------

    [Fact]
    public async Task AfterGroupSubmit_SplitMatchesWhoOrderedWhat_AndAddsUpToTheOrder()
    {
        _group.Status = GroupReservationStatus.Open;
        _order.Status = OrderStatus.Created;
        _order.Dishes = [Line("steak", 1), Line("tomyum", 1)];
        var sessions = new Mock<IGuestSessionRepository>();
        sessions.Setup(r => r.GetAllByGroupReservationIdAsync(GroupId)).ReturnsAsync(
        [
            Session("s-andrii", "Andrii", true, ("caesar", 2)),
            Session("s-olena", "Olena", true, ("caesar", 1)),
            Session("s-ivan", "Ivan", true, ("tomyum", 1)),
            Session("s-taras", "Taras", false, ("caesar", 7)),
        ]);
        var groups = new GroupReservationService(_groupRepo.Object, new Mock<ITableRepository>().Object, _reservationRepo.Object,
            sessions.Object, _userService.Object, new Mock<ILocationService>().Object, _orderRepo.Object);

        await groups.SubmitGroupPreOrderAsync(OwnerId, GroupId, ReservationId);
        var split = await Split(WaiterId);

        Assert.Equal(
            // Owner first; guests in order of first appearance in the order lines: tomyum (existing line) before caesar (added)
            [("Yevhenii", 12.5m + 220m), ("Ivan", 220m), ("Andrii", 360m), ("Olena", 180m)],
            split.Participants.Select(p => (p.Name, p.Total)).ToList());
        Assert.Equal(_order.Dishes.Sum(l => l.Quantity * _catalog.Single(d => d.Id == l.DishId).Price), split.Total);
    }

    // ---------- handler ----------

    [Fact]
    public async Task Handler_Success_Returns200CamelCaseContract()
    {
        _order.Dishes = [Line("caesar", 2, shares: [("s-andrii", "Andrii", 2)]), Line("steak", 1, OrderItemState.Canceled)];
        _groupRepo.Setup(r => r.GetByReservationIdAsync(ReservationId)).ReturnsAsync((GroupReservation?)null);
        _order.Dishes.Add(Line("tomyum", 1));

        var response = await new GetBillSplitHandler(Service()).HandleRequest(Request(OwnerId), Context());

        Assert.Equal(200, response.StatusCode);
        using var json = JsonDocument.Parse(response.Body);
        var root = json.RootElement;
        Assert.Equal(["participants", "total", "excludedCanceled"], root.EnumerateObject().Select(p => p.Name).ToList());
        Assert.Equal(580, root.GetProperty("total").GetDecimal());
        Assert.Equal(1, root.GetProperty("excludedCanceled").GetInt32());

        var owner = root.GetProperty("participants")[0];
        Assert.Equal(["guestSessionId", "name", "type", "items", "total"], owner.EnumerateObject().Select(p => p.Name).ToList());
        Assert.Equal(JsonValueKind.Null, owner.GetProperty("guestSessionId").ValueKind);
        Assert.Equal("Owner", owner.GetProperty("type").GetString());

        var guest = root.GetProperty("participants")[1];
        Assert.Equal("s-andrii", guest.GetProperty("guestSessionId").GetString());
        Assert.Equal("Guest", guest.GetProperty("type").GetString());
        Assert.Equal("Andrii", guest.GetProperty("name").GetString());
        Assert.Equal(360, guest.GetProperty("total").GetDecimal());
        var item = guest.GetProperty("items")[0];
        Assert.Equal(["dishId", "name", "quantity", "price", "subtotal"], item.EnumerateObject().Select(p => p.Name).ToList());
        Assert.Equal(("caesar", "Caesar Salad", 2, 180m, 360m),
            (item.GetProperty("dishId").GetString(), item.GetProperty("name").GetString(), item.GetProperty("quantity").GetInt32(),
             item.GetProperty("price").GetDecimal(), item.GetProperty("subtotal").GetDecimal()));
    }

    [Fact]
    public async Task Handler_AssignedWaiter_Returns200()
    {
        _order.Dishes = [Line("caesar", 1)];

        var response = await new GetBillSplitHandler(Service()).HandleRequest(Request(WaiterId), Context());

        Assert.Equal(200, response.StatusCode);
    }

    [Theory]
    [InlineData("other-customer")]
    [InlineData("other-waiter")]
    public async Task Handler_NotOwnerNorAssignedWaiter_Returns403(string callerId)
    {
        _order.Dishes = [Line("caesar", 1)];

        var response = await new GetBillSplitHandler(Service()).HandleRequest(Request(callerId), Context());

        Assert.Equal(403, response.StatusCode);
        Assert.Contains("error", response.Body);
    }

    [Fact]
    public async Task Handler_NoReservation_Returns404()
    {
        var response = await new GetBillSplitHandler(Service()).HandleRequest(Request(OwnerId, "missing"), Context());

        Assert.Equal(404, response.StatusCode);
    }

    [Fact]
    public async Task Handler_NoOrder_Returns404()
    {
        _orderRepo.Setup(r => r.GetOrderByReservationIdAsync(ReservationId)).ReturnsAsync((Order?)null);

        var response = await new GetBillSplitHandler(Service()).HandleRequest(Request(OwnerId), Context());

        Assert.Equal(404, response.StatusCode);
    }

    [Theory]
    [InlineData("no request context")]
    [InlineData("no authorizer")]
    [InlineData("no claims")]
    [InlineData("claims without sub")]
    [InlineData("empty sub")]
    public async Task Handler_WithoutSub_Returns401_ServiceNotCalled(string @case)
    {
        var request = new APIGatewayProxyRequest { PathParameters = new Dictionary<string, string> { ["reservationId"] = ReservationId } };
        request.RequestContext = @case switch
        {
            "no request context" => null,
            "no authorizer" => new APIGatewayProxyRequest.ProxyRequestContext(),
            "no claims" => new APIGatewayProxyRequest.ProxyRequestContext { Authorizer = new APIGatewayCustomAuthorizerContext() },
            "claims without sub" => new APIGatewayProxyRequest.ProxyRequestContext
            {
                Authorizer = new APIGatewayCustomAuthorizerContext { Claims = new Dictionary<string, string> { ["email"] = "x@y.z" } },
            },
            _ => new APIGatewayProxyRequest.ProxyRequestContext
            {
                Authorizer = new APIGatewayCustomAuthorizerContext { Claims = new Dictionary<string, string> { ["sub"] = "" } },
            },
        };
        var service = new Mock<IBillSplitService>();

        var response = await new GetBillSplitHandler(service.Object).HandleRequest(request, Context());

        Assert.Equal(401, response.StatusCode);
        service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handler_MissingReservationId_Returns400()
    {
        var service = new Mock<IBillSplitService>();
        var request = Request(OwnerId);
        request.PathParameters = new Dictionary<string, string>();

        var response = await new GetBillSplitHandler(service.Object).HandleRequest(request, Context());

        Assert.Equal(400, response.StatusCode);
        service.VerifyNoOtherCalls();
    }

    // ---------- helpers ----------

    private static OrderItem Line(string dishId, int quantity, OrderItemState state = OrderItemState.Pending,
        (string Id, string Name, int Quantity)[]? shares = null) => new()
    {
        DishId = dishId,
        Quantity = quantity,
        State = state,
        Guests = shares?.Select(s => new OrderItemGuestShare { GuestSessionId = s.Id, GuestName = s.Name, Quantity = s.Quantity }).ToList(),
    };

    private static GuestSession Session(string id, string name, bool isSubmitted, params (string DishId, int Quantity)[] dishes) => new()
    {
        Id = id, GroupReservationId = GroupId, GuestName = name, IsSubmitted = isSubmitted,
        Dishes = dishes.Select(d => new OrderItem { DishId = d.DishId, Quantity = d.Quantity }).ToList(),
    };

    private static APIGatewayProxyRequest Request(string userId, string reservationId = ReservationId) => new()
    {
        PathParameters = new Dictionary<string, string> { ["reservationId"] = reservationId },
        RequestContext = new APIGatewayProxyRequest.ProxyRequestContext
        {
            Authorizer = new APIGatewayCustomAuthorizerContext { Claims = new Dictionary<string, string> { ["sub"] = userId } },
        },
    };

    private static ILambdaContext Context()
    {
        var context = new Mock<ILambdaContext>();
        context.SetupGet(c => c.Logger).Returns(new Mock<ILambdaLogger>().Object);
        return context.Object;
    }
}
