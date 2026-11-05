import { ParticipantType } from './participant.model';

/** Позиція учасника в розбивці рахунку. */
export interface BillSplitItem {
  dishId: string;
  name: string;
  quantity: number;
  price: number;
  subtotal: number;
}

/** Учасник розбивки: Owner першим, далі гості. guestSessionId може бути null для бронювання без групи. */
export interface BillSplitParticipant {
  guestSessionId: string | null;
  name: string;
  type: ParticipantType;
  items: BillSplitItem[];
  total: number;
}

/** GET /reservations/{reservationId}/bill-split */
export interface BillSplitResponse {
  participants: BillSplitParticipant[];
  total: number;
  /** Кількість скасованих позицій order, які не увійшли в суми. */
  excludedCanceled: number;
}
