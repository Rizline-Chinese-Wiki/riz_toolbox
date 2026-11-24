#requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string]$VcVars = ''
)
. (Join-Path $PSScriptRoot 'Toolchain.Common.ps1')
if (-not $VcVars) { $VcVars = Get-RizVcVarsPath }

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
function Absolute([string]$Path) {
    if (-not [IO.Path]::IsPathFullyQualified($Path)) { throw "Absolute path required: $Path" }
    $full = [IO.Path]::GetFullPath($Path)
    if ($full -match '[\r\n"%&|<>^]') { throw "Unsupported command path characters: $full" }
    $full
}
function Hash([string]$Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }
function WriteJson([object]$Value,[string]$Path) {
    [IO.File]::WriteAllText($Path,($Value | ConvertTo-Json -Depth 16),[Text.UTF8Encoding]::new($false))
}
$output = Absolute $OutputDirectory
$vcvarsPath = Absolute $VcVars
if (Test-Path -LiteralPath $output) { throw 'Use a fresh nonexistent output directory.' }
$originals = Join-Path $repo 'native/RizToolbox.EngineBootstrap/EngineTransportOriginals.asm'
$fixtureRoot = Join-Path $repo 'native/RizToolbox.EngineTransportOriginalFixture'
$cpp = Join-Path $fixtureRoot 'TransportOriginalFixture.cpp'
$targets = Join-Path $fixtureRoot 'TransportOriginalTargets.asm'
$gameProfile = Join-Path $repo 'native/RizToolbox.EngineBootstrap/EngineTransportGameProfile.h'
$sourcePaths = @($originals,$cpp,$targets,
    (Join-Path $repo 'native/RizToolbox.EngineBootstrap/EngineTransportOriginals.h'),
    (Join-Path $fixtureRoot 'TransportOriginalFixture.h'),$gameProfile,$PSCommandPath,
    (Join-Path $PSScriptRoot 'Test-EngineTransportOriginalFixture.ps1'),
    (Join-Path $repo 'docs/validation/foundation-engine-transport-production-20261026/verify-transport-originals.py'))
