$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$sources = Get-ChildItem -LiteralPath (Join-Path $repo 'src') -Filter '*.cs' -File -Recurse | Where-Object { $_.FullName -notmatch '[\\/](obj|bin)[\\/]' }
foreach ($source in $sources) {
    $code = Get-Content -LiteralPath $source.FullName -Raw
    if ($code -match 'Task\.Run\s*\(|\.GetAwaiter\s*\(|\.Wait\s*\(|\.Result\b') { throw "Task blocking or offloading in product source: $($source.Name)" }
    if ($source.Name -like '*.Synchronous.cs' -and $code -match '\bawait\b|\b\w+Async\s*\(') { throw "Async call found in native synchronous source: $($source.Name)" }
}
Write-Output 'PASS: native I/O source guard; no sync-over-async or Task.Run in product code.'
