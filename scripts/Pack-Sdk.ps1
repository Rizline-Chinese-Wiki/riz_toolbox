#requires -Version 7.2
[CmdletBinding()]
param([switch]$SkipBuild, [string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot 'src/RizToolbox.Sdk/RizToolbox.Sdk.csproj'
$properties = ([xml](Get-Content -LiteralPath $project -Raw)).Project.PropertyGroup
$contractVersion = [version]$properties.PackageVersion
$packageName = "$($properties.PackageId).$contractVersion.nupkg"
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repoRoot 'artifacts/sdk' }
$output = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDirectory)
New-Item -ItemType Directory -Path $output -Force | Out-Null
$packIntermediate = Join-Path (Split-Path -Parent $output) ('sdk-pack-intermediate/' + [Guid]::NewGuid().ToString('N'))
$arguments = @('pack', $project, '-c', 'Release', '--output', $output, "-p:NuspecOutputPath=$packIntermediate", '--verbosity', 'minimal')
if ($SkipBuild) { $arguments += '--no-build' }
& dotnet @arguments
if ($LASTEXITCODE -ne 0) { throw 'SDK reference package build failed.' }
$package = Join-Path $output $packageName
if (-not (Test-Path -LiteralPath $package -PathType Leaf)) { throw 'Expected SDK reference package was not produced.' }
# SDK 8's NuGet pack writes random OPC relationship IDs and ZIP timestamps.
# Canonicalize this unsigned, host-owned package without changing any DLL or API contents.
$payloads = [Collections.Generic.SortedDictionary[string,byte[]]]::new([StringComparer]::Ordinal)
$zip = [IO.Compression.ZipFile]::OpenRead($package)
try {
    foreach ($entry in $zip.Entries) {
        if ($entry.FullName -eq '.signature.p7s') { throw 'Signed packages cannot be canonicalized.' }
        if ($entry.Length -gt 16MB) { throw 'Unexpected SDK package entry size.' }
        $source = $entry.Open(); $buffer = [IO.MemoryStream]::new()
        try { $source.CopyTo($buffer); $bytes = $buffer.ToArray() } finally { $source.Dispose(); $buffer.Dispose() }
        $name = $entry.FullName
        if ($name -match '^package/services/metadata/core-properties/[a-f0-9]+\.psmdcp$') { $name = 'package/services/metadata/core-properties/sdk.psmdcp' }
        if ($name -eq '_rels/.rels') {
            $xml = [xml][Text.Encoding]::UTF8.GetString($bytes)
            foreach ($relationship in $xml.DocumentElement.ChildNodes) {
                if ($relationship.NodeType -ne [Xml.XmlNodeType]::Element) { continue }
                if ($relationship.Type -eq 'http://schemas.microsoft.com/packaging/2010/07/manifest') { $relationship.SetAttribute('Id','RManifest') }
                elseif ($relationship.Type -eq 'http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties') {
                    $relationship.SetAttribute('Id','RCore'); $relationship.SetAttribute('Target','/package/services/metadata/core-properties/sdk.psmdcp')
                } else { throw 'Unexpected SDK package relationship.' }
            }
            $bytes = [Text.UTF8Encoding]::new($false).GetBytes($xml.OuterXml)
        }
        $payloads.Add($name,$bytes)
    }
} finally { $zip.Dispose() }
$canonical = $package + '.' + [Guid]::NewGuid().ToString('N') + '.canonical'
try {
    $stream = [IO.FileStream]::new($canonical,[IO.FileMode]::CreateNew,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
    $outputZip = [IO.Compression.ZipArchive]::new($stream,[IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($entry in $payloads.GetEnumerator()) {
            $created = $outputZip.CreateEntry($entry.Key,[IO.Compression.CompressionLevel]::Optimal)
            $created.LastWriteTime = [DateTimeOffset]::new(2000,1,1,0,0,0,[TimeSpan]::Zero)
            $destination = $created.Open()
            try { $destination.Write($entry.Value,0,$entry.Value.Length) } finally { $destination.Dispose() }
        }
    } finally { $outputZip.Dispose(); $stream.Dispose() }
    Move-Item -LiteralPath $canonical -Destination $package -Force
} finally { if (Test-Path -LiteralPath $canonical) { Remove-Item -LiteralPath $canonical } }
@{ schemaVersion=1; sdkContractVersion=$contractVersion.ToString(); assemblyVersion=[string]$properties.AssemblyVersion; package=$packageName; sha256=(Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash.ToLowerInvariant() } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output 'sdk-manifest.json') -Encoding utf8
Write-Output "SDK reference feed: $output"
