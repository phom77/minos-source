import { HttpContextToken, HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, throwError } from 'rxjs';

import { SessionService } from '../auth/session.service';
import { isMinosRequest } from './minos-request';

export const SESSION_REQUIRED = new HttpContextToken<boolean>(() => false);

export const sessionErrorInterceptor: HttpInterceptorFn = (request, next) => {
  if (!isMinosRequest(request) || !request.context.get(SESSION_REQUIRED)) return next(request);
  const session = inject(SessionService);
  const identity = session.account();
  return next(request).pipe(catchError((error: unknown) => {
    if (error instanceof HttpErrorResponse && error.status === 401 && session.account() === identity) {
      session.signedOut('expired');
    }
    return throwError(() => error);
  }));
};
