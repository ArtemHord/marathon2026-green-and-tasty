import { JwtHelperService } from '@auth0/angular-jwt';
import { applyDemoLoginFromUrl, createDemoTokens, DEMO_USER, isDemoAuthorization } from './demo-auth';

describe('demo auth (dev-only)', () => {
  const jwt = new JwtHelperService();

  it('creates a token the app accepts: decodable, not expired, with the Customer role', () => {
    const { idToken, accessToken } = createDemoTokens();
    expect(accessToken).toBe(idToken);
    expect(jwt.isTokenExpired(idToken)).toBeFalse();
    expect(jwt.decodeToken(idToken)['cognito:groups']).toEqual([DEMO_USER.role]);
  });

  it('recognises only demo tokens', () => {
    expect(isDemoAuthorization('Bearer ' + createDemoTokens().idToken)).toBeTrue();
    expect(isDemoAuthorization('Bearer real.jwt.token')).toBeFalse();
    expect(isDemoAuthorization(null)).toBeFalse();
  });

  it('logs in from ?demo and removes the parameter from the address', () => {
    const storage = jasmine.createSpyObj<Storage>('Storage', ['setItem']);
    const history = jasmine.createSpyObj<History>('History', ['replaceState']);
    const applied = applyDemoLoginFromUrl({ href: 'http://localhost:4200/reservations/demo-reservation/group-order?demo' }, storage, history);

    expect(applied).toBeTrue();
    expect(storage.setItem).toHaveBeenCalledWith('user', JSON.stringify(DEMO_USER));
    expect(history.replaceState).toHaveBeenCalledWith(undefined, '', '/reservations/demo-reservation/group-order');
  });

  it('does nothing without ?demo', () => {
    const storage = jasmine.createSpyObj<Storage>('Storage', ['setItem']);
    const history = jasmine.createSpyObj<History>('History', ['replaceState']);
    expect(applyDemoLoginFromUrl({ href: 'http://localhost:4200/main' }, storage, history)).toBeFalse();
    expect(storage.setItem).not.toHaveBeenCalled();
  });
});

describe('demo auth · safeguards (production)', () => {
  const DEMO_URL = 'http://localhost:4200/reservations/demo-reservation/group-order?demo';
  const REAL_AUTH = JSON.stringify({ idToken: 'eyJhbGciOiJSUzI1NiJ9.eyJpc3MiOiJodHRwczovL2NvZ25pdG8ifQ.sig', accessToken: 'a', refreshToken: 'r' });
  const REAL_USER = JSON.stringify({ id: 'u-1', firstName: 'Real', lastName: 'User', email: 'real@test', role: 'Customer' });

  function memoryStorage(initial: Record<string, string> = {}) {
    const data = new Map(Object.entries(initial));
    return {
      data,
      getItem: (key: string) => data.get(key) ?? null,
      setItem: jasmine.createSpy('setItem').and.callFake((key: string, value: string) => data.set(key, value)),
    };
  }

  it('real session + ?demo → auth and user untouched, ?demo removed from the address', () => {
    const storage = memoryStorage({ auth: REAL_AUTH, user: REAL_USER });
    const history = jasmine.createSpyObj<History>('History', ['replaceState']);

    expect(applyDemoLoginFromUrl({ href: DEMO_URL }, storage, history)).toBeFalse();
    expect(storage.setItem).not.toHaveBeenCalled();
    expect(storage.data.get('auth')).toBe(REAL_AUTH);
    expect(storage.data.get('user')).toBe(REAL_USER);
    expect(history.replaceState).toHaveBeenCalledWith(undefined, '', '/reservations/demo-reservation/group-order');
  });

  it('unreadable stored auth is treated as a real session and kept', () => {
    const storage = memoryStorage({ auth: 'not-json' });
    const history = jasmine.createSpyObj<History>('History', ['replaceState']);
    expect(applyDemoLoginFromUrl({ href: DEMO_URL }, storage, history)).toBeFalse();
    expect(storage.data.get('auth')).toBe('not-json');
  });

  it('?demo on any other path → demo login is not applied', () => {
    for (const href of [
      'http://localhost:4200/main?demo',
      'http://localhost:4200/reservations?demo',
      'http://localhost:4200/reservations/real-uuid/group-order?demo',
      'http://localhost:4200/reservations/demo-reservation-x/group-order?demo',
    ]) {
      const storage = memoryStorage();
      const history = jasmine.createSpyObj<History>('History', ['replaceState']);
      expect(applyDemoLoginFromUrl({ href }, storage, history)).withContext(href).toBeFalse();
      expect(storage.setItem).withContext(href).not.toHaveBeenCalled();
    }
  });

  it('no session on the demo path → demo login is applied', () => {
    const storage = memoryStorage();
    const history = jasmine.createSpyObj<History>('History', ['replaceState']);
    expect(applyDemoLoginFromUrl({ href: DEMO_URL }, storage, history)).toBeTrue();
    expect(isDemoAuthorization('Bearer ' + JSON.parse(storage.data.get('auth')!).idToken)).toBeTrue();
    expect(storage.data.get('user')).toBe(JSON.stringify(DEMO_USER));
  });

  it('an existing demo session is refreshed (not a real session)', () => {
    const storage = memoryStorage({ auth: JSON.stringify(createDemoTokens(0)), user: JSON.stringify(DEMO_USER) });
    const history = jasmine.createSpyObj<History>('History', ['replaceState']);
    expect(applyDemoLoginFromUrl({ href: DEMO_URL }, storage, history)).toBeTrue();
    expect(storage.setItem).toHaveBeenCalledWith('user', JSON.stringify(DEMO_USER));
  });
});
