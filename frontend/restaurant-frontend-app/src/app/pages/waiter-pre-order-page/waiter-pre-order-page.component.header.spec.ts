// Заголовок сторінки офіціанта: «Pre-order of customer X, Table 7 (час)».
// Баг: «Unknown table» (API бронювань не віддавав tableName) і час на годину раніше
// (order.reservationTime — рядок, відформатований бекендом в UTC).
import { formatDate } from '@angular/common';
import { TestBed, fakeAsync, flushMicrotasks } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { Order, WaiterPreOrderState } from '@app/models/orders/order.model';
import { PreOrdersService } from '@app/services/pre-orders/pre-orders.service';
import { WaiterPreOrderPageComponent } from './waiter-pre-order-page.component';

const STORAGE_KEY = 'waiterPreOrderData';
const START = '2026-10-21T16:00:00Z';
const END = '2026-10-21T17:30:00Z';
const LOCAL_TIME = `${formatDate(START, 'MMM dd, y, h:mm a', 'en-US')} - ${formatDate(END, 'h:mm a', 'en-US')}`;
const UTC_SNAPSHOT = 'Oct 21, 2026, 4:00 PM - 5:30 PM';

const BASE_ORDER: Order = {
  orderId: 'ord-1',
  reservationAddress: 'Rustaveli Avenue 48',
  reservationTime: UTC_SNAPSHOT,
  reservationId: 'res-1',
  orderItems: [],
  status: 1,
};

describe('WaiterPreOrderPage · title (table and local time)', () => {
  let preOrders: jasmine.SpyObj<PreOrdersService>;

  function create(order: Partial<Order>, nav: Partial<WaiterPreOrderState> = {}): string {
    sessionStorage.setItem(
      STORAGE_KEY,
      JSON.stringify({ reservationId: 'res-1', createdByName: 'Yevhenii', createdByRole: 'Customer', tableName: 'Unknown table', ...nav }),
    );
    preOrders.getByReservationId.and.returnValue(of({ ...BASE_ORDER, ...order }));
    TestBed.configureTestingModule({
      imports: [WaiterPreOrderPageComponent],
      providers: [provideRouter([]), { provide: PreOrdersService, useValue: preOrders }],
    });
    const fixture = TestBed.createComponent(WaiterPreOrderPageComponent);
    fixture.detectChanges();
    flushMicrotasks();
    fixture.detectChanges();
    return (fixture.nativeElement as HTMLElement).querySelector('.title')!.textContent!.replace(/\s+/g, ' ').trim();
  }

  beforeEach(() => {
    preOrders = jasmine.createSpyObj<PreOrdersService>('PreOrdersService', ['getByReservationId']);
  });

  afterEach(() => sessionStorage.removeItem(STORAGE_KEY));

  it('table from order.tableName, without a double «Table» prefix', fakeAsync(() => {
    const title = create({ tableName: 'Table 7', tableNumber: 7 });
    expect(title).toContain(', Table 7 (');
    expect(title).not.toContain('Table Table');
    expect(title).not.toContain('Unknown table');
  }));

  it('falls back to the table label passed by the reservation card', fakeAsync(() => {
    expect(create({}, { tableName: 'Table 3' })).toContain(', Table 3 (');
  }));

  it('a bare number in old navigation data still gets one «Table» prefix', fakeAsync(() => {
    expect(create({}, { tableName: '3' })).toContain(', Table 3 (');
  }));

  it('nothing known → «Unknown table»', fakeAsync(() => {
    expect(create({}, { tableName: '' })).toContain(', Unknown table (');
  }));

  it('time from reservationStartTime / reservationEndTime in local time, not the UTC snapshot', fakeAsync(() => {
    expect(create({ reservationStartTime: START, reservationEndTime: END })).toContain(`(${LOCAL_TIME})`);
  }));

  it('older API: ISO time from the reservation card (navigation state)', fakeAsync(() => {
    expect(create({}, { startTime: START, endTime: END })).toContain(`(${LOCAL_TIME})`);
  }));

  it('no ISO time anywhere → the old reservationTime string', fakeAsync(() => {
    expect(create({})).toContain(`(${UTC_SNAPSHOT})`);
  }));
});
