import { startTransition, useEffect, useEffectEvent, useRef, useState } from 'react'
import './App.css'
import { GameHubClient } from './lib/gameHub'
import type {
  ActionAcceptedDto,
  ActionRejectedDto,
  AttackEntityActionDto,
  ConnectedDto,
  CreateMatchRequestDto,
  EntityStateDto,
  GameStateDto,
  MapTileDto,
  MapViewDto,
  MatchSummaryDto,
  MoveEntityActionDto,
  PlayerJoinedDto,
  PlayerLeftDto,
  PlayerPresenceDto,
} from './types/game'

const reconnectTokenStorageKey = 'warlords.reconnectToken'
const lastGameStorageKey = 'warlords.lastGameId'
const playerNameStorageKey = 'warlords.playerName'
const defaultServerUrl = import.meta.env.VITE_SERVER_URL ?? 'http://localhost:5118'
const boardRadius = 6

type ConnectionStatus = 'disconnected' | 'connecting' | 'connected' | 'reconnecting'
type VisibleBoardCell = { x: number; y: number; tile: MapTileDto; entity?: EntityStateDto }

function App() {
  const hubRef = useRef(new GameHubClient())
  const nextClientSequenceRef = useRef(1)
  const autoConnectAttemptRef = useRef<string | null>(null)
  const previousGameStateRef = useRef<GameStateDto | null>(null)
  const [serverUrl, setServerUrl] = useState(defaultServerUrl)
  const [connectionStatus, setConnectionStatus] = useState<ConnectionStatus>('disconnected')
  const [playerId, setPlayerId] = useState<string | null>(null)
  const [playerName, setPlayerName] = useState<string>(() => localStorage.getItem(playerNameStorageKey) ?? '')
  const [currentDisplayName, setCurrentDisplayName] = useState<string | null>(null)
  const [savedReconnectToken, setSavedReconnectToken] = useState<string | null>(() => localStorage.getItem(reconnectTokenStorageKey))
  const [reconnectToken, setReconnectToken] = useState<string | null>(null)
  const [lastGameId, setLastGameId] = useState<string | null>(() => localStorage.getItem(lastGameStorageKey))
  const [matches, setMatches] = useState<MatchSummaryDto[]>([])
  const [showMatches, setShowMatches] = useState(false)
  const [gameState, setGameState] = useState<GameStateDto | null>(null)
  const [mapData, setMapData] = useState<MapViewDto | null>(null)
  const [mapError, setMapError] = useState<string | null>(null)
  const [activity, setActivity] = useState<string[]>([])
  const [error, setError] = useState<string | null>(null)
  const [isBusy, setIsBusy] = useState(false)
  const [selectedEntityId, setSelectedEntityId] = useState('')
  const [attackTargetEntityId, setAttackTargetEntityId] = useState('')
  const [createForm, setCreateForm] = useState<CreateMatchRequestDto>({ gameId: `match-${crypto.randomUUID().slice(0, 8)}`, mapId: 'demo-10x10', minPlayers: 2, maxPlayers: 2, autoStart: false, turnTimeLimitSeconds: 0, disconnectGraceSeconds: 120 })
  const [joinGameId, setJoinGameId] = useState('')
  const [moveForm, setMoveForm] = useState({ entityId: '', x: 0, y: 0 })

  const appendActivity = useEffectEvent((message: string) => startTransition(() => setActivity((current) => [message, ...current].slice(0, 12))))

  const loadNextClientSequence = useEffectEvent((nextPlayerId: string) => {
    const storedValue = localStorage.getItem(getClientSequenceStorageKey(nextPlayerId))
    const parsedValue = storedValue ? Number.parseInt(storedValue, 10) : Number.NaN
    nextClientSequenceRef.current = Number.isFinite(parsedValue) && parsedValue > 0 ? parsedValue : 1
  })

  const reserveClientSequence = useEffectEvent(() => {
    const activePlayerId = playerId
    const sequence = nextClientSequenceRef.current
    nextClientSequenceRef.current += 1

    if (activePlayerId) {
      localStorage.setItem(getClientSequenceStorageKey(activePlayerId), String(nextClientSequenceRef.current))
    }

    return sequence
  })

  const syncSelectedEntity = useEffectEvent((state: GameStateDto) => {
    const selected = state.entities.find((entity) => entity.entityId === selectedEntityId)
    const fallback = selected
      ?? state.entities.find((entity) => entity.ownerPlayerId === playerId)
      ?? state.entities.find((entity) => entity.ownerPlayerId === state.currentTurnPlayerId)
      ?? state.entities[0]
    if (!fallback) {
      setSelectedEntityId('')
      setAttackTargetEntityId('')
      setMoveForm({ entityId: '', x: 0, y: 0 })
      return
    }

    setSelectedEntityId(fallback.entityId)
    setMoveForm((current) => {
      const availableMoves = getAvailableMoveActionsForEntity(state, fallback.entityId)
      const preservedTarget = availableMoves.find((move) => move.x === current.x && move.y === current.y)
      const nextTarget = preservedTarget ?? availableMoves[0] ?? fallback
      return { entityId: fallback.entityId, x: nextTarget.x, y: nextTarget.y }
    })
    setAttackTargetEntityId((current) => {
      const availableAttacks = getAvailableAttackActionsForEntity(state, fallback.entityId)
      const preservedTarget = availableAttacks.find((attack) => attack.targetEntityId === current)
      return preservedTarget?.targetEntityId ?? availableAttacks[0]?.targetEntityId ?? ''
    })
  })

  const tryResumeGame = useEffectEvent(async (gameId: string | null, reason: string) => {
    if (!gameId) return
    try {
      const result = await hubRef.current.resumeGame(gameId)
      if (!result.resumed) {
        previousGameStateRef.current = null
        setGameState(null)
        setMapData(null)
        setMapError(null)
        setSelectedEntityId('')
        setAttackTargetEntityId('')
        setMoveForm({ entityId: '', x: 0, y: 0 })
        localStorage.removeItem(lastGameStorageKey)
        setLastGameId(null)
        await refreshMatchesCore()
        setError(`Couldn't rejoin the match: ${humanizeResumeFailure(result.reason)}`)
        return
      }
      appendActivity(describeResume(reason, gameId))
    } catch (resumeError) {
      previousGameStateRef.current = null
      setGameState(null)
      setMapData(null)
      setMapError(null)
      setSelectedEntityId('')
      setAttackTargetEntityId('')
      setMoveForm({ entityId: '', x: 0, y: 0 })
      localStorage.removeItem(lastGameStorageKey)
      setLastGameId(null)
      setError(`Couldn't rejoin the match: ${humanizeHubError(resumeError)}`)
    }
  })

  const refreshMatchesCore = useEffectEvent(async () => {
    const nextMatches = await hubRef.current.listMatches()
    setMatches(nextMatches)
    appendActivity(`Match list updated. ${nextMatches.length} match${nextMatches.length === 1 ? '' : 'es'} available.`)
  })

  useEffect(() => {
    localStorage.setItem(playerNameStorageKey, playerName)
  }, [playerName])

  useEffect(() => {
    if (connectionStatus !== 'disconnected') return
    if (!savedReconnectToken) return
    if (isBusy) return
    if (autoConnectAttemptRef.current === savedReconnectToken) return

    autoConnectAttemptRef.current = savedReconnectToken
    void connect(savedReconnectToken, true)
  }, [connectionStatus, isBusy, savedReconnectToken])

  useEffect(() => {
    if (!gameState) {
      setMapData(null)
      setMapError(null)
      return
    }

    let cancelled = false
    const loadMap = async () => {
      try {
        setMapError(null)
        const response = await fetch(new URL(`/maps/${gameState.mapId}`, ensureTrailingSlash(serverUrl)))
        if (!response.ok) throw new Error(`Map request failed: ${response.status}`)
        const map = (await response.json()) as MapViewDto
        if (!cancelled) setMapData(map)
      } catch (loadError) {
        if (!cancelled) setMapError(getErrorMessage(loadError))
      }
    }

    void loadMap()
    return () => {
      cancelled = true
    }
  }, [gameState?.mapId, serverUrl])

  const handleConnected = useEffectEvent(async (connected: ConnectedDto, shouldResumeLastGame: boolean) => {
    autoConnectAttemptRef.current = null
    setPlayerId(connected.playerId)
    loadNextClientSequence(connected.playerId)
    setCurrentDisplayName(connected.displayName)
    setReconnectToken(connected.reconnectToken)
    setSavedReconnectToken(connected.reconnectToken)
    localStorage.setItem(reconnectTokenStorageKey, connected.reconnectToken)
    setConnectionStatus('connected')
    setError(null)
    appendActivity(`Signed in as ${connected.displayName ?? connected.playerId}.`)
    await refreshMatchesCore()
    const resumeGameId = connected.resumeGameId ?? (shouldResumeLastGame ? localStorage.getItem(lastGameStorageKey) : null)
    if (resumeGameId) {
      localStorage.setItem(lastGameStorageKey, resumeGameId)
      setLastGameId(resumeGameId)
      await tryResumeGame(resumeGameId, 'connect')
    }
  })

  const syncConnectionInfo = useEffectEvent((connected: ConnectedDto) => {
    setPlayerId(connected.playerId)
    loadNextClientSequence(connected.playerId)
    setCurrentDisplayName(connected.displayName)
    setReconnectToken(connected.reconnectToken)
    setSavedReconnectToken(connected.reconnectToken)
    localStorage.setItem(reconnectTokenStorageKey, connected.reconnectToken)
    if (connected.resumeGameId) {
      localStorage.setItem(lastGameStorageKey, connected.resumeGameId)
      setLastGameId(connected.resumeGameId)
    }
  })

  const handleGameState = useEffectEvent((state: GameStateDto) => {
    const previousState = previousGameStateRef.current
    previousGameStateRef.current = state
    setGameState(state)
    setLastGameId(state.gameId)
    localStorage.setItem(lastGameStorageKey, state.gameId)
    syncSelectedEntity(state)
    for (const message of describePresenceChanges(previousState, state, playerId)) {
      appendActivity(message)
    }
    appendActivity(describeGameStateUpdate(state, playerId))
  })

  const connect = async (reconnectTokenToUse: string | null, shouldResumeLastGame: boolean) => {
    setIsBusy(true)
    setError(null)
    setConnectionStatus('connecting')
    if (!reconnectTokenToUse) {
      localStorage.removeItem(lastGameStorageKey)
      setLastGameId(null)
    }

    try {
      await hubRef.current.disconnect()
      setReconnectToken(reconnectTokenToUse)
      await hubRef.current.connect(serverUrl, reconnectTokenToUse, normalizePlayerName(playerName), {
        onConnected: (payload) => {
          void handleConnected(payload, shouldResumeLastGame)
        },
        onGameState: handleGameState,
        onActionAccepted: (_accepted: ActionAcceptedDto) => {},
        onActionRejected: (rejected: ActionRejectedDto) => {
          const message = humanizeActionRejected(rejected.reason)
          setError(message)
          appendActivity(message)
        },
        onPlayerJoined: (joined: PlayerJoinedDto) => {
          appendActivity(`${joined.displayName ?? joined.playerId} joined the match.`)
        },
        onPlayerLeft: (left: PlayerLeftDto) => {
          appendActivity(`${left.displayName ?? left.playerId} left the match.`)
        },
        onClosed: (closeError) => {
          autoConnectAttemptRef.current = null
          setConnectionStatus('disconnected')
          if (closeError) setError(closeError.message)
        },
        onReconnecting: (reconnectError) => {
          setConnectionStatus('reconnecting')
          appendActivity(reconnectError ? `Connection lost. Trying to reconnect: ${reconnectError.message}` : 'Connection lost. Trying to reconnect.')
        },
        onReconnected: async () => {
          setConnectionStatus('connected')
          appendActivity('Connection restored.')
          try {
            const connectionInfo = await hubRef.current.getConnectionInfo()
            syncConnectionInfo(connectionInfo)
            if (connectionInfo.resumeGameId) {
              await tryResumeGame(connectionInfo.resumeGameId, 'reconnect')
            }
          } catch (resumeError) {
            setError(`Couldn't restore the connection cleanly: ${getErrorMessage(resumeError)}`)
          }
        },
      })
    } catch (connectError) {
      setConnectionStatus('disconnected')
      setError(getErrorMessage(connectError))
    } finally {
      setIsBusy(false)
    }
  }

  const connectAsNewPlayer = async () => connect(null, false)

  const reconnectSavedIdentity = async () => {
    if (!savedReconnectToken) {
      setError('No saved reconnect token is available for this browser profile.')
      return
    }

    await connect(savedReconnectToken, true)
  }

  const disconnect = async () => {
    setIsBusy(true)
    try {
      await hubRef.current.disconnect()
      autoConnectAttemptRef.current = null
      setConnectionStatus('disconnected')
      appendActivity('Disconnected from the server.')
    } catch (disconnectError) {
      setError(getErrorMessage(disconnectError))
    } finally {
      setIsBusy(false)
    }
  }

  const startNewIdentity = async () => {
    setIsBusy(true)
    try {
      await hubRef.current.disconnect()
      localStorage.removeItem(reconnectTokenStorageKey)
      localStorage.removeItem(lastGameStorageKey)
      autoConnectAttemptRef.current = null
      setSavedReconnectToken(null)
      setReconnectToken(null)
      setLastGameId(null)
      setPlayerId(null)
      nextClientSequenceRef.current = 1
      setCurrentDisplayName(null)
      setGameState(null)
      setMapData(null)
      setMapError(null)
      setSelectedEntityId('')
      setAttackTargetEntityId('')
      setMoveForm({ entityId: '', x: 0, y: 0 })
      setConnectionStatus('disconnected')
      appendActivity('Started over with a fresh player identity.')
    } catch (identityError) {
      setError(getErrorMessage(identityError))
    } finally {
      setIsBusy(false)
    }
  }

  const refreshMatches = async () => {
    setIsBusy(true)
    setError(null)
    try {
      await refreshMatchesCore()
      setShowMatches(true)
    } catch (refreshError) {
      setError(getErrorMessage(refreshError))
    } finally {
      setIsBusy(false)
    }
  }

  const createMatch = async () => {
    setIsBusy(true)
    setError(null)
    try {
      await hubRef.current.createMatch(createForm)
      setJoinGameId(createForm.gameId)
      appendActivity(`Created match ${createForm.gameId}.`)
      await refreshMatchesCore()
    } catch (createError) {
      setError(getErrorMessage(createError))
    } finally {
      setIsBusy(false)
    }
  }

  const joinMatch = async (gameId: string) => {
    setIsBusy(true)
    setError(null)
    try {
      await hubRef.current.joinGame(gameId)
      setLastGameId(gameId)
      localStorage.setItem(lastGameStorageKey, gameId)
      appendActivity(`Joined match ${gameId}.`)
    } catch (joinError) {
      setError(getErrorMessage(joinError))
    } finally {
      setIsBusy(false)
    }
  }

  const leaveMatch = async () => {
    setIsBusy(true)
    setError(null)
    try {
      await hubRef.current.leaveGame()
      previousGameStateRef.current = null
      setGameState(null)
      setMapData(null)
      setLastGameId(null)
      setSelectedEntityId('')
      setAttackTargetEntityId('')
      localStorage.removeItem(lastGameStorageKey)
      appendActivity('Left the current match.')
    } catch (leaveError) {
      setError(getErrorMessage(leaveError))
    } finally {
      setIsBusy(false)
    }
  }

  const setReady = async (isReady: boolean) => {
    setIsBusy(true)
    setError(null)
    try {
      await hubRef.current.readyUp({ isReady })
    } catch (readyError) {
      setError(getErrorMessage(readyError))
    } finally {
      setIsBusy(false)
    }
  }

  const startMatch = async () => {
    setIsBusy(true)
    setError(null)
    try {
      await hubRef.current.startMatch()
    } catch (startError) {
      setError(getErrorMessage(startError))
    } finally {
      setIsBusy(false)
    }
  }

  const getState = async () => {
    setIsBusy(true)
    setError(null)
    try {
      await hubRef.current.getState()
    } catch (stateError) {
      setError(getErrorMessage(stateError))
    } finally {
      setIsBusy(false)
    }
  }

  const selectEntity = (entity: EntityStateDto) => {
    const availableMoves = getAvailableMoveActionsForEntity(gameState, entity.entityId)
    const availableAttacks = getAvailableAttackActionsForEntity(gameState, entity.entityId)
    const nextMoveTarget = availableMoves[0] ?? entity
    setSelectedEntityId(entity.entityId)
    setMoveForm({ entityId: entity.entityId, x: nextMoveTarget.x, y: nextMoveTarget.y })
    setAttackTargetEntityId(availableAttacks[0]?.targetEntityId ?? '')
  }

  const selectedEntity = gameState?.entities.find((entity) => entity.entityId === selectedEntityId) ?? null
  const availableMoveActions = getAvailableMoveActionsForEntity(gameState, selectedEntityId)
  const availableAttackActions = getAvailableAttackActionsForEntity(gameState, selectedEntityId)
  const selectedAttackTarget = gameState?.entities.find((entity) => entity.entityId === attackTargetEntityId) ?? null
  const isAvailableMoveSelected = availableMoveActions.some((move) => move.x === moveForm.x && move.y === moveForm.y)
  const isAvailableAttackSelected = availableAttackActions.some((attack) => attack.targetEntityId === attackTargetEntityId)
  const canControlSelectedEntity = Boolean(selectedEntity && playerId && selectedEntity.ownerPlayerId === playerId)
  const isPlayersTurn = Boolean(gameState && playerId && gameState.currentTurnPlayerId === playerId)
  const canSubmitMove = Boolean(gameState && selectedEntity && canControlSelectedEntity && isPlayersTurn && isAvailableMoveSelected)
  const canSubmitAttack = Boolean(gameState && selectedEntity && canControlSelectedEntity && isPlayersTurn && isAvailableAttackSelected)
  const canSelectMoveTarget = Boolean(selectedEntity && canControlSelectedEntity && isPlayersTurn)
  const canSelectAttackTarget = Boolean(selectedEntity && canControlSelectedEntity && isPlayersTurn)
  const canEndTurn = Boolean(isPlayersTurn && hasAvailableEndTurn(gameState))

  const handleBoardCellClick = async (cell: VisibleBoardCell) => {
    if (cell.entity) {
      if (cell.entity.ownerPlayerId === playerId) {
        selectEntity(cell.entity)
        return
      }

      if (!canSelectAttackTarget) return

      const isAvailableAttack = availableAttackActions.some((attack) => attack.targetEntityId === cell.entity?.entityId)
      if (!isAvailableAttack) return

      const isAlreadyTargeted = attackTargetEntityId === cell.entity.entityId
      setAttackTargetEntityId(cell.entity.entityId)
      if (isAlreadyTargeted && !isBusy) await submitAttack(cell.entity.entityId)
      return
    }

    if (!canSelectMoveTarget) return

    const isAvailableMove = availableMoveActions.some((move) => move.x === cell.x && move.y === cell.y)
    if (!isAvailableMove) return

    const isAlreadyTargeted = moveForm.x === cell.x && moveForm.y === cell.y
    setMoveForm((current) => ({ ...current, x: cell.x, y: cell.y }))
    if (isAlreadyTargeted && !isBusy) await submitMove(cell.x, cell.y)
  }

  const nudgeMove = (dx: number, dy: number) => {
    if (!selectedEntity || !canSelectMoveTarget) return
    const candidateX = selectedEntity.x + dx
    const candidateY = selectedEntity.y + dy
    const isAvailableMove = availableMoveActions.some((move) => move.x === candidateX && move.y === candidateY)
    if (!isAvailableMove) return
    setMoveForm((current) => ({ ...current, x: candidateX, y: candidateY }))
  }

  const submitEndTurn = async () => {
    if (!gameState || !canEndTurn) return
    setIsBusy(true)
    setError(null)
    try {
      await hubRef.current.submitAction({ type: 'endTurn', actionId: crypto.randomUUID(), clientSequence: reserveClientSequence(), expectedStateVersion: gameState.version })
    } catch (submitError) {
      setError(getErrorMessage(submitError))
    } finally {
      setIsBusy(false)
    }
  }

  const submitMove = async (overrideX?: number, overrideY?: number) => {
    if (!gameState || !moveForm.entityId) return
    const targetX = overrideX ?? moveForm.x
    const targetY = overrideY ?? moveForm.y
    const isAvailableMove = availableMoveActions.some((move) => move.x === targetX && move.y === targetY)
    if (!isAvailableMove || !canSubmitMove) return
    setIsBusy(true)
    setError(null)
    try {
      const action: MoveEntityActionDto = { type: 'move', actionId: crypto.randomUUID(), clientSequence: reserveClientSequence(), expectedStateVersion: gameState.version, entityId: moveForm.entityId, x: targetX, y: targetY }
      await hubRef.current.submitAction(action)
    } catch (submitError) {
      setError(getErrorMessage(submitError))
    } finally {
      setIsBusy(false)
    }
  }

  const submitAttack = async (overrideTargetEntityId?: string) => {
    if (!gameState || !selectedEntity) return
    const targetEntityId = overrideTargetEntityId ?? attackTargetEntityId
    const isAvailableAttack = availableAttackActions.some((attack) => attack.targetEntityId === targetEntityId)
    if (!isAvailableAttack || !canSubmitAttack) return
    setIsBusy(true)
    setError(null)
    try {
      const action: AttackEntityActionDto = { type: 'attack', actionId: crypto.randomUUID(), clientSequence: reserveClientSequence(), expectedStateVersion: gameState.version, entityId: selectedEntity.entityId, targetEntityId }
      await hubRef.current.submitAction(action)
    } catch (submitError) {
      setError(getErrorMessage(submitError))
    } finally {
      setIsBusy(false)
    }
  }

  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      const active = document.activeElement
      const isTypingIntoField = active instanceof HTMLInputElement || active instanceof HTMLTextAreaElement || active instanceof HTMLSelectElement
      if (isTypingIntoField || isBusy || !gameState || gameState.phase === 'Lobby' || !selectedEntity || !canSelectMoveTarget) return

      let deltaX = 0
      let deltaY = 0
      if (event.key === 'ArrowUp') deltaY = -1
      else if (event.key === 'ArrowDown') deltaY = 1
      else if (event.key === 'ArrowLeft') deltaX = -1
      else if (event.key === 'ArrowRight') deltaX = 1
      else return

      const candidateX = selectedEntity.x + deltaX
      const candidateY = selectedEntity.y + deltaY
      const isAvailableMove = availableMoveActions.some((move) => move.x === candidateX && move.y === candidateY)
      if (!isAvailableMove) return

      event.preventDefault()
      setMoveForm((current) => ({ ...current, x: candidateX, y: candidateY }))
      void submitMove(candidateX, candidateY)
    }

    window.addEventListener('keydown', onKeyDown)
    return () => {
      window.removeEventListener('keydown', onKeyDown)
    }
  }, [availableMoveActions, canSelectMoveTarget, gameState, isBusy, selectedEntity, submitMove])

  const board = buildVisibleBoard(mapData, gameState?.entities ?? [], selectedEntityId)
  const isLobby = gameState?.phase === 'Lobby'
  const connectedPlayers = gameState?.players.filter((player) => player.isConnected) ?? []
  const readyPlayerIds = new Set((gameState?.ready ?? []).filter((player) => player.isReady).map((player) => player.playerId))
  const connectedReadyCount = connectedPlayers.filter((player) => readyPlayerIds.has(player.playerId)).length
  const missingPlayers = Math.max(0, (gameState?.minPlayers ?? 0) - connectedPlayers.length)
  const currentPlayerLabel = getPlayerLabel(gameState?.players ?? [], gameState?.currentTurnPlayerId)
  const selectedOwnerLabel = selectedEntity ? getPlayerLabel(gameState?.players ?? [], selectedEntity.ownerPlayerId) : null
  const isHost = Boolean(gameState && playerId && gameState.hostPlayerId === playerId)
  const localPlayerReady = playerId ? readyPlayerIds.has(playerId) : false
  const canReadyUp = Boolean(isLobby && playerId && !localPlayerReady)
  const canUnready = Boolean(isLobby && playerId && localPlayerReady)
  const canRequestStart = Boolean(isLobby && isHost && connectedPlayers.length >= (gameState?.minPlayers ?? 0) && connectedPlayers.length > 0 && connectedReadyCount === connectedPlayers.length)
  const statusTitle = isLobby
    ? 'Lobby setup'
    : isPlayersTurn
      ? 'Your turn'
      : `${currentPlayerLabel}'s turn`
  const statusDetail = isLobby
    ? missingPlayers > 0
      ? `Waiting for ${missingPlayers} more player${missingPlayers === 1 ? '' : 's'} to join before the match can start.`
      : !localPlayerReady
        ? 'You are connected. Click Ready when you want to lock in and start.'
        : connectedReadyCount < connectedPlayers.length
          ? `Waiting for ${connectedPlayers.length - connectedReadyCount} more player${connectedPlayers.length - connectedReadyCount === 1 ? '' : 's'} to click Ready.`
          : isHost
            ? 'Everyone needed is ready. Press Start to begin the match.'
            : 'Everyone needed is ready. Waiting for the match creator to press Start.'
    : isPlayersTurn
      ? 'Select one of your units, then move, attack, or end your turn.'
      : `Wait for ${currentPlayerLabel} to finish their turn before acting.`
  const selectionHint = isLobby
    ? `${connectedReadyCount}/${connectedPlayers.length} connected players are ready.`
    : selectedEntity
      ? canControlSelectedEntity
        ? `${selectedEntityId} belongs to you.`
        : `${selectedEntityId} belongs to ${selectedOwnerLabel}.`
      : 'Select a unit on the board to see its available actions.'

  return (
    <main className="app-shell">
      <section className="hero-panel">
        <div>
          <p className="eyebrow">Warlords Client</p>
          <h1>SignalR test cockpit for the turn-based server</h1>
          <p className="lede">The board now consumes a server-authored available action list for movement, attacks, and end-turn availability, so the client only renders valid tactical options.</p>
        </div>
        <div className="status-card">
          <span className={`status-dot status-${connectionStatus}`} />
          <div>
            <strong>{connectionStatus}</strong>
            <div>{currentDisplayName ?? normalizePlayerName(playerName) ?? playerId ?? 'No player yet'}</div>
            <div className="status-subtle">{lastGameId ?? 'No saved match'}</div>
          </div>
        </div>
      </section>

      <section className="grid">
        <article className="panel">
          <h2>Connection</h2>
          <label className="field">
            <span>Player name</span>
            <input value={playerName} onChange={(event) => setPlayerName(event.target.value)} placeholder="Allan" maxLength={32} />
          </label>
          <label className="field">
            <span>Server URL</span>
            <input value={serverUrl} onChange={(event) => setServerUrl(event.target.value)} />
          </label>
          <label className="field">
            <span>Saved identity</span>
            <input value={savedReconnectToken ? 'Stored in this browser profile' : 'None saved yet'} readOnly />
          </label>
          <label className="field">
            <span>Current reconnect token</span>
            <input value={connectionStatus === 'connected' && reconnectToken ? reconnectToken : ''} readOnly placeholder="Appears after you connect" />
          </label>
          <div className="actions">
            <button onClick={() => { void connectAsNewPlayer() }} disabled={isBusy || connectionStatus === 'connected'}>Connect new player</button>
            <button onClick={() => { void reconnectSavedIdentity() }} disabled={isBusy || connectionStatus === 'connected' || !savedReconnectToken}>Reconnect saved player</button>
            <button onClick={disconnect} disabled={isBusy || connectionStatus === 'disconnected'}>Disconnect</button>
            <button onClick={startNewIdentity} disabled={isBusy}>Forget saved identity</button>
            <button onClick={refreshMatches} disabled={isBusy || !hubRef.current.isConnected}>Refresh matches</button>
            <button onClick={() => { void tryResumeGame(lastGameId, 'manual resume') }} disabled={isBusy || !hubRef.current.isConnected || !lastGameId}>Resume last game</button>
          </div>
          <p className="muted">Connect new player creates a fresh identity for this browser profile. Reconnect saved player reuses the stored identity and resumes the saved match when possible.</p>
          {error ? <p className="error">{error}</p> : null}
        </article>

        <article className="panel">
          <h2>Create match</h2>
          <div className="field-grid">
            <label className="field"><span>Game ID</span><input value={createForm.gameId} onChange={(event) => setCreateForm((current) => ({ ...current, gameId: event.target.value }))} /></label>
            <label className="field"><span>Map ID</span><input value={createForm.mapId} onChange={(event) => setCreateForm((current) => ({ ...current, mapId: event.target.value }))} /></label>
            <label className="field"><span>Min players</span><input type="number" min={1} max={8} value={createForm.minPlayers} onChange={(event) => setCreateForm((current) => ({ ...current, minPlayers: Number(event.target.value) }))} /></label>
            <label className="field"><span>Max players</span><input type="number" min={1} max={8} value={createForm.maxPlayers} onChange={(event) => setCreateForm((current) => ({ ...current, maxPlayers: Number(event.target.value) }))} /></label>
            <label className="field"><span>Turn limit (s)</span><input type="number" min={0} value={createForm.turnTimeLimitSeconds} onChange={(event) => setCreateForm((current) => ({ ...current, turnTimeLimitSeconds: Number(event.target.value) }))} /></label>
            <label className="field"><span>Disconnect grace (s)</span><input type="number" min={0} value={createForm.disconnectGraceSeconds} onChange={(event) => setCreateForm((current) => ({ ...current, disconnectGraceSeconds: Number(event.target.value) }))} /></label>
          </div>
          <label className="toggle"><input type="checkbox" checked={createForm.autoStart} onChange={(event) => setCreateForm((current) => ({ ...current, autoStart: event.target.checked }))} /><span>Auto-start when ready</span></label>
          <button onClick={createMatch} disabled={isBusy || !hubRef.current.isConnected}>Create match</button>
        </article>

        <article className="panel panel-wide">
          <div className="panel-heading"><h2>Matches</h2><button type="button" className="secondary-button" onClick={() => setShowMatches((current) => !current)}>{showMatches ? 'Hide' : 'Show'}</button></div>
          {showMatches ? <><div className="inline-join"><input placeholder="Enter match id" value={joinGameId} onChange={(event) => setJoinGameId(event.target.value)} /><button onClick={() => joinMatch(joinGameId)} disabled={isBusy || !hubRef.current.isConnected || !joinGameId}>Join by id</button></div>
          <div className="match-list">{matches.length === 0 ? <p className="muted">No matches loaded yet.</p> : null}{matches.map((match) => <button key={match.gameId} className="match-card" onClick={() => joinMatch(match.gameId)} disabled={isBusy || !hubRef.current.isConnected}><strong>{match.gameId}</strong><span>{match.mapId}</span><span>{match.phase} • {match.playerCount} players</span></button>)}</div></> : <p className="muted">Match list hidden. Refresh matches to update it, or show it when you want to join.</p>}
        </article>

        <article className="panel panel-wide">
          <h2>Current game</h2>
          {gameState ? (<>
            <div className="state-summary"><div><span className="label">Game</span><strong>{gameState.gameId}</strong></div><div><span className="label">Phase</span><strong>{gameState.phase}</strong></div><div><span className="label">Turn</span><strong>{gameState.turnNumber}</strong></div><div><span className="label">Current player</span><strong>{getPlayerLabel(gameState.players, gameState.currentTurnPlayerId)}</strong></div></div>
            <div className="actions"><button onClick={() => setReady(true)} disabled={isBusy || !canReadyUp}>Ready</button><button onClick={() => setReady(false)} disabled={isBusy || !canUnready}>Unready</button><button onClick={startMatch} disabled={isBusy || !canRequestStart} title={isHost ? 'Start the match once everyone is ready.' : 'Only the match creator can start the match.'}>Start match</button><button onClick={getState} disabled={isBusy}>Sync</button><button onClick={leaveMatch} disabled={isBusy}>Leave</button></div>
            <div className="turn-banner"><span>{statusTitle}</span><span>{selectionHint}</span></div>
            <p className="phase-guidance">{statusDetail}</p>
            <div className="subgrid board-layout">
              <section className="subpanel board-panel">
                <div className="panel-heading"><h3>Map slice</h3><span className="muted">Green cells move, red enemy units attack</span></div>
                {mapError ? <p className="error">Map load failed: {mapError}</p> : null}
                {board ? (<><div className="board-grid" style={{ gridTemplateColumns: `repeat(${board.width}, minmax(0, 1fr))` }}>{board.cells.map((cell) => {
                  const isAvailableMove = availableMoveActions.some((move) => move.x === cell.x && move.y === cell.y)
                  const isAvailableAttack = Boolean(cell.entity?.entityId && availableAttackActions.some((attack) => attack.targetEntityId === cell.entity?.entityId))
                  return <button key={`${cell.x}:${cell.y}`} className={['board-cell', `tile-${normalizeToken(cell.tile.type)}`, cell.tile.isBlocked ? 'blocked' : '', cell.tile.owner ? `owner-${normalizeToken(cell.tile.owner)}` : '', cell.entity ? 'occupied' : '', cell.entity?.entityId === selectedEntityId ? 'selected' : '', moveForm.x === cell.x && moveForm.y === cell.y ? 'targeted' : '', attackTargetEntityId === cell.entity?.entityId ? 'attack-targeted' : '', isAvailableMove ? 'legal-move' : '', isAvailableAttack ? 'legal-attack' : ''].filter(Boolean).join(' ')} onClick={() => { void handleBoardCellClick(cell) }}><span className="coord">{cell.x},{cell.y}</span><span className="terrain">{cell.tile.type}</span>{cell.entity ? <strong>{shortLabel(getPlayerLabel(gameState.players, cell.entity.ownerPlayerId))}</strong> : null}</button>
                })}</div><div className="legend">{board.legend.map((tile) => <span key={`${tile.tileId}-${tile.type}-${tile.owner ?? 'none'}`} className={`legend-chip tile-${normalizeToken(tile.type)}`}>{tile.type}{tile.owner ? `:${tile.owner}` : ''}</span>)}</div></>) : <p className="muted">No map slice available yet.</p>}
              </section>
              <section className="subpanel"><h3>Players</h3><ul className="entity-list">{gameState.players.map((player) => { const ready = gameState.ready.find((item) => item.playerId === player.playerId); const slot = gameState.slots.find((item) => item.playerId === player.playerId); return <li key={player.playerId}><strong>{player.displayName ?? player.playerId}</strong><span>{player.playerId}</span><span>{player.isConnected ? 'connected' : 'disconnected'}</span><span>{ready?.isReady ? 'ready' : 'not ready'}</span><span>{slot?.slot ?? 'no slot'}</span></li> })}</ul></section>
              <section className="subpanel"><div className="panel-heading"><h3>Entities</h3>{mapData ? <span className="muted">{mapData.width}×{mapData.height}</span> : null}</div><ul className="entity-list">{gameState.entities.map((entity) => <li key={entity.entityId}><button className="entity-button" onClick={() => selectEntity(entity)}><strong>{getPlayerLabel(gameState.players, entity.ownerPlayerId)}</strong><span>{entity.entityId}</span><span>at ({entity.x}, {entity.y})</span></button></li>)}</ul>{mapData ? <div className="spawn-list"><span className="label">Spawn points</span><div className="spawn-chips">{mapData.spawnPoints.map((spawn) => <span key={`${spawn.owner}-${spawn.x}-${spawn.y}`} className="spawn-chip">{spawn.owner} ({spawn.x},{spawn.y})</span>)}</div></div> : null}</section>
            </div>
            <section className="subpanel action-panel"><h3>Actions</h3><div className="field-grid action-grid"><label className="field"><span>Entity ID</span><input value={moveForm.entityId} onChange={(event) => setMoveForm((current) => ({ ...current, entityId: event.target.value }))} /></label><label className="field"><span>Move X</span><input type="number" value={moveForm.x} onChange={(event) => setMoveForm((current) => ({ ...current, x: Number(event.target.value) }))} /></label><label className="field"><span>Move Y</span><input type="number" value={moveForm.y} onChange={(event) => setMoveForm((current) => ({ ...current, y: Number(event.target.value) }))} /></label><label className="field"><span>Attack target</span><input value={attackTargetEntityId} onChange={(event) => setAttackTargetEntityId(event.target.value)} /></label><label className="field"><span>Target owner</span><input value={selectedAttackTarget ? getPlayerLabel(gameState.players, selectedAttackTarget.ownerPlayerId) : ''} readOnly /></label><label className="field"><span>Target cell</span><input value={selectedAttackTarget ? `${selectedAttackTarget.x}, ${selectedAttackTarget.y}` : ''} readOnly /></label></div><div className="actions movement-actions"><button onClick={() => nudgeMove(0, -1)} disabled={isBusy || !selectedEntity || !availableMoveActions.some((move) => move.x === selectedEntity.x && move.y === selectedEntity.y - 1)}>Up</button><button onClick={() => nudgeMove(-1, 0)} disabled={isBusy || !selectedEntity || !availableMoveActions.some((move) => move.x === selectedEntity.x - 1 && move.y === selectedEntity.y)}>Left</button><button onClick={() => nudgeMove(1, 0)} disabled={isBusy || !selectedEntity || !availableMoveActions.some((move) => move.x === selectedEntity.x + 1 && move.y === selectedEntity.y)}>Right</button><button onClick={() => nudgeMove(0, 1)} disabled={isBusy || !selectedEntity || !availableMoveActions.some((move) => move.x === selectedEntity.x && move.y === selectedEntity.y + 1)}>Down</button><button onClick={() => void submitMove()} disabled={isBusy || !canSubmitMove}>Submit move</button><button onClick={() => void submitAttack()} disabled={isBusy || !canSubmitAttack}>Attack target</button><button onClick={submitEndTurn} disabled={isBusy || !canEndTurn}>End turn</button></div><p className="muted">{isLobby ? 'Get enough players connected, then everyone should click Ready. Once everyone needed is ready, start the match if it does not auto-start.' : 'Click one of your units to select it. Green cells are legal moves, red enemy units are legal attack targets.'}</p></section>
          </>) : <p className="muted">No active game state yet.</p>}
        </article>

        <article className="panel"><h2>Activity</h2><ul className="activity-list">{activity.map((entry, index) => <li key={`${index}-${entry}`}>{entry}</li>)}</ul></article>
      </section>
    </main>
  )
}

