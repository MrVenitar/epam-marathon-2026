using APIHandler.Models.GroupReservation;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace APIHandler.Services.GroupReservations;

public interface IGroupPreOrderService
{
    /// <summary>
    /// Consolidates the submitted guest carts of a group into the reservation's pre-order and
    /// marks the order as SubmittedByCustomer and the group as Submitted. Owner only.
    /// </summary>
    /// <returns>The consolidated dishes (one entry per dishId) that were added to the order.</returns>
    Task<IReadOnlyList<SubmittedDishResponse>> SubmitAsync(string userId, string groupReservationId, string reservationId);
}
