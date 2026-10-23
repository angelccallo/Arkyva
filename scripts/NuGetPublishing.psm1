function Assert-ReleaseVersion {
    param([Parameter(Mandatory)][string]$Version, [switch]$AllowStable)
    $number = '(0|[1-9][0-9]*)'
    $identifier = '(0|[1-9][0-9]*|[0-9]*[a-z-][0-9a-z-]*)'
    if ($Version -cnotmatch "^$number\.$number\.$number(-$identifier(\.$identifier)*)?$") {
        throw 'Use canonical lowercase SemVer: major.minor.patch[-prerelease]. No leading zeros or build metadata.'
    }
    if (-not $Version.Contains('-') -and -not $AllowStable) { throw 'Stable publication requires explicit AllowStable approval.' }
}

function Get-NuGetVersions {
    param([Parameter(Mandatory)][string]$PackageId)
    $uri = 'https://api.nuget.org/v3-flatcontainer/' + $PackageId.ToLowerInvariant() + '/index.json'
    try {
        $response = Invoke-RestMethod -Uri $uri -TimeoutSec 30 -ErrorAction Stop
        if ($null -eq $response.versions -or @($response.versions).Count -eq 0) { throw 'Invalid version index response.' }
        return @($response.versions)
    } catch {
        if ($null -ne $_.Exception.Response -and [int]$_.Exception.Response.StatusCode -eq 404) { return @() }
        throw "Unable to verify NuGet.org version index for $PackageId. Publication must stop."
    }
}

function Assert-NuGetAvailability {
    param([Parameter(Mandatory)][string]$Version, [switch]$AllowExistingIds)
    $result = @()
    foreach ($id in @('Arkyva', 'Arkyva.Core', 'Arkyva.Storage.FileSystem', 'Arkyva.Metadata.SqlServer')) {
        $versions = @(Get-NuGetVersions -PackageId $id)
        if ($versions -contains $Version) { throw "$id $Version is already published. Do not reuse this version." }
        if ($versions.Count -ne 0 -and -not $AllowExistingIds) {
            throw "Package identifier conflict: $id already exists. Confirm ownership manually; do not rename or publish automatically."
        }
        $result += [pscustomobject]@{ PackageId = $id; RequestedVersion = $Version; VisibleVersions = $versions; CheckedUtc = [DateTime]::UtcNow.ToString('o') }
    }
    return $result
}

function Assert-ArtifactManifest {
    param([Parameter(Mandatory)][string]$PackageDirectory, [Parameter(Mandatory)][string]$ManifestPath, [Parameter(Mandatory)][string]$Version)
    $manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
    $ids = @('Arkyva.Core', 'Arkyva.Storage.FileSystem', 'Arkyva.Metadata.SqlServer', 'Arkyva')
    if ($manifest.Count -ne 4 -or @(Get-ChildItem -LiteralPath $PackageDirectory -File -Filter '*.nupkg').Count -ne 4) { throw 'Exactly four approved packages are required.' }
    foreach ($id in $ids) {
        $entry = @($manifest | Where-Object { $_.Id -ceq $id -and $_.Version -ceq $Version })
        if ($entry.Count -ne 1) { throw "Invalid artifact manifest entry for $id." }
        $file = Join-Path $PackageDirectory "$id.$Version.nupkg"
        if (-not (Test-Path -LiteralPath $file) -or (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ne $entry[0].Sha256) { throw "Artifact checksum mismatch for $id." }
    }
}

Export-ModuleMember -Function Assert-ReleaseVersion, Get-NuGetVersions, Assert-NuGetAvailability, Assert-ArtifactManifest
