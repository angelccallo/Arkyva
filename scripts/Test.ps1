param([switch]$RunSqlIntegration)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$reports = Join-Path $repo 'artifacts/test-results'
New-Item -ItemType Directory -Path $reports -Force | Out-Null
$previousSql = $env:ARKYVA_SQL_TEST_CONNECTION
if ($RunSqlIntegration -and [string]::IsNullOrWhiteSpace($previousSql)) { throw 'Explicit SQL integration requires ARKYVA_SQL_TEST_CONNECTION.' }
if (-not $RunSqlIntegration) { $env:ARKYVA_SQL_TEST_CONNECTION = $null }
$suites = @('ContractTests', 'FileSystemTests', 'ServiceTests', 'RecoveryTests', 'ConfigurationTests', 'SqlServerTests')
$results = @()
try {
    foreach ($suite in $suites) {
        $exe = Join-Path $repo "tests/Arkyva.$suite/bin/Release/net472/Arkyva.$suite.exe"
        if (-not (Test-Path -LiteralPath $exe)) { throw "Build Release first: $suite" }
        $output = @(& $exe)
        $code = $LASTEXITCODE
        $output | ForEach-Object { Write-Host $_ }
        $text = $output -join "`n"
        $checks = @($output | Where-Object { $_ -match '^PASS:' }).Count
        if ($text -match 'SQL integration passed; total checks: (\d+)') { $checks = [int]$Matches[1] }
        elseif ($text -match 'Offline validation checks passed: (\d+)') { $checks = [int]$Matches[1] }
        $skipped = @()
        if ($text -match 'SKIP SQL') { $skipped = @('SQL repository integration', 'Builder SQL/FileSystem E2E') }
        $results += [pscustomobject]@{ Suite = $suite; PassedChecks = $checks; ExitCode = $code; SkippedGroups = $skipped }
        if ($code -ne 0) { throw "Suite failed: $suite (exit $code)" }
    }
} finally {
    $env:ARKYVA_SQL_TEST_CONNECTION = $previousSql
    $summary = [pscustomobject]@{
        TimestampUtc = [DateTime]::UtcNow.ToString('o')
        PassedChecks = ($results | Measure-Object -Property PassedChecks -Sum).Sum
        FailedSuites = @($results | Where-Object { $_.ExitCode -ne 0 }).Count
        CompletedSuites = $results.Count
        Suites = $results
    }
    $summary | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $reports 'summary.json') -Encoding UTF8
}