foreach ($path in $sourcePaths + @($vcvarsPath)) {
    $null = Absolute $path
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing build input: $path" }
}
$inputs = @($sourcePaths | ForEach-Object { [ordered]@{path=$_;sha256=Hash $_;bytes=(Get-Item -LiteralPath $_).Length} })
$null = New-Item -ItemType Directory -Path $output
$manifestPath = Join-Path $output 'transport-original-fixture-build.json'
$manifest = [ordered]@{
    schemaVersion=1;kind='engine-transport-original-fixture-build';startedUtc=[DateTime]::UtcNow.ToString('o')
    buildSucceeded=$false;buildExitCode=$null;sourceHashesUnchanged=$false;fixtureExecuted=$false
    selfOwnedOnly=$true;gameExecuted=$false;sourcePatched=$false;productionAdmissionVerified=$false
    outputDirectory=$output;compilerEnvironment=$vcvarsPath;sources=$inputs;files=@();inputArchive=$null;failure=$null
}
WriteJson $manifest $manifestPath
try {
    $archivePath = Join-Path $output 'transport-original-sources.zip'
    $zip = [IO.Compression.ZipFile]::Open($archivePath,'Create')
    try {
        foreach ($row in $inputs) {
            $relative = [IO.Path]::GetRelativePath($repo,$row.path).Replace('\','/')
            $entry = $zip.CreateEntry($relative,[IO.Compression.CompressionLevel]::Optimal)
            $destination = $entry.Open();$source = [IO.File]::Open($row.path,'Open','Read','Read')
            try { $source.CopyTo($destination) } finally { $source.Dispose();$destination.Dispose() }
            if ((Hash $row.path) -ne $row.sha256) { throw "Source changed during archive: $($row.path)" }
        }
    } finally { $zip.Dispose() }
    $manifest.inputArchive = [ordered]@{path=$archivePath;sha256=Hash $archivePath;entries=$inputs.Count}
    $exports = foreach ($name in @('Register','Remove','Consume','Pump','Drain','Callback','Yield','Dispatch','Batch')) {
        foreach ($suffix in @('','PrefixEnd','End')) {
            '/EXPORT:RizEngineTransportOriginal' + $name + $suffix
            '/EXPORT:RtTransportBaseline' + $name + $suffix
        }
    }
    $exportOptions = $exports -join ' '
    $fixture = Join-Path $output 'EngineTransportOriginal.Fixture.exe'
    $syntaxSource = Join-Path $output 'TransportGameProfile.Syntax.cpp'
    $syntaxText = '#include "' + $gameProfile.Replace('\','/') + '"' + "`n" +
        'static_assert(RizEngineTransportGameProfile::TypedCount == 13);' + "`n" +
        'static_assert(RizEngineTransportGameProfile::SiteCount == 23);' + "`n" +
        'static_assert(!RizEngineTransportGameProfile::ProductionFirstInstallAuthorized);' + "`n"
    [IO.File]::WriteAllText($syntaxSource,$syntaxText,[Text.UTF8Encoding]::new($false))
    $commands = @(
        '@echo off','setlocal DisableDelayedExpansion','chcp 65001 >nul',
        "call `"$vcvarsPath`" >nul",'if errorlevel 1 exit /b 1',
        "cl /nologo /std:c++17 /c /W4 /WX /Fo`"$output\TransportGameProfile.Syntax.obj`" `"$syntaxSource`"",'if errorlevel 1 exit /b 1',
        "ml64 /nologo /c /Zi /Fo`"$output\EngineTransportOriginals.obj`" /Fl`"$output\EngineTransportOriginals.lst`" `"$originals`"",'if errorlevel 1 exit /b 1',
        "ml64 /nologo /c /Zi /Fo`"$output\TransportOriginalTargets.obj`" /Fl`"$output\TransportOriginalTargets.lst`" `"$targets`"",'if errorlevel 1 exit /b 1',
        "cl /nologo /std:c++17 /O2 /W4 /WX /EHa /MD /Zi /guard:cf /Fo`"$output\TransportOriginalFixture.obj`" /Fd`"$output\TransportOriginalFixture.compiler.pdb`" `"$cpp`" `"$output\EngineTransportOriginals.obj`" `"$output\TransportOriginalTargets.obj`" /link /OUT:`"$fixture`" /PDB:`"$output\EngineTransportOriginal.Fixture.pdb`" /MAP:`"$output\EngineTransportOriginal.Fixture.map`" /INCREMENTAL:NO /OPT:NOICF /DYNAMICBASE /NXCOMPAT /CETCOMPAT /GUARD:CF $exportOptions",'if errorlevel 1 exit /b 1',
        "dumpbin /nologo /headers /imports /exports /unwindinfo /relocations `"$fixture`" > `"$output\EngineTransportOriginal.Fixture.pe.txt`"",'if errorlevel 1 exit /b 1',
        "dumpbin /nologo /disasm `"$fixture`" > `"$output\EngineTransportOriginal.Fixture.disasm.txt`"",'if errorlevel 1 exit /b 1',
        'exit /b 0'
    )
    $commandPath = Join-Path $output 'build-transport-original-fixture.cmd'
    [IO.File]::WriteAllLines($commandPath,$commands,[Text.UTF8Encoding]::new($false))
    & $env:ComSpec /d /v:off /c $commandPath 2>&1 | Tee-Object -FilePath (Join-Path $output 'build.log')
    $manifest.buildExitCode = $LASTEXITCODE
    if ($LASTEXITCODE -ne 0) { throw "Transport original fixture build failed: $LASTEXITCODE" }
    foreach ($row in $inputs) { if ((Hash $row.path) -ne $row.sha256) { throw "Source changed during build: $($row.path)" } }
    $manifest.sourceHashesUnchanged=$true
    $names = @('EngineTransportOriginal.Fixture.exe','EngineTransportOriginal.Fixture.pdb','EngineTransportOriginal.Fixture.map',
        'EngineTransportOriginals.lst','TransportOriginalTargets.lst','EngineTransportOriginal.Fixture.pe.txt',
        'EngineTransportOriginal.Fixture.disasm.txt','build-transport-original-fixture.cmd','build.log','transport-original-sources.zip',
        'TransportGameProfile.Syntax.cpp','TransportGameProfile.Syntax.obj')
    $manifest.files = @($names | ForEach-Object {
        $path = Join-Path $output $_
        [ordered]@{name=$_;path=$path;sha256=Hash $path;bytes=(Get-Item -LiteralPath $path).Length}
    })
    $manifest.buildSucceeded=$true
} catch { $manifest.failure=$_.Exception.Message;throw }
finally { $manifest.completedUtc=[DateTime]::UtcNow.ToString('o');WriteJson $manifest $manifestPath }
Write-Output "Transport original fixture build: $output"
