using Amazon.CognitoIdentityProvider.Model;
using APIHandler.Constants;
using APIHandler.Constants.Orders;
using APIHandler.Entities;
using APIHandler.Exceptions;
using APIHandler.Models.GroupReservation;
using APIHandler.Models.GuestSessions;
using APIHandler.Repositories.Auth;
using APIHandler.Repositories.Dishes;
using APIHandler.Repositories.GroupReservations;
using APIHandler.Repositories.GuestSessions;
using APIHandler.Repositories.Orders;
using APIHandler.Repositories.Reservations;
using APIHandler.Repositories.Tables;
using APIHandler.Services.GroupReservations;
using APIHandler.Services.GuestSessions;
using Moq;
using System.Text.Json;

namespace APIHandler.Tests.Services;

/// <summary>
/// Owner functionality of the group pre-order (homework-backend.md):
/// L1 GET sessions snapshot, L2 owner session id + consolidated submit, L3 delete guest session.
/// </summary>
public class GroupPreOrderTests
{
    private const string OwnerId = "owner-user";
    private const string OtherUserId = "other-user";
    private const string GroupId = "group-1";
    private const string ReservationId = "reservation-1";

    private readonly Mock<IGuestSessionRepository> _guestSessions = new();
    private readonly Mock<IGroupReservationRepository> _groups = new();
    private readonly Mock<IDishRepository> _dishes = new();
    private readonly Mock<IReservationRepository> _reservations = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<ITableRepository> _tables = new();
    private readonly Mock<IOrderRepository> _orders = new();

    private readonly GuestSessionService _guestSessionService;
    private readonly GroupPreOrderService _groupPreOrderService;

    private readonly GroupReservation _group;
    private readonly Reservation _reservation;

