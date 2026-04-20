# Next Session Pickup

## Current state

- The server is now seat-based internally.
- Grace expiry no longer removes units. It unclaims the seat and pauses the match.
- Turn progression and actions are blocked while any required seat is unclaimed.
- Same-identity reconnect within grace still works.
- A new identity can no longer implicitly take the first open seat in an in-progress match.
- Explicit seat claim is now implemented on the server with `ClaimSeat`.
- Server tests are green: `51/51`.

## What changed most recently

- `JoinGame` now returns `SeatClaimRequired` if an in-progress match has an unclaimed seat and the joining identity does not already own a seat.
- `ClaimSeat(gameId, seatId)` now exists in the protocol, service, hub, and engine.
- Explicit claim successfully rebinds the seat while preserving that seat's units on the board.

## Next task

Build the client-side seat claim flow.

### Goal

When a player opens or resumes a paused match with unclaimed seats, the browser should:

- show that the match is paused for seat claim
- list claimable seats
- let the player choose which seat to take over
- call the new `ClaimSeat` hub method
- resume normal play once all required seats are claimed

## Files to start with

- `C:\Code\Warlords\Client\src\App.tsx`
- `C:\Code\Warlords\Client\src\lib\gameHub.ts`
- `C:\Code\Warlords\Client\src\types\game.ts`
- `C:\Code\Warlords\Server\GameServer\GameServer.Protocol\GameHubDtos.cs`
- `C:\Code\Warlords\Server\GameServer\GameServer\Networking\GameHub.cs`

## Recommended order

1. Update the client TypeScript types for `ClaimSeatRequestDto`, `ClaimSeatResultDto`, `SeatStatusDto`, and paused-seat-claim state.
2. Add a `claimSeat` method in `gameHub.ts`.
3. Update `App.tsx` so `SeatClaimRequired` becomes a guided flow instead of a generic error.
4. Render a simple seat-picker when `gameState.isPausedForSeatClaim` is true and there are unclaimed active seats.
5. After claim succeeds, refresh local UI state from the returned `GameState`.
6. Add client-side activity messages like `Bob claimed the white seat.`

## Validation to run next session

- `dotnet test C:\Code\Warlords\Server\GameServer\GameServer.sln`
- `npm run build` in `C:\Code\Warlords\Client`
- manual browser test:
  - start a 2-player match
  - let one player exceed disconnect grace
  - verify the match pauses
  - open the missing player's browser or a new player identity
  - verify the UI offers seat selection
  - claim the correct seat
  - verify the match unpauses and continues with the preserved board state

## Important constraint

Do not reintroduce implicit seat takeover on `JoinGame`. The intended model now is:

- `JoinGame` for reconnect / lobby join
- `ClaimSeat` for explicit takeover of an unclaimed active seat
