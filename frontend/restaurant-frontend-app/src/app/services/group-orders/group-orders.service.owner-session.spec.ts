// getOwnerSessionId$ — тихий запит: 404 «owner-сесії ще немає» очікуваний і не має давати toast.
import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { AuthenticationService } from '@app/auth/services/authentication.service';
import { SKIP_ERROR_TOAST } from '@app/auth/skip-error-toast.context';
import { tokenInterceptor } from '@app/auth/token.interceptor';
import { ToastService } from '@app/services/toast/toast.service';
import { API_BASE_URL } from '@app/shared/config/api-base-url.token';
import { GroupOrdersService } from './group-orders.service';

const BASE_URL = 'https://api.test';
const OWNER_SESSION_URL = `${BASE_URL}/reservations/res-1/guest-session-id`;

describe('GroupOrdersService · getOwnerSessionId$ (silent)', () => {
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

  it('sets SKIP_ERROR_TOAST on the request', () => {
    service.getOwnerSessionId$('res-1').subscribe();

    const req = http.expectOne(OWNER_SESSION_URL);
    expect(req.request.context.get(SKIP_ERROR_TOAST)).toBeTrue();
    req.flush({ guestSessionId: 'owner-grp-1' });
  });

  it('404 shows no toast and still reaches the subscriber as HttpErrorResponse 404', () => {
    let status: number | undefined;
    service.getOwnerSessionId$('res-1').subscribe({ error: (error: HttpErrorResponse) => (status = error.status) });

    http
      .expectOne(OWNER_SESSION_URL)
      .flush({ error: 'Owner session for reservation res-1 was not found.' }, { status: 404, statusText: 'Not Found' });

    expect(toast.error).not.toHaveBeenCalled();
    expect(status).toBe(404);
  });
});
