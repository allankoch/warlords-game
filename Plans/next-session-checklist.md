# Warlords Next Session Checklist

Date: 2026-04-19  
Repo root: `C:\Code\Warlords`

This checklist is intentionally narrow. It is for the next coding session only.

## Session goal

Make reconnect and resume behavior explicit, then start protecting it with service-level tests.

## Scope for the session

Stay focused on these two outcomes:
- a written reconnect/resume behavior note
- the first batch of service-level tests for the core happy path

Do not expand gameplay in this session.

## Tasks for the next session

### 1. Write down the intended reconnect/resume rules

Create or update a short note that answers these questions:
- what does reconnect restore immediately: identity only, or identity plus active match context?
- what should happen after browser refresh?
- what should happen after temporary connection loss?
- what should happen if the match still exists in persistence but is not in memory?
- what should happen if the reconnecting player was previously in an in-progress match?
- what should the client do if resume fails?

Definition of done:
- there is one short written reference for reconnect/resume behavior

### 2. Inspect the current service flow against that intended behavior

Review the current code paths involved in:
- connect
- disconnect
- join
- reconnect
- persisted snapshot reload

Specifically check:
- whether the behavior already matches the intended rules
- where the current code is relying on client-side assumptions
- whether there are obvious mismatches between hub behavior and client behavior

Definition of done:
- any reconnect/resume gaps are identified before writing tests

### 3. Add the first service-level tests for the happy path

Add tests covering:
- player connects
- player creates a match
- second player connects and joins
- both players ready
- match starts successfully

The point of this batch is to establish the basic orchestration baseline first.

Definition of done:
- the basic two-player create/join/start flow has service-level test coverage

### 4. Add one reconnect-oriented test

Choose one of these and implement it in the same session:
- disconnect and reconnect restores the same identity
- reconnecting player can rejoin or resume the same match
- persisted match can be reloaded and resumed correctly

Pick the smallest test that best matches the intended reconnect rules from task 1.

Definition of done:
- at least one reconnect/resume behavior is protected by a test

### 5. Capture unresolved questions at the end

Do not let open questions stay implicit.

Write down:
- behaviors that still need a product decision
- behaviors that are currently inconsistent
- follow-up tests needed in the next session

Definition of done:
- the next session can start from a known list instead of re-discovery

## Nice-to-have only if time remains

Only do these if the core tasks above are complete:
- add a stale-state rejection service test
- add an out-of-order client-sequence test
- draft the manual browser smoke-test checklist

## Stop conditions

End the session after the following are true:
- reconnect/resume behavior is written down
- happy-path service tests exist for create/join/start
- one reconnect-oriented test exists
- unresolved questions are recorded

## Expected deliverables

By the end of the next session, there should be:
- one short reconnect/resume note
- one new or expanded service test file
- one reconnect/resume test
- one short list of follow-up questions or next tests
