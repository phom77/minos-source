import { HttpErrorResponse } from '@angular/common/http';

import { toApiError } from './api-error';

describe('toApiError', () => {
  it('keeps safe ProblemDetails fields available to a feature', () => {
    const error = new HttpErrorResponse({
      status: 409,
      error: {
        title: 'Account already exists',
        detail: 'Use a different email address.',
        traceId: 'trace-123',
        exception: 'must not be copied',
      },
    });

    expect(toApiError(error)).toEqual({
      kind: 'api',
      status: 409,
      title: 'Account already exists',
      detail: 'Use a different email address.',
      traceId: 'trace-123',
    });
  });

  it('uses a useful message for a transport failure', () => {
    const error = new HttpErrorResponse({ status: 0 });

    expect(toApiError(error).kind).toBe('network');
    expect(toApiError(error).detail).toBe('Check your connection and try again.');
  });

  it.each([
    '<html>Internal server response</html>',
    { title: ['invalid'], detail: { exception: 'private data' }, traceId: 123 },
    null,
  ])('handles a missing or malformed ProblemDetails body safely: %j', (body) => {
    const error = new HttpErrorResponse({ status: 502, error: body });

    expect(toApiError(error)).toEqual({
      kind: 'api',
      status: 502,
      title: 'The request could not be completed',
      detail: null,
      traceId: null,
    });
  });

  it('does not expose an unexpected exception message', () => {
    expect(toApiError(new Error('private diagnostic data'))).toEqual({
      kind: 'unexpected',
      status: null,
      title: 'Something went wrong',
      detail: null,
      traceId: null,
    });
  });
});
