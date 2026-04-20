# Warlords Short-Term Tasks

Date: 2026-04-19  
Repo root: `C:\Code\Warlords`

This file is the concrete follow-up to `server-review-and-roadmap.md`.

The goal is no longer just reconnect reliability. The goal is to convert the prototype to a reclaimable seat model that supports pausing and resuming real matches safely.

## Current objective

Stabilize the existing two-player browser prototype and shift it to a seat-based match model:
- connect
- create/join
- ready/start
- move
- attack
- end turn
- disconnect/reconnect
- seat reclaim after disconnect
- save and later resume a match

## Immediate task list

### 1. Lock in the new seat/reclaim rules

Why:
- the intended behavior is now broader than reconnect
- the current implementation still assumes player identities own seats directly

Tasks:
- keep `seat-claim-session-rules.md` up to date as the authoritative rule note
- use it to define:
  - reconnect within grace
  - seat becoming unclaimed after grace
  - save/load reclaim behavior
  - paused progression while seats are unclaimed
  - host transfer

Done when:
- future work is anchored to a clear seat-based model

### 2. Refactor server state to explicit seats

Why:
- reclaimable seats, paused progression, and save-load claim all require seats to exist independently

Tasks:
- separate seat identity from player identity
- move slot ownership and turn order to seat state
- keep units attached to seats
- represent claimed vs disconnected vs unclaimed explicitly

Done when:
- the match model can express an unclaimed seat without deleting units or losing turn state

### 3. Update persistence and restore for seat-based snapshots

Why:
- saving at any time is now a core requirement, including while seats are missing

Tasks:
- extend snapshot schema for seat state
- persist host, turn order, and seat claim state independently
- restore matches with unclaimed seats intact

Done when:
- a saved match can be restored with its seat ownership situation preserved

### 4. Add seat claim flows

Why:
- players need a way to take over an open seat later

Tasks:
- allow automatic reclaim for same identity within grace
- allow explicit claim of an unclaimed seat after grace
- allow claim selection when loading a saved game
- reject claims for seats still protected by grace

Done when:
- a player can reconnect or explicitly claim an open seat through supported paths

### 5. Pause play while seats are unclaimed

Why:
- the game must not progress around a missing player

Tasks:
- block turn progression while required seats are unclaimed
- block actions if the match is in a paused-for-reclaim state
- keep the board state intact during the pause

Done when:
- the match cannot continue unfairly while a seat is abandoned

### 6. Transfer host on permanent disappearance

Why:
- administrative actions should not be trapped on a permanently lost seat

Tasks:
- define deterministic host transfer
- apply it when host seat becomes permanently unclaimed
- test the rule

Done when:
- lobby/control flow stays operable after permanent host loss

## Suggested order for the next coding sessions

### Session 1
- lock the seat/reclaim rules
- begin the server domain refactor toward explicit seats

### Session 2
- update snapshot persistence and load paths
- add service tests around unclaimed seats and paused matches

### Session 3
- add seat claim flows and host transfer
- expose enough state for the client to support claims

### Session 4
- add save/resume UI and reclaim UX
- re-run the smoke checklist

## Candidate next gameplay slice after the seat model is stable

Do not start this until the seat model is in place.

Best candidate:
- add a real win condition or castle capture

That likely means:
- clear ownership consequences
- a visible objective beyond movement
- tests covering the rule and its client-facing outcome

## Non-goals right now

These should wait unless they directly support the seat refactor:
- multiple new mechanics in parallel
- large UI redesign
- AI opponents
- large-scale map/content expansion

## Short version

Next focus:
1. seat-claim rules
2. explicit seat state in the server model
3. seat-aware persistence
4. seat claim flows
5. paused progression while seats are missing
6. host transfer
