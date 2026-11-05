// Блок «Split bill» (A7): рендер, loading / порожній / помилка + Try again, Copy summary.
import { TestBed, fakeAsync, flushMicrotasks } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { Subject, of, throwError } from 'rxjs';
import { BillSplitResponse } from '@app/models/group-orders/bill-split.model';
import { GroupOrdersService } from '@app/services/group-orders/group-orders.service';
import { ToastService } from '@app/services/toast/toast.service';
import { SplitBillComponent } from './split-bill.component';

const SPLIT: BillSplitResponse = {
  participants: [
    {
      guestSessionId: 'owner-grp-1',
      name: 'Yevhenii',
      type: 'Owner',
      items: [],
      total: 0,
    },
    {
      guestSessionId: 's1',
      name: 'Andrii',
      type: 'Guest',
      items: [{ dishId: 'd1', name: 'Caesar Salad', quantity: 2, price: 180, subtotal: 360 }],
      total: 360,
    },
    {
      guestSessionId: 's2',
      name: 'Olena',
      type: 'Guest',
      items: [{ dishId: 'd2', name: 'Tom Yum', quantity: 1, price: 220, subtotal: 220 }],
      total: 220,
    },
  ],
  total: 580,
  excludedCanceled: 0,
};

const SUMMARY = [
  'Yevhenii — $0.00',
  'Andrii — $360.00 (Caesar Salad ×2)',
  'Olena — $220.00 (Tom Yum ×1)',
  'Total — $580.00',
].join('\n');

function buttonByText(root: ParentNode, text: string): HTMLButtonElement | undefined {
  return [...root.querySelectorAll('button')].find((button) => button.textContent?.trim() === text) as
    | HTMLButtonElement
    | undefined;
}

