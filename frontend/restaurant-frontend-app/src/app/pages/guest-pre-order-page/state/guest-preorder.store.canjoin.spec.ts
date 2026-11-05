// GuestPreorderStore і нове поле CanJoin з GET /guest/invites/{token}.
// CanJoin описує, чи можна приєднатися НОВОМУ гостю. Гість, що вже приєднався (сесія в localStorage),
// має й далі працювати, поки група Open, навіть якщо тепер вона повна.
import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { GuestInvite } from '@app/models/reservations/guest-invite.model';
import { GuestSessionsService } from '@app/services/guest-sessions/guest-sessions.service';
import { GuestInviteService } from '@app/services/reservation/guest-invite.service';
import { GuestPreorderStore } from './guest-preorder.store';

const TOKEN = 'tok-canjoin';
const INVITE: GuestInvite = {
  GroupReservationId: 'grp-1',
  LocationId: 'loc-1',
  RestaurantName: 'Green & Tasty',
  LocationAddress: 'Rustaveli Avenue 48',
  StartTime: '2026-10-21T16:00:00Z',
  EndTime: '2026-10-21T17:30:00Z',
  MaxGuestCount: 4,
  TableNumber: 7,
  InviterName: 'Yevhenii',
};

describe('GuestPreorderStore · CanJoin', () => {
  let store: GuestPreorderStore;
  let inviteService: jasmine.SpyObj<GuestInviteService>;
  let sessions: jasmine.SpyObj<GuestSessionsService>;

  function setup(): void {
    TestBed.configureTestingModule({
      providers: [
        { provide: GuestInviteService, useValue: inviteService },
        { provide: GuestSessionsService, useValue: sessions },
      ],
    });
    store = TestBed.inject(GuestPreorderStore);
  }

  beforeEach(() => {
    localStorage.clear();
    inviteService = jasmine.createSpyObj<GuestInviteService>('GuestInviteService', ['getInvite$', 'getInviteToken$']);
    sessions = jasmine.createSpyObj<GuestSessionsService>('GuestSessionsService', ['createSession', 'getMenu']);
    sessions.getMenu.and.returnValue(of({ dishes: {} }) as never);
  });

  afterEach(() => localStorage.clear());

  for (const status of ['Open', 'Submitted', 'Closed'] as const) {
    it(`новий гість: CanJoin false (Status ${status}) → expired`, async () => {
      setup();
      inviteService.getInvite$.and.returnValue(of({ ...INVITE, Status: status, CanJoin: false }));

      await store.loadInviteByToken(TOKEN);

      expect(store.inviteError()).toBe('expired');
      expect(store.invite()).toBeNull();
    });
  }

  it('повторне завантаження з CanJoin false прибирає раніше завантажений invite', async () => {
    setup();
    inviteService.getInvite$.and.returnValue(of({ ...INVITE, Status: 'Open', CanJoin: true }));
    await store.loadInviteByToken(TOKEN);
    expect(store.invite()).not.toBeNull();

    inviteService.getInvite$.and.returnValue(of({ ...INVITE, Status: 'Submitted', CanJoin: false }));
    await store.loadInviteByToken(TOKEN);

    expect(store.invite()).toBeNull();
    expect(store.inviteError()).toBe('expired');
  });

  /**
   * Гість зайняв останнє місце (група стала повною, але ще Open) і перезавантажив сторінку /join/{token}:
   * initContext → loadInviteByToken → CanJoin false → inviteError 'expired' → сторінка редіректить на
   * /guest-pre-order-expired, хоча його сесія жива і кошик ще можна редагувати й підтвердити.
   */
  it('гість, що вже приєднався, у повній Open групі не отримує expired після перезавантаження', async () => {
    localStorage.setItem(`guestPreorder:${TOKEN}:guestSessionId`, 'gs-1');
    localStorage.setItem(`guestPreorder:${TOKEN}:guestSessionToken`, 'c2Vzc2lvbg==');
    setup();
    inviteService.getInvite$.and.returnValue(of({ ...INVITE, Status: 'Open', CanJoin: false }));

    store.initContext(TOKEN);
    await store.loadInviteByToken(TOKEN);

    expect(store.guestSessionId()).toBe('gs-1');
    expect(store.inviteError()).not.toBe('expired');
  });

  it('гість, що вже приєднався, після Submit групи — expired (група закрита для змін)', async () => {
    localStorage.setItem(`guestPreorder:${TOKEN}:guestSessionId`, 'gs-1');
    localStorage.setItem(`guestPreorder:${TOKEN}:guestSessionToken`, 'c2Vzc2lvbg==');
    setup();
    inviteService.getInvite$.and.returnValue(of({ ...INVITE, Status: 'Submitted', CanJoin: false }));

    store.initContext(TOKEN);
    await store.loadInviteByToken(TOKEN);

    expect(store.inviteError()).toBe('expired');
  });
});
