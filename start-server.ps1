Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$serverProject = Join-Path $repoRoot 'Server\GameServer\GameServer\GameServer.csproj'

Set-Location $repoRoot
dotnet run --project $serverProject
