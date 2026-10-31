/** GET /reservations/{reservationId}/guest-session-id */
export interface OwnerSessionResponse {
  /** Id of the owner's own session — the participant with this id is "My Pre-Order". */
  guestSessionId: string;
  /** Group of this reservation; used for the sessions / status / submit / remove calls. */
  groupReservationId: string;
}
