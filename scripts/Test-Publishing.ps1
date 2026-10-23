$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'NuGetPublishing.psm1') -Force
$checks = 0
foreach ($version in @('1.0.0-preview.1', '1.0.0-preview.2', '0.1.0-alpha', '1.0.0-rc.0')) {
    Assert-ReleaseVersion -Version $version
    $checks++
}
foreach ($version in @('1.0.0', '1.0.1', '1.1.0')) {
    Assert-ReleaseVersion -Version $version -AllowStable
    $checks++
    $failed = $false
    try { Assert-ReleaseVersion -Version $version } catch { $failed = $true }
    if (-not $failed) { throw 'Stable versions must require explicit approval.' }
    $checks++
}
foreach ($version in @('01.0.0-preview.1', '1.0', '1.0.0-preview.01', '1.0.0-Preview.1', '1.0.0+build', '1.0.0-rc..1', '../1.0.0', '1.0.0;echo', '1.0.0-')) {
    $failed = $false
    try { Assert-ReleaseVersion -Version $version -AllowStable } catch { $failed = $true }
    if (-not $failed) { throw "Invalid SemVer accepted: $version" }
    $checks++
}
# Mock only the public read operation to exercise fail-closed policy, never login or push.
$module = Get-Module NuGetPublishing
& $module {
    $original = ${function:Get-NuGetVersions}
    try {
        function Get-NuGetVersions { param($PackageId) return @() }
        if (@(Assert-NuGetAvailability -Version '1.0.0-preview.1').Count -ne 4) { throw 'Missing package checks.' }
        function Get-NuGetVersions { param($PackageId) return @('1.0.0-preview.1') }
        $rejected = $false
        try { Assert-NuGetAvailability -Version '1.0.0-preview.1' -AllowExistingIds } catch { $rejected = $true }
        if (-not $rejected) { throw 'Duplicate published version accepted.' }
        $rejected = $false
        try { Assert-NuGetAvailability -Version '1.0.0-preview.2' } catch { $rejected = $true }
        if (-not $rejected) { throw 'Existing ID accepted without explicit ownership confirmation.' }
        if (@(Assert-NuGetAvailability -Version '1.0.0-preview.2' -AllowExistingIds).Count -ne 4) { throw 'Owned update policy failed.' }
        function Get-NuGetVersions { param($PackageId) throw 'Simulated network failure' }
        $rejected = $false
        try { Assert-NuGetAvailability -Version '1.0.0-preview.2' } catch { $rejected = $true }
        if (-not $rejected) { throw 'Network failure incorrectly accepted.' }
    } finally { Set-Item Function:Get-NuGetVersions -Value $original }
}
$checks += 5
$root = Join-Path (Split-Path $PSScriptRoot -Parent) ('artifacts/publishing-policy/' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root -Force | Out-Null
$manifest = @()
foreach ($id in @('Arkyva', 'Arkyva.Core', 'Arkyva.Storage.FileSystem', 'Arkyva.Metadata.SqlServer')) {
    $file = Join-Path $root "$id.1.0.0-preview.1.nupkg"
    # Test-only bytes exercise checksums; real ZIP semantics are validated separately by Test-Packages.
    'Synthetic checksum fixture, never uploaded.' | Set-Content -LiteralPath $file
    $manifest += [pscustomobject]@{ Id = $id; Version = '1.0.0-preview.1'; Sha256 = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash }
}
$path = Join-Path $root 'inspection.json'
$manifest | ConvertTo-Json | Set-Content -LiteralPath $path
Assert-ArtifactManifest -PackageDirectory $root -ManifestPath $path -Version '1.0.0-preview.1'
$checks++
$manifest[0].Sha256 = '0' * 64
$manifest | ConvertTo-Json | Set-Content -LiteralPath $path
$rejected = $false
try { Assert-ArtifactManifest -PackageDirectory $root -ManifestPath $path -Version '1.0.0-preview.1' } catch { $rejected = $true }
if (-not $rejected) { throw 'Tampered artifact was accepted.' }
$checks++
$rejected = $false
try { Assert-ArtifactManifest -PackageDirectory $root -ManifestPath $path -Version '1.0.0-preview.2' } catch { $rejected = $true }
if (-not $rejected) { throw 'Wrong artifact version was accepted.' }
$checks++
'Not an approved package.' | Set-Content -LiteralPath (Join-Path $root 'Extra.1.0.0-preview.1.nupkg')
$rejected = $false
try { Assert-ArtifactManifest -PackageDirectory $root -ManifestPath $path -Version '1.0.0-preview.1' } catch { $rejected = $true }
if (-not $rejected) { throw 'Unexpected fifth package was accepted.' }
$checks++
Write-Output "PASS: $checks publication-policy checks (no authentication or public writes)."
