import { DEMO_RESERVATION_ID, GroupOrdersMockStore, MockHttpError } from './group-orders.mock-store';

describe('GroupOrdersMockStore · bill split', () => {
  let store: GroupOrdersMockStore;
  beforeEach(() => (store = new GroupOrdersMockStore()));

  it('404 before submit — there is no order yet', () => {
    expect(() => store.billSplit()).toThrowMatching((e) => e instanceof MockHttpError && e.status === 404);
  });

  it('after submit: submitted participants only, totals per person and overall', () => {
    store.submit(DEMO_RESERVATION_ID);
    const split = store.billSplit();
    expect(split.participants.map((p) => [p.name, p.type, p.total])).toEqual([
      ['Yevhenii', 'Owner', 26.5],
      ['Kateryna', 'Guest', 24],
    ]);
    expect(split.participants[1].items).toEqual([
      { dishId: 'dish-spring-salad', name: 'Spring Salad', quantity: 2, price: 12, subtotal: 24 },
    ]);
    expect(split.total).toBe(50.5);
    expect(split.excludedCanceled).toBe(0);
  });
});
