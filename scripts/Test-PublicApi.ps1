param([string]$BaselineDirectory, [string]$DependencyDirectory)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
if (-not $DependencyDirectory) {
    $latestStandard = Join-Path $repo 'artifacts/standard-tests/latest-bin.txt'
    if (Test-Path -LiteralPath $latestStandard) { $DependencyDirectory = (Get-Content -LiteralPath $latestStandard -Raw).Trim() }
    else { $DependencyDirectory = Split-Path ((Get-Content -LiteralPath (Join-Path $repo 'artifacts/package-smoke/latest.txt') -Raw).Trim()) -Parent }
}
# A fresh AppDomain for each read avoids unifying same-identity assemblies from different targets.
# Windows PowerShell/.NET Framework only; production assemblies have no dependency on this helper.
if (-not ('ArkyvaApiReader' -as [type])) {
$helpers = Join-Path $repo 'artifacts/api-helpers'
New-Item -ItemType Directory -Path $helpers -Force | Out-Null
$helper = Join-Path $helpers ([guid]::NewGuid().ToString('N') + '.dll')
Add-Type -OutputAssembly $helper -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
public sealed class ArkyvaApiReader : MarshalByRefObject {
    public string[] Read(string file, string dependencies) {
        AppDomain.CurrentDomain.AssemblyResolve += (sender, args) => {
            string name = new AssemblyName(args.Name).Name;
            if (name.StartsWith("Arkyva", StringComparison.Ordinal)) return null;
            string candidate = System.IO.Path.Combine(dependencies, name + ".dll");
            return System.IO.File.Exists(candidate) ? Assembly.LoadFrom(candidate) : null;
        };
        var result = new List<string>();
        foreach (var type in Assembly.LoadFrom(file).GetExportedTypes()) {
            result.Add(type.FullName + "|base=" + (type.BaseType == null ? "" : type.BaseType.FullName) + "|sealed=" + type.IsSealed + "|abstract=" + type.IsAbstract);
            foreach (var contract in type.GetInterfaces()) result.Add(type.FullName + "|interface=" + contract.FullName);
            foreach (var member in type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)) {
                string text = type.FullName + "|" + member.MemberType + "|" + member.ToString();
                var field = member as FieldInfo;
                if (field != null && field.IsLiteral) text += "|value=" + field.GetRawConstantValue();
                result.Add(text);
            }
        }
        return result.OrderBy(x => x, StringComparer.Ordinal).ToArray();
    }
}
'@
[Reflection.Assembly]::LoadFrom($helper) | Out-Null
}
function Read-IsolatedApi([string]$file) {
    $setup = New-Object AppDomainSetup
    $setup.ApplicationBase = Split-Path $file -Parent
    $domain = [AppDomain]::CreateDomain('Arkyva-api-' + [guid]::NewGuid().ToString('N'), $null, $setup)
    try {
        $reader = $domain.CreateInstanceFromAndUnwrap([ArkyvaApiReader].Assembly.Location, [ArkyvaApiReader].FullName)
        return @($reader.Read($file, $DependencyDirectory))
    } finally { [AppDomain]::Unload($domain) }
}
foreach ($id in @('Arkyva', 'Arkyva.Core', 'Arkyva.Storage.FileSystem', 'Arkyva.Metadata.SqlServer')) {
    $native = Read-IsolatedApi (Join-Path $repo "src/$id/bin/Release/net472/$id.dll")
    $standard = Read-IsolatedApi (Join-Path $repo "src/$id/bin/Release/netstandard2.0/$id.dll")
    if (@(Compare-Object $native $standard).Count -ne 0) { throw "Public API differs between targets: $id" }
    if ($BaselineDirectory) {
        $baseline = Read-IsolatedApi (Join-Path ([IO.Path]::GetFullPath($BaselineDirectory)) "$id.dll")
        if (@(Compare-Object $baseline $standard).Count -ne 0) { throw "Public API changed from baseline: $id" }
    }
    Write-Output "PASS: public API identical across targets$(if ($BaselineDirectory) { ' and preview.2 baseline' }): $id"
}
