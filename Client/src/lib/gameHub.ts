import * as signalR from '@microsoft/signalr'
import type {
  ActionAcceptedDto,
  ActionRejectedDto,
  ConnectedDto,
  CreateMatchRequestDto,
  GameStateDto,
  MatchSummaryDto,
  PlayerActionDto,
  PlayerJoinedDto,
  PlayerLeftDto,
  ReadyUpRequestDto,
  JoinGameResultDto,
  ResumeGameResultDto,
} from '../types/game'

export interface HubCallbacks {
  onConnected: (connected: ConnectedDto) => void
  onGameState: (state: GameStateDto) => void
  onActionAccepted: (accepted: ActionAcceptedDto) => void
  onActionRejected: (rejected: ActionRejectedDto) => void
  onPlayerJoined: (joined: PlayerJoinedDto) => void
  onPlayerLeft: (left: PlayerLeftDto) => void
  onClosed: (error?: Error) => void
  onReconnecting: (error?: Error) => void
  onReconnected: () => void
}

export class GameHubClient {
  private connection: signalR.HubConnection | null = null

  async connect(baseUrl: string, reconnectToken: string | null, displayName: string | null, callbacks: HubCallbacks) {
    const url = new URL('/hubs/game', ensureTrailingSlash(baseUrl))
    if (reconnectToken) {
      url.searchParams.set('reconnectToken', reconnectToken)
    }
    if (displayName) {
      url.searchParams.set('displayName', displayName)
    }

    const connection = new signalR.HubConnectionBuilder()
      .withUrl(url.toString())
      .withAutomaticReconnect()
      .configureLogging(signalR.LogLevel.Information)
      .build()

    connection.on('Connected', callbacks.onConnected)
    connection.on('GameState', callbacks.onGameState)
    connection.on('ActionAccepted', callbacks.onActionAccepted)
    connection.on('ActionRejected', callbacks.onActionRejected)
    connection.on('PlayerJoined', callbacks.onPlayerJoined)
    connection.on('PlayerLeft', callbacks.onPlayerLeft)
    connection.onclose(callbacks.onClosed)
    connection.onreconnecting(callbacks.onReconnecting)
    connection.onreconnected(() => callbacks.onReconnected())

    await connection.start()
    this.connection = connection
  }

  async disconnect() {
    const connection = this.connection
    this.connection = null
    if (connection) {
      await connection.stop()
    }
  }

  async listMatches(limit = 50) {
    return this.requireConnection().invoke<MatchSummaryDto[]>('ListMatches', limit)
  }

  async getConnectionInfo() {
    return this.requireConnection().invoke<ConnectedDto>('GetConnectionInfo')
  }

  async createMatch(request: CreateMatchRequestDto) {
    await this.requireConnection().invoke('CreateMatch', request)
  }

  async joinGame(gameId: string) {
    return this.requireConnection().invoke<JoinGameResultDto>('JoinGame', { gameId })
  }

  async resumeGame(gameId: string) {
    return this.requireConnection().invoke<ResumeGameResultDto>('ResumeGame', { gameId })
  }

  async leaveGame() {
    await this.requireConnection().invoke('LeaveGame')
  }

  async readyUp(request: ReadyUpRequestDto) {
    await this.requireConnection().invoke('ReadyUp', request)
  }

  async startMatch() {
    await this.requireConnection().invoke('StartMatch')
  }

  async getState() {
    await this.requireConnection().invoke('GetState')
  }

  async submitAction(action: PlayerActionDto) {
    await this.requireConnection().invoke('SubmitAction', action)
  }

  get isConnected() {
    return this.connection?.state === signalR.HubConnectionState.Connected
  }

  private requireConnection() {
    if (!this.connection) {
      throw new Error('Not connected')
    }

    return this.connection
  }
}

function ensureTrailingSlash(baseUrl: string) {
  return baseUrl.endsWith('/') ? baseUrl : `${baseUrl}/`
}

