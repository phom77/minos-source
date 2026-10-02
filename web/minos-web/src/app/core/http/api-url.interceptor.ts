import { HttpContextToken, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';

import { API_CONFIGURATION } from '../config/api.config';

// Feature data-access explicitly marks Minos requests; assets and other services stay untouched.
export const MINOS_API_REQUEST = new HttpContextToken<boolean>(() => false);

export const apiUrlInterceptor: HttpInterceptorFn = (request, next) => {
  const { apiBaseUrl } = inject(API_CONFIGURATION);

  if (
    !request.context.get(MINOS_API_REQUEST) ||
    !apiBaseUrl ||
    !request.url.startsWith('/') ||
    request.url.startsWith('//')
  ) {
    return next(request);
  }

  const normalizedBaseUrl = apiBaseUrl.replace(/\/+$/, '');
  return next(request.clone({ url: `${normalizedBaseUrl}${request.url}` }));
};
