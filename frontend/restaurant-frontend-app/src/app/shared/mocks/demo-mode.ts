import { HttpInterceptorFn } from '@angular/common/http';
import { EnvironmentProviders, provideAppInitializer } from '@angular/core';
import { applyDemoLoginFromUrl } from './auth/demo-auth';
import { groupOrdersMockInterceptor } from './group-orders/group-orders-mock.interceptor';

/**
 * Демо без бекенду: /reservations/demo-reservation/group-order?demo
 *
 * Працює і в `npm start`, і в production-збірці на CloudFront (раніше було лише під isDevMode(),
 * тому на задеплоєному застосунку запити йшли на справжній API → «Group reservation not found»).
 * Безпечно для справжніх даних: мок відповідає ЛИШЕ на ідентифікатори demo-reservation / demo-group /
 * demo-invite-token (справжні id — UUID; на ці шляхи — незалежно від токена) і на /orders, /notifications
 * з демо-токеном; усе інше йде на API без змін.
 */
export const DEMO_INTERCEPTORS: readonly HttpInterceptorFn[] = [groupOrdersMockInterceptor];

/**
 * ?demo на /reservations/demo-reservation/… — мок-логін як Owner демо-бронювання (до першої навігації роутера).
 * Справжню сесію не перезаписує і на інших шляхах не спрацьовує — див. applyDemoLoginFromUrl.
 */
export function provideDemoLogin(): EnvironmentProviders {
  return provideAppInitializer(() => {
    applyDemoLoginFromUrl();
  });
}
