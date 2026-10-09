import { isProblemId } from '../../problems/data-access/problems-api';

export const verdicts = ['Accepted', 'WrongAnswer', 'CompilationError', 'RuntimeError', 'TimeLimitExceeded', 'MemoryLimitExceeded', 'OutputLimitExceeded'] as const;
export type Verdict = typeof verdicts[number];
export type SubmissionStatus = 'Pending' | 'Judging' | 'Completed' | 'SystemFailed';
export interface SubmissionSummary {
  readonly id: string; readonly problemId: string; readonly problemVersionId: string;
  readonly status: SubmissionStatus; readonly verdict: Verdict | null;
  readonly createdAtUtc: string; readonly completedAtUtc: string | null;
}
export interface SubmissionTest {
  readonly order: number; readonly status: 'Passed' | 'NotExecuted' | Exclude<Verdict, 'Accepted' | 'CompilationError'>;
  readonly cpuTimeMilliseconds: number | null; readonly wallTimeMilliseconds: number | null; readonly memoryPeakBytes: number | null;
}
export interface SubmissionResult {
  readonly attemptId: string; readonly verdict: Verdict | null; readonly compilerDiagnostics: string;
  readonly maximumCpuTimeMilliseconds: number | null; readonly maximumWallTimeMilliseconds: number | null;
  readonly maximumMemoryBytes: number | null; readonly completedAtUtc: string; readonly tests: readonly SubmissionTest[];
}
export interface Submission extends Omit<SubmissionSummary, 'verdict'> {
  readonly language: string; readonly toolchainId: string; readonly source: string;
  readonly attempts: readonly { readonly id: string; readonly number: number; readonly status: 'Active' | 'Succeeded' | 'InfrastructureFailed' | 'LeaseExpired';
    readonly failureKind: 'ExecutorFailure' | 'WorkerFailure' | 'LeaseExpired' | null; readonly startedAtUtc: string; readonly finishedAtUtc: string | null }[];
  readonly result: SubmissionResult | null;
  readonly configuration: { readonly timeLimitMilliseconds: number; readonly memoryLimitMegabytes: number; readonly checker: 'Exact'; readonly testCount: number } | null;
}
export interface SubmissionPage { readonly items: readonly SubmissionSummary[]; readonly page: number; readonly pageSize: number; readonly total: number; }
export const activeSubmission = (status: SubmissionStatus) => status === 'Pending' || status === 'Judging';
const labels: Record<string, string> = {
  Pending: 'Queued', Judging: 'Judging', Completed: 'Completed', SystemFailed: 'Judging unavailable',
  Accepted: 'Accepted', WrongAnswer: 'Wrong answer', CompilationError: 'Compilation error', RuntimeError: 'Runtime error',
  TimeLimitExceeded: 'Time limit exceeded', MemoryLimitExceeded: 'Memory limit exceeded', OutputLimitExceeded: 'Output limit exceeded',
  Passed: 'Passed', NotExecuted: 'Not run',
};
export const outcomeLabel = (status: SubmissionStatus, verdict: Verdict | null) => labels[status === 'Completed' && verdict ? verdict : status];
export const testLabel = (status: SubmissionTest['status']) => labels[status];
export function outcomeTone(status: SubmissionStatus, verdict: Verdict | null): 'neutral' | 'success' | 'warning' | 'danger' {
  if (activeSubmission(status)) return 'warning';
  if (status === 'SystemFailed') return 'danger';
  if (status === 'Completed' && verdict) return verdict === 'Accepted' ? 'success' : 'danger';
  return 'neutral';
}
export const sourceBytes = (source: string) => new TextEncoder().encode(source).length;
export function sourceError(source: string): string | null {
  if (!source.trim()) return 'Enter your C++20 code.';
  if (source.includes('\0')) return 'Remove null characters from your code.';
  if (sourceBytes(source) > 65_536) return 'Keep your code within 65,536 bytes.';
  return null;
}
export const record = (v: unknown): v is Record<string, unknown> => typeof v === 'object' && v !== null;
const id = (v: unknown): v is string => typeof v === 'string' && isProblemId(v);
const integer = (v: unknown): v is number => typeof v === 'number' && Number.isSafeInteger(v) && v >= 0;
const positive = (v: unknown): v is number => integer(v) && v > 0;
const measurement = (v: unknown) => v === null || integer(v);
const date = (v: unknown): v is string => typeof v === 'string' && Number.isFinite(Date.parse(v));
const nullableDate = (v: unknown) => v === null || date(v);
const verdict = (v: unknown) => v === null || verdicts.some(item => item === v);
const base = (v: unknown): v is Record<string, unknown> => record(v) && id(v['id']) && id(v['problemId']) && id(v['problemVersionId']) &&
  ['Pending', 'Judging', 'Completed', 'SystemFailed'].includes(String(v['status'])) && date(v['createdAtUtc']) && nullableDate(v['completedAtUtc']);
export const isSubmissionSummary = (v: unknown): v is SubmissionSummary => base(v) && verdict(v['verdict']);
export function isSubmission(v: unknown): v is Submission {
  if (!base(v) || typeof v['source'] !== 'string' || sourceBytes(v['source']) > 65_536 || typeof v['language'] !== 'string' ||
      typeof v['toolchainId'] !== 'string' || !Array.isArray(v['attempts']) || !v['attempts'].every(a => record(a) && id(a['id']) && positive(a['number']) &&
        ['Active', 'Succeeded', 'InfrastructureFailed', 'LeaseExpired'].includes(String(a['status'])) &&
        (a['failureKind'] === null || ['ExecutorFailure', 'WorkerFailure', 'LeaseExpired'].includes(String(a['failureKind']))) && date(a['startedAtUtc']) && nullableDate(a['finishedAtUtc']))) return false;
  const c = v['configuration'];
  if (c !== null && (!record(c) || !positive(c['timeLimitMilliseconds']) || !positive(c['memoryLimitMegabytes']) || c['checker'] !== 'Exact' || !positive(c['testCount']))) return false;
  const r = v['result'];
  return r === null || (record(r) && id(r['attemptId']) && verdict(r['verdict']) && typeof r['compilerDiagnostics'] === 'string' && sourceBytes(r['compilerDiagnostics']) <= 32_768 &&
    measurement(r['maximumCpuTimeMilliseconds']) && measurement(r['maximumWallTimeMilliseconds']) && measurement(r['maximumMemoryBytes']) && date(r['completedAtUtc']) &&
    Array.isArray(r['tests']) && r['tests'].every(t => record(t) && positive(t['order']) &&
      ['NotExecuted', 'Passed', 'WrongAnswer', 'RuntimeError', 'TimeLimitExceeded', 'MemoryLimitExceeded', 'OutputLimitExceeded'].includes(String(t['status'])) &&
      measurement(t['cpuTimeMilliseconds']) && measurement(t['wallTimeMilliseconds']) && measurement(t['memoryPeakBytes'])));
}
