# Warlords Revised Roadmap

Date: 2026-04-19  
Repo root: `C:\Code\Warlords`  
Covered areas: `Server`, `Client`, shared client/server contract

## Current position

The project is no longer at the “can we get a browser talking to the server?” stage.

There is already a working vertical slice:
- ASP.NET server on `.NET 10`
- SignalR-based multiplayer transport
- React + TypeScript client
- reconnect token flow
- match create/join/ready/start
- map loading and rendering
- two-player movement loop on a 10x10 prototype map
- persistence-backed session state

That changes the priority.

The most important next step is not broad feature expansion. The most important next step is to harden the current slice so future gameplay work is built on stable rules, stable session lifecycle, and a stable client/server contract.

## What this plan changes

The earlier roadmap assumed:
- frontend integration was still ahead of us
- the playable browser loop still needed to be built
- transport/CORS/client wiring were the main blockers

That is no longer true.

The revised approach is:
1. stabilize the existing playable slice
2. remove ambiguity in the client/server contract
3. clean up prototype debt that will slow future work
4. then expand gameplay one thin slice at a time

## Main recommendation

Continue from the current prototype, but do not mainly continue by adding more mechanics yet.

Use the next stretch of work to make the current loop reliable under:
- reconnects
- invalid actions
- stale state submissions
- browser refreshes
- match restore from persistence
- repeated local testing

If the current slice is shaky, every new rule will cost more to build and more to debug.

## Revised priorities

### Priority 1: Harden the existing vertical slice

Goal: make the current two-player prototype reliable enough that it becomes a trustworthy foundation.

Focus areas:
- connection lifecycle consistency
- reconnect and resume behavior
- match state persistence and reload behavior
- action sequencing and stale-state rejection
- deterministic turn progression
- server/client state synchronization after disconnects and reconnects

Definition of done:
- two players can repeatedly connect, create/join, ready, start, move, end turn, disconnect, reconnect, and continue without manual cleanup
- refresh/reconnect behavior is predictable and documented
- invalid or stale actions fail cleanly and visibly

### Priority 2: Lock down the contract between server and client

Goal: stop contract drift between C# DTOs and TypeScript models.

Current reality:
- the server contract is implemented in C#
- the client mirrors it in handwritten TypeScript
- the UI now depends on authoritative server state plus server-authored available actions

That is workable, but it becomes a maintenance risk if left informal.

Tasks:
- document the authoritative game lifecycle from the browser’s perspective
- define stable semantics for `Connected`, `GameState`, `ActionAccepted`, and `ActionRejected`
- document reconnect behavior clearly:
  - identity restore
  - match resume
  - what the client should do after reconnect
- document action requirements:
  - `ActionId`
  - `ClientSequence`
  - `ExpectedStateVersion`
- define the meaning of important rejection codes such as:
  - `NotYourTurn`
  - `StaleState`
  - `OutOfOrder`
  - `MatchAlreadyStarted`
  - `GameFull`

Definition of done:
- the client can be maintained without guessing server behavior
- new gameplay features can add contract changes intentionally instead of implicitly

### Priority 3: Add test coverage above the pure rules engine

Goal: catch regressions where the real risk now lives.

The engine matters, but the current higher-value bugs are likely in:
- session orchestration
- reconnect behavior
- persistence restore
- hub/service interaction
- action ordering and replay

Recommended coverage:
- `GameService` tests for connect, create, join, leave, disconnect, reconnect, submit action, and tick
- persistence-oriented tests that load snapshots back into active sessions
- tests for stale state and out-of-order sequence handling
- tests for reconnecting into an active match
- tests for timeout-driven turn changes
- tests for “all players disconnected” and similar lifecycle edge cases

Definition of done:
- regressions in session lifecycle are caught before manual browser testing
- persistence and reconnect behavior are protected by repeatable tests

### Priority 4: Clean up prototype debt

Goal: reduce accidental complexity before feature expansion.

Areas to clean up:
- temporary helper scripts and local test artifacts that no longer belong in the main flow
- old or duplicate model concepts that are no longer part of the active gameplay path
- repo clutter from generated outputs that should not shape architecture decisions
- leftover naming that still reflects “prototype wiring” instead of stable concepts

Questions to answer:
- which action/entity classes are legacy versus active?
- which data structures are now source-of-truth, and which are only transitional?
- which files are safe to delete versus still useful for local testing?

Definition of done:
- the active architecture is easier to understand at a glance
- feature work no longer has to route around prototype leftovers

### Priority 5: Expand gameplay one thin slice at a time

Goal: grow the game without destabilizing the working loop.

Do not expand in multiple directions at once.

Recommended approach:
- choose one next mechanic
- implement it server-first
- expose it through the authoritative contract
- reflect it in the UI
- add tests in the same change set

Suggested next slices:
1. attack resolution with actual gameplay consequences
2. hit points / damage / death removal
3. win-loss condition for the prototype
4. terrain-specific movement or combat effects
5. economy / capture / structures

Guideline:
- every new mechanic should land with protocol changes, engine rules, persistence compatibility, and client handling together

### Priority 6: Operational hardening

Goal: make the project easier to run, debug, and evolve.

Tasks:
- improve logging around connect/disconnect/join/leave/action rejection paths
- add startup validation for maps and storage
- define snapshot compatibility expectations
- decide how accepted action logs should support debugging or replay
- decide when inactive sessions should be reloaded or evicted

Definition of done:
- local debugging is faster
- production-style issues are easier to reason about
- persistence evolution has an explicit path

## Recommended next backlog

If continuing from today’s state, the backlog should be:

1. Add service-level and end-to-end tests for the current two-player prototype loop.
2. Write a short contract document for reconnect, resume, versioning, and rejection semantics.
3. Review and clean prototype debt in both `Server` and `Client`.
4. Verify persistence/reconnect behavior with repeated local runs and codify the expected behavior in tests.
5. Only then choose the next gameplay slice to add.

## What not to do yet

Avoid these for now unless they directly support the hardening work:
- adding several new action types at once
- broad UI redesign unrelated to gameplay/testing clarity
- speculative abstractions for future factions, AI, or content scale
- major persistence redesign before current lifecycle behavior is proven

## Working principle for the next phase

The client should remain a thin projection of authoritative server state.

The server should remain the owner of:
- legality
- sequencing
- turn state
- outcomes
- available actions

The safest growth path is:
- keep the loop small
- make it reliable
- then add one rule at a time

## Immediate recommendation

The current prototype is good enough to stop proving that the architecture can work.

The next goal is to prove that the current gameplay slice is stable under real usage patterns. Once that is true, feature expansion becomes much cheaper and much less risky.
