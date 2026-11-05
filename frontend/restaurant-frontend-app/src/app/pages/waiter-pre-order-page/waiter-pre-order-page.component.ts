import { PreOrdersService } from '@app/services/pre-orders/pre-orders.service';
import {
  ChangeDetectorRef,
  Component,
  inject,
  Input,
  LOCALE_ID,
  OnInit,
  ChangeDetectionStrategy,
} from '@angular/core';
import { formatDate } from '@angular/common';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { OrderItem } from '@app/models/dishes/order-item.model';
import { DishCardComponent } from '@app/shared/custom-components/dish-card/dish-card.component';
import { firstValueFrom } from 'rxjs';
import { WaiterPreOrderState } from '@app/models/orders/order.model';
import { CustomButtonComponent } from '@app/shared/custom-components/custom-button/custom-button.component';
import { formatGuestShares } from '@app/shared/helpers/bill-split-helpers';
import { toTableLabel } from '@app/shared/helpers/table-label';

@Component({
  selector: 'app-waiter-pre-order-page',
  imports: [DishCardComponent, RouterLink, CustomButtonComponent],
  templateUrl: './waiter-pre-order-page.component.html',
  changeDetection: ChangeDetectionStrategy.Eager,
  styleUrl: './waiter-pre-order-page.component.scss',
})
export class WaiterPreOrderPageComponent implements OnInit {
  reservationLabel = '';
  orderItems: OrderItem[] = [];
  /** dishId → «Andrii ×2 · Olena ×1»; лише для позицій групового pre-order з guests. */
  guestLabels: Record<string, string> = {};

  reservationId = '';
  createdByName = '';
  createdByRole = '';
  /** Повний підпис столу: «Table 7» / «Unknown table». */
  tableName = '';
  reservationTime = '';
  @Input() customClass = '';

  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private cdr = inject(ChangeDetectorRef);
  private readonly locale = inject(LOCALE_ID);

  preOrdersService = inject(PreOrdersService);

  ngOnInit(): void {
    const navState = this.router.getCurrentNavigation()?.extras.state as
      | WaiterPreOrderState
      | undefined;
    let data = navState;

    if (!data?.reservationId) {
      const stored = sessionStorage.getItem('waiterPreOrderData');
      if (stored) {
        data = JSON.parse(stored);
        console.log('data', data);
      }
    }

    if (!data?.reservationId) {
      console.warn('Reservation ID not found');
      return;
    }

    this.createdByName = data.createdByName;
    this.createdByRole = data.createdByRole;
    this.tableName = toTableLabel(data.tableName);
    this.reservationId = data.reservationId;
    const navigationTime = this.formatReservationTime(data.startTime, data.endTime);

    firstValueFrom(this.preOrdersService.getByReservationId(data.reservationId))
      .then((order) => {
        console.log('order', order);
        console.log('ORDER FROM SERVER:', JSON.stringify(order, null, 2));

        this.orderItems = order.orderItems ?? [];
        this.guestLabels = this.buildGuestLabels(this.orderItems);
        // order.reservationTime — текстовий знімок, відформатований бекендом в UTC → «на годину раніше».
        // Показуємо ISO-час у локальному часі (як картка): з order, потім із navigation state, потім старий рядок.
        this.reservationTime =
          this.formatReservationTime(order.reservationStartTime, order.reservationEndTime) ||
          navigationTime ||
          order.reservationTime ||
          '';
        // order.tableName уже «Table 7» — без додаткового префікса
        this.tableName = order.tableName || this.tableName;
        // this.refreshOrder();
      })
      .catch((err) => {
        console.error('Error loading order:', err);
      });
  }

  /** «Oct 21, 2026, 7:00 PM - 8:30 PM» у часовій зоні браузера; без ISO-часу → ''. */
  private formatReservationTime(startTime?: string, endTime?: string): string {
    if (!startTime || !endTime || Number.isNaN(Date.parse(startTime)) || Number.isNaN(Date.parse(endTime))) {
      return '';
    }
    const start = formatDate(startTime, 'MMM dd, y, h:mm a', this.locale);
    const end = formatDate(endTime, 'h:mm a', this.locale);
    return `${start} - ${end}`;
  }

  private buildGuestLabels(items: OrderItem[]): Record<string, string> {
    const labels: Record<string, string> = {};
    for (const item of items) {
      const label = formatGuestShares(item.guests);
      if (label) {
        labels[item.dish.id] = label;
      }
    }
    return labels;
  }

  onRemove(dishId: string) {
    this.orderItems = this.orderItems.filter((item) => item.dish.id !== dishId);
  }

