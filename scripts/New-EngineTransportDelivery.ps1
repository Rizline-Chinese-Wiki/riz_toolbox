#requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$CandidateDirectory,
    [Parameter(Mandatory)][string]$OutputZip
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'EngineTransportPackage.Common.ps1')
$repo = Split-Path -Parent $PSScriptRoot
$candidatePath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($CandidateDirectory)
$candidate = Get-EngineTransportProductionCandidatePackage $candidatePath
$target = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputZip)
$parent = Assert-EngineOrdinaryPath (Split-Path -Parent $target) -Directory
if ([IO.Path]::GetExtension($target) -ine '.zip' -or (Test-Path -LiteralPath $target) -or
    $target.StartsWith($candidate.Root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Choose a new ZIP path outside the candidate directory.'
}

$members = [ordered]@{}
function Add-MemberFile([string]$Name, [string]$Source, [string]$Hash) {
    $members.Add($Name, [pscustomobject]@{ source=$Source; hash=$Hash })
}
$manifestName = 'transport-candidate-package-manifest.json'
$manifestFile = Get-EnginePackageFile $candidate.Root $manifestName
Add-MemberFile "candidate/$manifestName" $manifestFile (Get-EnginePackageHash $manifestFile)
foreach ($folder in 'package','evidence') {
    $rows = if ($folder -eq 'package') { $candidate.Manifest.files } else { $candidate.Manifest.evidence }
    foreach ($row in $rows) {
        $relative = "$folder/$($row.path)"
        Add-MemberFile "candidate/$relative" (Get-EnginePackageFile $candidate.Root $relative) $row.sha256
    }
}
$support = 'dependencies.lock.json','.tools/bepinex-789.zip','LICENSE','THIRD_PARTY_NOTICES.md',
    'docs/Install-V5.md','scripts/Install-EngineTransportCandidate.ps1','scripts/Uninstall.ps1',
    'scripts/Install.Common.ps1','scripts/EngineTransportPackage.Common.ps1','scripts/EngineProductionPackage.Common.ps1',
    'scripts/verify-engine-transport-production-package.py','scripts/Prepare-Dependencies.ps1','scripts/Send-HostCommand.ps1'
foreach ($relative in $support) {
    $file = Get-EnginePackageFile $repo $relative
    $name = if ($relative -eq 'docs/Install-V5.md') { '安装说明.md' } else { $relative }
    Add-MemberFile $name $file (Get-EnginePackageHash $file)
}
$base = Read-EnginePackageJson (Get-EnginePackageFile $candidate.Root 'evidence/base-package-manifest.json')
if ($base.Contains('basicPlugins') -and $base.basicPlugins.Count) {
    $guide = Get-EnginePackageFile $repo 'docs/Basic-plugins.md'
    Add-MemberFile 'docs/Basic-plugins.md' $guide (Get-EnginePackageHash $guide)
    foreach ($id in $base.basicPlugins) {
        $name = $id.Substring('basic.'.Length)
        $relative = "examples/RizToolbox.BasicPlugins/configuration/$name.example.json"
        $config = Get-EnginePackageFile $repo $relative
        Add-MemberFile $relative $config (Get-EnginePackageHash $config)
    }
}
$lock = Get-Content -Raw -LiteralPath $members['dependencies.lock.json'].source | ConvertFrom-Json
if ($lock.bepinex.sha256 -cne $members['.tools/bepinex-789.zip'].hash) { throw 'Bundled runtime dependency hash mismatch.' }

$partial = Join-Path $parent ([Guid]::NewGuid().ToString('N') + '.partial')
$stream = [IO.File]::Open($partial, [IO.FileMode]::CreateNew, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
try {
    try {
        $zip = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create, $true)
        try {
            foreach ($entry in $members.GetEnumerator()) {
                $compression = if ($entry.Key.EndsWith('.zip')) { 'NoCompression' } else { 'Optimal' }
                $null = [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $entry.Value.source, $entry.Key, $compression)
            }
        } finally { $zip.Dispose() }
        $stream.Position = 0
        $zip = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Read, $true)
        try {
            if ($zip.Entries.Count -ne $members.Count) { throw 'Incomplete delivery archive.' }
            foreach ($entry in $members.GetEnumerator()) {
                $content = $zip.GetEntry($entry.Key).Open()
                try { $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($content)).ToLowerInvariant() }
                finally { $content.Dispose() }
                if ($hash -cne $entry.Value.hash) { throw "ZIP content changed: $($entry.Key)" }
            }
        } finally { $zip.Dispose() }
    } finally { $stream.Dispose() }
    [IO.File]::Move($partial, $target)
} finally { if (Test-Path -LiteralPath $partial) { Remove-Item -LiteralPath $partial } }
[pscustomobject]@{ archive=$target; bytes=(Get-Item -LiteralPath $target).Length; sha256=(Get-EnginePackageHash $target)
    members=$members.Count; candidateManifestSha256=$members["candidate/$manifestName"].hash }
