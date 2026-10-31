import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { AuthenticationService } from '@app/auth/services/authentication.service';
import { SKIP_ERROR_TOAST } from '@app/auth/skip-error-toast.context';
import { API_BASE_URL } from '@app/shared/config/api-base-url.token';
import { GroupOrdersService } from './group-orders.service';

describe('GroupOrdersService', () => {
  let service: GroupOrdersService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: API_BASE_URL, useValue: 'https://test.api' },
        {
          provide: AuthenticationService,
          useValue: { getTokens: () => ({ idToken: 'owner-token', accessToken: 'access-token' }) },
        },
      ],
    });
    service = TestBed.inject(GroupOrdersService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('gets the owner session and group of a reservation', () => {
    service.getOwnerSession$('res-1').subscribe((response) => {
      expect(response.groupReservationId).toBe('group-1');
    });

    const req = httpMock.expectOne('https://test.api/reservations/res-1/guest-session-id');
    expect(req.request.method).toBe('GET');
    req.flush({ guestSessionId: 'owner-group-1', groupReservationId: 'group-1' });
  });

  it('loads the group sessions with the owner token, showing errors by default', () => {
    service.getSessions$('group-1').subscribe();

    const req = httpMock.expectOne('https://test.api/guest/groups/group-1/sessions');
    expect(req.request.method).toBe('GET');
    expect(req.request.headers.get('Authorization')).toBe('Bearer owner-token');
    expect(req.request.context.get(SKIP_ERROR_TOAST)).toBeFalse();
    req.flush({ participants: [], total: 0 });
  });

  it('marks background status refreshes as silent', () => {
    service.getStatus$('group-1', { silent: true }).subscribe();

    const req = httpMock.expectOne('https://test.api/guest/groups/group-1/status');
    expect(req.request.method).toBe('GET');
    expect(req.request.context.get(SKIP_ERROR_TOAST)).toBeTrue();
    req.flush({ currentParticipants: 1, maxCapacity: 4, status: 'Open' });
  });

  it('submits the consolidated pre-order with the reservation id', () => {
    service.submit$('group-1', 'res-1').subscribe((dishes) => {
      expect(dishes).toEqual([{ dishId: 'dish1', quantity: 3 }]);
    });

    const req = httpMock.expectOne('https://test.api/group-reservations/group-1/submit');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ reservationId: 'res-1' });
    req.flush([{ dishId: 'dish1', quantity: 3 }]);
  });

  it('removes a guest with the owner token', () => {
    service.removeSession$('group-1', 'session-2').subscribe();

    const req = httpMock.expectOne('https://test.api/guest/groups/group-1/sessions/session-2');
    expect(req.request.method).toBe('DELETE');
    expect(req.request.headers.get('Authorization')).toBe('Bearer owner-token');
    req.flush({ participants: [], total: 0 });
  });
});