function describeGameStateUpdate(state: GameStateDto, localPlayerId: string | null) {
  const connectedCount = state.players.filter((player) => player.isConnected).length
  const readyCount = state.ready.filter((player) => player.isReady).length
  const currentPlayerLabel = getPlayerLabel(state.players, state.currentTurnPlayerId)

  if (state.phase === 'Lobby') {
    return `Lobby: ${connectedCount} connected, ${readyCount} ready.`
  }

  if (state.lastAction?.type === 'move') {
    const action = state.lastAction
    const mover = state.entities.find((entity) => entity.entityId === action.entityId)
    const moverLabel = getPlayerLabel(state.players, mover?.ownerPlayerId)
    return `${moverLabel} moved to (${action.x}, ${action.y}). Turn ${state.turnNumber}: ${currentPlayerLabel} is up.`
  }

  if (state.lastAction?.type === 'attack') {
    const action = state.lastAction
    const attacker = state.entities.find((entity) => entity.entityId === action.entityId)
    const attackerLabel = getPlayerLabel(state.players, attacker?.ownerPlayerId)
    return `${attackerLabel} attacked. Turn ${state.turnNumber}: ${currentPlayerLabel} is up.`
  }

  if (state.lastAction?.type === 'endTurn') {
    if (state.lastAction.actionId.startsWith('sys:endTurn:')) {
      return `The turn timer expired. Turn ${state.turnNumber}: ${currentPlayerLabel} is up.`
    }

    const endedBy = localPlayerId && state.currentTurnPlayerId !== localPlayerId ? 'The turn passed.' : 'Turn ended.'
    return `${endedBy} Turn ${state.turnNumber}: ${currentPlayerLabel} is up.`
  }

  return `Turn ${state.turnNumber}: ${currentPlayerLabel} is up.`
}

