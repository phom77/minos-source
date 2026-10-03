export function safeReturnUrl(value: string | null | undefined): string {
  if (!value || !value.startsWith('/') || /[\\\u0000-\u001f\u007f]/.test(value)) return '/account';
  try {
    const decoded = decodeURIComponent(value);
    const path = decoded.split(/[?#]/)[0];
    // Only workspace routes: no transport endpoints, external destinations, or auth loops.
    if (!/^\/(problems(?:[/?#]|$)|account(?:[?#]|$))/.test(value) ||
        !/^\/(problems(?:[/?#]|$)|account(?:[?#]|$))/.test(decoded) ||
        /[\\\u0000-\u001f\u007f]/.test(decoded) || /[%;()]/.test(path) ||
        path.split('/').some((part) => part === '.' || part === '..')) {
      return '/account';
    }
    return value;
  } catch { return '/account'; }
}
