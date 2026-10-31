using APIHandler.Models.Orders;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace APIHandler.Models.GuestSessions;

/// <summary>
/// Snapshot of a group pre-order: every participant with their dishes and totals.
/// Returned by GET /guest/groups/{groupReservationId}/sessions and
/// DELETE /guest/groups/{groupReservationId}/sessions/{guestSessionId}.
/// JSON names are fixed here so the contract does not depend on the serializer settings.
/// </summary>
public record GetAllSessionsResponse(
    [property: JsonPropertyName("participants")] IEnumerable<Participant> Participants,
    [property: JsonPropertyName("total")] decimal Total);

public record Participant(
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("type")]
    [property: JsonConverter(typeof(JsonStringEnumConverter<ParticipantType>))] ParticipantType Type,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("items")] IEnumerable<OrderItemResponse> Items,
    [property: JsonPropertyName("total")] decimal Total,
    [property: JsonPropertyName("isSubmitted")] bool IsSubmitted);

public enum ParticipantType
{
    Owner,
    Guest
}
