import * as signalR from '@microsoft/signalr';

const baseUrl = 'http://localhost:5118';
const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

function createClient(tag) {
  const events = [];
  let latestState = null;
  let connectedPayload = null;
  let lastAccepted = null;
  let lastRejected = null;

  const connection = new signalR.HubConnectionBuilder()
    .withUrl(new URL('/hubs/game', baseUrl).toString())
    .withAutomaticReconnect()
    .configureLogging(signalR.LogLevel.Warning)
    .build();

  connection.on('Connected', (payload) => {
    connectedPayload = payload;
    events.push(`${tag}:Connected:${payload.playerId}`);
  });
  connection.on('GameState', (state) => {
    latestState = state;
    events.push(`${tag}:GameState:${state.gameId}:${state.version}`);
  });
  connection.on('ActionAccepted', (accepted) => {
    lastAccepted = accepted;
    events.push(`${tag}:ActionAccepted:${accepted.actionId}`);
  });
  connection.on('ActionRejected', (rejected) => {
    lastRejected = rejected;
    events.push(`${tag}:ActionRejected:${rejected.reason}`);
  });

  return {
    tag,
    connection,
    getSnapshot: () => ({ connectedPayload, latestState, lastAccepted, lastRejected, events }),
  };
}

const clients = Array.from({ length: 8 }, (_, index) => createClient(`p${index + 1}`));

try {
  for (const client of clients) {
    await client.connection.start();
  }
  await sleep(300);

  const matchId = `atk-${Date.now()}`;
  await clients[0].connection.invoke('CreateMatch', {
    gameId: matchId,
    mapId: 'map01',
    minPlayers: 8,
    maxPlayers: 8,
    autoStart: false,
    turnTimeLimitSeconds: 60,
    disconnectGraceSeconds: 120,
  });
  await sleep(200);

  for (const client of clients.slice(1)) {
    await client.connection.invoke('JoinGame', { gameId: matchId });
    await sleep(60);
  }
  await sleep(300);

  for (const client of clients) {
    await client.connection.invoke('ReadyUp', { isReady: true });
  }
  await sleep(300);

  await clients[0].connection.invoke('StartMatch');
  await sleep(500);

  const hostState = clients[0].getSnapshot().latestState;
  const attackAction = hostState?.availableActions?.find((action) => action.type === 'attack');
  if (!attackAction) {
    throw new Error(`No available attack action found for host. Actions: ${JSON.stringify(hostState?.availableActions ?? [])}`);
  }

  const entityCountBefore = hostState.entities.length;
  await clients[0].connection.invoke('SubmitAction', {
    type: 'attack',
    actionId: `attack-${Date.now()}`,
    clientSequence: 1,
    expectedStateVersion: hostState.version,
    entityId: attackAction.entityId,
    targetEntityId: attackAction.targetEntityId,
  });
  await sleep(500);

  console.log(JSON.stringify({
    attackAction,
    hostStateBeforeAttack: hostState,
    hostStateAfterAttack: clients[0].getSnapshot().latestState,
    hostAccepted: clients[0].getSnapshot().lastAccepted,
    hostRejected: clients[0].getSnapshot().lastRejected,
    entityCountBefore,
    entityCountAfter: clients[0].getSnapshot().latestState?.entities?.length ?? null,
  }, null, 2));
} catch (error) {
  console.error(error);
  process.exitCode = 1;
} finally {
  for (const client of clients) {
    await client.connection.stop();
  }
}
