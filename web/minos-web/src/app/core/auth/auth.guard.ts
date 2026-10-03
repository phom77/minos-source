import { inject } from '@angular/core';
import { CanActivateFn } from '@angular/router';
import { map } from 'rxjs';

import { AuthPrompt } from './auth-prompt.service';
import { SessionService } from './session.service';

export const authenticatedGuard: CanActivateFn = (_route, state) => {
  const prompt = inject(AuthPrompt);
  return inject(SessionService).ensure().pipe(map((session) =>
    session.status !== 'guest' ? true : prompt.entryTree('login', state.url, session.reason === 'expired' ? 'expired' : ''),
  ));
};
