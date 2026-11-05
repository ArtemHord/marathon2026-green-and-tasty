import { CurrencyPipe, DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import {
  EMPTY,
  Observable,
  Subscription,
  catchError,
  defer,
  exhaustMap,
  filter,
  finalize,
  forkJoin,
  interval,
  map,
  of,
  switchMap,
  tap,
  throwError,
} from 'rxjs';
import { ParticipantCardComponent } from '@app/components/group-orders/participant-card/participant-card.component';
import { SplitBillComponent } from '@app/components/group-orders/split-bill/split-bill.component';
import { GroupSessionsResponse } from '@app/models/group-orders/group-sessions-response.model';
import { GroupReservationStatus } from '@app/models/group-orders/group-status.model';
import { Participant } from '@app/models/group-orders/participant.model';
import { GuestInvite } from '@app/models/reservations/guest-invite.model';
import { GroupOrdersService } from '@app/services/group-orders/group-orders.service';
import { ToastService } from '@app/services/toast/toast.service';
import { ModalComponent } from '@app/shared/custom-components/modal/modal.component';
import { consolidatePreOrder } from '@app/shared/helpers/consolidate-pre-order';
import { ConsolidatedLine, GroupOrderLoadState } from './models';

/** Інтервал фонової синхронізації, поки група Open. */
export const GROUP_ORDER_SYNC_MS = 15_000;

const STATUS_LABELS: Record<GroupReservationStatus, string> = {
  Open: 'Collecting pre-orders',
  Submitted: 'Sent to Restaurant',
  Closed: 'Closed',
};

@Component({
  selector: 'app-group-order-page',
  imports: [ParticipantCardComponent, SplitBillComponent, ModalComponent, RouterLink, CurrencyPipe, DatePipe],
  templateUrl: './group-order-page.component.html',
  styleUrl: './group-order-page.component.scss',
  changeDetection: ChangeDetectionStrategy.Eager,
})
export class GroupOrderPageComponent {
  private readonly service = inject(GroupOrdersService);
  private readonly toast = inject(ToastService);
  protected readonly reservationId = inject(ActivatedRoute).snapshot.paramMap.get('reservationId') ?? '';

  private loadSub?: Subscription;
  private syncSub?: Subscription;
  private refreshSub?: Subscription;

  // --- стан ---
  readonly loadState = signal<GroupOrderLoadState>('loading');
  readonly invite = signal<GuestInvite | null>(null);
  readonly sessions = signal<GroupSessionsResponse | null>(null);
  readonly ownerSessionId = signal<string | null>(null);
  readonly status = signal<GroupReservationStatus | null>(null);

  readonly confirmOpen = signal(false);
  readonly submitting = signal(false);

  readonly lastUpdated = signal<Date | null>(null);
  readonly refreshing = signal(false);
  readonly syncFailed = signal(false);

  readonly removeTarget = signal<Participant | null>(null);
  readonly removing = signal(false);

  // --- похідне ---
  readonly participants = computed(() => this.sessions()?.participants ?? []);
  readonly consolidated = computed(() => consolidatePreOrder(this.participants()));
  /** Лише підтверджені кошики — це те, що піде в замовлення ресторану. */
  readonly grandTotal = computed(() => this.consolidated().total);
  readonly consolidatedLines = computed<ConsolidatedLine[]>(() =>
    this.consolidated().items.map((item) => ({
      dishId: item.dish.id,
      label: `${item.dish.name} × ${item.quantity}`,
      amount: item.dish.price * item.quantity,
    })),
  );

  readonly isOpen = computed(() => this.status() === 'Open');
  /**
   * «Sent to Restaurant»: показуємо блок Split bill. Він сам вантажить розбивку один раз при появі —
   * і після submit у цій вкладці, і при відкритті вже відправленої групи (polling тут уже зупинений).
   */
  readonly isSent = computed(() => this.status() === 'Submitted');
  readonly statusLabel = computed(() => {
    const status = this.status();
    return status ? STATUS_LABELS[status] : '';
  });
  readonly hasDishes = computed(() => this.participants().some((person) => person.items.length > 0));

  /**
   * Непідтверджені кошики гостей. Owner не рахується: його owner-сесія на бекенді створюється
   * ліниво з порожнім кошиком і API для її submit немає — страви Owner живуть у звичайному order.
   */
  readonly pendingCount = computed(
    () => this.participants().filter((person) => person.type !== 'Owner' && !person.isSubmitted).length,
  );
  readonly pendingMessage = computed(() => {
    const count = this.pendingCount();
    return count === 1
      ? "1 participant hasn't submitted their cart yet — their dishes won't be included in the order"
      : `${count} participants haven't submitted their cart yet — their dishes won't be included in the order`;
  });

  readonly canSubmit = computed(
    () => this.isOpen() && !this.submitting() && !this.removing() && this.consolidated().items.length > 0,
  );

  constructor() {
    inject(DestroyRef).onDestroy(() => {
      this.loadSub?.unsubscribe();
      this.stopSync();
    });
    this.load();
  }

  isMe(person: Participant): boolean {
    return person.id === this.ownerSessionId();
  }

  canRemove(person: Participant): boolean {
    return this.isOpen() && person.type === 'Guest' && !this.isMe(person);
  }

  // --- завантаження ---
  load(): void {
    this.loadSub?.unsubscribe();
    this.stopSync();
    this.loadState.set('loading');

    this.loadSub = this.service
      .getGroupInvite$(this.reservationId)
      .pipe(
        switchMap((invite) =>
          forkJoin({
            sessions: this.service.getSessions$(invite.GroupReservationId),
            ownerSessionId: this.ownerSessionId$(),
            status: this.service.getStatus$(invite.GroupReservationId),
          }).pipe(map((data) => ({ invite, ...data }))),
        ),
      )
      .subscribe({
        next: ({ invite, sessions, ownerSessionId, status }) => {
          this.invite.set(invite);
          this.sessions.set(sessions);
          this.ownerSessionId.set(ownerSessionId);
          this.status.set(status.status);
          this.lastUpdated.set(new Date());
          this.syncFailed.set(false);
          this.loadState.set('ready');
          if (status.status === 'Open') {
            this.startSync();
          }
        },
        error: (error: unknown) => {
          const notFound = error instanceof HttpErrorResponse && error.status === 404;
          this.loadState.set(notFound ? 'not-found' : 'error');
        },
      });
  }

  /** Немає власної сесії Owner (404) — екран працює, просто без «My Pre-Order». */
  private ownerSessionId$(): Observable<string | null> {
    return this.service
      .getOwnerSessionId$(this.reservationId)
      .pipe(
        catchError((error: unknown) =>
          error instanceof HttpErrorResponse && error.status === 404 ? of(null) : throwError(() => error),
        ),
      );
  }

  // --- фонова синхронізація ---
  refresh(): void {
    if (this.refreshing()) {
      return;
    }
    this.refreshSub = this.syncOnce$().subscribe();
  }

  private startSync(): void {
    this.stopSync();
    this.syncSub = interval(GROUP_ORDER_SYNC_MS)
      .pipe(
        filter(() => !this.refreshing()),
        exhaustMap(() => this.syncOnce$()),
      )
      .subscribe();
  }

  private stopSync(): void {
    this.syncSub?.unsubscribe();
    this.syncSub = undefined;
    this.refreshSub?.unsubscribe();
    this.refreshSub = undefined;
    this.refreshing.set(false);
  }

  private syncOnce$(): Observable<unknown> {
    const groupId = this.invite()?.GroupReservationId;
    if (!groupId) {
      return EMPTY;
    }
    return defer(() => {
      this.refreshing.set(true);
      return forkJoin({
        sessions: this.service.getSessions$(groupId, { silent: true }),
        status: this.service.getStatus$(groupId, { silent: true }),
      });
    }).pipe(
      tap(({ sessions, status }) => {
        if (!this.isOpen()) {
          return; // група вже відправлена локально — застарілу відповідь ігноруємо
        }
        this.sessions.set(sessions);
        this.status.set(status.status);
        this.lastUpdated.set(new Date());
        this.syncFailed.set(false);
        if (status.status !== 'Open') {
          this.stopSync();
        }
      }),
      catchError(() => {
        this.syncFailed.set(true);
        return EMPTY;
      }),
      finalize(() => this.refreshing.set(false)),
    );
  }

  // --- Submit ---
  openConfirm(): void {
    if (this.canSubmit()) {
      this.confirmOpen.set(true);
    }
  }

  closeConfirm(): void {
    if (!this.submitting()) {
      this.confirmOpen.set(false);
    }
  }

  submit(): void {
    const groupId = this.invite()?.GroupReservationId;
    if (!groupId || !this.canSubmit()) {
      return; // захист від подвійного кліку: поки запит іде, canSubmit() === false
    }
    this.submitting.set(true);
    this.service.submit$(groupId, this.reservationId).subscribe({
      next: () => {
        this.stopSync();
        this.status.set('Submitted');
        this.submitting.set(false);
        this.confirmOpen.set(false);
        this.toast.success('Pre-order sent to the restaurant');
      },
      // toast з текстом від API вже показав tokenInterceptor — не дублюємо
      error: (error: unknown) => {
        this.submitting.set(false);
        this.confirmOpen.set(false);
        if (error instanceof HttpErrorResponse && error.status === 400) {
          // напр. група вже відправлена з іншої вкладки: один тихий перезапит статусу/сесій
          // тим самим silent-шляхом, що й polling; якщо статус уже не Open — екран стане read-only
          this.refresh();
        }
      },
    });
  }

  // --- Remove Guest ---
  askRemove(person: Participant): void {
    if (this.canRemove(person) && !this.removing()) {
      this.removeTarget.set(person);
    }
  }

  closeRemove(): void {
    if (!this.removing()) {
      this.removeTarget.set(null);
    }
  }

  confirmRemove(): void {
    const groupId = this.invite()?.GroupReservationId;
    const target = this.removeTarget();
    if (!groupId || !target || this.removing()) {
      return;
    }
    this.removing.set(true);
    this.service.removeSession$(groupId, target.id).subscribe({
      next: (sessions) => {
        this.sessions.set(sessions);
        this.lastUpdated.set(new Date());
        this.removing.set(false);
        this.removeTarget.set(null);
        if (this.isOpen()) {
          this.startSync(); // скидаємо таймер, щоб застаріла фонова відповідь не повернула гостя
        }
      },
      error: () => {
        this.removing.set(false);
        this.removeTarget.set(null);
      },
    });
  }
}
