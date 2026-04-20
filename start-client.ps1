Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$clientDir = Join-Path $repoRoot 'Client'

Set-Location $clientDir

if (-not $env:VITE_SERVER_URL) {
    $env:VITE_SERVER_URL = 'http://localhost:5118'
}

& 'C:\Program Files\nodejs\npm.cmd' run dev
