import { PublishedProblem } from '../data-access/problems-api';

// Test-only data. Production pages always call the public API.
export const publishedProblem: PublishedProblem = {
  problemId: '11111111-1111-1111-1111-111111111111', versionId: '22222222-2222-2222-2222-222222222222',
  versionNumber: 2, title: 'Sum two numbers', publishedAtUtc: '2026-10-02T10:30:00Z',
  statement: 'Add the two supplied integers.\n\n<script>alert("untrusted")</script>',
  timeLimitMilliseconds: 1000, memoryLimitMegabytes: 128, checker: 'Exact', privateTestCount: 4,
  samples: [{ input: '  1 2\n', output: '3\n' }],
};
