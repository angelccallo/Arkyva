param([switch]$RunSqlIntegration)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$work = Join-Path $repo ('artifacts/standard-tests/' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work -Force | Out-Null
$results = @()
$previousSql = $env:ARKYVA_SQL_TEST_CONNECTION
if ($RunSqlIntegration -and [string]::IsNullOrWhiteSpace($previousSql)) { throw 'Explicit SQL integration requires ARKYVA_SQL_TEST_CONNECTION.' }
if (-not $RunSqlIntegration) { $env:ARKYVA_SQL_TEST_CONNECTION = $null }
try {
    foreach ($suite in @('ContractTests', 'FileSystemTests', 'ServiceTests', 'RecoveryTests', 'ConfigurationTests', 'SqlServerTests')) {
        $directory = Join-Path $work $suite
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
        $references = @('Arkyva', 'Arkyva.Core', 'Arkyva.Storage.FileSystem', 'Arkyva.Metadata.SqlServer') | ForEach-Object {
            $file = Join-Path $repo "src/$_/bin/Release/netstandard2.0/$_.dll"
            if (-not (Test-Path -LiteralPath $file)) { throw 'Build both targets before testing Standard assemblies.' }
            '<Reference Include="' + $_ + '"><HintPath>' + [Security.SecurityElement]::Escape($file) + '</HintPath></Reference>'
        }
        $sources = [Security.SecurityElement]::Escape((Join-Path $repo "tests/Arkyva.$suite/*.cs"))
        $sharedSources = ''
        if ($suite -eq 'RecoveryTests') {
            $sharedSources = '<Compile Include="' + [Security.SecurityElement]::Escape((Join-Path $repo 'tests/Arkyva.ServiceTests/Fakes.cs')) + '" />'
        }
        $scripts = [Security.SecurityElement]::Escape((Join-Path $repo 'src/Arkyva.Metadata.SqlServer/Scripts/*.sql'))
        $project = Join-Path $directory "Arkyva.$suite.csproj"
@"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net472</TargetFramework><OutputType>Exe</OutputType><IsPackable>false</IsPackable><TreatWarningsAsErrors>true</TreatWarningsAsErrors><DefineConstants>ARKYVA_NETSTANDARD</DefineConstants><AutoGenerateBindingRedirects>true</AutoGenerateBindingRedirects><GenerateBindingRedirectsOutputType>true</GenerateBindingRedirectsOutputType></PropertyGroup>
  <ItemGroup>$($references -join '')<Compile Include="$sources" />$sharedSources<None Include="$scripts" Link="Scripts/%(Filename)%(Extension)" CopyToOutputDirectory="PreserveNewest" /><PackageReference Include="Microsoft.Data.SqlClient" Version="6.1.7" /><PackageReference Include="System.Security.Cryptography.Pkcs" Version="8.0.2" /></ItemGroup>
</Project>
"@ | Set-Content -LiteralPath $project -Encoding UTF8
        & dotnet build $project -c Release -warnaserror "-p:RestorePackagesPath=$(Join-Path $work 'packages')"
        if ($LASTEXITCODE -ne 0) { throw "Standard assembly runner build failed: $suite" }
        (Join-Path $directory 'bin/Release/net472') | Set-Content -LiteralPath (Join-Path $repo 'artifacts/standard-tests/latest-bin.txt') -Encoding UTF8
        $output = @(& (Join-Path $directory "bin/Release/net472/Arkyva.$suite.exe"))
        $code = $LASTEXITCODE
        $output | ForEach-Object { Write-Host $_ }
        $text = $output -join "`n"
        $checks = @($output | Where-Object { $_ -match '^PASS:' }).Count
        if ($text -match 'SQL integration passed; total checks: (\d+)') { $checks = [int]$Matches[1] }
        elseif ($text -match 'Offline validation checks passed: (\d+)') { $checks = [int]$Matches[1] }
        $results += [pscustomobject]@{ Suite = $suite; PassedChecks = $checks; ExitCode = $code; SqlSkipped = ($text -match 'SKIP SQL') }
        if ($code -ne 0) { throw "Standard assembly suite failed: $suite" }
    }
} finally {
    $env:ARKYVA_SQL_TEST_CONNECTION = $previousSql
    $reports = Join-Path $repo 'artifacts/test-results'
    New-Item -ItemType Directory -Path $reports -Force | Out-Null
    [pscustomobject]@{ Target = 'netstandard2.0 assemblies executed on Framework runner'; PassedChecks = ($results | Measure-Object PassedChecks -Sum).Sum; Suites = $results } |
        ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $reports 'standard-summary.json') -Encoding UTF8
}
