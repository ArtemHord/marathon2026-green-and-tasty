import { ComponentFixture, TestBed } from '@angular/core/testing';

import { ReservationConfirmedComponent } from './reservation-confirmed.component';
import { Reservation, ReservationStatus } from '@app/models/reservations/reservation.model';

const RESERVATION: Reservation = {
  id: 'res-1',
  tableId: 'table-uuid',
  customerId: 'cust-1',
  startTime: new Date('2026-10-21T16:00:00Z'),
  endTime: new Date('2026-10-21T17:30:00Z'),
  guestCount: 2,
  status: ReservationStatus.Reserved,
  locationAddress: 'Rustaveli Avenue 48',
  tableCapacity: 4,
  waiterId: 'waiter-1',
};

describe('ReservationConfirmedComponent', () => {
  let component: ReservationConfirmedComponent;
  let fixture: ComponentFixture<ReservationConfirmedComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ReservationConfirmedComponent],
    }).compileComponents();

    fixture = TestBed.createComponent(ReservationConfirmedComponent);
    component = fixture.componentInstance;
    // reservation — обов'язковий input: без нього компонент падає з NG0950
    fixture.componentRef.setInput('reservation', RESERVATION);
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
