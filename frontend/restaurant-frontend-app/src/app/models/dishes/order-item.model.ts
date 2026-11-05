import { DishListResponse } from './dish-list-response.model';

/** Частка гостя в позиції групового order (GET /orders/{reservationId} → orderItems[].guests). */
export interface OrderItemGuestShare {
  guestSessionId: string;
  guestName: string;
  quantity: number;
}

export interface OrderItem {
  dish: DishListResponse;
  quantity: number;
  /** 0 Pending · 1 Confirmed · 2 Canceled (API: GET/POST /orders). */
  state: number; // later must be replaced with an enum, for now using numbers for speed
  /** Лише для групового pre-order після submit; у старих / звичайних замовленнях відсутнє або null. */
  guests?: OrderItemGuestShare[] | null;
}
