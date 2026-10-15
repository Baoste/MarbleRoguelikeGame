param([Parameter(Mandatory=$true)][string]$UnityEditor)
$ErrorActionPreference = 'Stop'
$ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$UnityExecutable = (Resolve-Path -LiteralPath $UnityEditor).Path
$WorkRoot = Join-Path $ProjectRoot '.utmp/game-ai'
New-Item -ItemType Directory -Force (Join-Path $WorkRoot 'temp') | Out-Null
$env:TEMP = Join-Path $WorkRoot 'temp'
$env:TMP = $env:TEMP
$Suites = @(
    @{ Platform = 'EditMode'; Filter = 'GameAI.Tests'; Name = 'placement-tests' },
    @{ Platform = 'PlayMode'; Filter = 'MRes.Tests.MResLuaTests'; Name = 'lua-tests' }
)
foreach ($Suite in $Suites) {
    $Results = Join-Path $WorkRoot ($Suite.Name + '.xml')
    $Log = Join-Path $WorkRoot ($Suite.Name + '.log')
    # Test Runner exits the editor itself; -quit would terminate before tests run.
    & $UnityExecutable -batchmode -nographics --game-ai-build -projectPath $ProjectRoot -runTests -testPlatform $Suite.Platform -testFilter $Suite.Filter -testResults $Results -logFile $Log | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "Unity tests failed. Read $Log" }
    if (!(Test-Path -LiteralPath $Results)) { throw "Unity did not produce $Results" }
    [xml]$Report = Get-Content -LiteralPath $Results
    if ($Report.'test-run'.result -ne 'Passed' -or [int]$Report.'test-run'.total -lt 1) {
        throw "Unity tests did not pass. Read $Results"
    }
    Write-Host ($Suite.Name + ': ' + $Report.'test-run'.passed + ' passed')
}
