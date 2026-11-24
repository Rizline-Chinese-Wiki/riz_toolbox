#requires -Version 7.2
[CmdletBinding()]
param(
    [string]$VcVars = '',
    [string]$OutputDirectory,
    [string]$HelperDirectory,
    [string]$DobbyPath,
    [switch]$SkipHelperBuild
)
. (Join-Path $PSScriptRoot 'Toolchain.Common.ps1')
if (-not $VcVars) { $VcVars = Get-RizVcVarsPath }

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repoRoot 'artifacts\initialization-context-native' }
if (-not $HelperDirectory) { $HelperDirectory = Join-Path $OutputDirectory 'helper' }
if (-not $DobbyPath) { $DobbyPath = Join-Path $repoRoot '.tools\bepinex-789\BepInEx\core\dobby.dll' }
$nativeOutput = [IO.Path]::GetFullPath($OutputDirectory)
$helperOutput = [IO.Path]::GetFullPath($HelperDirectory)
$vcvarsPath = [IO.Path]::GetFullPath($VcVars)
$dobbyFullPath = [IO.Path]::GetFullPath($DobbyPath)
$sourceRoot = Join-Path $repoRoot 'native\RizToolbox.JudgeContext'
foreach ($path in @($sourceRoot, $nativeOutput, $helperOutput, $vcvarsPath, $dobbyFullPath)) {
    if ($path -match '[\r\n"%&|<>^]') { throw 'Build paths contain unsupported cmd metacharacters.' }
}
foreach ($path in @($vcvarsPath, $dobbyFullPath)) { if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Required existing tool is missing: $path" } }
if ((Get-FileHash -LiteralPath $dobbyFullPath -Algorithm SHA256).Hash.ToLowerInvariant() -ne '8015de7d867245a1095d13947a63763878e4cf5fd3d3089b63cc39200b055ded') {
    throw 'UnsupportedBuild: the native initialization fixture requires the inspected Dobby image.'
}
if (-not $SkipHelperBuild) { & (Join-Path $PSScriptRoot 'Build-JudgeContext.ps1') -VcVars $vcvarsPath -OutputDirectory $helperOutput -DobbyPath $dobbyFullPath }
$helperDll = Join-Path $helperOutput 'RizToolbox.JudgeContext.dll'
if (-not (Test-Path -LiteralPath $helperDll -PathType Leaf)) { throw "Build the helper first: $helperDll" }
New-Item -ItemType Directory -Path $nativeOutput -Force | Out-Null
$fixture = Join-Path $nativeOutput 'InitializationContext.Fixture.exe'
$cmdPath = Join-Path $nativeOutput 'build-initialization-fixture.cmd'
$commands = @(
    '@echo off', "call `"$vcvarsPath`" >nul", 'if errorlevel 1 exit /b 1',
    "ml64 /nologo /c /Zi /Fo`"$nativeOutput\InitializationTargets.obj`" `"$sourceRoot\fixture\InitializationTargets.asm`"", 'if errorlevel 1 exit /b 1',
    "cl /nologo /std:c++17 /O2 /W4 /WX /EHa /MD /Zi /guard:cf /Fo`"$nativeOutput\InitializationFixture.obj`" /Fd`"$nativeOutput\InitializationFixture.compiler.pdb`" `"$sourceRoot\fixture\InitializationFixture.cpp`" `"$nativeOutput\InitializationTargets.obj`" /link /OUT:`"$fixture`" /PDB:`"$nativeOutput\InitializationContext.Fixture.pdb`" /INCREMENTAL:NO /OPT:NOICF /DYNAMICBASE /NXCOMPAT /CETCOMPAT /GUARD:CF", 'if errorlevel 1 exit /b 1'
)
[IO.File]::WriteAllLines($cmdPath, $commands, [Text.UTF8Encoding]::new($false))
& $env:ComSpec /d /c $cmdPath 2>&1 | Tee-Object -FilePath (Join-Path $nativeOutput 'build.log')
if ($LASTEXITCODE -ne 0) { throw "Native initialization context fixture build failed: $LASTEXITCODE" }
& $fixture $helperDll $dobbyFullPath 2>&1 | Tee-Object -FilePath (Join-Path $nativeOutput 'fixture-result.json')
if ($LASTEXITCODE -ne 0) { throw "Native initialization context fixture failed: $LASTEXITCODE" }
$files = @($helperDll, $dobbyFullPath, $fixture) | ForEach-Object {
    [ordered]@{ path=$_;sha256=(Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant() }
}
[ordered]@{schemaVersion=1;kind='native-initialization-context-unwind-fixture';gameRuntimeVerified=$false;files=@($files)} |
    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $nativeOutput 'fixture-manifest.json') -Encoding utf8
Write-Output "Native initialization context fixture: $nativeOutput"
