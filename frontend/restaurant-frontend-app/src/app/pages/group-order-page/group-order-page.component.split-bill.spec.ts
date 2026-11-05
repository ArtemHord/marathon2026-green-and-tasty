// A7 · блок «Split bill» на екрані Owner: лише в стані «Sent to Restaurant», розбивка вантажиться один раз.
import { TestBed, discardPeriodicTasks, fakeAsync, tick } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap } from '@angular/router';
import { of } from 'rxjs';
import { BillSplitResponse } from '@app/models/group-orders/bill-split.model';
import { GroupSessionsResponse } from '@app/models/group-orders/group-sessions-response.model';
import { GroupReservationStatus, GroupStatusResponse } from '@app/models/group-orders/group-status.model';
import { GuestInvite } from '@app/models/reservations/guest-invite.model';
import { GroupOrdersService } from '@app/services/group-orders/group-orders.service';
import { ToastService } from '@app/services/toast/toast.service';
import { GROUP_ORDER_SYNC_MS, GroupOrderPageComponent } from './group-order-page.component';

const caesar = { id: 'd1', name: 'Caesar Salad', price: 180 };
const SESSIONS: GroupSessionsResponse = {
  participants: [
    { id: 'owner', type: 'Owner', name: 'Yevhenii', isSubmitted: true, total: 0, items: [] },
    { id: 's1', type: 'Guest', name: 'Andrii', isSubmitted: true, total: 360, items: [{ dish: caesar, quantity: 2 }] },
  ],
  total: 360,
};
const SPLIT: BillSplitResponse = {
  participants: [
    {
      guestSessionId: 's1',
      name: 'Andrii',
      type: 'Guest',
      items: [{ dishId: 'd1', name: 'Caesar Salad', quantity: 2, price: 180, subtotal: 360 }],
      total: 360,
    },
  ],
  total: 360,
  excludedCanceled: 0,
};
const INVITE = {
  GroupReservationId: 'grp-1',
  LocationAddress: 'Rustaveli Avenue 48',
  MaxGuestCount: 5,
  StartTime: '2026-10-21T16:00:00Z',
  EndTime: '2026-10-21T17:30:00Z',
} as GuestInvite;

function status(value: GroupReservationStatus): GroupStatusResponse {
  return { currentParticipants: 2, maxCapacity: 5, status: value };
}

function buttonByText(root: ParentNode, text: string): HTMLButtonElement | undefined {
  return [...root.querySelectorAll('button')].find((button) => button.textContent?.trim() === text) as
    | HTMLButtonElement
    | undefined;
}

function splitBillHeading(element: HTMLElement): HTMLElement | undefined {
  return [...element.querySelectorAll<HTMLElement>('h2')].find((heading) => heading.textContent?.trim() === 'Split bill');
}

describe('GroupOrderPage · Split bill', () => {
  let service: jasmine.SpyObj<GroupOrdersService>;

  function create() {
    TestBed.configureTestingModule({
      imports: [GroupOrderPageComponent],
      providers: [
        { provide: GroupOrdersService, useValue: service },
        { provide: ToastService, useValue: jasmine.createSpyObj('ToastService', ['success', 'error', 'info']) },
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ reservationId: 'res-1' }) } } },
      ],
    });
    const fixture = TestBed.createComponent(GroupOrderPageComponent);
    fixture.detectChanges();
    return { fixture, element: fixture.nativeElement as HTMLElement };
  }

  beforeEach(() => {
    service = jasmine.createSpyObj<GroupOrdersService>('GroupOrdersService', [
      'getGroupInvite$',
      'getSessions$',
      'getOwnerSessionId$',
      'getStatus$',
      'submit$',
      'getBillSplit$',
    ]);
    service.getGroupInvite$.and.returnValue(of(INVITE));
    service.getSessions$.and.callFake(() => of(structuredClone(SESSIONS)));
    service.getOwnerSessionId$.and.returnValue(of('owner'));
    service.getStatus$.and.callFake(() => of(status('Open')));
    service.submit$.and.returnValue(of([{ dishId: 'd1', quantity: 2 }]));
    service.getBillSplit$.and.callFake(() => of(structuredClone(SPLIT)));
  });

  it('is not shown and not requested while the group is Open', fakeAsync(() => {
    const { fixture, element } = create();
    tick(GROUP_ORDER_SYNC_MS * 2); // polling іде, але розбивку не вантажить
    fixture.detectChanges();

    expect(element.textContent).toContain('Collecting pre-orders');
    expect(splitBillHeading(element)).toBeUndefined();
    expect(element.querySelector('app-split-bill')).toBeNull();
    expect(service.getBillSplit$).not.toHaveBeenCalled();
    discardPeriodicTasks();
  }));

  it('appears after Submit in this tab and loads the split once, by reservationId', fakeAsync(() => {
    const { fixture, element } = create();
    buttonByText(element, 'Submit Pre-Order')!.click();
    fixture.detectChanges();
    buttonByText(element.querySelector('[role="dialog"]')!, 'Submit')!.click();
    fixture.detectChanges();

    expect(element.textContent).toContain('Sent to Restaurant');
    expect(splitBillHeading(element)).toBeDefined();
    expect(element.querySelector('app-split-bill')?.textContent).toContain('Caesar Salad ×2');
    expect(service.getBillSplit$).toHaveBeenCalledOnceWith('res-1');

    tick(GROUP_ORDER_SYNC_MS * 3); // polling після submit зупинений — повторних запитів немає
    fixture.detectChanges();
    expect(service.getBillSplit$).toHaveBeenCalledTimes(1);
    discardPeriodicTasks();
  }));

  it('is shown when an already submitted group is opened', () => {
    service.getStatus$.and.callFake(() => of(status('Submitted')));
    const { fixture, element } = create();
    fixture.detectChanges();

    expect(splitBillHeading(element)).toBeDefined();
    expect(element.querySelector('.split-total-value')?.textContent?.trim()).toBe('$360.00');
    expect(service.getBillSplit$).toHaveBeenCalledOnceWith('res-1');
  });

  it('is not shown for a Closed group', () => {
    service.getStatus$.and.callFake(() => of(status('Closed')));
    const { element } = create();

    expect(splitBillHeading(element)).toBeUndefined();
    expect(service.getBillSplit$).not.toHaveBeenCalled();
  });

  it('appears when background sync brings Submitted (submit from another tab), loaded once', fakeAsync(() => {
    const { fixture, element } = create();
    service.getStatus$.and.callFake(() => of(status('Submitted')));
    tick(GROUP_ORDER_SYNC_MS);
    fixture.detectChanges();

    expect(splitBillHeading(element)).toBeDefined();
    tick(GROUP_ORDER_SYNC_MS * 2);
    fixture.detectChanges();
    expect(service.getBillSplit$).toHaveBeenCalledTimes(1);
    discardPeriodicTasks();
  }));
});
