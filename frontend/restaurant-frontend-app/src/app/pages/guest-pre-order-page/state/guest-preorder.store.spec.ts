import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { of, throwError } from 'rxjs';
import { GuestInvite } from '@app/models/reservations/guest-invite.model';
import { GuestSessionsService } from '@app/services/guest-sessions/guest-sessions.service';
import { GuestInviteService } from '@app/services/reservation/guest-invite.service';
import { GuestPreorderStore } from './guest-preorder.store';

const INVITE: GuestInvite = {
  GroupReservationId: 'grp-1',
  LocationId: 'loc-1',
  RestaurantName: 'Green & Tasty',
  LocationAddress: 'Rustaveli Avenue 48',
  StartTime: '2026-10-21T16:00:00Z',
  EndTime: '2026-10-21T17:30:00Z',
  MaxGuestCount: 5,
  TableNumber: 7,
  InviterName: 'Yevhenii',
};

describe('GuestPreorderStore · loadInviteByToken', () => {
  let store: GuestPreorderStore;
  let inviteService: jasmine.SpyObj<GuestInviteService>;

  beforeEach(() => {
    inviteService = jasmine.createSpyObj<GuestInviteService>('GuestInviteService', ['getInvite$', 'getInviteToken$']);
    TestBed.configureTestingModule({
      providers: [
        { provide: GuestInviteService, useValue: inviteService },
        { provide: GuestSessionsService, useValue: jasmine.createSpyObj('GuestSessionsService', ['createSession']) },
      ],
    });
    store = TestBed.inject(GuestPreorderStore);
  });

  it('CanJoin: false (full / Submitted / Closed group) → expired, invite is not exposed', async () => {
    inviteService.getInvite$.and.returnValue(of({ ...INVITE, Status: 'Submitted', CanJoin: false }));

    await store.loadInviteByToken('tok');

    expect(store.inviteError()).toBe('expired');
    expect(store.invite()).toBeNull();
    expect(store.inviteLoading()).toBeFalse();
  });

  it('CanJoin: true → invite is loaded, no error', async () => {
    inviteService.getInvite$.and.returnValue(of({ ...INVITE, Status: 'Open', CanJoin: true }));

    await store.loadInviteByToken('tok');

    expect(store.inviteError()).toBeNull();
    expect(store.invite()?.GroupReservationId).toBe('grp-1');
  });

  it('CanJoin missing (older backend) → behaves as before: invite is loaded', async () => {
    inviteService.getInvite$.and.returnValue(of(INVITE));

    await store.loadInviteByToken('tok');

    expect(store.inviteError()).toBeNull();
    expect(store.invite()).toEqual(INVITE);
  });

  it('404 → expired; 500 → retryable', async () => {
    inviteService.getInvite$.and.returnValue(throwError(() => new HttpErrorResponse({ status: 404 })));
    await store.loadInviteByToken('tok');
    expect(store.inviteError()).toBe('expired');

    inviteService.getInvite$.and.returnValue(throwError(() => new HttpErrorResponse({ status: 500 })));
    await store.loadInviteByToken('tok');
    expect(store.inviteError()).toBe('retryable');
  });
});
