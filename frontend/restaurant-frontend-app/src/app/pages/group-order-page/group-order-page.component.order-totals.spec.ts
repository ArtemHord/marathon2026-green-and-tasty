// Sent to Restaurant: Grand Total і «Consolidated pre-order» — з bill-split (що реально в order, без Canceled),
// а не з кошиків гостей. Поки розбивку не завантажено / помилка — старий розрахунок (сумісність з L2/L3).
import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute, convertToParamMap } from '@angular/router';
import { Subject, of, throwError } from 'rxjs';
import { BillSplitResponse } from '@app/models/group-orders/bill-split.model';
import { GroupSessionsResponse } from '@app/models/group-orders/group-sessions-response.model';
import { GroupReservationStatus, GroupStatusResponse } from '@app/models/group-orders/group-status.model';
import { GuestInvite } from '@app/models/reservations/guest-invite.model';
import { GroupOrdersService } from '@app/services/group-orders/group-orders.service';
import { ToastService } from '@app/services/toast/toast.service';
import { GroupOrderPageComponent } from './group-order-page.component';

const caesar = { id: 'd1', name: 'Caesar Salad', price: 180 };
const tomYum = { id: 'd2', name: 'Tom Yum', price: 220 };
// кошики: Caesar ×3 + Tom Yum ×1 = 760
const SESSIONS: GroupSessionsResponse = {
  participants: [
    { id: 'owner', type: 'Owner', name: 'Yevhenii', isSubmitted: true, total: 180, items: [{ dish: caesar, quantity: 1 }] },
    {
      id: 's1', type: 'Guest', name: 'Andrii', isSubmitted: true, total: 580,
      items: [{ dish: caesar, quantity: 2 }, { dish: tomYum, quantity: 1 }],
    },
  ],
  total: 760,
};
// офіціант скасував Tom Yum: в order лишився лише Caesar ×3 = 540
const SPLIT: BillSplitResponse = {
  participants: [
    {
      guestSessionId: 'owner-grp-1', name: 'Yevhenii', type: 'Owner', total: 180,
      items: [{ dishId: 'd1', name: 'Caesar Salad', quantity: 1, price: 180, subtotal: 180 }],
    },
    {
      guestSessionId: 's1', name: 'Andrii', type: 'Guest', total: 360,
      items: [{ dishId: 'd1', name: 'Caesar Salad', quantity: 2, price: 180, subtotal: 360 }],
    },
  ],
  total: 540,
  excludedCanceled: 1,
};
const INVITE = {
  GroupReservationId: 'grp-1', LocationAddress: 'Rustaveli Avenue 48', MaxGuestCount: 5,
  StartTime: '2026-10-21T16:00:00Z', EndTime: '2026-10-21T17:30:00Z',
} as GuestInvite;

function status(value: GroupReservationStatus): GroupStatusResponse {
  return { currentParticipants: 2, maxCapacity: 5, status: value };
}

describe('GroupOrderPage · totals after Sent come from the order (bill-split)', () => {
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
    fixture.detectChanges(); // (loaded) з блоку Split bill оновлює підсумки сторінки
    const element = fixture.nativeElement as HTMLElement;
    return {
      fixture,
      grandTotal: () => element.querySelector('.grand-total-value')?.textContent?.trim(),
      lines: () =>
        [...element.querySelectorAll('.consolidated-item')].map((item) =>
          [item.querySelector('.consolidated-name')?.textContent?.trim(), item.querySelector('.consolidated-price')?.textContent?.trim()],
        ),
    };
  }

  beforeEach(() => {
    service = jasmine.createSpyObj<GroupOrdersService>('GroupOrdersService', [
      'getGroupInvite$', 'getSessions$', 'getOwnerSessionId$', 'getStatus$', 'submit$', 'getBillSplit$',
    ]);
    service.getGroupInvite$.and.returnValue(of(INVITE));
    service.getSessions$.and.callFake(() => of(structuredClone(SESSIONS)));
    service.getOwnerSessionId$.and.returnValue(of('owner'));
    service.getStatus$.and.callFake(() => of(status('Submitted')));
    service.getBillSplit$.and.callFake(() => of(structuredClone(SPLIT)));
  });

  it('Grand Total = billSplit.total (canceled dish excluded)', () => {
    expect(create().grandTotal()).toBe('$540.00');
  });

  it('consolidated list = items of all participants aggregated by dishId', () => {
    expect(create().lines()).toEqual([['Caesar Salad × 3', '$540.00']]);
  });

  it('while the split is loading — the old calculation from carts', () => {
    service.getBillSplit$.and.returnValue(new Subject<BillSplitResponse>());
    const page = create();
    expect(page.grandTotal()).toBe('$760.00');
    expect(page.lines()).toEqual([['Caesar Salad × 3', '$540.00'], ['Tom Yum × 1', '$220.00']]);
  });

  it('split failed to load — falls back to the old calculation', () => {
    service.getBillSplit$.and.returnValue(throwError(() => new HttpErrorResponse({ status: 500 })));
    expect(create().grandTotal()).toBe('$760.00');
  });

  it('Open group — bill-split is not used (consolidatePreOrder as before)', () => {
    service.getStatus$.and.callFake(() => of(status('Open')));
    const page = create();
    expect(page.grandTotal()).toBe('$760.00');
    expect(service.getBillSplit$).not.toHaveBeenCalled();
    page.fixture.destroy(); // зупиняє polling
  });
});
