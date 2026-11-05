// Демо /reservations/demo-reservation/group-order?demo має працювати і в production-збірці (CloudFront, без бекенду).
// Раніше мок і мок-логін реєструвались лише під isDevMode() → у prod запити йшли на API → «Group reservation not found».
import { TestBed, fakeAsync, tick } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { appConfig } from '@app/app.config';
import { GuestInvite } from '@app/models/reservations/guest-invite.model';
import { BillSplitResponse } from '@app/models/group-orders/bill-split.model';
import { GroupOrdersService } from '@app/services/group-orders/group-orders.service';
import { API_BASE_URL } from '@app/shared/config/api-base-url.token';
import { DEMO_INTERCEPTORS } from './demo-mode';
import { groupOrdersMockInterceptor, groupOrdersMockStore } from './group-orders/group-orders-mock.interceptor';
import { DEMO_GROUP_ID, DEMO_RESERVATION_ID } from './group-orders/group-orders.mock-store';

const BASE_URL = 'https://api.test';
const MOCK_LATENCY_MS = 400;

describe('demo mode (app config)', () => {
  let http: HttpTestingController;
  let service: GroupOrdersService;

  beforeEach(() => {
    groupOrdersMockStore.reset();
    TestBed.configureTestingModule({
      providers: [...appConfig.providers, provideHttpClientTesting(), { provide: API_BASE_URL, useValue: BASE_URL }],
    });
    http = TestBed.inject(HttpTestingController);
    service = TestBed.inject(GroupOrdersService);
  });

  afterEach(() => {
    http.verify();
    groupOrdersMockStore.reset();
  });

  it('registers the group pre-order mock unconditionally (not behind isDevMode)', () => {
    expect(DEMO_INTERCEPTORS).toContain(groupOrdersMockInterceptor);
  });

  it('answers the demo reservation without reaching the API', fakeAsync(() => {
    let invite: GuestInvite | undefined;
    service.getGroupInvite$(DEMO_RESERVATION_ID).subscribe((value) => (invite = value));
    tick(MOCK_LATENCY_MS * 2); // invite-token → invite

    http.expectNone(() => true);
    expect(invite?.GroupReservationId).toBe(DEMO_GROUP_ID);
  }));

  it('serves the bill split for the demo reservation after submit', fakeAsync(() => {
    service.submit$(DEMO_GROUP_ID, DEMO_RESERVATION_ID).subscribe();
    tick(MOCK_LATENCY_MS);

    let split: BillSplitResponse | undefined;
    service.getBillSplit$(DEMO_RESERVATION_ID).subscribe((value) => (split = value));
    tick(MOCK_LATENCY_MS);

    http.expectNone(() => true);
    expect(split?.participants.map((person) => person.name)).toEqual(['Yevhenii', 'Kateryna']);
    expect(split?.total).toBe(50.5);
  }));

  describe('with a real (non-demo) Cognito session in localStorage', () => {
    const REAL_AUTH = JSON.stringify({ idToken: 'eyJhbGciOiJSUzI1NiJ9.eyJpc3MiOiJjb2duaXRvIn0.sig', accessToken: 'a' });
    let previousAuth: string | null;

    beforeEach(() => {
      previousAuth = localStorage.getItem('auth');
      localStorage.setItem('auth', REAL_AUTH);
    });

    afterEach(() => {
      if (previousAuth === null) localStorage.removeItem('auth');
      else localStorage.setItem('auth', previousAuth);
    });

    it('the demo screen still opens: demo-* paths are mocked regardless of the token', fakeAsync(() => {
      let invite: GuestInvite | undefined;
      let participants = 0;
      service.getGroupInvite$(DEMO_RESERVATION_ID).subscribe((value) => (invite = value));
      service.getSessions$(DEMO_GROUP_ID).subscribe((value) => (participants = value.participants.length));
      tick(MOCK_LATENCY_MS * 2);

      http.expectNone(() => true);
      expect(invite?.GroupReservationId).toBe(DEMO_GROUP_ID);
      expect(participants).toBe(4);
    }));
  });

  it('passes real (non-demo) reservations through to the API', () => {
    service.getBillSplit$('real-reservation-id').subscribe();
    http.expectOne(`${BASE_URL}/reservations/real-reservation-id/bill-split`).flush({
      participants: [],
      total: 0,
      excludedCanceled: 0,
    });
  });
});
