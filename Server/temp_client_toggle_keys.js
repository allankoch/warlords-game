const fs = require('fs');

const appPath = 'C:/Code/Warlords/Client/src/App.tsx';
const cssPath = 'C:/Code/Warlords/Client/src/App.css';
let app = fs.readFileSync(appPath, 'utf8');
let css = fs.readFileSync(cssPath, 'utf8');

function replaceRegex(source, pattern, replacement, label) {
  const next = source.replace(pattern, replacement);
  if (next === source) {
    throw new Error(`Missing snippet for ${label}`);
  }

  return next;
}

app = replaceRegex(
  app,
  /  const \[matches, setMatches\] = useState<MatchSummaryDto\[\]>\(\[\]\)\r?\n/,
  "  const [matches, setMatches] = useState<MatchSummaryDto[]>([])\n  const [showMatches, setShowMatches] = useState(false)\n",
  'showMatches state',
);

app = replaceRegex(
  app,
  /  const refreshMatches = async \(\) => \{\r?\n    setIsBusy\(true\)\r?\n    setError\(null\)\r?\n    try \{\r?\n      await refreshMatchesCore\(\)\r?\n    \} catch \(refreshError\) \{\r?\n      setError\(getErrorMessage\(refreshError\)\)\r?\n    \} finally \{\r?\n      setIsBusy\(false\)\r?\n    \}\r?\n  \}\r?\n/,
  `  const refreshMatches = async () => {
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
`,
  'refresh opens matches',
);

app = replaceRegex(
  app,
  /  const submitAttack = async \(overrideTargetEntityId\?: string\) => \{[\s\S]*?  \}\r?\n\r?\n  const board = buildVisibleBoard\(mapData, gameState\?\.entities \?\? \[\], selectedEntityId\)\r?\n/,
  `  const submitAttack = async (overrideTargetEntityId?: string) => {
    if (!gameState || !selectedEntity) return
    const targetEntityId = overrideTargetEntityId ?? attackTargetEntityId
    const isAvailableAttack = availableAttackActions.some((attack) => attack.targetEntityId === targetEntityId)
    if (!isAvailableAttack || !canSubmitAttack) return
    setIsBusy(true)
    setError(null)
    try {
      const action: AttackEntityActionDto = { type: 'attack', actionId: crypto.randomUUID(), clientSequence: nextClientSequenceRef.current++, expectedStateVersion: gameState.version, entityId: selectedEntity.entityId, targetEntityId }
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
`,
  'arrow movement effect',
);

app = replaceRegex(
  app,
  /        <article className="panel panel-wide">\r?\n          <h2>Matches<\/h2>\r?\n          <div className="inline-join"><input placeholder="Enter match id" value=\{joinGameId\} onChange=\{\(event\) => setJoinGameId\(event\.target\.value\)\} \/><button onClick=\{\(\) => joinMatch\(joinGameId\)\} disabled=\{isBusy \|\| !hubRef\.current\.isConnected \|\| !joinGameId\}>Join by id<\/button><\/div>\r?\n          <div className="match-list">\{matches\.length === 0 \? <p className="muted">No matches loaded yet\.<\/p> : null\}\{matches\.map\(\(match\) => <button key=\{match\.gameId\} className="match-card" onClick=\{\(\) => joinMatch\(match\.gameId\)\} disabled=\{isBusy \|\| !hubRef\.current\.isConnected\}><strong>\{match\.gameId\}<\/strong><span>\{match\.mapId\}<\/span><span>\{match\.phase\} • \{match\.playerCount\} players<\/span><\/button>\)\}<\/div>\r?\n        <\/article>/,
  `        <article className="panel panel-wide">
          <div className="panel-heading"><h2>Matches</h2><button type="button" className="secondary-button" onClick={() => setShowMatches((current) => !current)}>{showMatches ? 'Hide' : 'Show'}</button></div>
          {showMatches ? <><div className="inline-join"><input placeholder="Enter match id" value={joinGameId} onChange={(event) => setJoinGameId(event.target.value)} /><button onClick={() => joinMatch(joinGameId)} disabled={isBusy || !hubRef.current.isConnected || !joinGameId}>Join by id</button></div>
          <div className="match-list">{matches.length === 0 ? <p className="muted">No matches loaded yet.</p> : null}{matches.map((match) => <button key={match.gameId} className="match-card" onClick={() => joinMatch(match.gameId)} disabled={isBusy || !hubRef.current.isConnected}><strong>{match.gameId}</strong><span>{match.mapId}</span><span>{match.phase} • {match.playerCount} players</span></button>)}</div></> : <p className="muted">Match list hidden. Refresh matches to update it, or show it when you want to join.</p>}
        </article>`,
  'matches toggle',
);

css = replaceRegex(
  css,
  /\.phase-guidance \{ margin: 0 0 18px; color: #e5d5b5; font-size: 0\.96rem; \}\r?\n/,
  `.phase-guidance { margin: 0 0 18px; color: #e5d5b5; font-size: 0.96rem; }
.secondary-button { color: #f3eedc; background: rgba(255, 255, 255, 0.08); border: 1px solid rgba(216, 190, 128, 0.18); }
.secondary-button:hover:not(:disabled) { background: rgba(255, 255, 255, 0.12); }
`,
  'secondary button style',
);

fs.writeFileSync(appPath, app);
fs.writeFileSync(cssPath, css);
