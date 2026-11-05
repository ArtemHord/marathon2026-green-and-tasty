import { OrderItem } from '../dishes/order-item.model';

export interface Order {
  orderId: string;
  reservationAddress: string;
  reservationTime: string;
  reservationId: string;
  orderItems: OrderItem[];
  status: number;
  /** ISO UTC (…Z) — форматувати в локальному часі. reservationTime — текстовий знімок у UTC (застарілий). */
  reservationStartTime?: string;
  reservationEndTime?: string;
  tableNumber?: number;
  /** Готовий підпис «Table 7» — без додаткового префікса. */
  tableName?: string;
}

export interface WaiterPreOrderState {
  reservationId: string;
  createdByName: string;
  createdByRole: string;
  /** Повний підпис столу («Table 7») або 'Unknown table'. */
  tableName: string;
  /** ISO-час бронювання — сторінка офіціанта форматує його в локальному часі браузера. */
  startTime?: string;
  endTime?: string;
}
