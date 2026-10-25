param([string]$Version = '1.0.0-preview.4', [switch]$AllowStable)
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'NuGetPublishing.psm1') -Force
Assert-ReleaseVersion -Version $Version -AllowStable:$AllowStable
$repo = Split-Path $PSScriptRoot -Parent
& dotnet restore (Join-Path $repo 'Arkyva.sln') "-p:Version=$Version"
if ($LASTEXITCODE -ne 0) { throw 'Release restore failed.' }
& dotnet build (Join-Path $repo 'Arkyva.sln') -c Release --no-restore -warnaserror "-p:Version=$Version"
if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }
& (Join-Path $PSScriptRoot 'Audit.ps1')
& (Join-Path $PSScriptRoot 'Test-NativeIO.ps1')
& (Join-Path $PSScriptRoot 'Test-Publishing.ps1')
& (Join-Path $PSScriptRoot 'Test.ps1')
& (Join-Path $PSScriptRoot 'Test-NetStandard.ps1')
# AppDomain-based API inspection uses Windows PowerShell (also on pwsh CI runners).
& powershell.exe -NoProfile -File (Join-Path $PSScriptRoot 'Test-PublicApi.ps1')
if ($LASTEXITCODE -ne 0) { throw 'Public API parity check failed.' }
& dotnet pack (Join-Path $repo 'Arkyva.sln') -c Release --no-build --no-restore -o (Join-Path $repo 'artifacts/packages') "-p:Version=$Version"
if ($LASTEXITCODE -ne 0) { throw 'Release pack failed.' }
& (Join-Path $PSScriptRoot 'Test-Packages.ps1') -Version $Version
