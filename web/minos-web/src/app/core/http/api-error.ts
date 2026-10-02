import { HttpErrorResponse } from '@angular/common/http';

export interface ApiError {
  readonly kind: 'api' | 'network' | 'unexpected';
  readonly status: number | null;
  readonly title: string;
  readonly detail: string | null;
  readonly traceId: string | null;
}

interface ProblemDetailsPayload {
  readonly title?: unknown;
  readonly detail?: unknown;
  readonly traceId?: unknown;
}

export function toApiError(error: unknown): ApiError {
  if (!(error instanceof HttpErrorResponse)) {
    return {
      kind: 'unexpected',
      status: null,
      title: 'Something went wrong',
      detail: null,
      traceId: null,
    };
  }

  if (error.status === 0) {
    return {
      kind: 'network',
      status: 0,
      title: 'Minos could not be reached',
      detail: 'Check your connection and try again.',
      traceId: null,
    };
  }

  const body: unknown = error.error;
  const payload = isProblemDetails(body) ? body : null;
  return {
    kind: 'api',
    status: error.status,
    title: readString(payload?.title) ?? 'The request could not be completed',
    detail: readString(payload?.detail),
    traceId: readString(payload?.traceId),
  };
}

function isProblemDetails(value: unknown): value is ProblemDetailsPayload {
  return typeof value === 'object' && value !== null;
}

function readString(value: unknown): string | null {
  return typeof value === 'string' && value.trim().length > 0 ? value : null;
}
