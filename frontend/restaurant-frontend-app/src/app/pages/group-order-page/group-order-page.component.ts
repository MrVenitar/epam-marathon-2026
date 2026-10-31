import { CurrencyPipe, DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  inject,
  OnInit,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { catchError, EMPTY, exhaustMap, filter, forkJoin, interval, Observable, switchMap, tap } from 'rxjs';

import { ParticipantCardComponent } from '@app/components/group-orders/participant-card/participant-card.component';
import { GroupSessionsResponse } from '@app/models/group-orders/group-sessions-response.model';
import { GroupReservationStatus, GroupStatusResponse } from '@app/models/group-orders/group-status.model';
import { Participant } from '@app/models/group-orders/participant.model';
import { ReservationCustomer } from '@app/models/reservations/reservation.model';
import { GroupOrdersService } from '@app/services/group-orders/group-orders.service';
import { ToastService } from '@app/services/toast/toast.service';
import { ModalComponent } from '@app/shared/custom-components/modal/modal.component';
import { consolidatePreOrder } from '@app/shared/helpers/consolidate-pre-order';

/** How often the owner's screen picks up guests' changes while the group is open. */
export const GROUP_REFRESH_INTERVAL_MS = 15_000;

type LoadState = 'loading' | 'ready' | 'error';

/**
 * Owner's group pre-order: what every participant has chosen, the consolidated order that goes to
 * the restaurant, submitting it and removing guests. Route: /reservations/:reservationId/group-order
 */
@Component({
  selector: 'app-group-order-page',
  imports: [CurrencyPipe, DatePipe, RouterLink, ParticipantCardComponent, ModalComponent],
  templateUrl: './group-order-page.component.html',
  styleUrl: './group-order-page.component.scss',
  changeDetection: ChangeDetectionStrategy.Eager,
})
export class GroupOrderPageComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly groupOrders = inject(GroupOrdersService);
  private readonly toast = inject(ToastService);
  private readonly destroyRef = inject(DestroyRef);

  readonly reservationId = this.route.snapshot.paramMap.get('reservationId') ?? '';

  readonly loadState = signal<LoadState>('loading');
  readonly errorMessage = signal('');
  readonly reservation = signal<ReservationCustomer | null>(null);
  readonly groupId = signal<string | null>(null);
  readonly ownerSessionId = signal<string | null>(null);
  readonly participants = signal<Participant[]>([]);
  readonly status = signal<GroupReservationStatus>('Open');

  readonly lastUpdated = signal<Date | null>(null);
  readonly refreshing = signal(false);
  readonly refreshFailed = signal(false);

  readonly confirmSubmitOpen = signal(false);
  readonly submitting = signal(false);
  readonly guestToRemove = signal<Participant | null>(null);
  readonly removing = signal(false);

  readonly isOpen = computed(() => this.status() === 'Open');

  /** Only submitted carts go to the restaurant — this is what the Grand Total shows. */
  readonly consolidated = computed(() => consolidatePreOrder(this.participants()));

  readonly pendingCount = computed(
    () => this.participants().filter((person) => person.type === 'Guest' && !person.isSubmitted).length,
  );

  readonly canSubmit = computed(
    () => this.isOpen() && this.consolidated().items.length > 0 && !this.submitting(),
  );

  readonly statusLabel = computed(() => {
    switch (this.status()) {
      case 'Open':
        return 'Collecting pre-orders';
      case 'Submitted':
        return 'Sent to Restaurant';
      default:
        return 'Closed';
    }
  });

  ngOnInit(): void {
    this.load();
    this.startAutoRefresh();
  }

  /** First load (and "Try again"): reservation + owner session, then the group itself. */
  load(): void {
    if (!this.reservationId) {
      this.fail('Reservation not found');
      return;
    }

    this.loadState.set('loading');
    forkJoin({
      reservation: this.groupOrders.getReservation$(this.reservationId),
      owner: this.groupOrders.getOwnerSession$(this.reservationId),
    })
      .pipe(
        tap(({ reservation, owner }) => {
          this.reservation.set(reservation);
          this.groupId.set(owner.groupReservationId);
          this.ownerSessionId.set(owner.guestSessionId);
        }),
        switchMap(({ owner }) => this.fetchGroup$(owner.groupReservationId, false)),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: () => this.loadState.set('ready'),
        error: (error: HttpErrorResponse) => this.fail(this.describeLoadError(error)),
      });
  }

  /** "Refresh" button: same as the background refresh, but right now. */
  refreshNow(): void {
    const groupId = this.groupId();
    if (!groupId || this.refreshing()) {
      return;
    }
    this.refreshing.set(true);
    this.fetchGroup$(groupId, true)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => this.refreshing.set(false),
        error: () => {
          this.refreshing.set(false);
          this.refreshFailed.set(true);
        },
      });
  }

  openSubmitDialog(): void {
    if (this.canSubmit()) {
      this.confirmSubmitOpen.set(true);
    }
  }

  closeSubmitDialog(): void {
    this.confirmSubmitOpen.set(false);
  }

  submit(): void {
    const groupId = this.groupId();
    if (!groupId || !this.canSubmit()) {
      return;
    }
    this.submitting.set(true);
    this.groupOrders
      .submit$(groupId, this.reservationId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.submitting.set(false);
          this.confirmSubmitOpen.set(false);
          this.status.set('Submitted');
          this.toast.success('Pre-order sent to the restaurant');
        },
        // The error toast with the API message is shown by tokenInterceptor.
        error: () => {
          this.submitting.set(false);
          this.confirmSubmitOpen.set(false);
        },
      });
  }

  askToRemove(person: Participant): void {
    this.guestToRemove.set(person);
  }

  cancelRemove(): void {
    this.guestToRemove.set(null);
  }

  confirmRemove(): void {
    const groupId = this.groupId();
    const person = this.guestToRemove();
    if (!groupId || !person || this.removing()) {
      return;
    }
    this.removing.set(true);
    this.groupOrders
      .removeSession$(groupId, person.id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (snapshot) => {
          this.applySessions(snapshot);
          this.removing.set(false);
          this.guestToRemove.set(null);
          this.toast.success(`${person.name} was removed from the group`);
        },
        error: () => {
          this.removing.set(false);
          this.guestToRemove.set(null);
        },
      });
  }

  /** Guests change their carts on their own phones: pick that up every 15 s while the group is open. */
  private startAutoRefresh(): void {
    interval(GROUP_REFRESH_INTERVAL_MS)
      .pipe(
        // only while the group is open and the tab is visible (no requests from a forgotten tab)
        filter(
          () =>
            this.loadState() === 'ready' &&
            this.isOpen() &&
            !this.refreshing() &&
            document.visibilityState === 'visible',
        ),
        // a slow request is not started twice
        exhaustMap(() => {
          const groupId = this.groupId();
          return groupId
            ? this.fetchGroup$(groupId, true).pipe(
                catchError(() => {
                  this.refreshFailed.set(true);
                  return EMPTY;
                }),
              )
            : EMPTY;
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe();
  }

  private fetchGroup$(
    groupId: string,
    silent: boolean,
  ): Observable<{ sessions: GroupSessionsResponse; status: GroupStatusResponse }> {
    return forkJoin({
      sessions: this.groupOrders.getSessions$(groupId, { silent }),
      status: this.groupOrders.getStatus$(groupId, { silent }),
    }).pipe(
      tap(({ sessions, status }) => {
        this.applySessions(sessions);
        this.status.set(status.status);
      }),
    );
  }

  private applySessions(snapshot: GroupSessionsResponse): void {
    this.participants.set(snapshot.participants ?? []);
    this.lastUpdated.set(new Date());
    this.refreshFailed.set(false);
  }

  private fail(message: string): void {
    this.errorMessage.set(message);
    this.loadState.set('error');
  }

  private describeLoadError(error: HttpErrorResponse): string {
    switch (error.status) {
      case 403:
        return 'Only the owner of this reservation can see its group pre-order';
      case 404:
        return 'Group reservation not found';
      default:
        return "Couldn't load the group pre-order";
    }
  }
}
