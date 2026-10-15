param([string]$Python = 'python')
$ErrorActionPreference = 'Stop'
$ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$WorkRoot = Join-Path $ProjectRoot '.utmp/game-ai'
New-Item -ItemType Directory -Force (Join-Path $WorkRoot 'temp') | Out-Null
$env:TEMP = Join-Path $WorkRoot 'temp'
$env:TMP = $env:TEMP
$env:PYTHONDONTWRITEBYTECODE = '1'
$TrainingPython = Join-Path $WorkRoot 'venv/Scripts/python.exe'
& $Python -m venv (Join-Path $WorkRoot 'venv')
if ($LASTEXITCODE -ne 0) { throw 'Could not create the project Python environment.' }
& $TrainingPython -m pip install --cache-dir (Join-Path $WorkRoot 'pip-cache') --upgrade pip
if ($LASTEXITCODE -ne 0) { throw 'pip upgrade failed.' }
& $TrainingPython -m pip install --cache-dir (Join-Path $WorkRoot 'pip-cache') -r (Join-Path $PSScriptRoot 'requirements.txt') --extra-index-url https://download.pytorch.org/whl/cpu
if ($LASTEXITCODE -ne 0) { throw 'Training dependency installation failed.' }
Write-Output "Training Python: $TrainingPython"
