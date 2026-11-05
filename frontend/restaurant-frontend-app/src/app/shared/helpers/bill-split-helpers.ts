import { OrderItemGuestShare } from '@app/models/dishes/order-item.model';
import { BillSplitItem, BillSplitResponse } from '@app/models/group-orders/bill-split.model';

const MONEY_FORMAT = new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD' });

/** 360 → «$360.00» (той самий формат, що й CurrencyPipe з дефолтною локаллю en-US). */
export function formatMoney(amount: number): string {
  return MONEY_FORMAT.format(amount);
}

/** Рядок під позицією на сторінці офіціанта: «Andrii ×2 · Olena ×1». Немає часток → ''. */
export function formatGuestShares(guests: OrderItemGuestShare[] | null | undefined): string {
  return (guests ?? []).map((share) => `${share.guestName} ×${share.quantity}`).join(' · ');
}

/** «Caesar Salad ×2, Tom Yum ×1» */
export function formatBillSplitItems(items: BillSplitItem[]): string {
  return items.map((item) => `${item.name} ×${item.quantity}`).join(', ');
}

/**
 * Текст для «Copy summary»: рядок на учасника + підсумок.
 *   Andrii — $360.00 (Caesar Salad ×2)
 *   Total — $580.00
 */
export function formatBillSplitSummary(split: BillSplitResponse): string {
  const lines = split.participants.map((person) => {
    const dishes = formatBillSplitItems(person.items);
    const base = `${person.name} — ${formatMoney(person.total)}`;
    return dishes ? `${base} (${dishes})` : base;
  });
  lines.push(`Total — ${formatMoney(split.total)}`);
  return lines.join('\n');
}
