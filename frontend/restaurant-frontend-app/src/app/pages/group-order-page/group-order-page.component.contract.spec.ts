// Контракт екрана Owner з реальним бекендом (docs/features/group-preorder.md, A3 кроки 3–5).
// Доповнює homework-tests: один таймер, exhaustMap, read-only після submit / з іншої вкладки,
// подвійний клік на Remove Guest, 404 guest-session-id, дані у форматі справжнього API.
import { TestBed, discardPeriodicTasks, fakeAsync, tick } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { Observable, Subject, of, throwError } from 'rxjs';
import { GroupOrdersService } from '@app/services/group-orders/group-orders.service';
import { ToastService } from '@app/services/toast/toast.service';
import { GroupSessionsResponse } from '@app/models/group-orders/group-sessions-response.model';
import { GroupStatusResponse } from '@app/models/group-orders/group-status.model';
import { SubmittedDish } from '@app/models/group-orders/submit-group-pre-order.model';
import { GuestInvite } from '@app/models/reservations/guest-invite.model';
import { GroupOrderPageComponent, GROUP_ORDER_SYNC_MS } from './group-order-page.component';

// Повний DishResponse, як його віддає бекенд (зайві поля не мають заважати).
const caesar = { id: 'dish1', name: 'Caesar Salad', price: 180, description: 'd', dishType: 'MainCourse', weight: '200 g', imageUrl: '', state: 'Available' };
const tomYum = { id: 'dish2', name: 'Tom Yum', price: 220, description: 'd', dishType: 'MainCourse', weight: '300 g', imageUrl: '', state: 'Available' };

/**
 * Snapshot у форматі справжнього бекенду: owner-сесія створюється ліниво — порожня і не submitted,
 * owner іде першим; `total` з API = 760 (усі, включно з несабміченою Olena).
 */
const REAL_SESSIONS: GroupSessionsResponse = {
  participants: [
    { id: 'owner-grp-1', type: 'Owner', name: 'Yevhenii', isSubmitted: false, total: 0, items: [] },
    { id: 'andrii', type: 'Guest', name: 'Andrii', isSubmitted: true, total: 360, items: [{ dish: caesar, quantity: 2 }] },
    { id: 'kateryna', type: 'Guest', name: 'Kateryna', isSubmitted: true, total: 180, items: [{ dish: caesar, quantity: 1 }] },
    { id: 'olena', type: 'Guest', name: 'Olena', isSubmitted: false, total: 220, items: [{ dish: tomYum, quantity: 1 }] },
  ],
  total: 760,
};
const INVITE = { GroupReservationId: 'grp-1', LocationAddress: 'Rustaveli Avenue 48', MaxGuestCount: 5,
  StartTime: '2026-10-21T16:00:00Z', EndTime: '2026-10-21T17:30:00Z' } as GuestInvite;

const status = (value: GroupStatusResponse['status']): GroupStatusResponse => ({ currentParticipants: 4, maxCapacity: 5, status: value });

function buttonByText(root: ParentNode, text: string): HTMLButtonElement | undefined {
  return [...root.querySelectorAll('button')].find((b) => b.textContent?.trim() === text) as HTMLButtonElement | undefined;
}

function buttonsByText(root: ParentNode, text: string): HTMLButtonElement[] {
  return [...root.querySelectorAll('button')].filter((b) => b.textContent?.trim() === text) as HTMLButtonElement[];
}

