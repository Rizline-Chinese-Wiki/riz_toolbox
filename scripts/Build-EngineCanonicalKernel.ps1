#requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$OutputDirectory,
    [switch]$SkipEntryFixture,
    [string]$VcVars = ''
)
# Canonical EngineNodeKernel: the base four-kind EngineNodeContext.cpp alone,
# linked against seh.lib and kernel32.lib. It is the exact artifact that
# docs/validation/foundation-engine-kernel-20261025/verify-engine-kernel.py
# inspects (8 named exports, two system imports, no entry/TLS/load-config).
# The production kernel additionally bundles the inline, scheduler and wait
# contexts and is exercised by their own fixtures; the frozen kernel gate keeps
# verifying this canonical composition.
. (Join-Path $PSScriptRoot 'Toolchain.Common.ps1')
if (-not $VcVars) { $VcVars = Get-RizVcVarsPath }

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$source = Join-Path $repoRoot 'native/RizToolbox.JudgeContext/EngineNodeContext.cpp'
$header = Join-Path $repoRoot 'native/RizToolbox.JudgeContext/EngineNodeContext.h'
$probe = Join-Path $repoRoot 'native/RizToolbox.EngineNodeKernel/KernelEntryFixture.cpp'
$output = [IO.Path]::GetFullPath($OutputDirectory)
foreach ($path in @($source, $header, $probe, $output, $VcVars)) {
    if ($path -match '[\r\n"%&|<>^]') { throw 'Unsupported build path metacharacter.' }
}
foreach ($path in @($source, $header, $VcVars)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing kernel input: $path" }
}
if (-not $SkipEntryFixture -and -not (Test-Path -LiteralPath $probe -PathType Leaf)) { throw 'Missing kernel entry fixture source.' }
New-Item -ItemType Directory -Path $output -Force | Out-Null
$utf8 = [Text.UTF8Encoding]::new($false)
[IO.File]::WriteAllLines((Join-Path $output 'seh.def'), @('LIBRARY ntdll.dll', 'EXPORTS', '__C_specific_handler'), $utf8)
$commands = @('@echo off', 'chcp 65001 >nul', "call `"$VcVars`" >nul", 'if errorlevel 1 exit /b 1',
    "lib /nologo /machine:x64 /def:`"$output\seh.def`" /out:`"$output\seh.lib`"", 'if errorlevel 1 exit /b 1',
    "cl /nologo /c /std:c++17 /O2 /Oi /W4 /WX /GS- /Zl /EHa /Zi /DRIZ_JUDGE_CONTEXT_BUILD /Fo`"$output\EngineNodeContext.obj`" /Fd`"$output\EngineNodeContext.compiler.pdb`" `"$source`"", 'if errorlevel 1 exit /b 1',
    "link /nologo /DLL /NOENTRY /NODEFAULTLIB /MACHINE:X64 /INCREMENTAL:NO /OPT:REF /OPT:NOICF /DYNAMICBASE /NXCOMPAT /CETCOMPAT /GUARD:NO /DEBUG `"$output\EngineNodeContext.obj`" `"$output\seh.lib`" kernel32.lib /OUT:`"$output\RizToolbox.EngineNodeKernel.dll`" /IMPLIB:`"$output\RizToolbox.EngineNodeKernel.lib`" /PDB:`"$output\RizToolbox.EngineNodeKernel.pdb`"", 'if errorlevel 1 exit /b 1')
if (-not $SkipEntryFixture) {
    $commands += @("cl /nologo /std:c++17 /O2 /W4 /WX /EHa /MD /Zi /guard:cf /Fo`"$output\KernelEntryFixture.obj`" /Fd`"$output\KernelEntryFixture.compiler.pdb`" `"$probe`" /link /OUT:`"$output\EngineNodeKernel.EntryFixture.exe`" /PDB:`"$output\KernelEntryFixture.pdb`" /INCREMENTAL:NO /OPT:NOICF /DYNAMICBASE /NXCOMPAT /CETCOMPAT /GUARD:CF", 'if errorlevel 1 exit /b 1')
}
$names = @('RizToolbox.EngineNodeKernel.dll')
if (-not $SkipEntryFixture) { $names += 'EngineNodeKernel.EntryFixture.exe' }
foreach ($name in $names) {
    $commands += "dumpbin /nologo /headers /imports /exports /unwindinfo /relocations `"$output\$name`" > `"$output\$name.pe.txt`""
    $commands += 'if errorlevel 1 exit /b 1'
}
$commands += "dumpbin /nologo /headers /symbols /directives `"$output\EngineNodeContext.obj`" > `"$output\EngineNodeContext.obj.txt`""
$commands += 'if errorlevel 1 exit /b 1'
$command = Join-Path $output 'build-engine-node-kernel-canonical.cmd'
[IO.File]::WriteAllLines($command, $commands, $utf8)
& $env:ComSpec /d /c $command 2>&1 | Tee-Object -FilePath (Join-Path $output 'build.log')
if ($LASTEXITCODE -ne 0) { throw "Engine node kernel build failed: $LASTEXITCODE" }
function Hash([string]$Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }
$files = @($names | ForEach-Object { $path = Join-Path $output $_; @{ name = $_; path = $path; sha256 = (Hash $path); role = $(if ($_ -eq 'RizToolbox.EngineNodeKernel.dll') { 'kernel' } else { 'entry-fixture' }) } })
$sourcePaths = @($source, $header, $PSCommandPath)
if (-not $SkipEntryFixture) { $sourcePaths += $probe }
$inputs = @($sourcePaths | ForEach-Object { @{ path = $_; sha256 = (Hash $_) } })
[ordered]@{schemaVersion = 1; kind = 'engine-node-kernel-build'; files = $files; sources = $inputs; compilerEnvironment = $VcVars;
    entryFixtureBuilt = (-not $SkipEntryFixture); canonicalSourceReused = $true; canonicalBaseContextOnly = $true;
    fixtureExecuted = $false; gameExecuted = $false; installed = $false;
    productionFirstInstallVerified = $false; engineCancellationCompleted = $false;
    sourceHashesUnchanged = $true} | ConvertTo-Json -Depth 6 |
    Set-Content -LiteralPath (Join-Path $output 'kernel-manifest.json') -Encoding utf8
Write-Output "Canonical engine node kernel: $output"
