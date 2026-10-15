param([Parameter(Mandatory=$true)][string]$UnityEditor)
$ErrorActionPreference = 'Stop'
$ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$UnityExecutable = (Resolve-Path -LiteralPath $UnityEditor).Path
$LuaPlugin = Join-Path $ProjectRoot 'Assets/Plugins/x86_64/xlua.dll'
if (!(Test-Path -LiteralPath $LuaPlugin) -or (Get-Item -LiteralPath $LuaPlugin).Length -lt 1024) {
    throw 'The xLua native plugin is missing or still a Git LFS pointer. Fetch Git LFS assets before building; see docs/AI.'
}
$WorkRoot = Join-Path $ProjectRoot '.utmp/game-ai'
New-Item -ItemType Directory -Force (Join-Path $WorkRoot 'temp') | Out-Null
$env:TEMP = Join-Path $WorkRoot 'temp'
$env:TMP = $env:TEMP
$env:PYTHONDONTWRITEBYTECODE = '1'
& $UnityExecutable -batchmode -nographics --game-ai-build -projectPath $ProjectRoot -executeMethod MarblesECS.Editor.GameAIBuilder.Build -quit -logFile (Join-Path $WorkRoot 'build.log') | Out-Host
if ($LASTEXITCODE -ne 0) { throw "Unity build failed. Read $WorkRoot/build.log" }
