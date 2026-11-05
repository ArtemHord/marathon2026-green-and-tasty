// A7 · сторінка офіціанта: під позицією групового pre-order — хто що замовив («Andrii ×2 · Olena ×1»).
import { TestBed, fakeAsync, flushMicrotasks } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { DishListResponse } from '@app/models/dishes/dish-list-response.model';
import { OrderItem } from '@app/models/dishes/order-item.model';
import { Order, WaiterPreOrderState } from '@app/models/orders/order.model';
import { PreOrdersService } from '@app/services/pre-orders/pre-orders.service';
import { WaiterPreOrderPageComponent } from './waiter-pre-order-page.component';

const STORAGE_KEY = 'waiterPreOrderData';
const NAV_STATE: WaiterPreOrderState = {
  reservationId: 'res-1',
  createdByName: 'Yevhenii',
  createdByRole: 'Customer',
  tableName: 'T1',
};

function dish(id: string, name: string): DishListResponse {
  return {
    id,
    name,
    imageUrl: '',
    price: 180,
    state: 'Available',
    weight: '300 g',
    dishType: 'Appetizers',
    description: '',
    calories: '',
    proteins: '',
    carbohydrates: '',
    fats: '',
    vitamins: '',
  };
}

function order(orderItems: OrderItem[]): Order {
  return {
    orderId: 'ord-1',
    reservationAddress: 'Rustaveli Avenue 48',
    reservationTime: '18:00',
    reservationId: 'res-1',
    orderItems,
    status: 1,
  };
}

describe('WaiterPreOrderPage · guest breakdown under an item', () => {
  let preOrders: jasmine.SpyObj<PreOrdersService>;

  function create(items: OrderItem[]) {
    preOrders.getByReservationId.and.returnValue(of(order(items)));
    TestBed.configureTestingModule({
      imports: [WaiterPreOrderPageComponent],
      providers: [provideRouter([]), { provide: PreOrdersService, useValue: preOrders }],
    });
    const fixture = TestBed.createComponent(WaiterPreOrderPageComponent);
    fixture.detectChanges();
    flushMicrotasks(); // firstValueFrom(...).then(...)
    fixture.detectChanges();
    return { fixture, element: fixture.nativeElement as HTMLElement };
  }

  beforeEach(() => {
    sessionStorage.setItem(STORAGE_KEY, JSON.stringify(NAV_STATE));
    preOrders = jasmine.createSpyObj<PreOrdersService>('PreOrdersService', [
      'getByReservationId',
      'confirmDishInOrder',
      'rejectDishInOrder',
    ]);
  });

  afterEach(() => sessionStorage.removeItem(STORAGE_KEY));

  it('loads the order of the reservation from navigation data', fakeAsync(() => {
    create([]);
    expect(preOrders.getByReservationId).toHaveBeenCalledOnceWith('res-1');
  }));

  it('shows «Andrii ×2 · Olena ×1» under an item that has guests', fakeAsync(() => {
    const { element } = create([
      {
        dish: dish('d1', 'Caesar Salad'),
        quantity: 3,
        state: 0,
        guests: [
          { guestSessionId: 's1', guestName: 'Andrii', quantity: 2 },
          { guestSessionId: 's2', guestName: 'Olena', quantity: 1 },
        ],
      },
    ]);

    const lines = element.querySelectorAll('.pre-order-guests');
    expect(lines.length).toBe(1);
    expect(lines[0].textContent).toContain('Andrii ×2 · Olena ×1');
    expect(element.textContent).toContain('Quantity: 3');
  }));

  it('no breakdown line when guests is absent, null or empty (old / regular orders)', fakeAsync(() => {
    const { element } = create([
      { dish: dish('d1', 'Caesar Salad'), quantity: 1, state: 0 },
      { dish: dish('d2', 'Tom Yum'), quantity: 1, state: 0, guests: null },
      { dish: dish('d3', 'Pho'), quantity: 1, state: 0, guests: [] },
    ]);

    expect(element.querySelectorAll('app-dish-card').length).toBe(3);
    expect(element.querySelector('.pre-order-guests')).toBeNull();
  }));

  it('shows the line only for the items that have guests', fakeAsync(() => {
    const { element } = create([
      { dish: dish('d1', 'Caesar Salad'), quantity: 1, state: 0 },
      {
        dish: dish('d2', 'Tom Yum'),
        quantity: 1,
        state: 0,
        guests: [{ guestSessionId: 's2', guestName: 'Olena', quantity: 1 }],
      },
    ]);

    const cards = element.querySelectorAll('app-dish-card');
    expect(cards[0].querySelector('.pre-order-guests')).toBeNull();
    expect(cards[1].querySelector('.pre-order-guests')?.textContent).toContain('Olena ×1');
  }));
});
