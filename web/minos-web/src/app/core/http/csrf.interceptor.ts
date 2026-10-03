import { DOCUMENT } from '@angular/common';
import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, switchMap, throwError } from 'rxjs';

import { API_CONFIGURATION } from '../config/api.config';
import { CsrfService } from './csrf.service';
import { isMinosRequest } from './minos-request';

export const csrfInterceptor: HttpInterceptorFn = (request, next) => {
  if (!isMinosRequest(request) || ['GET', 'HEAD', 'OPTIONS'].includes(request.method)) {
    return next(request);
  }
  const base = inject(API_CONFIGURATION).apiBaseUrl;
  const location = inject(DOCUMENT).location;
  // Cookie authentication is supported only through a same-origin API/reverse proxy.
  if (base && new URL(base, location.href).origin !== location.origin) return next(request);
  const csrf = inject(CsrfService);
  return csrf.getToken().pipe(switchMap((token) =>
    next(request.clone({ setHeaders: { 'X-CSRF-TOKEN': token } })),
  ), catchError((error: unknown) => {
    // The API returns 400 for invalid protection as well as validation. Fetch fresh protection
    // on the next explicit attempt; never replay a possibly completed mutation automatically.
    if (error instanceof HttpErrorResponse && error.status === 400) csrf.invalidate();
    return throwError(() => error);
  }));
};
