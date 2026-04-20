# Seat Claim Session Rules

Date: 2026-04-19

This note replaces the older implicit reconnect model. The intended system is now seat-based, not strictly identity-based.

## Core rule

A match seat persists independently of the currently connected identity.

That means:
- a seat can be claimed
- a seat can become temporarily disconnected
- a seat can become unclaimed
- the units and board state for that seat still remain in the match

## Reconnect within grace

If a player disconnects and returns within disconnect grace:
- the same identity should automatically reclaim the same seat
- the seat should remain unavailable to other identities during grace
- the player should continue from the same game state

## After disconnect grace expires

If the player does not return before grace expires:
- the seat becomes unclaimed
- the units for that seat remain on the board
- the game must not progress to later turns while any required seat is unclaimed
- the same identity may later reclaim that seat
- a different identity may also claim that seat

## Anti-cheating rule

When a seat is unclaimed:
- no turn progression should occur past that missing seat
- the match should effectively pause until all required seats are reclaimed

This is meant to prevent play from continuing while one side is missing.

## Saved game behavior

The game should be saveable at any time, including:
- during normal play
- during disconnect grace
- after a seat has become unclaimed

When loading a saved game:
- players should see which seats are available to claim
- players should be able to choose which seat to take over
- players should not be forced back into their original identity

## Lobby rules

Lobby ready state should not survive disconnect:
- disconnecting in lobby clears ready
- reconnecting requires the player to ready again

## Host transfer

If the host disappears permanently:
- host should transfer to another active claimed seat

Preferred default:
- transfer host to the first connected claimed seat in deterministic slot order

## Model implication

The current codebase is too identity-centric for these rules.

The next implementation should separate:
- seat identity
- connected player identity
- ownership of units on the board
- turn order
- host authority

The target model should be:
- seats are stable
- identities claim seats
- units belong to seats
- turn order runs on seats
- saves persist seats directly
