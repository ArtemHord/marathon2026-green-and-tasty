import { Tokens } from '@app/auth/models/tokens.model';
import { UserModel } from '@app/auth/models/user.model';
import { Roles } from '@app/shared/constants/roles';

/**
 * Мок-авторизація для демо групового pre-order — працює в `npm start` і в production-збірці (CloudFront).
 * Відкрий /reservations/demo-reservation/group-order?demo — і ти залогінений як Owner демо-бронювання.
 * Реєстрація, логін і бекенд не потрібні. Вийти — кнопка Sign out у шапці.
 * Запобіжники: демо-логін лише на шляхах демо-бронювання (DEMO_LOGIN_PATH_PREFIX) і ніколи поверх
 * справжньої сесії — тоді лише прибираємо ?demo, а демо-екран і так відкриється через мок.
 * Для рівня 3 (справжній бекенд) вийди з демо і залогінься справжнім акаунтом.
 */
export const DEMO_LOGIN_PARAM = 'demo';
export const DEMO_ISSUER = 'green-and-tasty-demo';
/** Демо-логін застосовується лише тут (той самий id, що й DEMO_RESERVATION_ID у мок-бекенді). */
export const DEMO_LOGIN_PATH_PREFIX = '/reservations/demo-reservation';
const AUTH_STORAGE_KEY = 'auth';

export const DEMO_USER: UserModel = {
  id: 'demo-owner',
  firstName: 'Yevhenii',
  lastName: 'Demo',
  email: 'owner@demo.test',
  role: Roles.CUSTOMER,
};

const base64Url = (value: object): string =>
  btoa(unescape(encodeURIComponent(JSON.stringify(value)))).replace(/=+$/, '').replace(/\+/g, '-').replace(/\//g, '_');

/** Непідписаний JWT: фронтенд токен лише декодує (exp, ролі), а перевіряє його тільки бекенд. */
export function createDemoTokens(now = Date.now()): Tokens {
  const iat = Math.floor(now / 1000);
  const payload = {
    iss: DEMO_ISSUER,
    sub: DEMO_USER.id,
    email: DEMO_USER.email,
    given_name: DEMO_USER.firstName,
    family_name: DEMO_USER.lastName,
    'cognito:groups': [DEMO_USER.role],
    iat,
    exp: iat + 30 * 24 * 60 * 60,
  };
  const token = `${base64Url({ alg: 'none', typ: 'JWT' })}.${base64Url(payload)}.demo`;
  return { idToken: token, accessToken: token };
}

/** true, якщо запит іде з демо-токеном (його бачить мок-бекенд після tokenInterceptor). */
export function isDemoAuthorization(header: string | null): boolean {
  if (!header?.startsWith('Bearer ')) return false;
  return isDemoToken(header.slice(7));
}

function isDemoToken(token: string | undefined): boolean {
  if (!token) return false;
  try {
    const payload = JSON.parse(atob(token.split('.')[1].replace(/-/g, '+').replace(/_/g, '/')));
    return payload.iss === DEMO_ISSUER;
  } catch {
    return false;
  }
}

/** У сховищі вже є справжня (не демо) сесія — її не можна перезаписувати демо-токенами. */
function hasRealSession(storedAuth: string | null | undefined): boolean {
  if (!storedAuth) return false;
  try {
    const tokens = JSON.parse(storedAuth) as { idToken?: string };
    return !isDemoToken(tokens.idToken);
  } catch {
    return true; // не можемо розібрати — вважаємо справжньою і не чіпаємо
  }
}

function isDemoLoginPath(pathname: string): boolean {
  return pathname === DEMO_LOGIN_PATH_PREFIX || pathname.startsWith(`${DEMO_LOGIN_PATH_PREFIX}/`);
}

/**
 * Викликається один раз на старті застосунку (dev і production). Якщо в URL є ?demo і це шлях
 * демо-бронювання — кладе демо-токени і користувача в localStorage і прибирає параметр з адреси.
 * Справжню сесію не перезаписує: тоді лише прибирає ?demo (демо-екран відкриє мок і так).
 * На інших шляхах нічого не робить. Повертає true, якщо демо-логін застосовано.
 */
export function applyDemoLoginFromUrl(
  location: Pick<Location, 'href'> = window.location,
  storage: Pick<Storage, 'setItem'> & Partial<Pick<Storage, 'getItem'>> = localStorage,
  history: Pick<History, 'replaceState' | 'state'> = window.history,
): boolean {
  const url = new URL(location.href);
  if (!url.searchParams.has(DEMO_LOGIN_PARAM) || !isDemoLoginPath(url.pathname)) return false;

  const keepRealSession = hasRealSession(storage.getItem?.(AUTH_STORAGE_KEY));
  if (!keepRealSession) {
    storage.setItem(AUTH_STORAGE_KEY, JSON.stringify(createDemoTokens()));
    storage.setItem('user', JSON.stringify(DEMO_USER));
  }

  url.searchParams.delete(DEMO_LOGIN_PARAM);
  history.replaceState(history.state, '', url.pathname + url.search + url.hash);
  return !keepRealSession;
}
