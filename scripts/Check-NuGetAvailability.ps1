param([Parameter(Mandatory)][string]$Version, [switch]$AllowExistingIds, [switch]$AllowStable)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'NuGetPublishing.psm1') -Force
Assert-ReleaseVersion -Version $Version -AllowStable:$AllowStable
$result = @(Assert-NuGetAvailability -Version $Version -AllowExistingIds:$AllowExistingIds)
$directory = Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts'
New-Item -ItemType Directory -Path $directory -Force | Out-Null
$result | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $directory 'nuget-availability.json') -Encoding UTF8
foreach ($item in $result) { Write-Output ("PASS: {0} {1} is not in the public version index (visible existing versions: {2})." -f $item.PackageId, $Version, $item.VisibleVersions.Count) }
Write-Output 'Public index absence is not a reservation or an authorization to publish.'