describe('SplitBillComponent', () => {
  let service: jasmine.SpyObj<GroupOrdersService>;
  let toast: jasmine.SpyObj<ToastService>;

  function create() {
    TestBed.configureTestingModule({
      imports: [SplitBillComponent],
      providers: [
        { provide: GroupOrdersService, useValue: service },
        { provide: ToastService, useValue: toast },
      ],
    });
    const fixture = TestBed.createComponent(SplitBillComponent);
    fixture.componentRef.setInput('reservationId', 'res-1');
    fixture.detectChanges();
    return { fixture, element: fixture.nativeElement as HTMLElement };
  }

  beforeEach(() => {
    service = jasmine.createSpyObj<GroupOrdersService>('GroupOrdersService', ['getBillSplit$']);
    toast = jasmine.createSpyObj<ToastService>('ToastService', ['success', 'error', 'info']);
    service.getBillSplit$.and.returnValue(of(structuredClone(SPLIT)));
  });

  describe('render', () => {
    it('loads the split for the given reservation once', () => {
      create();
      expect(service.getBillSplit$).toHaveBeenCalledOnceWith('res-1');
    });

    it('is a <section> labelled by the «Split bill» heading', () => {
      const { element } = create();
      const section = element.querySelector('section')!;
      const heading = element.querySelector(`#${section.getAttribute('aria-labelledby')}`);
      expect(heading?.tagName).toBe('H2');
      expect(heading?.textContent?.trim()).toBe('Split bill');
    });

    it('shows every participant with dishes, per-person total and the grand total', () => {
      const { element } = create();
      const people = element.querySelectorAll('.people > li');
      expect(people.length).toBe(3);

      expect(people[1].querySelector('.person-name')?.textContent?.trim()).toBe('Andrii');
      expect(people[1].querySelector('.dish-name')?.textContent?.trim()).toBe('Caesar Salad ×2');
      expect(people[1].querySelector('.person-total')?.textContent?.trim()).toBe('$360.00');
      expect(people[2].textContent).toContain('Tom Yum ×1');
      expect(people[2].querySelector('.person-total')?.textContent?.trim()).toBe('$220.00');
      expect(element.querySelector('.split-total-value')?.textContent?.trim()).toBe('$580.00');
    });

    it('marks the Owner with a text label, not only colour', () => {
      const { element } = create();
      const owner = element.querySelectorAll('.people > li')[0];
      expect(owner.querySelector('.person-role')?.textContent?.trim()).toBe('Owner');
      expect(owner.textContent).toContain('No dishes');
    });

    it('mentions canceled dishes that were excluded', () => {
      service.getBillSplit$.and.returnValue(of({ ...structuredClone(SPLIT), excludedCanceled: 1 }));
      const { element } = create();
      expect(element.textContent).toContain('1 canceled dish is not included in the split.');
    });

    it('emits (loaded) with the response — the page takes Grand Total from it', () => {
      TestBed.configureTestingModule({
        imports: [SplitBillComponent],
        providers: [
          { provide: GroupOrdersService, useValue: service },
          { provide: ToastService, useValue: toast },
        ],
      });
      const fixture = TestBed.createComponent(SplitBillComponent);
      const emitted: BillSplitResponse[] = [];
      fixture.componentInstance.loaded.subscribe((split) => emitted.push(split));
      fixture.componentRef.setInput('reservationId', 'res-1');
      fixture.detectChanges();
      expect(emitted).toEqual([SPLIT]);
    });

    it('does not emit (loaded) on error', () => {
      service.getBillSplit$.and.returnValue(throwError(() => new HttpErrorResponse({ status: 500 })));
      TestBed.configureTestingModule({
        imports: [SplitBillComponent],
        providers: [
          { provide: GroupOrdersService, useValue: service },
          { provide: ToastService, useValue: toast },
        ],
      });
      const fixture = TestBed.createComponent(SplitBillComponent);
      const loadedSpy = jasmine.createSpy('loaded');
      fixture.componentInstance.loaded.subscribe(loadedSpy);
      fixture.componentRef.setInput('reservationId', 'res-1');
      fixture.detectChanges();
      expect(loadedSpy).not.toHaveBeenCalled();
    });

    it('«Copy summary» has an aria-label that contains the visible text', () => {
      const { element } = create();
      const button = buttonByText(element, 'Copy summary')!;
      expect(button.getAttribute('aria-label')).toContain('Copy summary');
    });
  });

  describe('states', () => {
    it('loading: status message, no list, no copy button', () => {
      service.getBillSplit$.and.returnValue(new Subject<BillSplitResponse>());
      const { element } = create();
      expect(element.querySelector('[role="status"]')?.textContent).toContain('Loading split bill');
      expect(element.querySelector('section')?.getAttribute('aria-busy')).toBe('true');
      expect(element.querySelector('.people')).toBeNull();
      expect(buttonByText(element, 'Copy summary')).toBeUndefined();
    });

    it('empty: no participants → «No dishes to split yet», no copy button', () => {
      service.getBillSplit$.and.returnValue(of({ participants: [], total: 0, excludedCanceled: 0 }));
      const { element } = create();
      expect(element.textContent).toContain('No dishes to split yet');
      expect(element.querySelector('.people')).toBeNull();
      expect(buttonByText(element, 'Copy summary')).toBeUndefined();
    });

    it('empty: participants without dishes (e.g. everything canceled) is the empty state too', () => {
      service.getBillSplit$.and.returnValue(
        of({
          participants: [{ guestSessionId: 's1', name: 'Andrii', type: 'Guest' as const, items: [], total: 0 }],
          total: 0,
          excludedCanceled: 2,
        }),
      );
      expect(create().element.textContent).toContain('No dishes to split yet');
    });

    it('404 (no order yet) is the empty state, not an error', () => {
      service.getBillSplit$.and.returnValue(throwError(() => new HttpErrorResponse({ status: 404 })));
      const { element } = create();
      expect(element.textContent).toContain('No dishes to split yet');
      expect(element.querySelector('[role="alert"]')).toBeNull();
    });

    it('error: «Couldn\'t load the split bill.» with «Try again»', () => {
      service.getBillSplit$.and.returnValue(throwError(() => new HttpErrorResponse({ status: 500 })));
      const { element } = create();
      const alert = element.querySelector('[role="alert"]');
      expect(alert?.textContent).toContain("Couldn't load the split bill.");
      expect(buttonByText(alert!, 'Try again')).toBeDefined();
      expect(buttonByText(element, 'Copy summary')).toBeUndefined();
    });

    it('«Try again» requests the split again and renders it', () => {
      service.getBillSplit$.and.returnValue(throwError(() => new HttpErrorResponse({ status: 500 })));
      const { fixture, element } = create();

      service.getBillSplit$.and.returnValue(of(structuredClone(SPLIT)));
      buttonByText(element, 'Try again')!.click();
      fixture.detectChanges();

      expect(service.getBillSplit$).toHaveBeenCalledTimes(2);
      expect(element.querySelector('[role="alert"]')).toBeNull();
      expect(element.querySelectorAll('.people > li').length).toBe(3);
    });
  });

  describe('Copy summary', () => {
    let writeText: jasmine.Spy<(text: string) => Promise<void>>;

    beforeEach(() => {
      writeText = jasmine.createSpy('writeText').and.resolveTo();
      spyOnProperty(navigator, 'clipboard', 'get').and.returnValue({ writeText } as unknown as Clipboard);
    });

    it('writes one line per participant plus the total to navigator.clipboard', fakeAsync(() => {
      const { element } = create();
      buttonByText(element, 'Copy summary')!.click();
      flushMicrotasks();
      expect(writeText).toHaveBeenCalledOnceWith(SUMMARY);
    }));

    it('shows the «Copied» toast after a successful copy', fakeAsync(() => {
      const { element } = create();
      buttonByText(element, 'Copy summary')!.click();
      flushMicrotasks();
      expect(toast.success).toHaveBeenCalledOnceWith('Copied');
    }));

    it('clipboard rejected → error toast, no «Copied»', fakeAsync(() => {
      writeText.and.rejectWith(new Error('denied'));
      const { element } = create();
      buttonByText(element, 'Copy summary')!.click();
      flushMicrotasks();
      expect(toast.success).not.toHaveBeenCalled();
      expect(toast.error).toHaveBeenCalledOnceWith("Couldn't copy the summary");
    }));
  });
});
