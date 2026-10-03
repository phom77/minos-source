export interface CurrentAccount {
  readonly id: string;
  readonly displayName: string;
  readonly role: 'Solver' | 'Setter' | 'Administrator';
  readonly createdAtUtc: string;
}

export type SessionState =
  | { readonly status: 'checking' }
  | { readonly status: 'authenticated'; readonly account: CurrentAccount }
  | { readonly status: 'guest'; readonly reason: 'signed-out' | 'expired' }
  | { readonly status: 'unavailable' };

export function isCurrentAccount(value: unknown): value is CurrentAccount {
  if (typeof value !== 'object' || value === null) return false;
  const account = value as Partial<CurrentAccount>;
  return typeof account.id === 'string' && account.id.length > 0 &&
    typeof account.displayName === 'string' && account.displayName.trim().length > 0 &&
    ['Solver', 'Setter', 'Administrator'].includes(account.role ?? '') &&
    typeof account.createdAtUtc === 'string' && Number.isFinite(Date.parse(account.createdAtUtc));
}
