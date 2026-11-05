import { HttpClient, HttpContext, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ToastService } from '../services/toast/toast.service';
import { AuthenticationService } from './services/authentication.service';
import { SKIP_ERROR_TOAST } from './skip-error-toast.context';
import { tokenInterceptor } from './token.interceptor';
import { Tokens } from './models/tokens.model';

const URL = 'https://test.api/reservations';

describe('tokenInterceptor', () => {
  let tokens: Tokens | null = null;
  let http: HttpClient;
  let httpMock: HttpTestingController;
  let toastService: jasmine.SpyObj<ToastService>;

  beforeEach(() => {
    toastService = jasmine.createSpyObj<ToastService>('ToastService', ['error']);
    const authService = jasmine.createSpyObj<AuthenticationService>('AuthenticationService', [
      'getTokens',
    ]);
    authService.getTokens.and.callFake(() => tokens);

    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([tokenInterceptor])),
        provideHttpClientTesting(),
        { provide: ToastService, useValue: toastService },
        { provide: AuthenticationService, useValue: authService },
      ],
    });

    http = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
    tokens = null;
  });

  it('shows the server message in a toast for a 400 response', () => {
    http.post(URL, {}).subscribe({ error: () => undefined });

    httpMock
      .expectOne(URL)
      .flush({ message: 'Overlaps' }, { status: 400, statusText: 'Bad Request' });

    expect(toastService.error).toHaveBeenCalledWith('Overlaps', 'Error');
  });

  it('does not show a toast but still rethrows when SKIP_ERROR_TOAST is set', () => {
    let receivedStatus: number | undefined;
    http
      .post(URL, {}, { context: new HttpContext().set(SKIP_ERROR_TOAST, true) })
      .subscribe({ error: (error) => (receivedStatus = error.status) });

    httpMock
      .expectOne(URL)
      .flush({ message: 'Overlaps' }, { status: 400, statusText: 'Bad Request' });

    expect(toastService.error).not.toHaveBeenCalled();
    expect(receivedStatus).toBe(400);
  });

  describe('guest endpoints', () => {
    const GROUP_SESSIONS_URL = 'https://test.api/guest/groups/grp-1/sessions';
    const GUEST_SESSION_URL = 'https://test.api/guest/sessions';

    it('attaches the Cognito id token to owner endpoints under /guest/groups/', () => {
      tokens = { idToken: 'id-token', accessToken: 'access-token' };
      http.get(GROUP_SESSIONS_URL).subscribe();

      const req = httpMock.expectOne(GROUP_SESSIONS_URL);
      expect(req.request.headers.get('Authorization')).toBe('Bearer id-token');
      req.flush({ participants: [], total: 0 });
    });

    it('keeps an explicit Authorization header on /guest/groups/ requests', () => {
      tokens = { idToken: 'id-token', accessToken: 'access-token' };
      http.get(GROUP_SESSIONS_URL, { headers: { Authorization: 'Bearer guest-token' } }).subscribe();

      const req = httpMock.expectOne(GROUP_SESSIONS_URL);
      expect(req.request.headers.get('Authorization')).toBe('Bearer guest-token');
      req.flush({ participants: [], total: 0 });
    });

    it('does not attach the Cognito token to other guest endpoints', () => {
      tokens = { idToken: 'id-token', accessToken: 'access-token' };
      http.post(GUEST_SESSION_URL, {}).subscribe();

      const req = httpMock.expectOne(GUEST_SESSION_URL);
      expect(req.request.headers.has('Authorization')).toBeFalse();
      req.flush({});
    });

    it('shows a toast for a failed guest request', () => {
      http.get(GROUP_SESSIONS_URL).subscribe({ error: () => undefined });

      httpMock
        .expectOne(GROUP_SESSIONS_URL)
        .flush({ message: 'Not owner' }, { status: 403, statusText: 'Forbidden' });

      expect(toastService.error).toHaveBeenCalledWith('Not owner', 'Forbidden');
    });

    it('does not show a toast but still rethrows a guest error when SKIP_ERROR_TOAST is set', () => {
      let receivedStatus: number | undefined;
      http
        .get(GROUP_SESSIONS_URL, { context: new HttpContext().set(SKIP_ERROR_TOAST, true) })
        .subscribe({ error: (error) => (receivedStatus = error.status) });

      httpMock
        .expectOne(GROUP_SESSIONS_URL)
        .flush({ message: 'Server error' }, { status: 500, statusText: 'Server Error' });

      expect(toastService.error).not.toHaveBeenCalled();
      expect(receivedStatus).toBe(500);
    });

    it('401 on an owner endpoint under /guest/groups/ uses the regular Cognito message', () => {
      http.get(GROUP_SESSIONS_URL).subscribe({ error: () => undefined });

      httpMock
        .expectOne(GROUP_SESSIONS_URL)
        .flush({ message: 'Unauthorized' }, { status: 401, statusText: 'Unauthorized' });

      expect(toastService.error).toHaveBeenCalledOnceWith('Please log in to perform the action!', 'Unauthorized');
    });

    it('401 on a guest-session endpoint keeps the invite-link message', () => {
      http.post(GUEST_SESSION_URL, {}).subscribe({ error: () => undefined });

      httpMock
        .expectOne(GUEST_SESSION_URL)
        .flush({ message: 'Unauthorized' }, { status: 401, statusText: 'Unauthorized' });

      expect(toastService.error).toHaveBeenCalledOnceWith(
        'Session expired. Please re-open the invite link.',
        'Unauthorized'
      );
    });
  });
});