  confirmDish(dishId: string) {
    this.preOrdersService.confirmDishInOrder(this.reservationId, dishId).subscribe({
      next: () => {
        const index = this.orderItems.findIndex((i) => i.dish.id === dishId);
        if (index !== -1) {
          const item = this.orderItems[index];
          const updatedItem: OrderItem = {
            ...item,
            dish: {
              ...item.dish,
              status: 'Confirmed',
            },
            state: 1, // Update state to 'Confirmed'
          };
          this.orderItems.splice(index, 1, updatedItem);
          this.orderItems = [...this.orderItems];
          this.cdr.detectChanges();
          console.log('Updated orderItems:', this.orderItems);
        }
      },
      error: (err) => {
        console.error(err);
      },
    });
  }

  rejectDish(dishId: string) {
    this.preOrdersService.rejectDishInOrder(this.reservationId, dishId).subscribe({
      next: () => {
        const index = this.orderItems.findIndex((i) => i.dish.id === dishId);
        if (index !== -1) {
          const item = this.orderItems[index];
          const updatedItem: OrderItem = {
            ...item,
            dish: {
              ...item.dish,
              status: 'Rejected',
            },
            state: 2, // Update state to 'Rejected'
          };
          this.orderItems.splice(index, 1, updatedItem);
          this.orderItems = [...this.orderItems];
          this.cdr.detectChanges();
          console.log('Dish rejected and updated locally:', updatedItem);
          console.log('Updated orderItems:', this.orderItems);
        }
      },
      error: (err) => {
        console.error('Failed to reject dish:', err);
      },
    });
  }

  // rejectDish(dishId: string) {
  //   this.preOrdersService.rejectDishInOrder(this.reservationId, dishId).subscribe({
  //     next: async () => {
  //       const order = await firstValueFrom(
  //         this.preOrdersService.getByReservationId(this.reservationId)
  //       );
  //       this.orderItems = order.orderItems ?? [];
  //       this.cdr.detectChanges();
  //       console.log('Order reloaded:', this.orderItems);
  //     },
  //     error: (err) => {
  //       console.error('Failed to reject dish:', err);
  //     },
  //   });
  // }

  // confirmDish(dishId: string) {
  //   this.preOrdersService.confirmDishInOrder(this.reservationId, dishId).subscribe({
  //     next: async () => {
  //       const index = this.orderItems.findIndex((i) => i.dish.id === dishId);
  //       if (index !== -1) {
  //         const item = this.orderItems[index];
  //         const updatedItem: OrderItem = {
  //           ...item,
  //           dish: {
  //             ...item.dish,
  //             status: 'Confirmed',
  //           },
  //         };
  //         this.orderItems.splice(index, 1, updatedItem);
  //         this.orderItems = [...this.orderItems];
  //         this.cdr.detectChanges();
  //         console.log('Locally updated orderItems (confirmed):', this.orderItems);
  //       }

  //       const order = await firstValueFrom(
  //         this.preOrdersService.getByReservationId(this.reservationId)
  //       );
  //       this.orderItems = order.orderItems ?? [];
  //       this.cdr.detectChanges();
  //       console.log('Order reloaded (confirmed):', this.orderItems);
  //       console.log('ORDER FROM SERVER:', JSON.stringify(this.orderItems, null, 2));
  //     },
  //     error: (err) => {
  //       console.error(err);
  //     },
  //   });
  // }

  // rejectDish(dishId: string) {
  //   this.preOrdersService.rejectDishInOrder(this.reservationId, dishId).subscribe({
  //     next: async () => {
  //       const index = this.orderItems.findIndex((i) => i.dish.id === dishId);
  //       if (index !== -1) {
  //         const item = this.orderItems[index];
  //         const updatedItem: OrderItem = {
  //           ...item,
  //           dish: {
  //             ...item.dish,
  //             status: 'Rejected',
  //           },
  //         };
  //         this.orderItems.splice(index, 1, updatedItem);
  //         this.orderItems = [...this.orderItems];
  //         this.cdr.detectChanges();
  //         console.log('Locally updated orderItems (rejected):', this.orderItems);
  //       }

  //       const order = await firstValueFrom(
  //         this.preOrdersService.getByReservationId(this.reservationId)
  //       );
  //       this.orderItems = order.orderItems ?? [];
  //       this.cdr.detectChanges();
  //       console.log('Order reloaded (rejected):', this.orderItems);
  //       console.log('ORDER FROM SERVER:', JSON.stringify(this.orderItems, null, 2));
  //     },
  //     error: (err) => {
  //       console.error('Failed to reject dish:', err);
  //     },
  //   });
  // }
}
