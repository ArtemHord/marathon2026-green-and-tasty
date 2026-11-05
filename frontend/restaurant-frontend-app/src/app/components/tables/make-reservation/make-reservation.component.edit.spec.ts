// Модалка редагування бронювання: без toast «Reservation created successfully.» (його показували при кожному
// натисканні «Edit Reservation», ще до відповіді API) і з підзаголовком про редагування.
import { TestBed } from '@angular/core/testing';
import { Table } from '@app/models/tables/table.model';
import { ToastService } from '@app/services/toast/toast.service';
import { MakeReservationComponent } from './make-reservation.component';

const TABLE = {
  id: 'table-uuid',
  tableNumber: 7,
  capacity: 4,
  locationId: 'loc-1',
  timeRanges: [{ start: '2026-10-21T16:00:00.000Z', end: '2026-10-21T17:30:00.000Z' }],
} as Table;

describe('MakeReservationComponent · edit mode', () => {
  let toast: jasmine.SpyObj<ToastService>;

  function create(mode?: 'create' | 'edit') {
    toast = jasmine.createSpyObj<ToastService>('ToastService', ['success', 'error', 'info']);
    TestBed.configureTestingModule({
      imports: [MakeReservationComponent],
      providers: [{ provide: ToastService, useValue: toast }],
    });
    const fixture = TestBed.createComponent(MakeReservationComponent);
    const component = fixture.componentInstance;
    component.title = 'Edit Reservation';
    component.locationName = 'Rustaveli Avenue 48';
    component.table = TABLE;
    component.selectedTimeRange = TABLE.timeRanges[0];
    component.guests = 2;
    if (mode) component.mode = mode;
    fixture.detectChanges();
    return { fixture, component, element: fixture.nativeElement as HTMLElement };
  }

  it('subtitle talks about editing and shows the table number', () => {
    const { element } = create('edit');
    const subtitle = element.querySelector('.subtitle')!.textContent!.replace(/\s+/g, ' ');
    expect(subtitle).toContain('You are editing your reservation at');
    expect(subtitle).toContain('Table 7');
    expect(subtitle).not.toContain('table-uuid');
  });

  it('create mode keeps «You are making a reservation at»', () => {
    expect(create().element.querySelector('.subtitle')!.textContent).toContain('You are making a reservation at');
  });

  it('submitting emits the changes and shows NO «created» toast (the parent toasts after the API answers)', () => {
    const { component, element } = create('edit');
    const emitted = jasmine.createSpy('makeReservation');
    component.makeReservation.subscribe(emitted);

    const button = [...element.querySelectorAll('button')].find((item) => item.textContent?.trim() === 'Edit Reservation')!;
    button.click();

    expect(emitted).toHaveBeenCalledOnceWith({ tableId: 'table-uuid', timeRange: TABLE.timeRanges[0], guests: 2 });
    expect(toast.success).not.toHaveBeenCalled();
  });

  it('close icon button has an accessible name', () => {
    expect(create('edit').element.querySelector('.close-btn')?.getAttribute('aria-label')).toBe('Close');
  });
});
