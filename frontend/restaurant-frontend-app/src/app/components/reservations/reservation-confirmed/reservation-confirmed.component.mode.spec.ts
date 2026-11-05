// Модалка після редагування бронювання: текст про ОНОВЛЕННЯ і номер столу замість UUID.
import { TestBed } from '@angular/core/testing';
import { Reservation, ReservationStatus } from '@app/models/reservations/reservation.model';
import { ReservationConfirmedComponent } from './reservation-confirmed.component';
import { ReservationConfirmationMode } from './models';

const TABLE_UUID = '6f1c2a9e-1d2b-4c3d-9e8f-0a1b2c3d4e5f';
const RESERVATION: Reservation = {
  id: 'res-1',
  tableId: TABLE_UUID,
  customerId: 'cust-1',
  startTime: new Date('2026-10-21T16:00:00Z'),
  endTime: new Date('2026-10-21T17:30:00Z'),
  guestCount: 3,
  status: ReservationStatus.Reserved,
  locationAddress: 'Rustaveli Avenue 48',
  tableCapacity: 4,
  restaurantName: 'Green & Tasty',
  waiterId: 'waiter-1',
};

describe('ReservationConfirmedComponent · created / updated', () => {
  function create(
    reservation: Partial<Reservation> = {},
    mode?: ReservationConfirmationMode,
    tableNumber?: number,
  ): HTMLElement {
    TestBed.configureTestingModule({ imports: [ReservationConfirmedComponent] });
    const fixture = TestBed.createComponent(ReservationConfirmedComponent);
    fixture.componentRef.setInput('reservation', { ...RESERVATION, ...reservation });
    if (mode) fixture.componentRef.setInput('mode', mode);
    if (tableNumber !== undefined) fixture.componentRef.setInput('tableNumber', tableNumber);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  const text = (element: HTMLElement) => element.textContent!.replace(/\s+/g, ' ');

  it('updated: «Reservation Updated!» and «successfully updated», no «made»', () => {
    const element = create({ tableName: 'Table 7' }, 'updated');
    expect(element.querySelector('h2')?.textContent?.trim()).toBe('Reservation Updated!');
    expect(text(element)).toContain('has been successfully updated.');
    expect(text(element)).not.toContain('successfully made');
  });

  it('created (default) keeps the original wording', () => {
    const element = create({ tableName: 'Table 7' });
    expect(element.querySelector('h2')?.textContent?.trim()).toBe('Reservation Confirmed!');
    expect(text(element)).toContain('has been successfully made.');
  });

  it('shows reservation.tableName instead of the table UUID', () => {
    const element = create({ tableName: 'Table 7', tableNumber: 7 }, 'updated');
    expect(text(element)).toContain('at Table 7');
    expect(text(element)).not.toContain(TABLE_UUID);
  });

  it('only tableNumber from the API → «Table N»', () => {
    expect(text(create({ tableNumber: 5 }, 'updated'))).toContain('at Table 5');
  });

  it('older API: falls back to the number passed by the parent', () => {
    expect(text(create({}, 'updated', 3))).toContain('at Table 3');
  });

  it('no table number at all → the «at Table …» part is omitted, UUID never shown', () => {
    const element = create({}, 'updated');
    expect(text(element)).not.toContain(TABLE_UUID);
    expect(text(element)).not.toContain('at Table');
  });
});
