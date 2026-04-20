# Reconnect And Resume Notes

Date: 2026-04-19  
Repo root: `C:\Code\Warlords`

This note describes the current reconnect and resume behavior of the project as it exists now.

It is intentionally split into:
- current implemented behavior
- client expectations
- persistence/restart behavior
- known gaps and decisions still worth making explicit

## Short version

Current behavior is:
- reconnect restores player identity
- reconnect does not automatically restore active match membership on the server
- the client resumes match participation by remembering `lastGameId` and replaying `JoinGame`
- if a match is not currently loaded in memory, the server can reload it from persisted snapshot storage when `JoinGame` is called

That means the current system is best described as:
- identity-aware reconnect
- client-driven match resume

It is not a full server-driven automatic session resume model.

## Current implemented behavior

### Identity restore

When the client connects:
- it may pass a `reconnectToken`
- the server resolves that token through the identity store
- if the token is valid, the same `PlayerId` is restored
- display name is also restored or updated

Result:
- reconnect preserves player identity across reconnects, refreshes, and later sessions, as long as the browser still has the reconnect token

### Active match tracking

The server tracks active match membership per live connection in memory.

Important detail:
- this active `GameId` is attached to the current connection/session in memory
- it is not restored automatically on `ConnectAsync`
- after reconnect, the new connection starts with identity but without active match membership

Result:
- reconnect by itself is not enough to get back into a match
- a follow-up `JoinGame` call is required

### Client-driven resume

The browser currently stores:
- reconnect token
- last joined game id

The client then does the following:
- on a deliberate reconnect using the saved identity, it reconnects with the saved token
- after successful connect, it tries to `JoinGame(lastGameId)`
- on SignalR automatic reconnection, it again tries to `JoinGame(lastGameId)`

Result:
- resume currently works by replaying `JoinGame` from the client
- the client is responsible for remembering the target match

### Join behavior after reconnect

When `JoinGame(gameId)` is called:
- if the match is already loaded in memory, the server uses that session
- if the match is not loaded, the server attempts to load it from persisted snapshot storage
- if the match is `InProgress` and the reconnecting player was already part of that match, rejoin is allowed
- if the match is `InProgress` and the player was not already part of it, join is rejected with `MatchAlreadyStarted`

Result:
- reconnecting players can resume in-progress matches they previously belonged to
- new players cannot join an in-progress match

## Browser refresh behavior

Current expected behavior after browser refresh:
- reconnect token remains in browser storage
- last game id remains in browser storage
- when the user reconnects with the saved identity, the client reconnects as the same player
- the client then attempts to rejoin the saved match

Important nuance:
- refresh itself does not automatically restore the match unless the client performs the reconnect-and-join flow
- current behavior depends on the client doing that follow-up step

## Temporary connection loss behavior

Current expected behavior after temporary connection loss:
- SignalR attempts automatic reconnection
- once reconnected, the client calls `JoinGame(lastGameId)` again
- if the match still exists, the player should re-enter it as the same identity

On the server side during disconnect:
- the connection entry is removed
- if the player had an active match, that player is marked disconnected in the match
- the updated match snapshot is persisted

Result:
- temporary connection loss is treated as disconnection plus later client-driven rejoin

## Server restart behavior

Current expected behavior after server restart:
- identity can still be restored through the reconnect token stored in SQLite
- match snapshots can still be restored from SQLite
- live in-memory session membership is gone
- when the client reconnects and calls `JoinGame(lastGameId)`, the server can reload that match from snapshot storage

Important implementation detail:
- when a match is reloaded from persistence, players are restored as disconnected first
- ready states are also reset to not ready on reload

Implications:
- lobby matches loaded from persistence will not come back exactly as a live in-memory lobby
- in-progress matches can be resumed by reconnecting players, but transient live-session state is intentionally rebuilt rather than preserved exactly

## What happens if resume fails

Current client behavior:
- if `JoinGame(lastGameId)` fails during resume, the client clears the saved `lastGameId`
- the reconnect token remains
- the user keeps the same identity, but no active match is assumed

This is a reasonable fallback for now.

## Practical definition of the current model

The cleanest description of the current system is:

- Identity persistence: server-backed
- Match snapshot persistence: server-backed
- Active session resume: client-initiated
- Active match association for a live connection: in-memory only

## Important caveats and open questions

### 1. Resume is a convention, not yet a first-class contract

Right now, the client knows to resume by:
- storing `lastGameId`
- reconnecting with the token
- calling `JoinGame(lastGameId)`

That works, but it is still an implicit workflow rather than a strongly declared server contract.

Question:
- should the server eventually return explicit resume metadata on connect, or is the current client-driven pattern the intended design?

### 2. Lobby restore semantics after restart are lossy

Reloading a persisted match marks all players disconnected and clears ready state.

That may be correct, but it should be treated as an intentional rule.

Question:
- after server restart, should lobby readiness be discarded as it is now, or should some of that state survive?

### 3. Active match membership is not persisted per identity

The server does not currently persist “player X is in game Y” as part of identity/session state outside the match snapshot itself.

That means:
- the client must remember the last game
- the server cannot tell a reconnecting client “you were in this active match” during `ConnectAsync`

Question:
- is client-owned `lastGameId` sufficient, or should server-side identity/session state eventually include active match linkage?

### 4. Resume after reconnect currently reuses `JoinGame`

This is good for simplicity because there is one re-entry path.

But it also means:
- resume and fresh join are not separate concepts in the API
- the client is responsible for knowing which one it is attempting

Question:
- should there eventually be an explicit `ResumeGame` method, or is `JoinGame` intentionally the single re-entry path?

## Working recommendation

For the current phase of the project, the intended behavior should be treated as:

- reconnect restores identity
- client attempts to resume the previously active match by calling `JoinGame(lastGameId)`
- server reloads the match from persistence if needed
- rejoin to in-progress matches is allowed only for players already present in that match
- if resume fails, the client drops the saved match reference and stays connected as the same player

This is a coherent model for the prototype and early hardening phase.

The next step is to protect this model with service-level tests so it becomes verified behavior rather than just observed behavior.
