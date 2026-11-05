import { ApplicationConfig, provideZoneChangeDetection } from '@angular/core';
import { provideRouter } from '@angular/router';

import { routes } from './app.routes';
import { provideHttpClient, withInterceptors, withXhr } from '@angular/common/http';
import { tokenInterceptor } from './auth/token.interceptor';
import { DEMO_INTERCEPTORS, provideDemoLogin } from './shared/mocks/demo-mode';
import { provideDaterangepickerLocale } from 'ngx-daterangepicker-bootstrap';

import { environment } from '../environments/environment';
import { API_BASE_URL } from './shared/config/api-base-url.token';

export const appConfig: ApplicationConfig = {
  providers: [
    // Демо (?demo + мок demo-reservation) — у dev і в production, див. shared/mocks/demo-mode.ts
    provideDemoLogin(),
    provideZoneChangeDetection({ eventCoalescing: true }),
    provideRouter(routes),
    provideHttpClient(
      withXhr(),
      // мок групового pre-order відповідає лише на demo-ідентифікатори, решта запитів — на справжній API
      withInterceptors([tokenInterceptor, ...DEMO_INTERCEPTORS]),
    ),
    provideDaterangepickerLocale({
      separator: ' - ',
      applyLabel: 'Apply',
      cancelLabel: 'Cancel',
      format: 'MMM D, YYYY',
    }),
    { provide: API_BASE_URL, useValue: environment.backendUrl },
  ],
};
