using APIHandler.Constants.Orders;
using APIHandler.Entities;
using APIHandler.Exceptions;
using APIHandler.Models.GroupReservation;
using APIHandler.Repositories.GroupReservations;
using APIHandler.Repositories.GuestSessions;
using APIHandler.Repositories.Orders;
using APIHandler.Repositories.Reservations;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace APIHandler.Services.GroupReservations;

/// <summary>
/// POST /group-reservations/{groupReservationId}/submit — the owner confirms the consolidated
/// group pre-order that is handed over to the restaurant.
/// </summary>
public class GroupPreOrderService(
    IGroupReservationRepository groupReservationRepository,
    IReservationRepository reservationRepository,
    IGuestSessionRepository guestSessionRepository,
    IOrderRepository orderRepository) : IGroupPreOrderService
{
    public const string ReservationMismatchMessage = "Reservation does not belong to this group reservation.";
    public const string OrderNotFoundMessage = "Active order not found for this reservation.";
    public const string NoSubmittedSessionsMessage = "No submitted guest sessions found to consolidate.";
    public const string NoDishesMessage = "No dishes selected in the submitted guest sessions.";

    public async Task<IReadOnlyList<SubmittedDishResponse>> SubmitAsync(string userId, string groupReservationId, string reservationId)
    {
        // 404: group or reservation does not exist
        var group = await groupReservationRepository.GetGroupReservationByIdAsync(groupReservationId)
            ?? throw new GroupReservationNotFoundException();

        var reservation = await reservationRepository.GetReservationByIdAsync(reservationId)
            ?? throw new ReservationNotFoundException($"Reservation {reservationId} was not found.");

        // 403: only the owner of the reservation may submit
        if (reservation.CustomerId != userId)
            throw new ActionForbiddenException("Only the reservation owner can submit the group pre-order.");

        // 400: the reservation from the body must be the one this group belongs to
        if (group.ReservationId != reservation.Id)
            throw new ValidationException(ReservationMismatchMessage);

        // 400: a group can be submitted only once and only while it is open
        if (group.Status == GroupReservationStatus.Submitted)
            throw new GroupReservationSubmittedException();

        if (group.Status != GroupReservationStatus.Open)
            throw new ValidationException($"Cannot submit order. Group reservation is '{group.Status}'.");

        // 400: the owner's own pre-order (the order of the reservation) must exist
        var order = await orderRepository.GetOrderByReservationIdAsync(reservationId)
            ?? throw new ValidationException(OrderNotFoundMessage);

        // 400: only carts the participants have submitted are taken
        var sessions = await guestSessionRepository.GetAllByGroupReservationIdAsync(groupReservationId);
        var submittedSessions = sessions.Where(s => s.IsSubmitted).ToList();

        if (submittedSessions.Count == 0)
            throw new ValidationException(NoSubmittedSessionsMessage);

        var consolidated = Consolidate(submittedSessions);

        // 400: the submitted carts contain no dishes at all
        if (consolidated.Count == 0)
            throw new ValidationException(NoDishesMessage);

        AddToOrder(order, consolidated);
        order.Status = OrderStatus.SubmittedByCustomer;
        await orderRepository.SaveOrderAsync(order);

        group.Status = GroupReservationStatus.Submitted;
        await groupReservationRepository.SaveGroupReservationAsync(group);

        return consolidated;
    }

    /// <summary>
    /// All dishes of the given sessions in one list, identical dishes merged by dishId with their
    /// quantities summed. Keeps the order in which dishes first appear.
    /// </summary>
    public static IReadOnlyList<SubmittedDishResponse> Consolidate(IEnumerable<GuestSession> sessions) =>
        sessions
            .SelectMany(s => s.Dishes ?? [])
            .Where(d => !string.IsNullOrEmpty(d.DishId) && d.Quantity > 0)
            .GroupBy(d => d.DishId)
            .Select(g => new SubmittedDishResponse(g.Key, g.Sum(d => d.Quantity)))
            .ToList();

    private static void AddToOrder(Order order, IEnumerable<SubmittedDishResponse> dishes)
    {
        order.Dishes ??= [];

        foreach (var dish in dishes)
        {
            // merge with the same dish already in the owner's pre-order (unless the waiter cancelled it)
            var existing = order.Dishes.FirstOrDefault(d => d.DishId == dish.DishId && d.State != OrderItemState.Canceled);
            if (existing is not null)
            {
                existing.Quantity += dish.Quantity;
            }
            else
            {
                order.Dishes.Add(new OrderItem
                {
                    DishId = dish.DishId,
                    Quantity = dish.Quantity,
                    State = OrderItemState.Pending
                });
            }
        }
    }
}
