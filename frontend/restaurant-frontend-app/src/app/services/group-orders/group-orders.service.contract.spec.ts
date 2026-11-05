// Контракт фронт ↔ бек для GroupOrdersService разом зі справжнім tokenInterceptor:
// URL і тіла як у docs/features/group-preorder.md, Cognito-токен на owner-ендпоінтах,
// toast з тексту реальних тіл помилок: { error } (401/403/404/валідація) і { message, errorCode } (400 бізнес-правил).
import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { AuthenticationService } from '@app/auth/services/authentication.service';
import { SKIP_ERROR_TOAST } from '@app/auth/skip-error-toast.context';
import { tokenInterceptor } from '@app/auth/token.interceptor';
import { GroupSessionsResponse } from '@app/models/group-orders/group-sessions-response.model';
import { ToastService } from '@app/services/toast/toast.service';
import { API_BASE_URL } from '@app/shared/config/api-base-url.token';
import { GroupOrdersService } from './group-orders.service';

const BASE_URL = 'https://api.test/develop';
const SNAPSHOT: GroupSessionsResponse = {
  participants: [{ id: 'owner-grp-1', type: 'Owner', name: 'Yevhenii', isSubmitted: false, total: 0, items: [] }],
  total: 0,
};

describe('Контракт · GroupOrdersService + tokenInterceptor', () => {
  let service: GroupOrdersService;
  let http: HttpTestingController;
  let toast: jasmine.SpyObj<ToastService>;

  beforeEach(() => {
    toast = jasmine.createSpyObj<ToastService>('ToastService', ['error', 'success', 'info']);
    const auth = jasmine.createSpyObj<AuthenticationService>('AuthenticationService', ['getTokens']);
    auth.getTokens.and.returnValue({ idToken: 'id-token', accessToken: 'access-token' });

    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([tokenInterceptor])),
        provideHttpClientTesting(),
        { provide: API_BASE_URL, useValue: BASE_URL },
        { provide: ToastService, useValue: toast },
        { provide: AuthenticationService, useValue: auth },
      ],
    });
    service = TestBed.inject(GroupOrdersService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  describe('URL, метод, тіло, Cognito id token', () => {
    it('getSessions$ → GET /guest/groups/{id}/sessions з Bearer idToken, не silent за замовчуванням', () => {
      let result: GroupSessionsResponse | undefined;
      service.getSessions$('grp-1').subscribe((snapshot) => (result = snapshot));

      const req = http.expectOne(`${BASE_URL}/guest/groups/grp-1/sessions`);
      expect(req.request.method).toBe('GET');
      expect(req.request.headers.get('Authorization')).toBe('Bearer id-token');
      expect(req.request.context.get(SKIP_ERROR_TOAST)).toBeFalse();
      req.flush(SNAPSHOT);
      expect(result).toEqual(SNAPSHOT);
    });

    it('getOwnerSessionId$ → GET /reservations/{id}/guest-session-id з токеном, повертає рядок', () => {
      let id: string | undefined;
      service.getOwnerSessionId$('res-1').subscribe((value) => (id = value));

      const req = http.expectOne(`${BASE_URL}/reservations/res-1/guest-session-id`);
      expect(req.request.method).toBe('GET');
      expect(req.request.headers.get('Authorization')).toBe('Bearer id-token');
      req.flush({ guestSessionId: 'owner-grp-1' });
      expect(id).toBe('owner-grp-1');
    });

    it('submit$ → POST /group-reservations/{id}/submit, тіло рівно { reservationId }, токен є', () => {
      let result: unknown;
      service.submit$('grp-1', 'res-1').subscribe((dishes) => (result = dishes));

      const req = http.expectOne(`${BASE_URL}/group-reservations/grp-1/submit`);
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual({ reservationId: 'res-1' });
      expect(Object.keys(req.request.body as object)).toEqual(['reservationId']);
      expect(req.request.headers.get('Authorization')).toBe('Bearer id-token');
      req.flush([{ dishId: 'dish1', quantity: 3 }]);
      expect(result).toEqual([{ dishId: 'dish1', quantity: 3 }]);
    });

    it('removeSession$ → DELETE /guest/groups/{id}/sessions/{guestSessionId} з токеном, відповідь — snapshot', () => {
      let result: GroupSessionsResponse | undefined;
      service.removeSession$('grp-1', 'guest-1').subscribe((snapshot) => (result = snapshot));

      const req = http.expectOne(`${BASE_URL}/guest/groups/grp-1/sessions/guest-1`);
      expect(req.request.method).toBe('DELETE');
      expect(req.request.body).toBeNull();
      expect(req.request.headers.get('Authorization')).toBe('Bearer id-token');
      req.flush(SNAPSHOT);
      expect(result).toEqual(SNAPSHOT);
    });

    it('getStatus$(id, { silent: true }) → GET /guest/groups/{id}/status з SKIP_ERROR_TOAST', () => {
      service.getStatus$('grp-1', { silent: true }).subscribe();

      const req = http.expectOne(`${BASE_URL}/guest/groups/grp-1/status`);
      expect(req.request.context.get(SKIP_ERROR_TOAST)).toBeTrue();
      req.flush({ currentParticipants: 2, maxCapacity: 5, status: 'Open' });
    });

    it('id з небезпечними символами кодуються в шляху', () => {
      service.removeSession$('grp/1', 'guest 1').subscribe();
      http.expectOne(`${BASE_URL}/guest/groups/grp%2F1/sessions/guest%201`).flush(SNAPSHOT);
    });
  });

  describe('реальні тіла помилок → текст toast', () => {
    it('submit 400 { message, errorCode } (повторний submit) → toast з message', () => {
      service.submit$('grp-1', 'res-1').subscribe({ error: () => undefined });
      http.expectOne(`${BASE_URL}/group-reservations/grp-1/submit`).flush(
        { message: 'Group pre-order has already been submitted.', errorCode: 'GroupReservationSubmitted' },
        { status: 400, statusText: 'Bad Request' },
      );
      expect(toast.error).toHaveBeenCalledOnceWith('Group pre-order has already been submitted.', 'Error');
    });

    it('submit 400 { message, errorCode: Unknown } (немає order) → toast з message', () => {
      service.submit$('grp-1', 'res-1').subscribe({ error: () => undefined });
      http.expectOne(`${BASE_URL}/group-reservations/grp-1/submit`).flush(
        { message: 'Pre-order for reservation res-1 was not found.', errorCode: 'Unknown' },
        { status: 400, statusText: 'Bad Request' },
      );
      expect(toast.error).toHaveBeenCalledOnceWith('Pre-order for reservation res-1 was not found.', 'Error');
    });

    it('submit 400 { error } (валідація тіла в хендлері) → toast з error', () => {
      service.submit$('grp-1', '').subscribe({ error: () => undefined });
      http.expectOne(`${BASE_URL}/group-reservations/grp-1/submit`).flush(
        { error: "'reservationId' is required." },
        { status: 400, statusText: 'Bad Request' },
      );
      expect(toast.error).toHaveBeenCalledOnceWith("'reservationId' is required.", 'Error');
    });

    it('submit 403 { error } → toast Forbidden з текстом error', () => {
      service.submit$('grp-1', 'res-1').subscribe({ error: () => undefined });
      http.expectOne(`${BASE_URL}/group-reservations/grp-1/submit`).flush(
        { error: 'Only the reservation owner can perform this action.' },
        { status: 403, statusText: 'Forbidden' },
      );
      expect(toast.error).toHaveBeenCalledOnceWith('Only the reservation owner can perform this action.', 'Forbidden');
    });

    it('GET sessions 404 { error } (гілка /guest/) → toast Not Found з текстом error', () => {
      service.getSessions$('missing').subscribe({ error: () => undefined });
      http.expectOne(`${BASE_URL}/guest/groups/missing/sessions`).flush(
        { error: 'Group reservation was not found' },
        { status: 404, statusText: 'Not Found' },
      );
      expect(toast.error).toHaveBeenCalledOnceWith('Group reservation was not found', 'Not Found');
    });

    it('DELETE owner-сесії 400 { message, errorCode } (гілка /guest/) → toast з message', () => {
      service.removeSession$('grp-1', 'owner-grp-1').subscribe({ error: () => undefined });
      http.expectOne(`${BASE_URL}/guest/groups/grp-1/sessions/owner-grp-1`).flush(
        { message: 'The owner session cannot be removed from the group.', errorCode: 'Unknown' },
        { status: 400, statusText: 'Bad Request' },
      );
      expect(toast.error).toHaveBeenCalledOnceWith('The owner session cannot be removed from the group.', 'Error');
    });

    it('silent sessions 403 — toast немає, помилка доходить до підписника зі статусом', () => {
      let status: number | undefined;
      service.getSessions$('grp-1', { silent: true }).subscribe({ error: (error: HttpErrorResponse) => (status = error.status) });
      http.expectOne(`${BASE_URL}/guest/groups/grp-1/sessions`).flush(
        { error: 'Only the reservation owner can perform this action.' },
        { status: 403, statusText: 'Forbidden' },
      );
      expect(toast.error).not.toHaveBeenCalled();
      expect(status).toBe(403);
    });

    it('guest-session-id 404 { error } доходить до сторінки як HttpErrorResponse 404', () => {
      let error: unknown;
      service.getOwnerSessionId$('res-1').subscribe({ error: (value: unknown) => (error = value) });
      http.expectOne(`${BASE_URL}/reservations/res-1/guest-session-id`).flush(
        { error: 'Owner session for reservation res-1 was not found.' },
        { status: 404, statusText: 'Not Found' },
      );
      expect(error instanceof HttpErrorResponse && error.status === 404).toBeTrue();
    });
  });
});