    public GroupPreOrderTests()
    {
        _guestSessionService = new GuestSessionService(
            _groups.Object, _guestSessions.Object, _dishes.Object, _reservations.Object, _users.Object, _tables.Object);

        _groupPreOrderService = new GroupPreOrderService(
            _groups.Object, _reservations.Object, _guestSessions.Object, _orders.Object);

        _group = new GroupReservation
        {
            GroupReservationId = GroupId,
            ReservationId = ReservationId,
            ReservationToken = "token",
            Status = GroupReservationStatus.Open,
        };

        _reservation = new Reservation
        {
            Id = ReservationId,
            TableId = "table-1",
            WaiterId = "waiter-1",
            CustomerId = OwnerId,
            GuestCount = 5,
            Status = ReservationStatus.Reserved,
        };

        _groups.Setup(r => r.GetGroupReservationByIdAsync(GroupId)).ReturnsAsync(() => _group);
        _groups.Setup(r => r.GetByReservationIdAsync(ReservationId)).ReturnsAsync(() => _group);
        _reservations.Setup(r => r.GetReservationByIdAsync(ReservationId)).ReturnsAsync(() => _reservation);

        _dishes
            .Setup(r => r.GetDishesByIdsAsync(It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync((IEnumerable<string> ids) => (IEnumerable<Dish>)AllDishes.Where(d => ids.Contains(d.Id)).ToList());

        _users
            .Setup(r => r.GetUserByIdAsync(OwnerId))
            .ReturnsAsync(new UserType { Attributes = [new AttributeType { Name = "given_name", Value = "Andrii" }] });
    }

    private static readonly List<Dish> AllDishes =
    [
        new Dish { Id = "dish1", Name = "Caesar Salad", Price = 180 },
        new Dish { Id = "dish2", Name = "Tom Yum", Price = 220 },
        new Dish { Id = "dish3", Name = "Cheesecake", Price = 100 },
    ];

    private static GuestSession Session(string id, bool submitted, params (string DishId, int Quantity)[] dishes) => new()
    {
        Id = id,
        GroupReservationId = GroupId,
        GuestName = id,
        IsSubmitted = submitted,
        Dishes = dishes.Select(d => new OrderItem { DishId = d.DishId, Quantity = d.Quantity, State = OrderItemState.Pending }).ToList(),
    };

    private static GuestSession OwnerSession(params (string DishId, int Quantity)[] dishes)
    {
        var session = Session($"owner-{GroupId}", submitted: false, dishes);
        session.IsOwnerSession = true;
        session.OwnerUserId = OwnerId;
        session.GuestName = "Owner";
        return session;
    }

    private void GivenSessions(params GuestSession[] sessions) =>
        _guestSessions.Setup(r => r.GetAllByGroupReservationIdAsync(GroupId)).ReturnsAsync(sessions.ToList());

    private Order GivenOrder(params (string DishId, int Quantity)[] dishes)
    {
        var order = new Order
        {
            OrderId = "order-1",
            CustomerId = OwnerId,
            ReservationId = ReservationId,
            ReservationAddress = "48 Rustaveli Avenue, Table 1",
            ReservationTime = "Nov 1, 2026, 7:00 p.m. - 8:30 p.m.",
            Dishes = dishes.Select(d => new OrderItem { DishId = d.DishId, Quantity = d.Quantity }).ToList(),
            Status = OrderStatus.Created,
        };
        _orders.Setup(r => r.GetOrderByReservationIdAsync(ReservationId)).ReturnsAsync(order);
        return order;
    }

    // ---------- L1: GET /guest/groups/{id}/sessions ----------

    [Fact]
    public async Task GetGroupSessions_ReturnsParticipantsWithItemsAndTotals_OwnerFirst()
    {
        GivenSessions(
            Session("session1", submitted: true, ("dish1", 2)),
            Session("session2", submitted: false, ("dish2", 1)),
            OwnerSession(("dish3", 1)),
            Session("session3", submitted: false));

        var result = await _guestSessionService.GetGroupSessionsAsync(OwnerId, GroupId);
        var participants = result.Participants.ToList();

        Assert.Equal(4, participants.Count);

        Assert.Equal(ParticipantType.Owner, participants[0].Type);
        Assert.Equal("Andrii", participants[0].Name);
        Assert.Equal(100m, participants[0].Total);

        Assert.Equal("session1", participants[1].Id);
        Assert.Equal(ParticipantType.Guest, participants[1].Type);
        Assert.Equal(360m, participants[1].Total);
        Assert.True(participants[1].IsSubmitted);
        var item = Assert.Single(participants[1].Items);
        Assert.Equal("dish1", item.Dish.Id);
        Assert.Equal(2, item.Quantity);

        Assert.Equal(220m, participants[2].Total);
        Assert.False(participants[2].IsSubmitted);

        Assert.Empty(participants[3].Items);
        Assert.Equal(0m, participants[3].Total);

        Assert.Equal(100m + 360m + 220m, result.Total);
    }

    [Fact]
    public async Task GetGroupSessions_NobodyJoined_ReturnsEmptyListAndZeroTotal()
    {
        GivenSessions();

        var result = await _guestSessionService.GetGroupSessionsAsync(OwnerId, GroupId);

        Assert.Empty(result.Participants);
        Assert.Equal(0m, result.Total);
        _dishes.Verify(r => r.GetDishesByIdsAsync(It.IsAny<IEnumerable<string>>()), Times.Never);
    }

    [Fact]
    public async Task GetGroupSessions_DishRemovedFromMenu_IsSkippedInsteadOfFailing()
    {
        GivenSessions(Session("session1", submitted: true, ("dish1", 1), ("deleted-dish", 3)));

        var result = await _guestSessionService.GetGroupSessionsAsync(OwnerId, GroupId);

        var participant = Assert.Single(result.Participants);
        Assert.Single(participant.Items);
        Assert.Equal(180m, result.Total);
    }

    [Fact]
    public async Task GetGroupSessions_NotOwner_ThrowsForbidden()
    {
        GivenSessions();
        await Assert.ThrowsAsync<ActionForbiddenException>(() => _guestSessionService.GetGroupSessionsAsync(OtherUserId, GroupId));
    }

    [Fact]
    public async Task GetGroupSessions_UnknownGroup_ThrowsNotFound()
    {
        await Assert.ThrowsAsync<GroupReservationNotFoundException>(() => _guestSessionService.GetGroupSessionsAsync(OwnerId, "missing-group"));
    }

    [Fact]
    public void Snapshot_SerializesWithContractNames()
    {
        var snapshot = new GetAllSessionsResponse(
            [new Participant("session1", ParticipantType.Guest, "Olena", [], 0m, false)], 0m);

        string json = JsonSerializer.Serialize(snapshot);

        Assert.Contains("\"participants\":", json);
        Assert.Contains("\"type\":\"Guest\"", json);
        Assert.Contains("\"isSubmitted\":false", json);
        Assert.Contains("\"total\":0", json);
    }

    // ---------- L2: GET /reservations/{id}/guest-session-id ----------

    [Fact]
    public async Task GetOwnerSessionId_FirstCall_CreatesOwnerSession()
    {
        _guestSessions.Setup(r => r.GetByGuestSessionId($"owner-{GroupId}")).ReturnsAsync((GuestSession?)null);

        var result = await _guestSessionService.GetOwnerSessionIdAsync(OwnerId, ReservationId);

        Assert.Equal($"owner-{GroupId}", result.GuestSessionId);
        Assert.Equal(GroupId, result.GroupReservationId);
        _guestSessions.Verify(r => r.SaveAsync(It.Is<GuestSession>(s =>
            s.Id == $"owner-{GroupId}" && s.IsOwnerSession && s.OwnerUserId == OwnerId && s.GroupReservationId == GroupId)), Times.Once);
    }

    [Fact]
    public async Task GetOwnerSessionId_SessionExists_ReturnsSameIdWithoutCreating()
    {
        _guestSessions.Setup(r => r.GetByGuestSessionId($"owner-{GroupId}")).ReturnsAsync(OwnerSession());

        var result = await _guestSessionService.GetOwnerSessionIdAsync(OwnerId, ReservationId);

        Assert.Equal($"owner-{GroupId}", result.GuestSessionId);
        _guestSessions.Verify(r => r.SaveAsync(It.IsAny<GuestSession>()), Times.Never);
    }

    [Fact]
    public async Task GetOwnerSessionId_NotOwner_ThrowsForbidden()
    {
        await Assert.ThrowsAsync<ActionForbiddenException>(() => _guestSessionService.GetOwnerSessionIdAsync(OtherUserId, ReservationId));
    }

    [Fact]
    public async Task GetOwnerSessionId_UnknownReservation_ThrowsNotFound()
    {
        await Assert.ThrowsAsync<ReservationNotFoundException>(() => _guestSessionService.GetOwnerSessionIdAsync(OwnerId, "missing-reservation"));
    }

    // ---------- L2: POST /group-reservations/{id}/submit ----------

    [Fact]
    public async Task Submit_ConsolidatesOnlySubmittedSessions_AndUpdatesOrderAndGroup()
    {
        GivenSessions(
            Session("session1", submitted: true, ("dish1", 2), ("dish2", 1)),
            Session("session2", submitted: true, ("dish1", 1)),
            Session("session3", submitted: false, ("dish3", 5)));
        var order = GivenOrder(("dish1", 1));

        var result = await _groupPreOrderService.SubmitAsync(OwnerId, GroupId, ReservationId);

        Assert.Equal(new[] { new SubmittedDishResponse("dish1", 3), new SubmittedDishResponse("dish2", 1) }, result);

        Assert.Equal(OrderStatus.SubmittedByCustomer, order.Status);
        Assert.Equal(4, order.Dishes.Single(d => d.DishId == "dish1").Quantity); // 1 from the owner + 3 from guests
        Assert.Equal(1, order.Dishes.Single(d => d.DishId == "dish2").Quantity);
        Assert.DoesNotContain(order.Dishes, d => d.DishId == "dish3");
        _orders.Verify(r => r.SaveOrderAsync(order), Times.Once);

        Assert.Equal(GroupReservationStatus.Submitted, _group.Status);
        _groups.Verify(r => r.SaveGroupReservationAsync(_group), Times.Once);
    }

    [Fact]
    public async Task Submit_UnknownGroup_ThrowsNotFound()
    {
        await Assert.ThrowsAsync<GroupReservationNotFoundException>(() => _groupPreOrderService.SubmitAsync(OwnerId, "missing-group", ReservationId));
    }

    [Fact]
    public async Task Submit_UnknownReservation_ThrowsNotFound()
    {
        await Assert.ThrowsAsync<ReservationNotFoundException>(() => _groupPreOrderService.SubmitAsync(OwnerId, GroupId, "missing-reservation"));
    }

    [Fact]
    public async Task Submit_NotOwner_ThrowsForbidden_AndChangesNothing()
    {
        GivenSessions(Session("session1", submitted: true, ("dish1", 1)));
        GivenOrder();

        await Assert.ThrowsAsync<ActionForbiddenException>(() => _groupPreOrderService.SubmitAsync(OtherUserId, GroupId, ReservationId));
        AssertNothingSaved();
    }

    [Fact]
    public async Task Submit_ReservationOfAnotherGroup_ThrowsValidation()
    {
        var otherReservation = new Reservation { Id = "reservation-2", TableId = "t", WaiterId = "w", CustomerId = OwnerId };
        _reservations.Setup(r => r.GetReservationByIdAsync("reservation-2")).ReturnsAsync(otherReservation);

        var ex = await Assert.ThrowsAsync<ValidationException>(() => _groupPreOrderService.SubmitAsync(OwnerId, GroupId, "reservation-2"));
        Assert.Equal(GroupPreOrderService.ReservationMismatchMessage, ex.Message);
    }

    [Fact]
    public async Task Submit_SecondTime_ThrowsAlreadySubmitted()
    {
        _group.Status = GroupReservationStatus.Submitted;
        await Assert.ThrowsAsync<GroupReservationSubmittedException>(() => _groupPreOrderService.SubmitAsync(OwnerId, GroupId, ReservationId));
        AssertNothingSaved();
    }

    [Fact]
    public async Task Submit_ClosedGroup_ThrowsValidation()
    {
        _group.Status = GroupReservationStatus.Closed;
        await Assert.ThrowsAsync<ValidationException>(() => _groupPreOrderService.SubmitAsync(OwnerId, GroupId, ReservationId));
        AssertNothingSaved();
    }

    [Fact]
    public async Task Submit_NoOrder_ThrowsValidation()
    {
        GivenSessions(Session("session1", submitted: true, ("dish1", 1)));
        _orders.Setup(r => r.GetOrderByReservationIdAsync(ReservationId)).ReturnsAsync((Order?)null);

        var ex = await Assert.ThrowsAsync<ValidationException>(() => _groupPreOrderService.SubmitAsync(OwnerId, GroupId, ReservationId));
        Assert.Equal(GroupPreOrderService.OrderNotFoundMessage, ex.Message);
        AssertNothingSaved();
    }

    [Fact]
    public async Task Submit_NoSubmittedSessions_ThrowsValidation()
    {
        GivenSessions(Session("session1", submitted: false, ("dish1", 1)));
        GivenOrder();

        var ex = await Assert.ThrowsAsync<ValidationException>(() => _groupPreOrderService.SubmitAsync(OwnerId, GroupId, ReservationId));
        Assert.Equal(GroupPreOrderService.NoSubmittedSessionsMessage, ex.Message);
        AssertNothingSaved();
    }

    [Fact]
    public async Task Submit_SubmittedSessionsWithoutDishes_ThrowsValidation()
    {
        GivenSessions(Session("session1", submitted: true));
        GivenOrder();

        var ex = await Assert.ThrowsAsync<ValidationException>(() => _groupPreOrderService.SubmitAsync(OwnerId, GroupId, ReservationId));
        Assert.Equal(GroupPreOrderService.NoDishesMessage, ex.Message);
        AssertNothingSaved();
    }

    [Fact]
    public void SubmittedDish_SerializesWithContractNames()
    {
        string json = JsonSerializer.Serialize(new[] { new SubmittedDishResponse("dish1", 3) });
        Assert.Equal("[{\"dishId\":\"dish1\",\"quantity\":3}]", json);
    }

    private void AssertNothingSaved()
    {
        _orders.Verify(r => r.SaveOrderAsync(It.IsAny<Order>()), Times.Never);
        _groups.Verify(r => r.SaveGroupReservationAsync(It.IsAny<GroupReservation>()), Times.Never);
    }

    // ---------- L3: DELETE /guest/groups/{id}/sessions/{guestSessionId} ----------

    [Fact]
    public async Task DeleteGuestSession_RemovesGuest_AndReturnsRecalculatedSnapshot()
    {
        var removed = Session("session1", submitted: true, ("dish1", 2));
        var remaining = Session("session2", submitted: false, ("dish2", 1));
        _guestSessions.Setup(r => r.GetByGuestSessionId("session1")).ReturnsAsync(removed);
        // the index may still return the deleted item for a moment (eventual consistency)
        GivenSessions(removed, remaining);

        var result = await _guestSessionService.DeleteGuestSessionAsync(OwnerId, GroupId, "session1");

        _guestSessions.Verify(r => r.DeleteAsync(removed), Times.Once);
        var participant = Assert.Single(result.Participants);
        Assert.Equal("session2", participant.Id);
        Assert.Equal(220m, result.Total);
    }

    [Fact]
    public async Task DeleteGuestSession_LastParticipant_ReturnsEmptySnapshot()
    {
        var removed = Session("session1", submitted: true, ("dish1", 2));
        _guestSessions.Setup(r => r.GetByGuestSessionId("session1")).ReturnsAsync(removed);
        GivenSessions();

        var result = await _guestSessionService.DeleteGuestSessionAsync(OwnerId, GroupId, "session1");

        Assert.Empty(result.Participants);
        Assert.Equal(0m, result.Total);
    }

    [Fact]
    public async Task DeleteGuestSession_SessionOfAnotherGroup_ThrowsValidation()
    {
        var foreign = Session("session9", submitted: false);
        foreign.GroupReservationId = "group-2";
        _guestSessions.Setup(r => r.GetByGuestSessionId("session9")).ReturnsAsync(foreign);

        await Assert.ThrowsAsync<ValidationException>(() => _guestSessionService.DeleteGuestSessionAsync(OwnerId, GroupId, "session9"));
        _guestSessions.Verify(r => r.DeleteAsync(It.IsAny<GuestSession>()), Times.Never);
    }

    [Fact]
    public async Task DeleteGuestSession_UnknownSession_ThrowsNotFound()
    {
        _guestSessions.Setup(r => r.GetByGuestSessionId("missing")).ReturnsAsync((GuestSession?)null);
        await Assert.ThrowsAsync<GuestSessionNotFoundException>(() => _guestSessionService.DeleteGuestSessionAsync(OwnerId, GroupId, "missing"));
    }

    [Fact]
    public async Task DeleteGuestSession_NotOwner_ThrowsForbidden()
    {
        await Assert.ThrowsAsync<ActionForbiddenException>(() => _guestSessionService.DeleteGuestSessionAsync(OtherUserId, GroupId, "session1"));
        _guestSessions.Verify(r => r.DeleteAsync(It.IsAny<GuestSession>()), Times.Never);
    }

    [Fact]
    public async Task DeleteGuestSession_OwnerSession_ThrowsValidation()
    {
        var owner = OwnerSession();
        _guestSessions.Setup(r => r.GetByGuestSessionId(owner.Id)).ReturnsAsync(owner);

        await Assert.ThrowsAsync<ValidationException>(() => _guestSessionService.DeleteGuestSessionAsync(OwnerId, GroupId, owner.Id));
        _guestSessions.Verify(r => r.DeleteAsync(It.IsAny<GuestSession>()), Times.Never);
    }

    [Fact]
    public async Task DeleteGuestSession_AfterSubmit_ThrowsAlreadySubmitted()
    {
        _group.Status = GroupReservationStatus.Submitted;
        var guest = Session("session1", submitted: true, ("dish1", 1));
        _guestSessions.Setup(r => r.GetByGuestSessionId("session1")).ReturnsAsync(guest);

        await Assert.ThrowsAsync<GroupReservationSubmittedException>(() => _guestSessionService.DeleteGuestSessionAsync(OwnerId, GroupId, "session1"));
        _guestSessions.Verify(r => r.DeleteAsync(It.IsAny<GuestSession>()), Times.Never);
    }
}
