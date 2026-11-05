// Waiter → «Pre-order»: що картка передає сторінці офіціанта (стіл і ISO-час бронювання).
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { Order, WaiterPreOrderState } from '@app/models/orders/order.model';
import { ReservationCustomer, ReservationStatusCustomer } from '@app/models/reservations/reservation.model';
import { API_BASE_URL } from '@app/shared/config/api-base-url.token';
import { Roles } from '@app/shared/constants/roles';
import { CardReservationComponent } from './card-reservation.component';

const RESERVATION: ReservationCustomer = {
  id: 'res-1',
  tableId: '0b9c7c1e-uuid',
  startTime: new Date('2026-10-21T16:00:00Z'),
  endTime: new Date('2026-10-21T17:30:00Z'),
  guestCount: 2,
  status: ReservationStatusCustomer.Reserved,
  restaurantName: 'Green & Tasty',
  locationAddress: 'Rustaveli Avenue 48',
  waiterId: 'waiter-1',
  createdByName: 'Yevhenii',
  createdByRole: 'Customer',
  preOrder: { orderItems: [{}] } as unknown as Order, // є позиції → кнопка веде на сторінку офіціанта
};

describe('CardReservationComponent · waiter pre-order navigation data', () => {
  let navigate: jasmine.Spy;

  function openPreOrder(overrides: Partial<ReservationCustomer>, selectedTableLabel = ''): WaiterPreOrderState {
    TestBed.configureTestingModule({
      providers: [{ provide: API_BASE_URL, useValue: 'https://test.api' }, provideRouter([])],
      imports: [CardReservationComponent],
    });
    navigate = spyOn(TestBed.inject(Router), 'navigate').and.resolveTo(true);
    const fixture = TestBed.createComponent(CardReservationComponent);
    fixture.componentRef.setInput('r', { ...RESERVATION, ...overrides });
    fixture.componentRef.setInput('role', Roles.WAITER);
    fixture.componentRef.setInput('selectedTableLabel', selectedTableLabel);
    fixture.detectChanges();

    const button = [...(fixture.nativeElement as HTMLElement).querySelectorAll('button')].find(
      (item) => item.textContent?.trim() === 'Pre-order',
    )!;
    button.click();
    return navigate.calls.mostRecent().args[1].state as WaiterPreOrderState;
  }

  afterEach(() => sessionStorage.removeItem('waiterPreOrderData'));

  it('uses reservation.tableName from the API («Table 7»)', () => {
    expect(openPreOrder({ tableName: 'Table 7', tableNumber: 7 }).tableName).toBe('Table 7');
  });

  it('older API without tableName: the label the card already shows', () => {
    expect(openPreOrder({}, 'Table 4').tableName).toBe('Table 4');
  });

  it('nothing known → «Unknown table», never the table UUID', () => {
    const state = openPreOrder({}, 'Table (Unknown)');
    expect(state.tableName).toBe('Unknown table');
    expect(state.tableName).not.toContain(RESERVATION.tableId);
  });

  it('passes ISO start/end so the page can show local time', () => {
    const state = openPreOrder({ tableName: 'Table 7' });
    expect(state.startTime).toBe('2026-10-21T16:00:00.000Z');
    expect(state.endTime).toBe('2026-10-21T17:30:00.000Z');
  });
});
