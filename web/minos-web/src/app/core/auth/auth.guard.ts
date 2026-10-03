import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { map } from 'rxjs';

import { safeReturnUrl } from './safe-return-url';
import { SessionService } from './session.service';

export const authenticatedGuard: CanActivateFn = (_route, state) => {
  const router = inject(Router);
  return inject(SessionService).ensure().pipe(map((session) =>
    session.status !== 'guest' ? true : router.createUrlTree(['/account/login'], {
      queryParams: { returnUrl: safeReturnUrl(state.url), ...(session.reason === 'expired' ? { expired: '1' } : {}) },
    }),
  ));
};
