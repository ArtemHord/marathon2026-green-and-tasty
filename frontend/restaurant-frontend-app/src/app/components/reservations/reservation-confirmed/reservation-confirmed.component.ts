import { DatePipe } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  input,
  output,
  signal,
} from '@angular/core';

import {
  Reservation,
  ReservationStatus,
} from '@app/models/reservations/reservation.model';
import { CustomButtonComponent } from '@app/shared/custom-components/custom-button/custom-button.component';
import { ReservationConfirmationMode } from './models';

@Component({
  selector: 'app-reservation-confirmed',
  standalone: true,
  imports: [
    DatePipe,
    CustomButtonComponent,
  ],
  templateUrl: './reservation-confirmed.component.html',
  styleUrl: './reservation-confirmed.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    '[class.fade-out]': 'isFading()',
  },
})
export class ReservationConfirmedComponent {
  readonly reservation = input.required<Reservation>();
  readonly customClass = input('');
  /** 'updated' — після редагування бронювання (інші заголовок і текст). */
  readonly mode = input<ReservationConfirmationMode>('created');
  /** Запасний номер столу, якщо API не віддав tableName/tableNumber. reservation.tableId — UUID, його не показуємо. */
  readonly tableNumber = input<number | null | undefined>(null);

  readonly closed = output<void>();
  readonly editReservation = output<void>();
  readonly cancelReservation = output<void>();

  readonly isFading = signal(false);

  protected readonly ReservationStatus = ReservationStatus;

  protected readonly title = computed(() =>
    this.mode() === 'updated' ? 'Reservation Updated!' : 'Reservation Confirmed!',
  );
  protected readonly resultText = computed(() =>
    this.mode() === 'updated' ? 'has been successfully updated.' : 'has been successfully made.',
  );
  /** reservation.tableName («Table 7») → reservation.tableNumber → номер від батька; UUID не показуємо. */
  protected readonly tableLabel = computed(() => {
    const reservation = this.reservation();
    const tableNumber = reservation.tableNumber || this.tableNumber();
    return reservation.tableName || (tableNumber ? `Table ${tableNumber}` : '');
  });

  onClose(): void {
    if (this.isFading()) {
      return;
    }

    this.isFading.set(true);
    this.closed.emit();
  }

  onEdit(): void {
    this.editReservation.emit();
  }

  onCancel(): void {
    this.cancelReservation.emit();
  }

  onBackdropClick(event: MouseEvent): void {
    if (event.target === event.currentTarget) {
      this.onClose();
    }
  }
}
