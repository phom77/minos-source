import { toApiError } from '../../core/http/api-error';
import { ProtectionChangedError } from '../../core/http/csrf.service';

export function authErrorMessage(error: unknown, operation: 'login' | 'register' | 'logout'): string {
  if (error instanceof ProtectionChangedError) return 'Your session changed. Check your account, then try again.';
  const { status, kind } = toApiError(error);
  if (status === 429) return 'Too many attempts. Wait a minute before trying again.';
  if (status === 401 && operation === 'login') return 'Sign-in failed. Check your email and password, then try again.';
  if (status === 403) return 'This request is not permitted. Check your session and try again.';
  if (kind === 'network' || status === null || (status !== null && status >= 500)) {
    return operation === 'logout'
      ? 'Sign-out could not be confirmed. Check your connection and try again; your session may still be active.'
      : 'Minos could not confirm this request. Check your connection and try again. If registration may have completed, try signing in.';
  }
  return operation === 'register'
    ? 'Registration could not be completed with these details. Check the fields or sign in if you already have an account.'
    : 'The request could not be completed. Check your connection and session protection, then try again.';
}