function getAvailableMoveActionsForEntity(gameState: GameStateDto | null, entityId: string) {
  if (!gameState || !entityId) return []
  return gameState.availableActions.filter((action): action is Extract<GameStateDto['availableActions'][number], { type: 'move' }> => action.type === 'move' && action.entityId === entityId)
}

function getAvailableAttackActionsForEntity(gameState: GameStateDto | null, entityId: string) {
  if (!gameState || !entityId) return []
  return gameState.availableActions.filter((action): action is Extract<GameStateDto['availableActions'][number], { type: 'attack' }> => action.type === 'attack' && action.entityId === entityId)
}

function hasAvailableEndTurn(gameState: GameStateDto | null) {
  if (!gameState) return false
  return gameState.availableActions.some((action) => action.type === 'endTurn')
}

function buildVisibleBoard(map: MapViewDto | null, entities: EntityStateDto[], selectedEntityId: string) {
  if (!map) return null
  const byCoordinate = new Map(entities.map((entity) => [`${entity.x}:${entity.y}`, entity]))
  const selected = entities.find((entity) => entity.entityId === selectedEntityId) ?? entities[0]
  const showFullMap = map.width <= (boardRadius * 2) + 1 && map.height <= (boardRadius * 2) + 1
  const centerX = selected?.x ?? Math.floor(map.width / 2)
  const centerY = selected?.y ?? Math.floor(map.height / 2)
  const minX = showFullMap ? 0 : Math.max(0, centerX - boardRadius)
  const maxX = showFullMap ? map.width - 1 : Math.min(map.width - 1, centerX + boardRadius)
  const minY = showFullMap ? 0 : Math.max(0, centerY - boardRadius)
  const maxY = showFullMap ? map.height - 1 : Math.min(map.height - 1, centerY + boardRadius)
  const palette = new Map(map.tilePalette.map((tile) => [tile.tileId, tile]))
  const cells: VisibleBoardCell[] = []
  const seenTiles = new Map<string, MapTileDto>()

  for (let y = minY; y <= maxY; y += 1) {
    for (let x = minX; x <= maxX; x += 1) {
      const tileId = map.tiles[(y * map.width) + x]
      const tile = palette.get(tileId) ?? fallbackTile(tileId)
      cells.push({ x, y, tile, entity: byCoordinate.get(`${x}:${y}`) })
      seenTiles.set(`${tile.tileId}:${tile.type}:${tile.owner ?? 'none'}`, tile)
    }
  }

  return { width: maxX - minX + 1, cells, legend: [...seenTiles.values()].sort((left, right) => left.type.localeCompare(right.type)) }
}

