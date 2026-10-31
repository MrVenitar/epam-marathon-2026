using System.Text.Json.Serialization;

namespace APIHandler.Models.GroupReservation;

/// <summary>Request body of POST /group-reservations/{groupReservationId}/submit.</summary>
public class SubmitGroupPreOrderRequest
{
    [JsonPropertyName("reservationId")]
    public string? ReservationId { get; set; }
}

/// <summary>One consolidated dish of the submitted group pre-order.</summary>
public record SubmittedDishResponse(
    [property: JsonPropertyName("dishId")] string DishId,
    [property: JsonPropertyName("quantity")] int Quantity);
