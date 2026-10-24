param([string]$Version = '1.0.0-preview.2', [switch]$InspectOnly, [string]$ExpectedRepositoryCommit)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$feed = Join-Path $repo 'artifacts/packages'
$ids = @('Arkyva', 'Arkyva.Core', 'Arkyva.Storage.FileSystem', 'Arkyva.Metadata.SqlServer')
$dependencies = @{
    'Arkyva' = @('Arkyva.Core', 'Arkyva.Storage.FileSystem', 'Arkyva.Metadata.SqlServer')
    'Arkyva.Core' = @()
    'Arkyva.Storage.FileSystem' = @('Arkyva.Core')
    'Arkyva.Metadata.SqlServer' = @('Arkyva.Core', 'Microsoft.Data.SqlClient', 'System.Security.Cryptography.Pkcs')
}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$packageResults = @()
foreach ($id in $ids) {
    $path = Join-Path $feed "$id.$Version.nupkg"
    if (-not (Test-Path -LiteralPath $path)) { throw "Missing package: $id" }
    $zip = [System.IO.Compression.ZipFile]::OpenRead($path)
    try {
        $names = @($zip.Entries | ForEach-Object { $_.FullName })
        foreach ($name in @("$id.nuspec", "lib/netstandard2.0/$id.dll", "lib/netstandard2.0/$id.xml", 'LICENSE', 'README.md')) {
            if ($names -notcontains $name) { throw "$id missing $name" }
        }
        foreach ($name in $names) {
            if ($name -match '(?i)(^|/)(bin|obj|tests|samples)/|\.(cs|pdb|config|user|env|tmp|context)$') { throw "Unexpected package entry: $name" }
            if ($name -notmatch '^(_rels/|package/|lib/netstandard2\.0/|contentFiles/any/any/Scripts/V\d+_[^/]+\.sql$|\[Content_Types\]\.xml$|LICENSE$|README\.md$|[^/]+\.nuspec$)') { throw "Unreviewed package entry: $name" }
        }
        $reader = [System.IO.StreamReader]::new($zip.GetEntry("$id.nuspec").Open())
        try { [xml]$manifest = $reader.ReadToEnd() } finally { $reader.Dispose() }
        $metadata = $manifest.SelectSingleNode("/*[local-name()='package']/*[local-name()='metadata']")
        if ($metadata.id -ne $id -or $metadata.version -ne $Version -or $metadata.authors -ne 'Angel Ccallo' -or
            $metadata.license.InnerText -ne 'MIT' -or $metadata.repository.url -ne 'https://github.com/angelccallo/Arkyva' -or
            $metadata.readme -ne 'README.md') { throw "Invalid metadata: $id" }
        if ($ExpectedRepositoryCommit -and $metadata.repository.commit -cne $ExpectedRepositoryCommit) { throw "Repository commit mismatch in $id." }
        $reader = [System.IO.StreamReader]::new($zip.GetEntry('LICENSE').Open())
        try { $licenseText = $reader.ReadToEnd() } finally { $reader.Dispose() }
        if ($licenseText -ne [System.IO.File]::ReadAllText((Join-Path $repo 'LICENSE'))) { throw "License changed in $id" }
        $actualDependencies = @($metadata.SelectNodes(".//*[local-name()='dependency']"))
        foreach ($required in $dependencies[$id]) {
            $dependency = @($actualDependencies | Where-Object { $_.id -eq $required })
            if ($dependency.Count -ne 1) { throw "Missing dependency $required in $id" }
            if ($required -like 'Arkyva*' -and $dependency[0].version -notin @($Version, "[$Version]", "[$Version, )", "[$Version,)")) { throw "Wrong preview dependency in $id" }
            if ($required -eq 'System.Security.Cryptography.Pkcs' -and $dependency[0].version -notlike '*8.0.2*') { throw 'Missing security version floor.' }
        }
        if ($id -eq 'Arkyva.Metadata.SqlServer' -and @($names | Where-Object { $_ -like 'contentFiles/any/any/Scripts/*.sql' }).Count -ne 4) { throw 'Expected four versioned SQL scripts.' }
        $packageResults += [pscustomobject]@{ Id = $id; Version = $Version; Entries = $names.Count; Sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash }
        Write-Output "PASS: inspected $id $Version"
    } finally { $zip.Dispose() }
}
$packageResults | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $repo 'artifacts/package-inspection.json') -Encoding UTF8
if ($InspectOnly) { return }
# A new consumer and package cache each run prevent a stale cached Arkyva package from hiding feed problems.
$work = Join-Path $repo ('artifacts/package-smoke/' + [guid]::NewGuid().ToString('N'))
$consumer = Join-Path $work 'consumer'
New-Item -ItemType Directory -Path $consumer -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $repo 'tests/Arkyva.PackageSmoke/Arkyva.PackageSmoke.csproj') -Destination $consumer
Copy-Item -LiteralPath (Join-Path $repo 'tests/Arkyva.PackageSmoke/Program.cs') -Destination $consumer
$project = Join-Path $consumer 'Arkyva.PackageSmoke.csproj'
[xml]$consumerProject = Get-Content -LiteralPath $project -Raw
$consumerProject.Project.ItemGroup.PackageReference.SetAttribute('Version', $Version)
$consumerProject.Save($project)
if ([System.IO.File]::ReadAllText($project) -match 'ProjectReference') { throw 'Consumer must use only NuGet references.' }
$escapedFeed = [System.Security.SecurityElement]::Escape($feed)
$config = Join-Path $consumer 'NuGet.Config'
@"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources><clear /><add key="arkyva-local" value="$escapedFeed" /><add key="nuget.org" value="https://api.nuget.org/v3/index.json" /></packageSources>
  <packageSourceMapping><clear /><packageSource key="arkyva-local"><package pattern="Arkyva" /><package pattern="Arkyva.*" /></packageSource><packageSource key="nuget.org"><package pattern="*" /></packageSource></packageSourceMapping>
</configuration>
"@ | Set-Content -LiteralPath $config -Encoding UTF8
& dotnet restore $project --configfile $config --packages (Join-Path $work 'packages')
if ($LASTEXITCODE -ne 0) { throw 'Clean package consumer restore failed.' }
& dotnet build $project -c Release --no-restore -warnaserror
if ($LASTEXITCODE -ne 0) { throw 'Clean package consumer build failed.' }
$assets = Get-Content -LiteralPath (Join-Path $consumer 'obj/project.assets.json') -Raw | ConvertFrom-Json
foreach ($id in $ids) {
    $library = $assets.libraries.PSObject.Properties["$id/$Version"]
    if ($null -eq $library -or $library.Value.type -ne 'package') { throw "$id was not resolved as a package." }
}
$exe = Join-Path $consumer 'bin/Release/net472/Arkyva.PackageSmoke.exe'
$previousSql = $env:ARKYVA_PACKAGE_TEST_CONNECTION
try {
    $env:ARKYVA_PACKAGE_TEST_CONNECTION = $null
    & $exe
    if ($LASTEXITCODE -ne 0) { throw 'Installed package consumer execution failed.' }
} finally { $env:ARKYVA_PACKAGE_TEST_CONNECTION = $previousSql }
$exe | Set-Content -LiteralPath (Join-Path $repo 'artifacts/package-smoke/latest.txt') -Encoding UTF8
$packageResults | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $repo 'artifacts/package-inspection.json') -Encoding UTF8
Write-Output 'PASS: all four preview packages restored from the local feed; clean net472 consumer built and ran.'
