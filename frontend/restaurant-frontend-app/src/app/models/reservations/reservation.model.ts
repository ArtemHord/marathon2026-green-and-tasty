import { FeedbackReservationResponseItem } from '../feedbacks/feedback-reservation-response-item.model';
import { Order } from '../orders/order.model';

export interface Reservation {
  id: string;
  tableId: string;
  customerId: string;
  startTime: Date;
  endTime: Date;
  guestCount: number;
  status: ReservationStatus;
  locationAddress: string;
  tableCapacity: number;
  restaurantName?: string;
  waiterId: string;
  hasSubmittedPreOrder?: boolean;
  preOrder?: Order;
  preOrders?: Order[];
  guestInviteLinkAvailable?: boolean;
  /** Номер столу (tableId — UUID). Немає, якщо бекенд не зміг завантажити стіл. */
  tableNumber?: number;
  /** Готовий підпис «Table 7». */
  tableName?: string;
}

export enum ReservationStatus {
  Reserved = 0,
  Cancelled = 1,
  InProggress = 2,
  Finished = 3,
}

export interface ReservationCustomer {
  id: string;
  tableId: string;
  startTime: Date;
  endTime: Date;
  guestCount: number;
  status: ReservationStatusCustomer;
  restaurantName: string;
  locationAddress: string;
  feedback?: FeedbackReservationResponseItem;
  tableName?: string;
  tableNumber?: number;
  customerId?: string;
  waiterId: string;
  hasSubmittedPreOrder?: boolean;
  preOrder?: Order;
  preOrders?: Order[];
  createdByName?: string;
  createdByRole?: 'Customer' | 'Waiter';
}

export enum ReservationStatusCustomer {
  Reserved = 0,
  Cancelled = 1,
  InProgress = 2,
  Finished = 3,
}

export interface MakeReservationRequestBody {
  tableId: string;
  startTime: string;
  endTime: string;
  guestCount: number;
  customerEmail?: string | null;
}

export interface CancelReservationResponse {
  message?: string;
  error?: string;
}