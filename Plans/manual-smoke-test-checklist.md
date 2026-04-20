# Manual Smoke Test Checklist

## Setup
- Start the server with `C:\Code\Warlords\start-server.ps1`.
- Start the client with `C:\Code\Warlords\start-client.ps1`.
- Open two browser tabs to the client URL.
- Sign in as two distinct players.

## Core Lobby Flow
- Create a new `10x10` match in the first tab.
- Join the same match from the second tab.
- Confirm both tabs show `2 connected`.
- Ready both players.
- Start the match from the host tab.

## Core Turn Flow
- Confirm player 1 gets the first turn.
- Select a unit and make at least one move.
- End the turn.
- Confirm player 2 becomes the active player.
- Make at least one move and end the turn.

## Refresh And Reconnect
- While player 1 is active, refresh player 1's tab.
- Confirm the tab auto-signs back in, rejoins the match, and player 1 keeps the turn.
- Make another action without seeing `OutOfOrder`.
- Repeat the same refresh test for player 2.
- Refresh the non-active player and confirm the match still resumes cleanly.

## Disconnect Grace
- Close or disconnect the active player's tab briefly.
- Confirm the other player does not immediately inherit the turn.
- Reopen the tab within disconnect grace.
- Confirm the returning player resumes the same match and keeps the turn.
- Leave a player disconnected past disconnect grace.
- Confirm the disconnected player is removed and the turn advances if needed.

## Server Restart Resume
- With a match in progress, stop the server.
- Restart the server.
- Refresh both tabs or reconnect them.
- Confirm both players restore identity and rejoin the persisted match.
- Confirm the board state and current turn match the pre-restart state.

## Activity Feed
- Confirm join and leave entries use display names, not raw `player-...` ids.
- Confirm reconnects read like `Rejoined match ...` rather than transport/debug noise.
- Confirm successful actions do not spam raw action GUIDs.
- Confirm lobby and turn updates are readable from a player perspective.

## Log Anything Odd
- Record exact steps for any mismatch between tabs.
- Record any duplicate activity messages.
- Record any reconnect, resume, or turn-order failures before moving to new feature work.
