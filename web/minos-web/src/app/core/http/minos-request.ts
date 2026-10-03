import { HttpRequest } from '@angular/common/http';

import { MINOS_API_REQUEST } from './api-url.interceptor';

// Typed clients use root-relative Minos paths. An arbitrary marked URL is not trusted.
export function isMinosRequest(request: HttpRequest<unknown>): boolean {
  return request.context.get(MINOS_API_REQUEST) &&
    /^\/(accounts|api)(\/|$)/.test(request.url) && !request.url.includes('\\');
}
