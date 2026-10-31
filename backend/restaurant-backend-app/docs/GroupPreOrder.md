# Group pre-order: owner functionality

Implementation of `homework-backend.md` (levels 1–3). All endpoints require a signed-in user
(Cognito token) and are available **only to the owner** of the reservation (`reservation.customerId`).

| Level | Endpoint | Handler | Service method |
|---|---|---|---|
| 1 | `GET /guest/groups/{groupReservationId}/sessions` | `GetGroupSessionsHandler` | `GuestSessionService.GetGroupSessionsAsync` |
| 2 | `GET /reservations/{reservationId}/guest-session-id` | `GetOwnerSessionIdHandler` | `GuestSessionService.GetOwnerSessionIdAsync` |
| 2 | `POST /group-reservations/{groupReservationId}/submit` | `SubmitGroupPreOrderHandler` | `GroupPreOrderService.SubmitAsync` |
| 3 | `DELETE /guest/groups/{groupReservationId}/sessions/{guestSessionId}` | `DeleteGuestSessionHandler` | `GuestSessionService.DeleteGuestSessionAsync` |

Unit tests: `Tests/Services/GroupPreOrderTests.cs` (run in CI by `.github/workflows/backend-ci.yml`).

## Status codes

| Situation | GET sessions | GET guest-session-id | POST submit | DELETE session |
|---|---|---|---|---|
| no / invalid token | 401 (API Gateway) | 401 | 401 | 401 |
| caller is not the owner (another customer, waiter) | 403 | 403 | 403 | 403 |
| group reservation not found | 404 | 404 | 404 | 404 |
| reservation not found | 404 | 404 | 404 | 404 |
| `reservationId` missing in body | – | – | 400 | – |
| reservation from body belongs to another group | – | – | 400 | – |
| group already submitted | – | – | 400 `Group reservation was submitted.` | 400 (same) |
| group closed (reservation cancelled/finished) | – | – | 400 | – |
| active order of the reservation not found | – | – | 400 `Active order not found for this reservation.` | – |
| no submitted guest session | – | – | 400 `No submitted guest sessions found to consolidate.` | – |
| submitted sessions contain no dishes | – | – | 400 `No dishes selected in the submitted guest sessions.` | – |
| guest session not found | – | – | – | 404 |
| guest session belongs to another group | – | – | – | 400 |
| guest session is the owner's own session | – | – | – | 400 `Owner session cannot be removed.` |

Error bodies: `{"error": "..."}` for 401/403/404, `{"message": "...", "errorCode": ...}` for 400
(the existing convention of the guest-session endpoints).

## Behaviour chosen where the specification is silent

- **Owner session.** `GET .../guest-session-id` creates the owner's own session on the first call.
  Its id is `owner-{groupReservationId}`, so repeated or simultaneous calls never create a second one.
  It is shown in the snapshot with `type: "Owner"` and the owner's first name, and is listed first.
- **Guest limit.** The owner always takes one seat; the owner's own session is not counted again
  when guests join (`CountByGroupReservationId` ignores it).
- **Submit.** Only sessions with `isSubmitted: true` are taken (the owner's session too, if submitted).
  Identical dishes are merged by `dishId`. The result is **added** to the reservation's existing
  pre-order: a dish already in it gets its quantity increased (unless the waiter cancelled it),
  otherwise a new `Pending` item is added. The order becomes `SubmittedByCustomer`,
  the group `Submitted`. The response lists only the consolidated guest dishes.
- **Repeated submit** → 400 `Group reservation was submitted.` (nothing changes).
- **Delete after submit** → 400: the order has already been sent to the restaurant, so removing a
  guest would make the snapshot disagree with it.
- **Delete the last participant** → 200 with `{"participants": [], "total": 0}`.
- **Empty group** → `{"participants": [], "total": 0}`.
- **A dish removed from the menu** is skipped in the snapshot instead of failing the request.