function fallbackTile(tileId: number): MapTileDto {
  return { tileId, type: 'unknown', owner: null, isBlocked: true }
}

function getPlayerLabel(players: PlayerPresenceDto[], playerId: string | null | undefined) {
  if (!playerId) return 'None'
  const player = players.find((candidate) => candidate.playerId === playerId)
  return player?.displayName ?? playerId
}

function normalizePlayerName(value: string | null) {
  const trimmed = value?.trim() ?? ''
  return trimmed.length > 0 ? trimmed.slice(0, 32) : null
}

function normalizeToken(value: string) {
  return value.replace(/[^a-z0-9]+/gi, '-').toLowerCase()
}

function shortLabel(value: string) {
  const parts = value.trim().split(/\s+/).filter(Boolean)
  if (parts.length >= 2) {
    return `${parts[0][0] ?? ''}${parts[1][0] ?? ''}`.toUpperCase()
  }

  return value.slice(0, 2).toUpperCase()
}

function ensureTrailingSlash(baseUrl: string) {
  return baseUrl.endsWith('/') ? baseUrl : `${baseUrl}/`
}

function describeResume(reason: string, gameId: string) {
  return reason === 'reconnect'
    ? `Rejoined match ${gameId} after reconnecting.`
    : `Rejoined match ${gameId}.`
}

