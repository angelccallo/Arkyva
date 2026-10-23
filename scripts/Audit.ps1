$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$directory = Join-Path $repo 'artifacts'
New-Item -ItemType Directory -Path $directory -Force | Out-Null
$output = & dotnet list (Join-Path $repo 'Arkyva.sln') package --vulnerable --include-transitive --format json
if ($LASTEXITCODE -ne 0) { throw 'NuGet vulnerability audit failed; no clean result can be claimed.' }
$text = $output -join "`n"
$text | Set-Content -LiteralPath (Join-Path $directory 'audit.json') -Encoding UTF8
$audit = $text | ConvertFrom-Json
$findings = @()
foreach ($project in $audit.projects) {
    foreach ($framework in $project.frameworks) {
        foreach ($package in @($framework.topLevelPackages) + @($framework.transitivePackages)) {
            if ($package.vulnerabilities) { $findings += $package.id }
        }
    }
}
if ($findings.Count -ne 0) { throw ('Vulnerable dependencies: ' + (($findings | Sort-Object -Unique) -join ', ')) }
Write-Output 'PASS: NuGet audit found no known vulnerabilities in the resolved direct/transitive packages.'
