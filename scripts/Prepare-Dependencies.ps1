#requires -Version 7.2
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repoRoot = Split-Path -Parent $PSScriptRoot
$lock = Get-Content -LiteralPath (Join-Path $repoRoot 'dependencies.lock.json') -Raw | ConvertFrom-Json
$toolsDir = Join-Path $repoRoot '.tools'
$archive = Join-Path $toolsDir 'bepinex-789.zip'
$destination = Join-Path $toolsDir 'bepinex-789'
New-Item -ItemType Directory -Path $toolsDir -Force | Out-Null
if (-not (Test-Path -LiteralPath $archive)) {
    $partial = $archive + '.' + [Guid]::NewGuid().ToString('N') + '.partial'
    try {
        Invoke-WebRequest -Uri $lock.bepinex.url -OutFile $partial
        if ((Get-FileHash -LiteralPath $partial -Algorithm SHA256).Hash.ToLowerInvariant() -ne $lock.bepinex.sha256) { throw 'Downloaded dependency archive hash mismatch.' }
        Move-Item -LiteralPath $partial -Destination $archive
    } finally { if (Test-Path -LiteralPath $partial) { Remove-Item -LiteralPath $partial } }
}
if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant() -ne $lock.bepinex.sha256) { throw 'Dependency archive hash mismatch. Rename/remove the invalid local archive before retrying.' }
if (-not (Test-Path -LiteralPath $destination)) {
    $staging = $destination + '.' + [Guid]::NewGuid().ToString('N') + '.extracting'
    # Failed extractions stay outside the trusted cache path for diagnosis.
    Expand-Archive -LiteralPath $archive -DestinationPath $staging
    Move-Item -LiteralPath $staging -Destination $destination
}
$zip = [IO.Compression.ZipFile]::OpenRead($archive)
$expectedFiles = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
try {
    foreach ($entry in $zip.Entries) {
        if (-not $entry.Name) { continue }
        $target = [IO.Path]::GetFullPath((Join-Path $destination $entry.FullName))
        $null = $expectedFiles.Add($target)
        if (-not $target.StartsWith($destination + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Dependency archive contains an unsafe path.' }
        if (-not (Test-Path -LiteralPath $target -PathType Leaf)) { throw "Incomplete dependency cache: $($entry.FullName). Rename/remove .tools/bepinex-789 and retry." }
        $stream = $entry.Open()
        try { $expected = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)).ToLowerInvariant() }
        finally { $stream.Dispose() }
        if ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected) { throw "Modified dependency cache: $($entry.FullName). Rename/remove .tools/bepinex-789 and retry." }
    }
    foreach ($file in Get-ChildItem -LiteralPath $destination -File -Recurse) {
        if (-not $expectedFiles.Contains($file.FullName)) { throw "Unexpected file in pinned dependency cache: $($file.FullName)" }
    }
} finally { $zip.Dispose() }
Write-Output "Verified pinned runtime/Doorstop bundle $($lock.bepinex.version) at $destination"
