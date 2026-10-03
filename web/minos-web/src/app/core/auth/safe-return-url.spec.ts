import { safeReturnUrl } from './safe-return-url';

describe('safeReturnUrl', () => {
  it.each(['/problems', '/problems?page=2#problem-library', '/account', '/account?from=nav'])(
    'preserves an internal workspace destination: %s', (url) => expect(safeReturnUrl(url)).toBe(url),
  );
  it.each([null, '', 'https://other.test', '//other.test', 'javascript:alert(1)', '/accounts/me',
    '/account/login', '/account/register', '/problems/../accounts/me', '/problems/%2e%2e/accounts/me',
    '/problems/..%252faccounts/me', '/problems\\other', '/problems%5cother', '/problems%0a', '/problems%zz',
    '/problems(aux:account/login)', '/problems;redirect=//other.test'])(
    'rejects unsafe, malformed, or looping returns: %s', (url) => expect(safeReturnUrl(url)).toBe('/account'),
  );
});
