using System.Text.Json.Serialization;

namespace APIHandler.Models.GuestSessions;

/// <summary>Response of GET /reservations/{reservationId}/guest-session-id.</summary>
public record OwnerSessionResponse([property: JsonPropertyName("guestSessionId")] string GuestSessionId);