function describePresenceChanges(previousState: GameStateDto | null, nextState: GameStateDto, localPlayerId: string | null) {
  if (!previousState || previousState.gameId !== nextState.gameId) return []

  const previousPlayers = new Map(previousState.players.map((player) => [player.playerId, player]))
  const nextPlayers = new Map(nextState.players.map((player) => [player.playerId, player]))
  const messages: string[] = []

  for (const player of nextState.players) {
    const previous = previousPlayers.get(player.playerId)
    if (!previous) continue

    const label = player.displayName ?? previous.displayName ?? player.playerId
    if (previous.isConnected && !player.isConnected) {
      messages.push(player.playerId === localPlayerId ? 'You disconnected. Reconnect grace is running.' : `${label} disconnected. Reconnect grace is running.`)
      continue
    }

    if (!previous.isConnected && player.isConnected) {
      messages.push(player.playerId === localPlayerId ? 'You reconnected.' : `${label} reconnected.`)
    }
  }

  for (const player of previousState.players) {
    if (nextPlayers.has(player.playerId)) continue
    const label = player.displayName ?? player.playerId
    messages.push(player.playerId === localPlayerId ? 'You were removed from the match after disconnect grace expired.' : `${label} was removed after disconnect grace expired.`)
  }

  return messages
}

