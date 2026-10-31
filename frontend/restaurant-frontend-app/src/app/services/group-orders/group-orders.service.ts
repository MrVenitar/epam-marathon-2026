import { HttpClient, HttpContext, HttpHeaders } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';

import { AuthenticationService } from '@app/auth/services/authentication.service';
import { SKIP_ERROR_TOAST } from '@app/auth/skip-error-toast.context';
import { GroupSessionsResponse } from '@app/models/group-orders/group-sessions-response.model';
import { GroupStatusResponse } from '@app/models/group-orders/group-status.model';
import { OwnerSessionResponse } from '@app/models/group-orders/owner-session.model';
import { GroupRequestOptions } from '@app/models/group-orders/request-options.model';
import {
  SubmitGroupPreOrderRequest,
  SubmittedDish,
} from '@app/models/group-orders/submit-group-pre-order.model';
import { ReservationCustomer } from '@app/models/reservations/reservation.model';
import { API_BASE_URL } from '@app/shared/config/api-base-url.token';

/**
 * Owner side of the group pre-order (backend: docs/GroupPreOrder.md).
 *
 * Note: `tokenInterceptor` does not add the user's token to URLs containing `/guest/`
 * (they normally carry a guest-session token). The owner endpoints under `/guest/groups/...`
 * need the owner's Cognito token, so it is added here explicitly.
 */
@Injectable({ providedIn: 'root' })
export class GroupOrdersService {
  private readonly baseUrl = inject(API_BASE_URL);
  private readonly http = inject(HttpClient);
  private readonly authService = inject(AuthenticationService);

  /** GET /reservations/{reservationId} — address, date, time and guest count for the header. */
  getReservation$(reservationId: string): Observable<ReservationCustomer> {
    return this.http.get<ReservationCustomer>(
      `${this.baseUrl}/reservations/${encodeURIComponent(reservationId)}`,
    );
  }

  /** GET /reservations/{reservationId}/guest-session-id — owner's own session and the group id. */
  getOwnerSession$(reservationId: string): Observable<OwnerSessionResponse> {
    return this.http.get<OwnerSessionResponse>(
      `${this.baseUrl}/reservations/${encodeURIComponent(reservationId)}/guest-session-id`,
    );
  }

  /** GET /guest/groups/{groupReservationId}/sessions — participants, their dishes and totals. */
  getSessions$(groupReservationId: string, options: GroupRequestOptions = {}): Observable<GroupSessionsResponse> {
    return this.http.get<GroupSessionsResponse>(
      `${this.baseUrl}/guest/groups/${encodeURIComponent(groupReservationId)}/sessions`,
      { headers: this.ownerHeaders(), context: this.context(options) },
    );
  }

  /** GET /guest/groups/{groupReservationId}/status — Open / Submitted / Closed. */
  getStatus$(groupReservationId: string, options: GroupRequestOptions = {}): Observable<GroupStatusResponse> {
    return this.http.get<GroupStatusResponse>(
      `${this.baseUrl}/guest/groups/${encodeURIComponent(groupReservationId)}/status`,
      { context: this.context(options) },
    );
  }

  /** POST /group-reservations/{groupReservationId}/submit — send the consolidated pre-order. */
  submit$(groupReservationId: string, reservationId: string): Observable<SubmittedDish[]> {
    const body: SubmitGroupPreOrderRequest = { reservationId };
    return this.http.post<SubmittedDish[]>(
      `${this.baseUrl}/group-reservations/${encodeURIComponent(groupReservationId)}/submit`,
      body,
    );
  }

  /** DELETE /guest/groups/{groupReservationId}/sessions/{guestSessionId} — returns the new snapshot. */
  removeSession$(groupReservationId: string, guestSessionId: string): Observable<GroupSessionsResponse> {
    return this.http.delete<GroupSessionsResponse>(
      `${this.baseUrl}/guest/groups/${encodeURIComponent(groupReservationId)}/sessions/${encodeURIComponent(guestSessionId)}`,
      { headers: this.ownerHeaders() },
    );
  }

  private ownerHeaders(): HttpHeaders {
    const idToken = this.authService.getTokens()?.idToken;
    return idToken ? new HttpHeaders({ Authorization: `Bearer ${idToken}` }) : new HttpHeaders();
  }

  private context(options: GroupRequestOptions): HttpContext {
    return new HttpContext().set(SKIP_ERROR_TOAST, options.silent === true);
  }
}
