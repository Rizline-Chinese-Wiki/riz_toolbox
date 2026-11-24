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
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repoRoot 'artifacts\doorstop-init-native' }
$nativeOutput = [IO.Path]::GetFullPath($OutputDirectory)
$vcvarsPath = [IO.Path]::GetFullPath($VcVars)
$source = Join-Path $repoRoot 'native\RizToolbox.Doorstop\riz_init_call.c'
$header = Join-Path $repoRoot 'native\RizToolbox.Doorstop\riz_init_call.h'
$fixtureSource = Join-Path $repoRoot 'native\RizToolbox.Doorstop\fixture\InitCallFixture.cpp'
foreach ($path in @($source, $fixtureSource, $nativeOutput, $vcvarsPath)) {
    if ($path -match '[\r\n"%&|<>^]') { throw 'Build paths contain unsupported cmd metacharacters.' }
}
foreach ($path in @($source, $header, $fixtureSource, $vcvarsPath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Required production source/build tool missing: $path" }
}
New-Item -ItemType Directory -Path $nativeOutput -Force | Out-Null
$fixture = Join-Path $nativeOutput 'Doorstop.InitCall.Fixture.exe'
$cmdPath = Join-Path $nativeOutput 'build-doorstop-init-fixture.cmd'
$commands = @(
    '@echo off', "call `"$vcvarsPath`" >nul", 'if errorlevel 1 exit /b 1',
    "cl /nologo /c /std:c11 /Zc:preprocessor- /O2 /W4 /WX /GS- /Ob2 /MD /GL- /Zi /guard:cf /Fo`"$nativeOutput\riz_init_call.obj`" /Fd`"$nativeOutput\riz_init_call.compiler.pdb`" `"$source`"", 'if errorlevel 1 exit /b 1',
    "cl /nologo /std:c++17 /O2 /W4 /WX /EHa /MD /Zi /guard:cf /Fo`"$nativeOutput\InitCallFixture.obj`" /Fd`"$nativeOutput\InitCallFixture.compiler.pdb`" `"$fixtureSource`" `"$nativeOutput\riz_init_call.obj`" wer.lib /link /OUT:`"$fixture`" /PDB:`"$nativeOutput\Doorstop.InitCall.Fixture.pdb`" /INCREMENTAL:NO /OPT:NOICF /DYNAMICBASE /NXCOMPAT /CETCOMPAT /GUARD:CF", 'if errorlevel 1 exit /b 1',
    "dumpbin /nologo /unwindinfo `"$fixture`" > `"$nativeOutput\unwindinfo.txt`"", 'if errorlevel 1 exit /b 1'
)
[IO.File]::WriteAllLines($cmdPath, $commands, [Text.UTF8Encoding]::new($false))
& $env:ComSpec /d /c $cmdPath 2>&1 | Tee-Object -FilePath (Join-Path $nativeOutput 'build.log')
if ($LASTEXITCODE -ne 0) { throw "Production Doorstop init-call fixture build failed: $LASTEXITCODE" }
$normalModes = @('ready-zero','disabled-zero','ready-positive','disabled-positive','ready-negative','disabled-negative',
    'ready-original-seh','disabled-original-seh','ready-original-cpp','disabled-original-cpp','bootstrap-seh','bootstrap-cpp')
$fatalModes = @('recursive-original','recursive-bootstrap','repeat','invalid-ready')
$results = foreach ($mode in @($normalModes + $fatalModes)) {
    $stdout = Join-Path $nativeOutput "fixture-$mode.json"
    $stderr = Join-Path $nativeOutput "fixture-$mode.stderr.txt"
    $process = Start-Process -FilePath $fixture -ArgumentList @($mode) -WindowStyle Hidden -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
    if (-not $process.WaitForExit(15000)) { throw "Owned Doorstop fixture case $mode exceeded its finite timeout; process id $($process.Id)." }
    $process.WaitForExit()
    $process.Refresh()
    $code = $process.ExitCode
    $text = [IO.File]::ReadAllText($stdout)
    $errors = [IO.File]::ReadAllText($stderr)
    if ($errors.Trim()) { throw "Doorstop fixture case $mode reported failed assertions: $errors" }
    $case = $text | ConvertFrom-Json
    if ($mode -in $normalModes) {
        if ($code -ne 0 -or -not $case.passed -or $case.failures -ne 0 -or -not $case.productionSourceExecuted -or $case.actualProducerExecuted -or $case.coreClrExecuted -or $case.gameCodeExecuted -or $case.coldStartupVerified) {
            throw "Doorstop production init case $mode failed: exit=$code; $text"
        }
        if ($mode.EndsWith('-seh')) {
            $expectedCode = if ($mode -eq 'bootstrap-seh') { [Convert]::ToUInt32('E0524942', 16) } else { [Convert]::ToUInt32('E052494F', 16) }
            $expectedStage = if ($mode -eq 'bootstrap-seh') { 1 } else { 0 }
            if (-not $case.caught -or $case.returned -or $case.exceptionCode -ne $expectedCode -or
                $case.raiseExceptionSourceFlags -ne 0 -or $case.sehFirstChanceCount -ne 1 -or
                $case.sehFirstChanceCode -ne $case.exceptionCode -or $case.sehFirstChanceFlags -ne $case.sehCaughtFlags -or
                $case.sehFirstChanceParameters -ne 3 -or $case.sehCaughtParameters -ne 3 -or
                @($case.sehFirstChancePayload).Count -ne 3 -or @($case.sehCaughtPayload).Count -ne 3 -or
                ($case.sehFirstChancePayload -join ',') -ne ($case.sehCaughtPayload -join ',') -or
                $case.sehCaughtPayload[0] -ne [Convert]::ToUInt64('FEDCBA9876543210', 16) -or
                $case.sehCaughtPayload[1] -ne $case.domainPointer -or $case.sehCaughtPayload[2] -ne $expectedStage) {
                throw "Doorstop SEH record was not preserved from passive first chance to the outer filter: $mode; $text"
            }
        } elseif ($mode.EndsWith('-cpp')) {
            if (-not $case.caught -or $case.returned -or $case.exceptionCode -ne [uint32]3765269347 -or $case.sehFirstChanceCount -ne 0) {
                throw "Doorstop native C++ exception transparency failed: $mode; $text"
            }
        } elseif ($case.caught -or -not $case.returned -or $case.sehFirstChanceCount -ne 0) {
            throw "Doorstop ordinary int return unexpectedly entered an exception path: $mode; $text"
        }
        $case
    } else {
        # RaiseFailFastException(NULL,...) uses STATUS_FAIL_FAST_EXCEPTION,
        # 0xC0000602 in the pinned Windows SDK. Assert the real child exit code;
        # a printed checkpoint or an arbitrary nonzero exit is insufficient.
        $exitBits = [BitConverter]::ToUInt32([BitConverter]::GetBytes([int]$code), 0)
        $originalExpected = if ($mode -eq 'invalid-ready') { 0 } else { 1 }
        $bootstrapExpected = if ($mode -in @('recursive-bootstrap','repeat')) { 1 } else { 0 }
        if ($exitBits -ne [uint32]3221227010 -or $case.kind -ne 'doorstop-init-rejection-checkpoint' -or $case.stage -ne $mode -or $case.failures -ne 0 -or $case.originalCalls -ne $originalExpected -or $case.bootstrapCalls -ne $bootstrapExpected) {
            throw "Doorstop real fail-fast case $mode failed: exit=$exitBits; $text"
        }
        [pscustomobject]@{ mode=$mode; passed=$true; checks=$case.checks; realExitCode=$exitBits; originalCalls=$case.originalCalls; bootstrapCalls=$case.bootstrapCalls; werNoUi=$true; checkpoint=$case }
    }
}
$checks = ($results | Where-Object { $_.PSObject.Properties.Name -contains 'checks' } | Measure-Object -Property checks -Sum).Sum
[ordered]@{ schemaVersion=1; kind='production-doorstop-init-call-contract'; passed=$true; independentProcesses=@($results).Count; normalCases=$normalModes.Count; realFailFastCases=$fatalModes.Count; checks=$checks; failures=0; productionSourceExecuted=$true; actualProducerExecuted=$false; coreClrExecuted=$false; gameCodeExecuted=$false; coldStartupVerified=$false; werNoUi=$true; cases=@($results) } |
    ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $nativeOutput 'fixture-result.json') -Encoding utf8
$files = @($source, $header, $fixtureSource, $fixture) | ForEach-Object {
    [ordered]@{ path=$_; sha256=(Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant() }
}
[ordered]@{ schemaVersion=1; kind='production-doorstop-init-call-contract'; files=@($files); sourceIsActualDoorstopInput=$true; fixtureLinksSameSource=$true; coldStartupVerified=$false } |
    ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $nativeOutput 'fixture-manifest.json') -Encoding utf8
Write-Output "Production Doorstop init-call fixture: $nativeOutput"
