import { ApplicationConfig, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideRouter, withInMemoryScrolling } from '@angular/router';

import { environment } from '../environments/environment';
import { API_CONFIGURATION } from './core/config/api.config';
import { apiUrlInterceptor } from './core/http/api-url.interceptor';
import { csrfInterceptor } from './core/http/csrf.interceptor';
import { sessionErrorInterceptor } from './core/http/session-error.interceptor';
import { routes } from './app.routes';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(
      routes,
      withInMemoryScrolling({ scrollPositionRestoration: 'top' }),
    ),
    provideHttpClient(withInterceptors([csrfInterceptor, sessionErrorInterceptor, apiUrlInterceptor])),
    { provide: API_CONFIGURATION, useValue: environment },
  ],
};
