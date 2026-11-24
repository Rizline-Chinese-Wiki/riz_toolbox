#requires -Version 7.2
[CmdletBinding()]
param(
    [string]$VcVars = '',
    [string]$OutputDirectory,
    [Parameter(Mandatory)][string]$HelperDirectory,
    [string]$DobbyPath
)
. (Join-Path $PSScriptRoot 'Toolchain.Common.ps1')
if (-not $VcVars) { $VcVars = Get-RizVcVarsPath }

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repoRoot 'artifacts/listener-context-native' }
if (-not $DobbyPath) { $DobbyPath = Join-Path $repoRoot '.tools/bepinex-789/BepInEx/core/dobby.dll' }
$nativeOutput = [IO.Path]::GetFullPath($OutputDirectory)
$helperDll = Join-Path ([IO.Path]::GetFullPath($HelperDirectory)) 'RizToolbox.JudgeContext.dll'
$dobbyDll = [IO.Path]::GetFullPath($DobbyPath)
$vcvarsPath = [IO.Path]::GetFullPath($VcVars)
$source = Join-Path $repoRoot 'native/RizToolbox.JudgeContext/fixture/ListenerFixture.cpp'
$asmSource = Join-Path $repoRoot 'native/RizToolbox.JudgeContext/fixture/ListenerCaller.asm'
foreach ($path in @($source, $asmSource, $nativeOutput, $helperDll, $dobbyDll, $vcvarsPath)) {
    if ($path -match '[\r\n"%&|<>^]') { throw 'Build paths contain unsupported cmd metacharacters.' }
}
foreach ($path in @($source, $asmSource, $helperDll, $dobbyDll, $vcvarsPath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Required listener fixture input is missing: $path" }
}
function Hash([string]$File) { (Get-FileHash -LiteralPath $File -Algorithm SHA256).Hash.ToLowerInvariant() }
if ((Hash $dobbyDll) -ne '8015de7d867245a1095d13947a63763878e4cf5fd3d3089b63cc39200b055ded') {
    throw 'UnsupportedBuild: the native listener fixture requires the inspected Dobby image.'
}
New-Item -ItemType Directory -Path $nativeOutput -Force | Out-Null
$fixture = Join-Path $nativeOutput 'ListenerContext.Fixture.exe'
$cmdPath = Join-Path $nativeOutput 'build-listener-context-fixture.cmd'
$commands = @(
    '@echo off', 'chcp 65001 >nul', "call `"$vcvarsPath`" >nul", 'if errorlevel 1 exit /b 1',
    "ml64 /nologo /c /Zi /Fo`"$nativeOutput\ListenerCaller.obj`" `"$asmSource`"", 'if errorlevel 1 exit /b 1',
    "cl /nologo /std:c++17 /O2 /W4 /WX /EHa /MD /Zi /guard:cf /Fo`"$nativeOutput\ListenerFixture.obj`" /Fd`"$nativeOutput\ListenerFixture.compiler.pdb`" `"$source`" `"$nativeOutput\ListenerCaller.obj`" /link /OUT:`"$fixture`" /PDB:`"$nativeOutput\ListenerContext.Fixture.pdb`" /INCREMENTAL:NO /OPT:NOICF /DYNAMICBASE /NXCOMPAT /CETCOMPAT /GUARD:CF", 'if errorlevel 1 exit /b 1',
    "dumpbin /nologo /unwindinfo `"$fixture`" > `"$nativeOutput\unwindinfo.txt`"", 'if errorlevel 1 exit /b 1',
    "dumpbin /nologo /disasm `"$fixture`" > `"$nativeOutput\disassembly.txt`"", 'if errorlevel 1 exit /b 1'
)
[IO.File]::WriteAllLines($cmdPath, $commands, [Text.UTF8Encoding]::new($false))
& $env:ComSpec /d /c $cmdPath 2>&1 | Tee-Object -FilePath (Join-Path $nativeOutput 'build.log')
if ($LASTEXITCODE -ne 0) { throw "Native listener fixture build failed: $LASTEXITCODE" }
$inputPaths = @($helperDll, $dobbyDll, $fixture)
$beforeHashes = @{}
foreach ($path in $inputPaths) { $beforeHashes[$path] = Hash $path }
$start = [Diagnostics.ProcessStartInfo]::new($fixture)
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.RedirectStandardOutput = $true
$start.RedirectStandardError = $true
$start.ArgumentList.Add($helperDll)
$start.ArgumentList.Add($dobbyDll)
$child = [Diagnostics.Process]::new()
$child.StartInfo = $start
if (-not $child.Start()) { throw 'Could not start the owned listener fixture executable.' }
try {
    $stdout = $child.StandardOutput.ReadToEndAsync()
    $stderr = $child.StandardError.ReadToEndAsync()
    if (-not $child.WaitForExit(60000)) {
        # This handle belongs only to the native fixture started above.
        $child.Kill($true)
        $child.WaitForExit()
        [IO.File]::WriteAllText((Join-Path $nativeOutput 'fixture-stdout.log'), $stdout.GetAwaiter().GetResult(), [Text.UTF8Encoding]::new($false))
        [IO.File]::WriteAllText((Join-Path $nativeOutput 'fixture-stderr.log'), $stderr.GetAwaiter().GetResult(), [Text.UTF8Encoding]::new($false))
        throw 'The owned listener fixture exceeded its finite timeout.'
    }
    $exitCode = $child.ExitCode
    $outText = $stdout.GetAwaiter().GetResult()
    $errText = $stderr.GetAwaiter().GetResult()
} finally { $child.Dispose() }
[IO.File]::WriteAllText((Join-Path $nativeOutput 'fixture-stdout.log'), $outText, [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText((Join-Path $nativeOutput 'fixture-stderr.log'), $errText, [Text.UTF8Encoding]::new($false))
$jsonLines = @($outText -split '\r?\n' | Where-Object { $_.StartsWith('{') })
if ($exitCode -ne 0 -or $jsonLines.Count -ne 1) { throw "Native listener fixture failed (exit $exitCode). See fixture-stdout.log and fixture-stderr.log in $nativeOutput" }
$value = $jsonLines[0] | ConvertFrom-Json -AsHashtable
if ($value.kind -ne 'native-listener-six-region-fixture' -or
    $value.passed -ne $true -or $value.failures -ne 0 -or $value.checks -le 0 -or
    $value.gameRuntimeVerified -ne $false -or $value.gameCodeExecuted -ne $false -or
    $value.firstInstallSafetyVerified -ne $false -or $value.allFutureListenersClosed -ne $false -or
    $value.actualCalleeReturnChecks -le 0 -or $value.actualCalleeReturnChecks -ne $value.originalCalls -or
    $value.actualCallbackStackWalks -le 0 -or $value.postInstructionBoundaryUnwinds -le 0 -or
    $value.fixtureCodeWrites -ne 14 -or $value.lateAttachOldCalls -ne 2 -or
    $value.originalSehCases -ne 6 -or $value.originalCppCases -ne 6 -or
    ($value.patchedSourceWidths -join ',') -ne '5,5,6' -or ($value.patchedPostWidths -join ',') -ne '6,9,9' -or
    ($value.postBoundaryOffsets -join ',') -ne '0,5,7,9') {
    throw 'The native listener fixture returned an invalid result.'
}
$jsonLines[0] | Set-Content -LiteralPath (Join-Path $nativeOutput 'fixture-result.json') -Encoding utf8
$fileChecks = @($inputPaths | ForEach-Object { @{path=$_;beforeSha256=$beforeHashes[$_];afterSha256=(Hash $_)} })
$unchanged = @($fileChecks | Where-Object { $_.beforeSha256 -ne $_.afterSha256 }).Count -eq 0
@{schemaVersion=1;kind='native-listener-context-runner';exitCode=$exitCode;files=$fileChecks;allMatched=$unchanged;
    gameRuntimeVerified=$false;gameCodeExecuted=$false;installed=$false} |
    ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $nativeOutput 'fixture-manifest.json') -Encoding utf8
if (-not $unchanged) { throw 'A listener fixture input changed on disk during execution.' }
Write-Output "Native listener fixture: $nativeOutput"
