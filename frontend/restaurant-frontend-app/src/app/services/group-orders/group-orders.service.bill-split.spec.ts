// getBillSplit$ — GET /reservations/{reservationId}/bill-split, тихий (блок Split bill показує помилку сам).
import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { AuthenticationService } from '@app/auth/services/authentication.service';
import { SKIP_ERROR_TOAST } from '@app/auth/skip-error-toast.context';
import { tokenInterceptor } from '@app/auth/token.interceptor';
import { BillSplitResponse } from '@app/models/group-orders/bill-split.model';
import { ToastService } from '@app/services/toast/toast.service';
import { API_BASE_URL } from '@app/shared/config/api-base-url.token';
import { GroupOrdersService } from './group-orders.service';

const BASE_URL = 'https://api.test';
const BILL_SPLIT_URL = `${BASE_URL}/reservations/res-1/bill-split`;
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

describe('GroupOrdersService · getBillSplit$', () => {
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

  it('GETs /reservations/{reservationId}/bill-split and returns the body as is', () => {
    let result: BillSplitResponse | undefined;
    service.getBillSplit$('res-1').subscribe((split) => (result = split));

    const req = http.expectOne(BILL_SPLIT_URL);
    expect(req.request.method).toBe('GET');
    req.flush(SPLIT);

    expect(result).toEqual(SPLIT);
  });

  it('encodes the reservation id in the URL', () => {
    service.getBillSplit$('res/1').subscribe();
    http.expectOne(`${BASE_URL}/reservations/res%2F1/bill-split`).flush(SPLIT);
  });

  it('is silent: SKIP_ERROR_TOAST is set', () => {
    service.getBillSplit$('res-1').subscribe();
    const req = http.expectOne(BILL_SPLIT_URL);
    expect(req.request.context.get(SKIP_ERROR_TOAST)).toBeTrue();
    req.flush(SPLIT);
  });

  it('error reaches the subscriber as HttpErrorResponse, without a toast', () => {
    let status: number | undefined;
    service.getBillSplit$('res-1').subscribe({ error: (error: HttpErrorResponse) => (status = error.status) });

    http.expectOne(BILL_SPLIT_URL).flush({ message: 'boom' }, { status: 500, statusText: 'Server Error' });

    expect(status).toBe(500);
    expect(toast.error).not.toHaveBeenCalled();
  });
});
