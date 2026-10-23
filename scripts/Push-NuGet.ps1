param([Parameter(Mandatory)][string]$Version, [switch]$AllowExistingIds, [switch]$AllowStable)
$ErrorActionPreference = 'Stop'
# This script performs public writes. It is intentionally NOT called by validation scripts or tests.
if ($env:GITHUB_ACTIONS -ne 'true' -or $env:GITHUB_REPOSITORY -cne 'angelccallo/Arkyva' -or
    $env:GITHUB_REF -cne 'refs/heads/main' -or [string]::IsNullOrWhiteSpace($env:NUGET_API_KEY)) {
    throw 'Publication requires the approved main-branch GitHub job and its temporary OIDC credential.'
}
Import-Module (Join-Path $PSScriptRoot 'NuGetPublishing.psm1') -Force
Assert-ReleaseVersion -Version $Version -AllowStable:$AllowStable
$repo = Split-Path $PSScriptRoot -Parent
$feed = Join-Path $repo 'artifacts/packages'
Assert-ArtifactManifest -PackageDirectory $feed -ManifestPath (Join-Path $repo 'artifacts/package-inspection.json') -Version $Version
Assert-NuGetAvailability -Version $Version -AllowExistingIds:$AllowExistingIds | Out-Null
foreach ($id in @('Arkyva.Core', 'Arkyva.Storage.FileSystem', 'Arkyva.Metadata.SqlServer', 'Arkyva')) {
    $package = Join-Path $feed "$id.$Version.nupkg"
    Write-Output "Publishing $id $Version (temporary credential is never printed)."
    & dotnet nuget push $package --api-key $env:NUGET_API_KEY --source https://api.nuget.org/v3/index.json
    if ($LASTEXITCODE -ne 0) { throw "Push failed for $id. Inspect server state before choosing a new version; no duplicate errors are ignored." }
    $deadline = [DateTime]::UtcNow.AddMinutes(10)
    while ($true) {
        if (@(Get-NuGetVersions -PackageId $id) -contains $Version) { break }
        if ([DateTime]::UtcNow -ge $deadline) { throw "NuGet.org has not indexed $id $Version within ten minutes. Publication may be pending; stop and inspect manually." }
        Write-Output "Waiting for NuGet.org validation/indexing of $id..."
        Start-Sleep -Seconds 10
    }
    Write-Output "Confirmed public index: $id $Version."
}
