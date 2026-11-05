// Перехід з картки бронювання customer на екран Owner «Group Pre-Order» (/reservations/:id/group-order).
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { ReservationCustomer, ReservationStatusCustomer } from '@app/models/reservations/reservation.model';
import { API_BASE_URL } from '@app/shared/config/api-base-url.token';
import { Roles } from '@app/shared/constants/roles';
import { CardReservationComponent } from './card-reservation.component';

const RESERVATION: ReservationCustomer = {
  id: 'res-1',
  tableId: 'table-1',
  startTime: new Date('2026-10-21T16:00:00Z'),
  endTime: new Date('2026-10-21T17:30:00Z'),
  guestCount: 3,
  status: ReservationStatusCustomer.Reserved,
  restaurantName: 'Green & Tasty',
  locationAddress: 'Rustaveli Avenue 48',
  waiterId: 'waiter-1',
};

function groupOrderLink(element: HTMLElement): HTMLAnchorElement | undefined {
  return [...element.querySelectorAll('a')].find((link) => link.textContent?.trim() === 'Group Pre-Order');
}

describe('CardReservationComponent · Group Pre-Order link', () => {
  function create(overrides: Partial<ReservationCustomer> = {}, role: string = Roles.CUSTOMER) {
    TestBed.configureTestingModule({
      providers: [{ provide: API_BASE_URL, useValue: 'https://test.api' }, provideRouter([])],
      imports: [CardReservationComponent],
    });
    const fixture = TestBed.createComponent(CardReservationComponent);
    fixture.componentRef.setInput('r', { ...RESERVATION, ...overrides });
    fixture.componentRef.setInput('role', role);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('customer, 2+ guests, Reserved → link to /reservations/{id}/group-order', () => {
    const link = groupOrderLink(create());
    expect(link).toBeDefined();
    expect(link!.getAttribute('href')).toBe('/reservations/res-1/group-order');
  });

  it('also shown while the reservation is In Progress (split bill at the table)', () => {
    expect(groupOrderLink(create({ status: ReservationStatusCustomer.InProgress }))).toBeDefined();
  });

  it('exactly 2 guests is enough (same threshold as Invite Link)', () => {
    expect(groupOrderLink(create({ guestCount: 2 }))).toBeDefined();
  });

  it('not shown for a single guest — there is nobody to invite', () => {
    expect(groupOrderLink(create({ guestCount: 1 }))).toBeUndefined();
  });

  for (const status of [ReservationStatusCustomer.Cancelled, ReservationStatusCustomer.Finished]) {
    it(`not shown for status ${ReservationStatusCustomer[status]}`, () => {
      expect(groupOrderLink(create({ status }))).toBeUndefined();
    });
  }

  it('not shown on the waiter card', () => {
    expect(groupOrderLink(create({}, Roles.WAITER))).toBeUndefined();
  });
});
