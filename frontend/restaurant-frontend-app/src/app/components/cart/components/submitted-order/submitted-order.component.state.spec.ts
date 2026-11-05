// Cart → submitted pre-order: мітка стану позиції. API GET /orders не віддає orderItems[].state,
// тож undefined ≠ «(Unknown)»: мітку просто не показуємо.
import { TestBed } from '@angular/core/testing';
import { DishListResponse } from '@app/models/dishes/dish-list-response.model';
import { OrderItem } from '@app/models/dishes/order-item.model';
import { Order } from '@app/models/orders/order.model';
import { SubmittedOrderComponent } from './submitted-order.component';

function dish(id: string, name: string, price: number): DishListResponse {
  return {
    id, name, price, imageUrl: '', state: 'Available', weight: '300 g', dishType: 'Appetizers', description: '',
    calories: '', proteins: '', carbohydrates: '', fats: '', vitamins: '',
  };
}

function order(orderItems: OrderItem[]): Order {
  return {
    orderId: 'ord-1',
    reservationAddress: 'Rustaveli Avenue 48',
    reservationTime: 'Oct 21, 2026, 4:00 PM - 5:30 PM',
    reservationId: 'res-1',
    orderItems,
    status: 1,
  };
}

describe('SubmittedOrderComponent · item state label', () => {
  function create(items: OrderItem[]) {
    TestBed.configureTestingModule({ imports: [SubmittedOrderComponent] });
    const fixture = TestBed.createComponent(SubmittedOrderComponent);
    fixture.componentRef.setInput('order', order(items));
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('no «(Unknown)» when the API sends no state', () => {
    const item = { dish: dish('d1', 'Caesar Salad', 180), quantity: 1 } as OrderItem; // як приходить з API
    const element = create([item]);
    expect(element.textContent).toContain('Caesar Salad');
    expect(element.textContent).not.toContain('Unknown');
  });

  const labels: [number, string][] = [
    [0, '(Pending)'],
    [1, '(Confirmed)'],
    [2, '(Rejected)'],
  ];
  for (const [state, label] of labels) {
    it(`state ${state} → ${label}`, () => {
      expect(create([{ dish: dish('d1', 'Caesar Salad', 180), quantity: 1, state }]).textContent).toContain(label);
    });
  }

  it('rejected dishes are not in the total', () => {
    const element = create([
      { dish: dish('d1', 'Caesar Salad', 180), quantity: 2, state: 1 },
      { dish: dish('d2', 'Tom Yum', 220), quantity: 1, state: 2 },
    ]);
    expect(element.querySelector('h3')?.textContent?.trim()).toBe('$360.00');
  });
});
