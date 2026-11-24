#requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$HelperPath,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string]$VcVars = ''
)
. (Join-Path $PSScriptRoot 'Toolchain.Common.ps1')
if (-not $VcVars) { $VcVars = Get-RizVcVarsPath }

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$sourceRoot = Join-Path $repoRoot 'native/RizToolbox.JudgeContext'
$source = Join-Path $sourceRoot 'fixture/CoroutineConfigurationFixture.cpp'
$header = Join-Path $sourceRoot 'CoroutineContext.h'
$output = [IO.Path]::GetFullPath($OutputDirectory)
$helper = [IO.Path]::GetFullPath($HelperPath)
$vcvarsPath = [IO.Path]::GetFullPath($VcVars)
foreach ($path in @($output, $helper, $vcvarsPath, $source, $header, $PSCommandPath)) {
    if ($path -match '[\r\n"%&|<>^!]') { throw 'Native fixture paths contain unsupported cmd metacharacters.' }
}
foreach ($path in @($helper, $vcvarsPath, $source, $header)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Required existing native fixture input is missing: $path" }
}
if (Test-Path -LiteralPath $output) { throw 'Preserve prior native fixture evidence; use a fresh output directory.' }
New-Item -ItemType Directory -Path $output | Out-Null
function Hash([string]$Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }
function WriteJson([string]$Path, $Value) {
    [IO.File]::WriteAllText($Path, ($Value | ConvertTo-Json -Depth 20), [Text.UTF8Encoding]::new($false))
}
$locks = [Collections.Generic.List[IDisposable]]::new()
$evidence = [ordered]@{
    schemaVersion = 1; kind = 'native-coroutine-configuration-test'; passed = $false
    buildExitCode = $null; vcvarsExitCode = $null; compileExitCode = $null; linkExitCode = $null
    runExitCode = $null; exitCode = $null; timedOut = $false; helperPath = $helper; helperHash = $null
    fixtureHash = $null; fixture = $null; inputHashesUnchanged = $false
    sources = @(); inputs = @(); artifacts = @(); failure = $null
    gameCodeExecuted = $false; gameRuntimeVerified = $false; productionFirstWriteAdmission = $false; installed = $false
}
try {
    foreach ($path in @($source, $header, $PSCommandPath)) {
        $locks.Add([IO.File]::Open($path, 'Open', 'Read', 'Read'))
        $evidence.sources += @{ path = $path; sha256 = Hash $path; bytes = (Get-Item -LiteralPath $path).Length }
    }
    foreach ($path in @($helper, $vcvarsPath)) {
        $locks.Add([IO.File]::Open($path, 'Open', 'Read', 'Read'))
        $evidence.inputs += @{ path = $path; sha256 = Hash $path; bytes = (Get-Item -LiteralPath $path).Length }
    }
    $evidence.helperHash = Hash $helper
    $fixture = Join-Path $output 'CoroutineConfiguration.Fixture.exe'
    $object = Join-Path $output 'CoroutineConfigurationFixture.obj'
    $batch = Join-Path $output 'build-coroutine-configuration.cmd'
    $commands = @(
        '@echo off',
        'setlocal DisableDelayedExpansion',
        "call `"$vcvarsPath`" >nul",
        'set "RIZ_COROUTINE_VCVARS_EXIT=%errorlevel%"',
        "> `"$output/vcvars-exit.txt`" echo %RIZ_COROUTINE_VCVARS_EXIT%",
        'if not "%RIZ_COROUTINE_VCVARS_EXIT%"=="0" exit /b %RIZ_COROUTINE_VCVARS_EXIT%',
        "cl /nologo /c /std:c++17 /O2 /W4 /WX /EHa /MD /Zi /guard:cf /Fo`"$object`" /Fd`"$output/CoroutineConfigurationFixture.compiler.pdb`" `"$source`"",
        'set "RIZ_COROUTINE_COMPILE_EXIT=%errorlevel%"',
        "> `"$output/compile-exit.txt`" echo %RIZ_COROUTINE_COMPILE_EXIT%",
        'if not "%RIZ_COROUTINE_COMPILE_EXIT%"=="0" exit /b %RIZ_COROUTINE_COMPILE_EXIT%',
        "link /nologo /WX `"$object`" kernel32.lib /OUT:`"$fixture`" /PDB:`"$output/CoroutineConfiguration.Fixture.pdb`" /DEBUG:FULL /INCREMENTAL:NO /OPT:NOICF /DYNAMICBASE /NXCOMPAT /CETCOMPAT /GUARD:CF /SUBSYSTEM:CONSOLE /MACHINE:X64",
        'set "RIZ_COROUTINE_LINK_EXIT=%errorlevel%"',
        "> `"$output/link-exit.txt`" echo %RIZ_COROUTINE_LINK_EXIT%",
        'exit /b %RIZ_COROUTINE_LINK_EXIT%'
    )
    [IO.File]::WriteAllLines($batch, $commands, [Text.UTF8Encoding]::new($false))
    # The serial parent runner owns process priority and its hidden build window.
    & $env:ComSpec /d /c $batch 2>&1 | Tee-Object -FilePath (Join-Path $output 'build.log')
    $evidence.buildExitCode = $LASTEXITCODE
    foreach ($stage in @('vcvars', 'compile', 'link')) {
        $stagePath = Join-Path $output "$stage-exit.txt"
        if (Test-Path -LiteralPath $stagePath) { $evidence["${stage}ExitCode"] = [int](Get-Content -LiteralPath $stagePath -Raw) }
    }
    if ($evidence.buildExitCode -ne 0 -or $evidence.vcvarsExitCode -ne 0 -or
        $evidence.compileExitCode -ne 0 -or $evidence.linkExitCode -ne 0) {
        throw "The owned native fixture build did not naturally exit zero (build $($evidence.buildExitCode))."
    }
    $evidence.fixtureHash = Hash $fixture
    $start = [Diagnostics.ProcessStartInfo]::new($fixture)
    $start.UseShellExecute = $false; $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true; $start.RedirectStandardError = $true
    $start.WorkingDirectory = $output; $start.ArgumentList.Add($helper)
    $child = [Diagnostics.Process]::new(); $child.StartInfo = $start
    try {
        if (-not $child.Start()) { throw 'Could not start the owned coroutine configuration fixture.' }
        $stdout = $child.StandardOutput.ReadToEndAsync(); $stderr = $child.StandardError.ReadToEndAsync()
        if (-not $child.WaitForExit(60000)) {
            $evidence.timedOut = $true; $child.Kill($true); $child.WaitForExit()
        }
        $evidence.runExitCode = $child.ExitCode; $evidence.exitCode = $child.ExitCode
        $outText = $stdout.GetAwaiter().GetResult(); $errorText = $stderr.GetAwaiter().GetResult()
    } finally { $child.Dispose() }
    [IO.File]::WriteAllText((Join-Path $output 'fixture-stdout.log'), $outText, [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText((Join-Path $output 'fixture-stderr.log'), $errorText, [Text.UTF8Encoding]::new($false))
    $lines = @($outText -split '\r?\n' | Where-Object { $_.StartsWith('{') })
    if ($lines.Count -eq 1) {
        $evidence.fixture = $lines[0] | ConvertFrom-Json -AsHashtable
        [IO.File]::WriteAllText((Join-Path $output 'fixture-result.json'), $lines[0] + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
    }
    if ($evidence.timedOut -or $evidence.runExitCode -ne 0 -or $null -eq $evidence.fixture) {
        throw "The owned native fixture did not naturally exit zero (run $($evidence.runExitCode))."
    }
    $value = $evidence.fixture
    if ($value.kind -cne 'native-coroutine-configuration-fixture' -or $value.passed -ne $true -or $value.failures -ne 0 -or $value.checks -le 0 -or
        $value.configurationBytes -ne 72 -or $value.configBytes -ne 40 -or $value.frameBytes -ne 48 -or $value.slotsPerKind -ne 32 -or
        $value.configuredKinds -ne 3 -or $value.readbackMatches -ne 3 -or $value.duplicateConfigureRejected -ne 3 -or
        $value.normalEntryCalls -ne 3 -or $value.originalExceptionCases -ne 1 -or $value.localMalformedFrameGuardCases -ne 4 -or
        $value.originalCalls -ne 4 -or $value.entered -ne 4 -or $value.exited -ne 4 -or $value.inFlight -ne 0 -or $value.replacementCalls -ne 0 -or
        $value.gameModulesAbsent -ne $true -or $value.gameCodeExecuted -ne $false -or $value.gameRuntimeVerified -ne $false -or
        $value.productionFirstWriteAdmission -ne $false -or $value.installed -ne $false -or @($value.roles).Count -ne 3) {
        throw 'The actual native coroutine configuration result is incomplete.'
    }
    for ($index = 0; $index -lt 3; $index++) {
        $role = $value.roles[$index]; $expectedCalls = if ($index -eq 1) { 2 } else { 1 }
        if ($role.kind -ne $index -or $role.slot -ne @(5, 9, 13)[$index] -or $role.beforeStatus -ne 2 -or $role.configureStatus -ne 0 -or
            $role.readStatus -ne 0 -or $role.duplicateConfigureStatus -ne 2 -or $role.duplicateReadStatus -ne 0 -or
            $role.configured -ne 1 -or $role.readbackKind -ne $index -or $role.readbackSlot -ne $role.slot -or $role.reserved -ne 0 -or
            $role.configAbiVersion -ne 1 -or $role.configByteSize -ne 40 -or $role.originalCalls -ne $expectedCalls -or
            $role.normalCalls -ne 1 -or $role.exceptionCalls -ne ($expectedCalls - 1) -or $role.entered -ne $expectedCalls -or
            $role.exited -ne $expectedCalls -or $role.inFlight -ne 0 -or $role.enterFaults -ne 0 -or $role.exitFaults -ne 0) {
            throw "The actual native role $index configuration or exactly-once accounting is incomplete."
        }
    }
    foreach ($row in $evidence.sources + $evidence.inputs) {
        if ((Hash $row.path) -cne $row.sha256) { throw 'A declared native fixture input changed during build or execution.' }
    }
    $evidence.inputHashesUnchanged = $true; $evidence.passed = $true
} catch { $evidence.failure = $_.Exception.Message; throw }
finally {
    foreach ($held in $locks) { $held.Dispose() }
    $evidence.artifacts = @(Get-ChildItem -LiteralPath $output -File | Where-Object Name -notin @('result.json', 'manifest.json') | ForEach-Object {
        @{ path = $_.FullName; sha256 = Hash $_.FullName; bytes = $_.Length }
    })
    WriteJson (Join-Path $output 'result.json') $evidence
    WriteJson (Join-Path $output 'manifest.json') ([ordered]@{
        schemaVersion = 1; kind = 'native-coroutine-configuration-manifest'; passed = $evidence.passed
        helperHash = $evidence.helperHash; fixtureHash = $evidence.fixtureHash; sources = $evidence.sources; inputs = $evidence.inputs
        artifacts = $evidence.artifacts; inputHashesUnchanged = $evidence.inputHashesUnchanged
        vcvarsExitCode = $evidence.vcvarsExitCode; compileExitCode = $evidence.compileExitCode; linkExitCode = $evidence.linkExitCode
        buildExitCode = $evidence.buildExitCode; runExitCode = $evidence.runExitCode; exitCode = $evidence.exitCode
        gameCodeExecuted = $false; productionFirstWriteAdmission = $false; installed = $false; failure = $evidence.failure
    })
}
Write-Output "Owned native coroutine configuration evidence: $output"
