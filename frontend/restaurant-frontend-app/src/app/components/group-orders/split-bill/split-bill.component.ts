import { CurrencyPipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, input, output, signal } from '@angular/core';
import { Subscription, defer } from 'rxjs';
import { BillSplitResponse } from '@app/models/group-orders/bill-split.model';
import { GroupOrdersService } from '@app/services/group-orders/group-orders.service';
import { ToastService } from '@app/services/toast/toast.service';
import { formatBillSplitSummary } from '@app/shared/helpers/bill-split-helpers';
import { SplitBillLoadState, SplitBillPersonRow } from './models';

/**
 * Блок «Split bill» екрана Owner: хто що замовив і скільки винен.
 * Завантажує GET /reservations/{reservationId}/bill-split один раз при появі (і по «Try again»).
 */
@Component({
  selector: 'app-split-bill',
  imports: [CurrencyPipe],
  templateUrl: './split-bill.component.html',
  styleUrl: './split-bill.component.scss',
  changeDetection: ChangeDetectionStrategy.Eager,
})
export class SplitBillComponent implements OnInit {
  private readonly service = inject(GroupOrdersService);
  private readonly toast = inject(ToastService);

  readonly reservationId = input.required<string>();
  /** Успішно завантажена розбивка — сторінка бере з неї Grand Total і консолідований список (те, що реально в order). */
  readonly loaded = output<BillSplitResponse>();

  private loadSub?: Subscription;

  readonly state = signal<SplitBillLoadState>('loading');
  /** null — розбивки немає (404). */
  readonly split = signal<BillSplitResponse | null>(null);

  readonly rows = computed<SplitBillPersonRow[]>(() =>
    (this.split()?.participants ?? []).map((person, index) => ({
      key: person.guestSessionId ?? `participant-${index}`,
      name: person.name,
      isOwner: person.type === 'Owner',
      dishes: person.items.map((item) => ({
        dishId: item.dishId,
        label: `${item.name} ×${item.quantity}`,
        amount: item.subtotal,
      })),
      total: person.total,
    })),
  );
  readonly total = computed(() => this.split()?.total ?? 0);
  readonly isEmpty = computed(() => this.rows().every((row) => row.dishes.length === 0));
  readonly canCopy = computed(() => this.state() === 'ready' && !this.isEmpty());
  readonly excludedNote = computed(() => {
    const count = this.split()?.excludedCanceled ?? 0;
    if (count <= 0) {
      return '';
    }
    return count === 1
      ? '1 canceled dish is not included in the split.'
      : `${count} canceled dishes are not included in the split.`;
  });

  constructor() {
    inject(DestroyRef).onDestroy(() => this.loadSub?.unsubscribe());
  }

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loadSub?.unsubscribe();
    this.state.set('loading');
    // defer: синхронна помилка в фабриці запиту теж іде в error-гілку, а не валить екран
    this.loadSub = defer(() => this.service.getBillSplit$(this.reservationId())).subscribe({
      next: (split) => {
        this.split.set(split);
        this.state.set('ready');
        this.loaded.emit(split);
      },
      error: (error: unknown) => {
        if (error instanceof HttpErrorResponse && error.status === 404) {
          this.split.set(null); // order ще немає — це порожній стан, а не помилка
          this.state.set('ready');
          return;
        }
        this.state.set('error');
      },
    });
  }

  copy(): void {
    const split = this.split();
    if (!split || !this.canCopy()) {
      return;
    }
    const clipboard = typeof navigator === 'undefined' ? undefined : navigator.clipboard;
    if (!clipboard) {
      this.toast.error("Couldn't copy the summary");
      return;
    }
    clipboard.writeText(formatBillSplitSummary(split)).then(
      () => this.toast.success('Copied'),
      () => this.toast.error("Couldn't copy the summary"),
    );
  }
}
