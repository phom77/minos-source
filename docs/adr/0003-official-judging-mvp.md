# ADR 0003: Official judging in the first end-to-end MVP

Decision register ID: D03\
Created: 2026-09-13\
Status: Accepted\
Decision owner: Project owner\
Accepted date: 2026-09-13

Related documents: [Decision register](../decisions.md), [M0 closure](../m0-closure.md), [Implementation roadmap](../implementation-plan.md)

## Context

Minos needs a clear first end-to-end workflow. Official judging and custom-input execution can share execution infrastructure, but have different product results, data exposure, quotas, and retention requirements.

## Decision

- Official judging is part of the first end-to-end MVP.
- The first usable evaluation workflow accepts source for a published problem, judges it asynchronously against official tests, and exposes the durable result/history.
- Custom-input Run is deferred until reliable official judging is complete.
- M8 remains the proposed implementation slot for Run after M7 development MVP acceptance. Run does not block M1 or the official-judging MVP.

## Alternatives and rationale

Including both workflows immediately adds scheduling, result, and retention concerns before official judging is reliable. Run-first development can exercise a runner but does not establish official test evaluation or durable submission correctness.

Official judging first focuses the release on Minos's central capability while leaving runner reuse available for a later increment.

## Consequences

- M1 demonstrates bounded compile/run evidence needed for judging, without building a custom Run endpoint.
- M5/M6 deliver official submission persistence, asynchronous processing, and durable results.
- M7 establishes reliability before adding custom executions.
- Later Run uses a distinct request/result lifecycle and does not create official acceptance or solving credit.
- This decision does not select checker semantics, test-order policy, queue implementation details, or final verdict precedence.

## Acceptance evidence

The project owner explicitly accepted official judging in the first end-to-end MVP and deferred custom-input Run until reliable official judging is complete, then requested M0 closure.
