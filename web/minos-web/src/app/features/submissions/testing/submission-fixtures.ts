import { Submission, SubmissionPage } from '../data-access/submission.models';
import { publishedProblem } from '../../problems/testing/problem-fixtures';

export const account = { id: 'solver-account', displayName: 'An', role: 'Solver' as const, createdAtUtc: '2026-10-01T00:00:00Z' };
export const submission: Submission = {
  id: '33333333-3333-3333-3333-333333333333', problemId: publishedProblem.problemId, problemVersionId: publishedProblem.versionId,
  language: 'cpp20', toolchainId: 'gcc-cpp20-pinned', source: '#include <iostream>\nint main() { std::cout << 3; }\n',
  status: 'Pending', createdAtUtc: '2026-10-07T08:00:00Z', completedAtUtc: null, attempts: [], result: null,
  configuration: { timeLimitMilliseconds: 1000, memoryLimitMegabytes: 128, checker: 'Exact', testCount: 2 },
};
export const completed: Submission = { ...submission, status: 'Completed', completedAtUtc: '2026-10-07T08:00:05Z',
  attempts: [{ id: '44444444-4444-4444-4444-444444444444', number: 1, status: 'Succeeded', failureKind: null, startedAtUtc: '2026-10-07T08:00:01Z', finishedAtUtc: '2026-10-07T08:00:05Z' }],
  result: { attemptId: '44444444-4444-4444-4444-444444444444', verdict: 'Accepted', compilerDiagnostics: '', completedAtUtc: '2026-10-07T08:00:05Z',
    maximumCpuTimeMilliseconds: 12, maximumWallTimeMilliseconds: 24, maximumMemoryBytes: 2_097_152,
    tests: [ { order: 1, status: 'Passed', cpuTimeMilliseconds: 0, wallTimeMilliseconds: 12, memoryPeakBytes: 1_048_576 },
      { order: 2, status: 'Passed', cpuTimeMilliseconds: 12, wallTimeMilliseconds: 24, memoryPeakBytes: 2_097_152 } ] },
};
export const history = (page = 1, pageSize = 20): SubmissionPage => ({ page, pageSize, total: 1,
  items: [{ id: completed.id, problemId: completed.problemId, problemVersionId: completed.problemVersionId, status: 'Completed', verdict: 'Accepted', createdAtUtc: completed.createdAtUtc, completedAtUtc: completed.completedAtUtc }] });
