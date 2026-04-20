import * as signalR from '@microsoft/signalr';

const baseUrl = 'http://localhost:5118';
const events = [];
let connectedPayload = null;
let latestState = null;
let lastAccepted = null;
let lastRejected = null;

const connection = new signalR.HubConnectionBuilder()
  .withUrl(`${baseUrl}/hubs/game`)
  .withAutomaticReconnect()
  .configureLogging(signalR.LogLevel.Warning)
  .build();

connection.on('Connected', (payload) => {
  connectedPayload = payload;
  events.push(`Connected:${payload.playerId}`);
});
connection.on('GameState', (state) => {
  latestState = state;
  events.push(`GameState:${state.gameId}:${state.version}`);
});
connection.on('ActionAccepted', (accepted) => {
  lastAccepted = accepted;
  events.push(`ActionAccepted:${accepted.actionId}`);
});
connection.on('ActionRejected', (rejected) => {
  lastRejected = rejected;
  events.push(`ActionRejected:${rejected.reason}`);
});
connection.on('PlayerJoined', (joined) => events.push(`PlayerJoined:${joined.playerId}`));
connection.on('PlayerLeft', (left) => events.push(`PlayerLeft:${left.playerId}`));

const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

try {
  await connection.start();
  await sleep(200);

  const matchId = `it-${Date.now()}`;
  await connection.invoke('ListMatches', 10);
  await connection.invoke('CreateMatch', {
    gameId: matchId,
    mapId: 'map01',
    minPlayers: 1,
    maxPlayers: 2,
    autoStart: false,
    turnTimeLimitSeconds: 60,
    disconnectGraceSeconds: 120,
  });
  await sleep(200);
  await connection.invoke('ReadyUp', { isReady: true });
  await sleep(100);
  await connection.invoke('StartMatch');
  await sleep(200);

  const stateAfterStart = latestState;
  if (stateAfterStart?.entities?.length) {
    const entity = stateAfterStart.entities[0];
    await connection.invoke('SubmitAction', {
      type: 'endTurn',
      actionId: `end-${Date.now()}`,
      clientSequence: 1,
      expectedStateVersion: stateAfterStart.version,
    });
    await sleep(200);

    await connection.invoke('SubmitAction', {
      type: 'move',
      actionId: `move-${Date.now()}`,
      clientSequence: 2,
      expectedStateVersion: latestState?.version,
      entityId: entity.entityId,
      x: entity.x,
      y: entity.y + 1,
    });
    await sleep(200);
  }

  console.log(JSON.stringify({
    connectedPayload,
    latestState,
    lastAccepted,
    lastRejected,
    events,
  }, null, 2));
} catch (error) {
  console.error(error);
  process.exitCode = 1;
} finally {
  await connection.stop();
}
