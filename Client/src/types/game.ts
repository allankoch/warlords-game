export interface ConnectedDto {
  playerId: string
  reconnectToken: string
  displayName: string | null
  resumeGameId: string | null
}

export interface CreateMatchRequestDto {
  gameId: string
  mapId: string
  minPlayers: number
  maxPlayers: number
  autoStart: boolean
  turnTimeLimitSeconds: number
  disconnectGraceSeconds: number
}

export interface ReadyUpRequestDto {
  isReady: boolean
}

export interface SendLobbyChatRequestDto {
  message: string
}

export interface ClaimSeatRequestDto {
  gameId: string
  seatId: string
}

export interface PlayerPresenceDto {
  playerId: string
  isConnected: boolean
  displayName: string | null
}

export interface PlayerReadyDto {
  playerId: string
  isReady: boolean
}

export interface PlayerSlotDto {
  playerId: string
  slot: string
}

export interface SeatStatusDto {
  seatId: string
  isActive: boolean
  isClaimed: boolean
  claimedByPlayerId: string | null
  displayName: string | null
  isConnected: boolean
  isReady: boolean
  hasUnits: boolean
}

export interface EntityStateDto {
  entityId: string
  ownerPlayerId: string
  x: number
  y: number
}

export interface AvailableEndTurnActionDto {
  type: 'endTurn'
}

export interface AvailableMoveActionDto {
  type: 'move'
  entityId: string
  x: number
  y: number
}

export interface AvailableAttackActionDto {
  type: 'attack'
  entityId: string
  targetEntityId: string
  x: number
  y: number
}

export type AvailableActionDto = AvailableEndTurnActionDto | AvailableMoveActionDto | AvailableAttackActionDto

export interface EndTurnActionDto {
  type: 'endTurn'
  actionId: string
  clientSequence: number
  expectedStateVersion?: number
}

export interface MoveEntityActionDto {
  type: 'move'
  actionId: string
  clientSequence: number
  expectedStateVersion?: number
  entityId: string
  x: number
  y: number
}

export interface AttackEntityActionDto {
  type: 'attack'
  actionId: string
  clientSequence: number
  expectedStateVersion?: number
  entityId: string
  targetEntityId: string
}

export type PlayerActionDto = EndTurnActionDto | MoveEntityActionDto | AttackEntityActionDto

export interface GameStateDto {
  gameId: string
  version: number
  mapId: string
  phase: string
  minPlayers: number
  maxPlayers: number
  hostPlayerId: string
  players: PlayerPresenceDto[]
  ready: PlayerReadyDto[]
  slots: PlayerSlotDto[]
  entities: EntityStateDto[]
  availableActions: AvailableActionDto[]
  currentTurnPlayerId: string | null
  turnNumber: number
  turnEndsAt: string | null
  serverActionSequence: number
  lastAction: PlayerActionDto | null
  serverTime: string
  seats: SeatStatusDto[] | null
  isPausedForSeatClaim: boolean
}

export interface ActionAcceptedDto {
  actionId: string
  stateVersion: number
  serverActionSequence: number
}

export interface ActionRejectedDto {
  actionId: string
  reason: string
  stateVersion: number
  serverActionSequence: number
}

export interface JoinGameResultDto {
  joined: boolean
  reason: string | null
  gameState: GameStateDto | null
}

export interface ResumeGameResultDto {
  resumed: boolean
  reason: string | null
  gameState: GameStateDto | null
}

export interface ClaimSeatResultDto {
  claimed: boolean
  reason: string | null
  gameState: GameStateDto | null
}

export interface MatchSummaryDto {
  gameId: string
  mapId: string
  phase: string
  isPausedForSeatClaim: boolean
  hostPlayerId: string
  hostDisplayName: string | null
  maxPlayers: number
  version: number
  serverActionSequence: number
  playerCount: number
  openSeatCount: number
  savedAt: string
}

export interface LobbyChatMessageDto {
  messageId: string
  playerId: string
  displayName: string | null
  message: string
  sentAt: string
}

export interface PlayerJoinedDto {
  gameId: string
  playerId: string
  displayName: string | null
}

export interface PlayerLeftDto {
  gameId: string
  playerId: string
  displayName: string | null
}

export interface MapTileDto {
  tileId: number
  type: string
  owner: string | null
  isBlocked: boolean
}

export interface SpawnPointDto {
  owner: string
  x: number
  y: number
}

export interface MapViewDto {
  mapId: string
  width: number
  height: number
  tiles: number[]
  tilePalette: MapTileDto[]
  spawnPoints: SpawnPointDto[]
}