function humanizeActionRejected(reason: string) {
  switch (reason) {
    case 'NotYourTurn':
      return `You can't act right now. It isn't your turn.`
    case 'PlayerDisconnected':
      return `You can't act while disconnected.`
    case 'OutOfOrder':
      return `That action arrived out of order. Try again.`
    case 'StaleState':
      return `Your view of the game was out of date. Try again after the latest update.`
    case 'UnknownGame':
      return `That match could not be found.`
    case 'NotInGame':
      return `You are not in an active match.`
    default:
      return `That action was rejected: ${humanizeReason(reason)}`
  }
}

function humanizeReason(reason: string | null | undefined) {
  if (!reason) return 'Unknown reason'

  switch (reason) {
    case 'MatchAlreadyStarted':
      return 'the match has already started'
    case 'GameFull':
      return 'the match is full'
    case 'UnknownGame':
      return 'the match could not be found'
    case 'NotConnected':
      return 'you are not connected'
    case 'GameIdRequired':
      return 'the match id is missing'
    default:
      return reason
  }
}

function humanizeResumeFailure(reason: string | null | undefined) {
  if (reason === 'MatchAlreadyStarted') {
    return 'your seat in that match was lost while you were disconnected.'
  }

  return humanizeReason(reason)
}

function humanizeHubError(error: unknown) {
  const message = getErrorMessage(error)
  const match = /HubException:\s*([A-Za-z0-9]+)/.exec(message)
  return match ? humanizeReason(match[1]) : message
}

function getClientSequenceStorageKey(playerId: string) {
  return `warlords.nextClientSequence.${playerId}`
}

function getErrorMessage(error: unknown) {
  return error instanceof Error ? error.message : 'Unknown error'
}

export default App