describe('Контракт · екран Owner з реальним API', () => {
  let service: jasmine.SpyObj<GroupOrdersService>;
  let toast: jasmine.SpyObj<ToastService>;

  function create() {
    TestBed.configureTestingModule({
      imports: [GroupOrderPageComponent],
      providers: [
        { provide: GroupOrdersService, useValue: service },
        { provide: ToastService, useValue: toast },
        provideRouter([]),
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ reservationId: 'res-1' }) } } },
      ],
    });
    const fixture = TestBed.createComponent(GroupOrderPageComponent);
    fixture.detectChanges();
    return { fixture, component: fixture.componentInstance, element: fixture.nativeElement as HTMLElement };
  }

  /** Кількість фонових (silent) запитів sessions. */
  const silentSessionCalls = () => service.getSessions$.calls.allArgs().filter(([, options]) => options?.silent === true).length;

  beforeEach(() => {
    service = jasmine.createSpyObj<GroupOrdersService>('GroupOrdersService', [
      'getGroupInvite$', 'getSessions$', 'getOwnerSessionId$', 'getStatus$', 'submit$', 'removeSession$',
    ]);
    toast = jasmine.createSpyObj<ToastService>('ToastService', ['success', 'error', 'info']);
    service.getGroupInvite$.and.returnValue(of(INVITE));
    service.getSessions$.and.callFake(() => of(structuredClone(REAL_SESSIONS)));
    service.getOwnerSessionId$.and.returnValue(of('owner-grp-1'));
    service.getStatus$.and.callFake(() => of(status('Open')));
  });

  describe('Grand Total на даних справжнього API', () => {
    it('$540 (лише submitted), а не total = $760 з API; owner-сесія — «My Pre-Order»', () => {
      const { element } = create();
      const grandTotal = element.querySelector('.grand-total')?.textContent ?? '';

      expect(grandTotal).toContain('$540.00');
      expect(grandTotal).not.toContain('$760.00');
      expect(element.textContent).toContain('My Pre-Order');
      expect(element.textContent).toContain('Caesar Salad × 3');
      expect(element.textContent).not.toContain('Tom Yum × 1');
    });

    it('Submit веде на POST з groupReservationId з invite і reservationId з URL', () => {
      service.submit$.and.returnValue(of([{ dishId: 'dish1', quantity: 3 }]));
      const { fixture, element } = create();

      buttonByText(element, 'Submit Pre-Order')!.click();
      fixture.detectChanges();
      buttonByText(element.querySelector('[role="dialog"]')!, 'Submit')!.click();
      fixture.detectChanges();

      expect(service.submit$).toHaveBeenCalledOnceWith('grp-1', 'res-1');
      expect(toast.success).toHaveBeenCalledOnceWith('Pre-order sent to the restaurant');
    });
  });

  describe('polling: один таймер', () => {
    it('стартує лише поки Open: перше завантаження не silent, далі кожні 15 с — silent', fakeAsync(() => {
      create();
      expect(service.getSessions$).toHaveBeenCalledOnceWith('grp-1');

      tick(GROUP_ORDER_SYNC_MS - 1);
      expect(silentSessionCalls()).toBe(0); // N−1: ще рано
      tick(1);
      expect(silentSessionCalls()).toBe(1); // N
      expect(service.getStatus$).toHaveBeenCalledWith('grp-1', { silent: true });
      tick(GROUP_ORDER_SYNC_MS);
      expect(silentSessionCalls()).toBe(2); // 2N
      discardPeriodicTasks();
    }));

    it('повторне завантаження (load / Try again) не запускає другий таймер', fakeAsync(() => {
      const { component } = create();
      component.load();
      component.load();

      tick(GROUP_ORDER_SYNC_MS);
      expect(silentSessionCalls()).toBe(1);
      tick(GROUP_ORDER_SYNC_MS);
      expect(silentSessionCalls()).toBe(2);
      discardPeriodicTasks();
    }));

    it('Try again після помилки: один таймер', fakeAsync(() => {
      service.getGroupInvite$.and.returnValues(
        throwError(() => new HttpErrorResponse({ status: 500 })),
        of(INVITE),
      );
      const { fixture, element } = create();
      expect(element.textContent).toContain("Couldn't load the group pre-order.");
      tick(GROUP_ORDER_SYNC_MS);
      expect(silentSessionCalls()).withContext('після помилки таймера немає').toBe(0);

      buttonByText(element, 'Try again')!.click();
      fixture.detectChanges();
      tick(GROUP_ORDER_SYNC_MS);
      expect(silentSessionCalls()).toBe(1);
      discardPeriodicTasks();
    }));

    it('exhaustMap: поки фоновий запит не відповів, новий не стартує', fakeAsync(() => {
      const pending$ = new Subject<GroupSessionsResponse>();
      const { fixture } = create();
      service.getSessions$.and.returnValue(pending$);

      tick(GROUP_ORDER_SYNC_MS * 4);
      expect(silentSessionCalls()).toBe(1);

      pending$.next(structuredClone(REAL_SESSIONS));
      pending$.complete();
      fixture.detectChanges();
      service.getSessions$.and.callFake(() => of(structuredClone(REAL_SESSIONS)));
      tick(GROUP_ORDER_SYNC_MS);
      expect(silentSessionCalls()).toBe(2);
      discardPeriodicTasks();
    }));

    it('Refresh, поки фоновий запит іде, другий запит не шле', fakeAsync(() => {
      const pending$ = new Subject<GroupSessionsResponse>();
      const { fixture, element } = create();
      service.getSessions$.and.returnValue(pending$);

      tick(GROUP_ORDER_SYNC_MS);
      fixture.detectChanges();
      const refresh = buttonByText(element, 'Refresh')!;
      expect(refresh.disabled).toBeTrue();
      fixture.componentInstance.refresh();
      expect(silentSessionCalls()).toBe(1);
      discardPeriodicTasks();
    }));

    it('після Remove Guest таймер перезапускається, але лишається один', fakeAsync(() => {
      const after = structuredClone(REAL_SESSIONS);
      after.participants = after.participants.filter((p) => p.id !== 'olena');
      service.removeSession$.and.returnValue(of(after));
      const { fixture, element } = create();

      tick(GROUP_ORDER_SYNC_MS / 2);
      element.querySelector<HTMLButtonElement>('[aria-label="Remove Olena"]')!.click();
      fixture.detectChanges();
      buttonByText(element.querySelector('[role="dialog"]')!, 'Remove Guest')!.click();
      fixture.detectChanges();

      tick(GROUP_ORDER_SYNC_MS);
      expect(silentSessionCalls()).toBe(1);
      tick(GROUP_ORDER_SYNC_MS);
      expect(silentSessionCalls()).toBe(2);
      discardPeriodicTasks();
    }));

    it('зупиняється, якщо фонова синхронізація принесла Submitted (submit з іншої вкладки) → read-only', fakeAsync(() => {
      const { fixture, element } = create();
      service.getStatus$.and.callFake(() => of(status('Submitted')));

      tick(GROUP_ORDER_SYNC_MS);
      fixture.detectChanges();
      expect(element.textContent).toContain('Sent to Restaurant');
      expect(buttonByText(element, 'Submit Pre-Order')).toBeUndefined();
      expect(buttonByText(element, 'Refresh')).toBeUndefined();
      expect(buttonsByText(element, 'Remove Guest').length).toBe(0);

      tick(GROUP_ORDER_SYNC_MS * 3);
      expect(silentSessionCalls()).toBe(1);
      discardPeriodicTasks();
    }));

    it('застаріла фонова відповідь після локального Submit не повертає екран у Open', fakeAsync(() => {
      const pendingSessions$ = new Subject<GroupSessionsResponse>();
      const pendingStatus$ = new Subject<GroupStatusResponse>();
      service.submit$.and.returnValue(of([{ dishId: 'dish1', quantity: 3 }]));
      const { fixture, element } = create();
      service.getSessions$.and.returnValue(pendingSessions$);
      service.getStatus$.and.returnValue(pendingStatus$);

      tick(GROUP_ORDER_SYNC_MS); // фоновий запит пішов і висить
      buttonByText(element, 'Submit Pre-Order')!.click();
      fixture.detectChanges();
      buttonByText(element.querySelector('[role="dialog"]')!, 'Submit')!.click();
      fixture.detectChanges();

      pendingSessions$.next(structuredClone(REAL_SESSIONS));
      pendingStatus$.next(status('Open'));
      pendingSessions$.complete();
      pendingStatus$.complete();
      fixture.detectChanges();

      expect(element.textContent).toContain('Sent to Restaurant');
      expect(buttonByText(element, 'Submit Pre-Order')).toBeUndefined();
      discardPeriodicTasks();
    }));

    it('при виході зі сторінки під час фонового запиту — нових запитів немає', fakeAsync(() => {
      const pending$ = new Subject<GroupSessionsResponse>();
      const { fixture } = create();
      service.getSessions$.and.returnValue(pending$);
      tick(GROUP_ORDER_SYNC_MS);
      fixture.destroy();

      tick(GROUP_ORDER_SYNC_MS * 3);
      expect(silentSessionCalls()).toBe(1);
      expect(pending$.observed).toBeFalse(); // підписку скасовано
    }));
  });

  describe('read-only, якщо група вже не Open при відкритті', () => {
    for (const value of ['Submitted', 'Closed'] as const) {
      it(`${value}: без Submit / Refresh / Remove Guest / жовтої плашки, polling не стартує`, fakeAsync(() => {
        service.getStatus$.and.callFake(() => of(status(value)));
        const { element } = create();

        expect(buttonByText(element, 'Submit Pre-Order')).toBeUndefined();
        expect(buttonByText(element, 'Refresh')).toBeUndefined();
        expect(buttonsByText(element, 'Remove Guest').length).toBe(0);
        expect(element.querySelector('.notice')).toBeNull();
        expect(element.textContent).not.toContain('Collecting pre-orders');

        tick(GROUP_ORDER_SYNC_MS * 3);
        expect(silentSessionCalls()).toBe(0);
        discardPeriodicTasks();
      }));
    }

    it('Submitted: бейдж «Sent to Restaurant»', () => {
      service.getStatus$.and.callFake(() => of(status('Submitted')));
      expect(create().element.textContent).toContain('Sent to Restaurant');
    });
  });

  describe('invite з CanJoin / Status (раунд 2): екран Owner від них не залежить', () => {
    it('повна Open група (CanJoin false) — звичайний екран Owner, Submit доступний, polling іде', fakeAsync(() => {
      service.getGroupInvite$.and.returnValue(of({ ...INVITE, Status: 'Open', CanJoin: false }));
      const { element } = create();

      expect(element.textContent).toContain('Collecting pre-orders');
      expect(buttonByText(element, 'Submit Pre-Order')!.disabled).toBeFalse();
      tick(GROUP_ORDER_SYNC_MS);
      expect(silentSessionCalls()).toBe(1);
      discardPeriodicTasks();
    }));

    it('перезавантаження після submit (invite Status Submitted, CanJoin false) → read-only «Sent to Restaurant»', () => {
      service.getGroupInvite$.and.returnValue(of({ ...INVITE, Status: 'Submitted', CanJoin: false }));
      service.getStatus$.and.callFake(() => of(status('Submitted')));
      const { element } = create();

      expect(element.textContent).toContain('Sent to Restaurant');
      expect(buttonByText(element, 'Submit Pre-Order')).toBeUndefined();
      expect(element.querySelector('.grand-total')?.textContent).toContain('$540.00');
    });

    it('404: «Back to reservations» — routerLink на /reservations', () => {
      service.getGroupInvite$.and.returnValue(throwError(() => new HttpErrorResponse({ status: 404 })));
      const { element } = create();

      const link = [...element.querySelectorAll('a')].find((a) => a.textContent?.trim() === 'Back to reservations');
      expect(link?.getAttribute('href')).toBe('/reservations');
    });
  });

  describe('GET guest-session-id', () => {
    it('404 (owner-сесії немає): екран працює, просто без «My Pre-Order»', () => {
      service.getOwnerSessionId$.and.returnValue(throwError(() => new HttpErrorResponse({ status: 404, error: { error: 'Owner session for reservation res-1 was not found.' } })));
      const { element } = create();

      expect(element.textContent).not.toContain("Couldn't load the group pre-order.");
      expect(element.textContent).not.toContain('Group reservation not found');
      expect(element.textContent).not.toContain('My Pre-Order');
      expect(element.textContent).toContain('Owner: Yevhenii');
      expect(element.querySelector('.grand-total')?.textContent).toContain('$540.00');
    });

    it('403 (не Owner): стан помилки, а не порожній екран', () => {
      service.getOwnerSessionId$.and.returnValue(throwError(() => new HttpErrorResponse({ status: 403, error: { error: 'Only the reservation owner can perform this action.' } })));
      const { element } = create();

      expect(element.textContent).toContain("Couldn't load the group pre-order.");
    });

    it('404 sessions (немає групи) → «Group reservation not found»', () => {
      service.getSessions$.and.returnValue(throwError(() => new HttpErrorResponse({ status: 404, error: { error: 'Group reservation was not found' } })));
      expect(create().element.textContent).toContain('Group reservation not found');
    });
  });

  describe('Submit: помилки справжнього бекенду', () => {
    it('400 { message, errorCode }: модалка закривається, кнопку можна натиснути знову, сторінка не дублює toast', () => {
      service.submit$.and.returnValue(throwError(() => new HttpErrorResponse({
        status: 400,
        error: { message: 'Pre-order for reservation res-1 was not found.', errorCode: 'Unknown' },
      })));
      const { fixture, element } = create();

      buttonByText(element, 'Submit Pre-Order')!.click();
      fixture.detectChanges();
      buttonByText(element.querySelector('[role="dialog"]')!, 'Submit')!.click();
      fixture.detectChanges();

      expect(element.querySelector('[role="dialog"]')).toBeNull();
      expect(buttonByText(element, 'Submit Pre-Order')!.disabled).toBeFalse();
      expect(element.textContent).toContain('Collecting pre-orders');
      expect(toast.error).not.toHaveBeenCalled(); // toast показує tokenInterceptor
      expect(toast.success).not.toHaveBeenCalled();
    });

    it('кнопка неактивна, якщо submitted-кошики є, але без страв (бекенд дав би 400)', () => {
      const empty = structuredClone(REAL_SESSIONS);
      empty.participants.forEach((p) => {
        if (p.isSubmitted) {
          p.items = [];
          p.total = 0;
        }
      });
      service.getSessions$.and.returnValue(of(empty));

      expect(buttonByText(create().element, 'Submit Pre-Order')!.disabled).toBeTrue();
    });
  });

  describe('Remove Guest', () => {
    it('подвійний клік на «Remove Guest» у діалозі — один DELETE', () => {
      const pending$ = new Subject<GroupSessionsResponse>();
      service.removeSession$.and.returnValue(pending$ as Observable<GroupSessionsResponse>);
      const { fixture, element } = create();

      element.querySelector<HTMLButtonElement>('[aria-label="Remove Olena"]')!.click();
      fixture.detectChanges();
      const dialog = element.querySelector('[role="dialog"]')!;
      buttonByText(dialog, 'Remove Guest')!.click();
      fixture.detectChanges();
      buttonByText(dialog, 'Remove Guest')?.click();
      buttonByText(dialog, 'Removing…')?.click();
      fixture.componentInstance.confirmRemove();
      fixture.detectChanges();

      expect(service.removeSession$).toHaveBeenCalledOnceWith('grp-1', 'olena');
    });

    it('поки йде DELETE, Submit недоступний', () => {
      service.removeSession$.and.returnValue(new Subject<GroupSessionsResponse>());
      const { fixture, element } = create();

      element.querySelector<HTMLButtonElement>('[aria-label="Remove Olena"]')!.click();
      fixture.detectChanges();
      buttonByText(element.querySelector('[role="dialog"]')!, 'Remove Guest')!.click();
      fixture.detectChanges();

      expect(fixture.componentInstance.canSubmit()).toBeFalse();
    });

    it('немає кнопки біля Owner і біля власної сесії, навіть якщо бекенд позначив її як Guest', () => {
      const sessions = structuredClone(REAL_SESSIONS);
      sessions.participants[0].type = 'Guest'; // старий баг бекенду: усі type = Guest
      service.getSessions$.and.returnValue(of(sessions));
      const { element } = create();

      expect(element.querySelector('[aria-label="Remove Yevhenii"]')).toBeNull();
      expect(buttonsByText(element, 'Remove Guest').length).toBe(3);
    });

    it('після успіху суми й Grand Total перераховуються з відповіді DELETE', () => {
      const after = structuredClone(REAL_SESSIONS);
      after.participants = after.participants.filter((p) => p.id !== 'kateryna');
      after.total = 580;
      service.removeSession$.and.returnValue(of(after));
      const { fixture, element } = create();

      element.querySelector<HTMLButtonElement>('[aria-label="Remove Kateryna"]')!.click();
      fixture.detectChanges();
      buttonByText(element.querySelector('[role="dialog"]')!, 'Remove Guest')!.click();
      fixture.detectChanges();

      expect(element.querySelector('.grand-total')?.textContent).toContain('$360.00');
      expect(element.textContent).toContain('Caesar Salad × 2');
      expect(element.textContent).not.toContain('Kateryna');
    });
  });
});
