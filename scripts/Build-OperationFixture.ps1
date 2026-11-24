#requires -Version 7.2
[CmdletBinding()]
param(
    [string]$VcVars = '',
    [string]$OutputDirectory
)
. (Join-Path $PSScriptRoot 'Toolchain.Common.ps1')
if (-not $VcVars) { $VcVars = Get-RizVcVarsPath }

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repoRoot 'artifacts/operation-native' }
$output = [IO.Path]::GetFullPath($OutputDirectory)
$vcvarsPath = [IO.Path]::GetFullPath($VcVars)
$source = Join-Path $repoRoot 'native/RizToolbox.JudgeContext/fixture/OperationFixture.cpp'
$managedSource = Join-Path $repoRoot 'native/RizToolbox.JudgeContext/fixture/OperationManagedFixture.cpp'
foreach ($path in @($source, $managedSource, $output, $vcvarsPath)) {
    if ($path -match '[\r\n"%&|<>^]') { throw 'Build paths contain unsupported cmd metacharacters.' }
}
foreach ($path in @($source, $managedSource, $vcvarsPath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Required operation fixture input is missing: $path" }
}
New-Item -ItemType Directory -Path $output -Force | Out-Null
$fixture = Join-Path $output 'OperationContext.Fixture.exe'
$managedFixture = Join-Path $output 'RizToolbox.OperationManagedFixture.dll'
$cmdPath = Join-Path $output 'build-operation-fixture.cmd'
$commands = @(
    '@echo off', 'chcp 65001 >nul', "call `"$vcvarsPath`" >nul", 'if errorlevel 1 exit /b 1',
    "cl /nologo /std:c++17 /O2 /W4 /WX /EHa /MD /Zi /guard:cf /Fo`"$output\OperationFixture.obj`" /Fd`"$output\OperationFixture.compiler.pdb`" `"$source`" /link /OUT:`"$fixture`" /PDB:`"$output\OperationFixture.pdb`" /INCREMENTAL:NO /OPT:NOICF /DYNAMICBASE /NXCOMPAT /CETCOMPAT /GUARD:CF", 'if errorlevel 1 exit /b 1',
    "cl /nologo /LD /std:c++17 /O2 /W4 /WX /EHa /MD /Zi /guard:cf /Fo`"$output\OperationManagedFixture.obj`" /Fd`"$output\OperationManagedFixture.compiler.pdb`" `"$managedSource`" /link /OUT:`"$managedFixture`" /IMPLIB:`"$output\RizToolbox.OperationManagedFixture.lib`" /PDB:`"$output\OperationManagedFixture.pdb`" /INCREMENTAL:NO /OPT:NOICF /DYNAMICBASE /NXCOMPAT /CETCOMPAT /GUARD:CF", 'if errorlevel 1 exit /b 1',
    "dumpbin /nologo /exports `"$managedFixture`" > `"$output\managed-exports.txt`"", 'if errorlevel 1 exit /b 1',
    "dumpbin /nologo /unwindinfo `"$fixture`" > `"$output\unwindinfo.txt`"", 'if errorlevel 1 exit /b 1',
    "dumpbin /nologo /disasm `"$fixture`" > `"$output\disassembly.txt`"", 'if errorlevel 1 exit /b 1'
)
[IO.File]::WriteAllLines($cmdPath, $commands, [Text.UTF8Encoding]::new($false))
& $env:ComSpec /d /c $cmdPath 2>&1 | Tee-Object -FilePath (Join-Path $output 'build.log')
if ($LASTEXITCODE -ne 0) { throw "Native operation fixture build failed: $LASTEXITCODE" }
$exportText = Get-Content -LiteralPath (Join-Path $output 'managed-exports.txt') -Raw
foreach ($name in @('RizOperationManaged_GetOriginal', 'RizOperationManaged_GetMethod', 'RizOperationManaged_Invoke')) {
    if ($exportText -notmatch ('(?m)\b' + [regex]::Escape($name) + '\s*$')) { throw "Missing joint operation fixture export: $name" }
}
[ordered]@{
    schemaVersion=1; kind='native-operation-context-fixture-build'; fixture=$fixture
    fixtureSha256=(Get-FileHash -Algorithm SHA256 -LiteralPath $fixture).Hash.ToLowerInvariant()
    sourceSha256=(Get-FileHash -Algorithm SHA256 -LiteralPath $source).Hash.ToLowerInvariant()
    managedFixture=$managedFixture
    managedFixtureSha256=(Get-FileHash -Algorithm SHA256 -LiteralPath $managedFixture).Hash.ToLowerInvariant()
    fixtureExecuted=$false; gameCodeExecuted=$false; installed=$false
} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $output 'fixture-build-manifest.json') -Encoding utf8
Write-Output "Native operation fixture: $fixture"
