import { BillSplitResponse } from '@app/models/group-orders/bill-split.model';
import {
  aggregateBillSplit,
  formatBillSplitItems,
  formatBillSplitSummary,
  formatGuestShares,
  formatMoney,
} from './bill-split-helpers';

describe('bill-split-helpers', () => {
  describe('formatMoney', () => {
    const cases: [number, string][] = [
      [0, '$0.00'],
      [360, '$360.00'],
      [12.5, '$12.50'],
      [1234, '$1,234.00'],
    ];
    for (const [amount, expected] of cases) {
      it(`${amount} → ${expected}`, () => expect(formatMoney(amount)).toBe(expected));
    }
  });

  describe('formatGuestShares', () => {
    it('joins shares as «Andrii ×2 · Olena ×1»', () => {
      expect(
        formatGuestShares([
          { guestSessionId: 's1', guestName: 'Andrii', quantity: 2 },
          { guestSessionId: 's2', guestName: 'Olena', quantity: 1 },
        ]),
      ).toBe('Andrii ×2 · Olena ×1');
    });

    it('returns an empty string for null, undefined and []', () => {
      expect(formatGuestShares(null)).toBe('');
      expect(formatGuestShares(undefined)).toBe('');
      expect(formatGuestShares([])).toBe('');
    });
  });

  describe('formatBillSplitItems', () => {
    it('lists dishes with quantities, comma separated', () => {
      expect(
        formatBillSplitItems([
          { dishId: 'd1', name: 'Caesar Salad', quantity: 2, price: 180, subtotal: 360 },
          { dishId: 'd2', name: 'Tom Yum', quantity: 1, price: 220, subtotal: 220 },
        ]),
      ).toBe('Caesar Salad ×2, Tom Yum ×1');
    });
  });

  describe('formatBillSplitSummary', () => {
    const split: BillSplitResponse = {
      participants: [
        {
          guestSessionId: 's1',
          name: 'Andrii',
          type: 'Guest',
          items: [{ dishId: 'd1', name: 'Caesar Salad', quantity: 2, price: 180, subtotal: 360 }],
          total: 360,
        },
        {
          guestSessionId: 's2',
          name: 'Olena',
          type: 'Guest',
          items: [{ dishId: 'd2', name: 'Tom Yum', quantity: 1, price: 220, subtotal: 220 }],
          total: 220,
        },
      ],
      total: 580,
      excludedCanceled: 0,
    };

    it('one line per participant, then the total', () => {
      expect(formatBillSplitSummary(split)).toBe(
        'Andrii — $360.00 (Caesar Salad ×2)\nOlena — $220.00 (Tom Yum ×1)\nTotal — $580.00',
      );
    });

    it('participant without dishes gets no parentheses', () => {
      const empty: BillSplitResponse = {
        participants: [{ guestSessionId: null, name: 'Yevhenii', type: 'Owner', items: [], total: 0 }],
        total: 0,
        excludedCanceled: 0,
      };
      expect(formatBillSplitSummary(empty)).toBe('Yevhenii — $0.00\nTotal — $0.00');
    });
  });
});

describe('aggregateBillSplit', () => {
  it('sums quantities and subtotals of the same dish across participants, first-seen order', () => {
    const split: BillSplitResponse = {
      participants: [
        {
          guestSessionId: 'o', name: 'Owner', type: 'Owner', total: 400,
          items: [
            { dishId: 'd2', name: 'Tom Yum', quantity: 1, price: 220, subtotal: 220 },
            { dishId: 'd1', name: 'Caesar Salad', quantity: 1, price: 180, subtotal: 180 },
          ],
        },
        {
          guestSessionId: 's1', name: 'Andrii', type: 'Guest', total: 360,
          items: [{ dishId: 'd1', name: 'Caesar Salad', quantity: 2, price: 180, subtotal: 360 }],
        },
      ],
      total: 760,
      excludedCanceled: 0,
    };
    expect(aggregateBillSplit(split)).toEqual([
      { dishId: 'd2', name: 'Tom Yum', quantity: 1, amount: 220 },
      { dishId: 'd1', name: 'Caesar Salad', quantity: 3, amount: 540 },
    ]);
  });

  it('no participants → []', () => {
    expect(aggregateBillSplit({ participants: [], total: 0, excludedCanceled: 0 })).toEqual([]);
  });
});
