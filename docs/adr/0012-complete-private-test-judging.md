# ADR 0012: Judge every private test and expose accurate counts

Date: 2026-10-07
Status: Accepted by the owner for implementation.

## Context

M6 stops at the first contestant failure. The owner requires the total number of passing private tests, including tests after a failure. Counting the executed prefix cannot satisfy this. Domain rejects later executed tests, so a policy change is necessary.

## Decision

After successful compilation, execute every pinned private test. Contestant failures do not stop subsequent tests. Missing/corrupt artifacts, incomplete measurements, cancellation, runner and cleanup faults remain infrastructure failures and never produce a partial official contestant result.

Accepted requires every test to pass. Otherwise the primary verdict is the status of the earliest failed test in published order. Per-test limit precedence remains unchanged. Runtime completion requires complete resource/exit/cleanup evidence for every test. CompilationError leaves all tests NotExecuted.

The owner's result adds total, executed, passed and failed counts derived from existing durable evidence. CompilationError has zero executed/passed/failed tests. Pre-M6 markers have a null summary. Historical M6 results retain accurate prefix counts and unexecuted tests; reading them does not relabel or execute those tests.

No migration or historical rewrite is required. New completions enforce the new policy; reads preserve older evidence.

## Consequences

Failed programs may occupy more time. Admission, bounded retries, renewal and sandbox limits remain mandatory. Later phases measure overhead and bound concurrency; they do not promise universal 2–3 second judging or authorize M8/public/distributed hosting.

Implementation stays in existing projects. See the [three-phase plan](../judging-improvement-plan.md).
