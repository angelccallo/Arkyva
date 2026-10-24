param([Parameter(Mandatory)][string]$ConsumerProject, [Parameter(Mandatory)][string]$ReportName)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$directory = Split-Path ([IO.Path]::GetFullPath($ConsumerProject)) -Parent
$assets = Get-Content -LiteralPath (Join-Path $directory 'obj/project.assets.json') -Raw | ConvertFrom-Json
$packages = @($assets.libraries.PSObject.Properties | Where-Object { $_.Value.type -eq 'package' } | ForEach-Object { $_.Name } | Sort-Object)
$target = @($assets.targets.PSObject.Properties)[0].Value
$sql = @($target.PSObject.Properties | Where-Object { $_.Name -like 'Arkyva.Metadata.SqlServer/*' })[0]
$seen = @{}
$queue = [Collections.Generic.Queue[string]]::new()
foreach ($dependency in $sql.Value.dependencies.PSObject.Properties) { $queue.Enqueue($dependency.Name) }
while ($queue.Count -gt 0) {
    $id = $queue.Dequeue()
    if ($seen.ContainsKey($id)) { continue }
    $seen[$id] = $true
    $node = @($target.PSObject.Properties | Where-Object { $_.Name.Split('/')[0] -eq $id })[0]
    foreach ($dependency in $node.Value.dependencies.PSObject.Properties) { $queue.Enqueue($dependency.Name) }
}
$redirects = 0
foreach ($config in @(Get-ChildItem -LiteralPath (Join-Path $directory 'bin') -Recurse -File -Filter '*.exe.config')) {
    [xml]$xml = Get-Content -LiteralPath $config.FullName -Raw
    $redirects += @($xml.SelectNodes("//*[local-name()='bindingRedirect']")).Count
}
$framework = @($assets.project.frameworks.PSObject.Properties)[0].Value
$direct = @($framework.dependencies.PSObject.Properties).Count
$compile = @($target.PSObject.Properties | Where-Object { $_.Name -like 'Arkyva*/*' } | ForEach-Object {
    [pscustomobject]@{ Package = $_.Name; Assets = @($_.Value.compile.PSObject.Properties.Name) }
})
$report = [pscustomobject]@{
    TimestampUtc = [DateTime]::UtcNow.ToString('o')
    Target = @($assets.targets.PSObject.Properties)[0].Name
    Packages = $packages; PackageCount = $packages.Count
    ExternalPackages = @($packages | Where-Object { $_ -notlike 'Arkyva*/*' }).Count
    DirectPackages = $direct; TransitivePackages = $packages.Count - $direct
    SqlExternalPackages = @($seen.Keys | Where-Object { $_ -notlike 'Arkyva*' } | Sort-Object)
    Azure = @($packages | Where-Object { $_ -like 'Azure.*' })
    Identity = @($packages | Where-Object { $_ -like 'Microsoft.Identity*' })
    Extensions = @($packages | Where-Object { $_ -like 'Microsoft.Extensions.*' })
    BindingRedirects = $redirects
    NuGetWarnings = @($assets.logs | Where-Object { $_.level -eq 'Warning' } | ForEach-Object { $_.code } | Sort-Object -Unique)
    RequiredUpdatesInEmptyConsumer = 0
    UpdateCaveat = 'No pre-existing packages in this clean consumer; upgrades/conflicts in a legacy application require its package inventory.'
    ProductCompileAssets = $compile
}
$reports = Join-Path $repo 'artifacts/dependency-reports'
New-Item -ItemType Directory -Path $reports -Force | Out-Null
$report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $reports ($ReportName + '.json')) -Encoding UTF8
Write-Output ("Dependencies: {0} packages; {1} external; {2} redirects; report {3}" -f $report.PackageCount, $report.ExternalPackages, $redirects, $ReportName)
