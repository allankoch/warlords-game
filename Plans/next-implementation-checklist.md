# Next Implementation Checklist

## 1. Lock in the seat-claim rules
- Use `seat-claim-session-rules.md` as the authoritative session model.
- Treat reconnect, reclaim, saved-game resume, and host transfer as seat-based behavior.
- Do not add more reconnect features against the old player-owned seat model.

## 2. Refactor the domain model to explicit seats
- Separate seat identity from connected player identity.
- Make units belong to seats rather than directly to reconnect identities.
- Move turn order and host authority to seats.
- Make unclaimed seats first-class state, not implicit absence.

## 3. Update persistence for seat-based saves
- Introduce a new snapshot shape/schema for seat state.
- Persist claimed, disconnected, and unclaimed seats explicitly.
- Support loading matches that contain unclaimed seats.
- Keep save-at-any-time behavior intact.

## 4. Add seat claim flows
- Add the server-side claim logic for reconnect, fresh claim, and save-load resume.
- Allow the same identity or a different identity to claim an open seat.
- Prevent claiming a seat still protected by reconnect grace.
- Expose enough state for the client to show claimable seats.

## 5. Enforce paused play while seats are missing
- Do not advance turn order while any required seat is unclaimed.
- Do not allow the match to silently play around a missing side.
- Keep board units for unclaimed seats on the map until reclaimed.

## 6. Add host transfer
- Transfer host when the host seat becomes permanently unclaimed.
- Use a deterministic replacement rule.
- Cover the rule with tests.

## 7. Re-run the regression pass
- Run the server test suite.
- Run the client build.
- Re-run the manual smoke checklist before expanding gameplay.
