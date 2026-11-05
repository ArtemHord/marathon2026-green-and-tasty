// Owner-сесія з реального бекенду (порожня, isSubmitted: false) і повторний submit з іншої вкладки.
import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute, convertToParamMap } from '@angular/router';
import { of, throwError } from 'rxjs';
import { GroupOrdersService } from '@app/services/group-orders/group-orders.service';
import { ToastService } from '@app/services/toast/toast.service';
import { GroupSessionsResponse } from '@app/models/group-orders/group-sessions-response.model';
import { GroupStatusResponse } from '@app/models/group-orders/group-status.model';
import { GuestInvite } from '@app/models/reservations/guest-invite.model';
import { GroupOrderPageComponent } from './group-order-page.component';

const caesar = { id: 'dish1', name: 'Caesar Salad', price: 180 };
const tomYum = { id: 'dish2', name: 'Tom Yum', price: 220 };

const SESSIONS: GroupSessionsResponse = {
  participants: [
    { id: 'owner-grp-1', type: 'Owner', name: 'Yevhenii', isSubmitted: false, total: 0, items: [] },
    { id: 'andrii', type: 'Guest', name: 'Andrii', isSubmitted: true, total: 360, items: [{ dish: caesar, quantity: 2 }] },
    { id: 'olena', type: 'Guest', name: 'Olena', isSubmitted: false, total: 220, items: [{ dish: tomYum, quantity: 1 }] },
  ],
  total: 580,
};
const INVITE = { GroupReservationId: 'grp-1', LocationAddress: 'Rustaveli Avenue 48', MaxGuestCount: 5,
  StartTime: '2026-10-21T16:00:00Z', EndTime: '2026-10-21T17:30:00Z' } as GuestInvite;
const status = (value: GroupStatusResponse['status']): GroupStatusResponse => ({ currentParticipants: 3, maxCapacity: 5, status: value });

function buttonByText(root: ParentNode, text: string): HTMLButtonElement | undefined {
  return [...root.querySelectorAll('button')].find((b) => b.textContent?.trim() === text) as HTMLButtonElement | undefined;
}

describe('GroupOrderPageComponent · owner-сесія і повторний submit', () => {
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

  function submitViaDialog(fixture: ReturnType<typeof create>['fixture'], element: HTMLElement): void {
    buttonByText(element, 'Submit Pre-Order')!.click();
    fixture.detectChanges();
    buttonByText(element.querySelector('[role="dialog"]')!, 'Submit')!.click();
    fixture.detectChanges();
  }

  beforeEach(() => {
    service = jasmine.createSpyObj<GroupOrdersService>('GroupOrdersService', [
      'getGroupInvite$', 'getSessions$', 'getOwnerSessionId$', 'getStatus$', 'submit$', 'removeSession$',
    ]);
    service.getGroupInvite$.and.returnValue(of(INVITE));
    service.getSessions$.and.callFake(() => of(structuredClone(SESSIONS)));
    service.getOwnerSessionId$.and.returnValue(of('owner-grp-1'));
    service.getStatus$.and.callFake(() => of(status('Open')));
  });

  describe('F4 · жовта плашка не рахує Owner', () => {
    it('незасабмічена порожня owner-сесія не входить у лічильник: лише Olena', () => {
      const text = create().element.querySelector('.notice')?.textContent ?? '';
      expect(text).toMatch(/1 participant hasn.t submitted/);
    });

    it('плашки немає, якщо не підтвердив лише Owner', () => {
      const ownerOnly = structuredClone(SESSIONS);
      ownerOnly.participants[2].isSubmitted = true;
      service.getSessions$.and.returnValue(of(ownerOnly));

      expect(create().element.querySelector('.notice')).toBeNull();
    });
  });

  describe('F5c · submit 400 (група вже відправлена з іншої вкладки)', () => {
    beforeEach(() => {
      service.submit$.and.returnValue(throwError(() => new HttpErrorResponse({ status: 400 })));
    });

    it('один тихий перезапит статусу і сесій; статус Submitted → read-only', () => {
      const { fixture, element } = create();
      service.getStatus$.and.callFake(() => of(status('Submitted')));

      submitViaDialog(fixture, element);

      expect(service.getSessions$.calls.allArgs().filter(([, options]) => options?.silent === true).length).toBe(1);
      expect(service.getStatus$).toHaveBeenCalledWith('grp-1', { silent: true });
      expect(element.textContent).toContain('Sent to Restaurant');
      expect(buttonByText(element, 'Submit Pre-Order')).toBeUndefined();
    });

    it('статус лишився Open — кнопку Submit можна натиснути знову', () => {
      const { fixture, element } = create();

      submitViaDialog(fixture, element);

      expect(service.getSessions$).toHaveBeenCalledTimes(2);
      expect(buttonByText(element, 'Submit Pre-Order')?.disabled).toBeFalse();
    });

    it('інші помилки (500) — без перезапиту', () => {
      service.submit$.and.returnValue(throwError(() => new HttpErrorResponse({ status: 500 })));
      const { fixture, element } = create();

      submitViaDialog(fixture, element);

      expect(service.getSessions$).toHaveBeenCalledTimes(1);
    });
  });
});
