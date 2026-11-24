#requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string]$UnityPath='D:/SteamLibrary/steamapps/common/Rizline/UnityPlayer.dll',
    [string]$VcVars = '',
    [ValidateRange(1,60)][int]$TimeoutSeconds=60
)
. (Join-Path $PSScriptRoot 'Toolchain.Common.ps1')
if (-not $VcVars) { $VcVars = Get-RizVcVarsPath }

$ErrorActionPreference='Stop'
$PSNativeCommandUseErrorActionPreference=$false
$repo=Split-Path -Parent $PSScriptRoot
function Absolute([string]$Path) {
    if (-not [IO.Path]::IsPathFullyQualified($Path)) { throw "Absolute path required: $Path" }
    $full=[IO.Path]::GetFullPath($Path)
    if ($full -match '[\r\n"%&|<>^]') { throw 'Unsupported command path characters.' }
    $full
}
function Hash([string]$Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }
function WriteJson([object]$Value,[string]$Path) {
    [IO.File]::WriteAllText($Path,($Value | ConvertTo-Json -Depth 24),[Text.UTF8Encoding]::new($false))
}
$output=Absolute $OutputDirectory;$unity=Absolute $UnityPath;$vcvarsPath=Absolute $VcVars
if (Test-Path -LiteralPath $output) { throw 'Use a fresh nonexistent output directory.' }
$cpp=Join-Path $repo 'native/RizToolbox.EngineTransportRelayFixture/RelayPreparation.cpp'
$relaysCpp=Join-Path $repo 'native/RizToolbox.EngineBootstrap/EngineTransportRelays.cpp'
$relaysHeader=Join-Path $repo 'native/RizToolbox.EngineBootstrap/EngineTransportRelays.h'
$factsHeader=Join-Path $repo 'native/RizToolbox.EngineBootstrap/EngineTransportGameProfile.h'
$sourcePaths=@($cpp,$relaysCpp,$relaysHeader,$factsHeader,$PSCommandPath)
foreach ($path in $sourcePaths+@($unity,$vcvarsPath)) {
    $null=Absolute $path
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing fixture input: $path" }
}
$manifest=[ordered]@{
    schemaVersion=1;kind='engine-transport-relay-preparation-checks';passed=$false
    startedUtc=[DateTime]::UtcNow.ToString('o');completedUtc=$null
    buildExitCode=$null;buildSucceeded=$false;sourceHashesUnchanged=$false;gameDiskHashUnchanged=$false
    started=$false;pid=$null;exitCode=$null;timedOut=$false;killed=$false
    child=$null;checks=0;childChecks=0;issues=@();sources=@();sourceAfter=@();files=@();inputArchive=$null
    fixtureExecuted=$false;relayCodeExecuted=$false;gameExecuted=$false;installerExecuted=$false
    productionAdmissionVerified=$false;sourcePatched=$false
    mappedSourceUnchanged=$false;mappedImageNeverExecutable=$false
    outputDirectory=$output;compilerEnvironment=$vcvarsPath;timeoutSeconds=$TimeoutSeconds
    unityDisk=[ordered]@{path=$unity;sha256=$null;expectedSha256=$null;bytes=$null;readOnly=$true}
}
function Require([bool]$Condition,[string]$Reason) {
    $script:manifest.checks++
    if (-not $Condition) { throw $Reason }
}
$null=New-Item -ItemType Directory -Path $output
$resultPath=Join-Path $output 'test-result.json'
$locks=[Collections.Generic.List[IDisposable]]::new()
$childProcess=[Diagnostics.Process]::new()
$stdoutFile=$null;$stderrFile=$null;$stdoutTask=$null;$stderrTask=$null
$timer=[Diagnostics.Stopwatch]::StartNew()
try {
    # These five sources and the original game file stay read-locked through
    # archive, strict build, the owned child and the final hash comparison.
    foreach ($path in $sourcePaths+@($unity)) { $locks.Add([IO.File]::Open($path,'Open','Read','Read')) }
    $manifest.sources=@($sourcePaths | ForEach-Object {
        [ordered]@{path=$_;sha256=Hash $_;bytes=(Get-Item -LiteralPath $_).Length}
    })
    $factMatches=[regex]::Matches([IO.File]::ReadAllText($factsHeader),'(?m)^inline constexpr char UnitySha256\[\]\s*=\s*"([a-fA-F0-9]{64})";')
    Require ($factMatches.Count -eq 1) 'Fixed facts must contain exactly one actual Unity disk SHA256.'
    $manifest.unityDisk.expectedSha256=$factMatches[0].Groups[1].Value.ToLowerInvariant()
    $manifest.unityDisk.sha256=Hash $unity;$manifest.unityDisk.bytes=(Get-Item -LiteralPath $unity).Length
    Require ($manifest.unityDisk.sha256 -eq $manifest.unityDisk.expectedSha256) 'Game disk SHA differs from the generated fixed facts.'
    WriteJson (@($manifest.sources)+@($manifest.unityDisk)) (Join-Path $output 'source-before.json')
    $archive=Join-Path $output 'relay-preparation-sources.zip'
    $zip=[IO.Compression.ZipFile]::Open($archive,[IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($row in $manifest.sources) {
            $relative=[IO.Path]::GetRelativePath($repo,$row.path).Replace('\','/')
            if ($relative.StartsWith('../') -or [IO.Path]::IsPathRooted($relative)) { throw 'Fixture source archive must stay within its repository.' }
            $entry=$zip.CreateEntry($relative,[IO.Compression.CompressionLevel]::Optimal)
            $destination=$entry.Open();$source=[IO.File]::OpenRead($row.path)
            try { $source.CopyTo($destination) } finally { $source.Dispose();$destination.Dispose() }
            Require ((Hash $row.path) -eq $row.sha256) "Source changed while archiving: $($row.path)"
        }
    } finally { $zip.Dispose() }
    $manifest.inputArchive=[ordered]@{path=$archive;sha256=Hash $archive;entries=$manifest.sources.Count;gameIncluded=$false}
    $fixture=Join-Path $output 'EngineTransportRelayPreparation.Fixture.exe'
    $commands=@(
        '@echo off','setlocal DisableDelayedExpansion','chcp 65001 >nul',
        "call `"$vcvarsPath`" >nul",'if errorlevel 1 exit /b 1',
        "cl /nologo /std:c++17 /c /W4 /WX /O2 /EHa /MD /Zi /guard:cf /Fo`"$output\RelayPreparation.obj`" /Fd`"$output\RelayPreparation.compiler.pdb`" `"$cpp`"",'if errorlevel 1 exit /b 1',
        "cl /nologo /std:c++17 /c /W4 /WX /O2 /EHa /MD /Zi /guard:cf /Fo`"$output\EngineTransportRelays.obj`" /Fd`"$output\EngineTransportRelays.compiler.pdb`" `"$relaysCpp`"",'if errorlevel 1 exit /b 1',
        "link /nologo /OUT:`"$fixture`" /PDB:`"$output\EngineTransportRelayPreparation.Fixture.pdb`" /MAP:`"$output\EngineTransportRelayPreparation.Fixture.map`" /DEBUG /INCREMENTAL:NO /OPT:NOICF /DYNAMICBASE /NXCOMPAT /CETCOMPAT /GUARD:CF `"$output\RelayPreparation.obj`" `"$output\EngineTransportRelays.obj`" bcrypt.lib kernel32.lib",'if errorlevel 1 exit /b 1',
        "dumpbin /nologo /headers /imports /unwindinfo /relocations `"$fixture`" > `"$output\fixture.pe.txt`"",'if errorlevel 1 exit /b 1',
        'exit /b 0'
    )
    $commandPath=Join-Path $output 'build-relay-preparation.cmd'
    [IO.File]::WriteAllLines($commandPath,$commands,[Text.UTF8Encoding]::new($false))
    & $env:ComSpec /d /v:off /c $commandPath 2>&1 | Tee-Object -FilePath (Join-Path $output 'build.log')
    $buildExit=$LASTEXITCODE;$manifest.buildExitCode=$buildExit
    Require ($buildExit -eq 0) "Strict relay preparation build failed: $buildExit"
    $manifest.buildSucceeded=$true
    $locks.Add([IO.File]::Open($fixture,'Open','Read','Read'))
    $manifest.executable=[ordered]@{path=$fixture;sha256=Hash $fixture;bytes=(Get-Item -LiteralPath $fixture).Length}
    $childPath=Join-Path $output 'child-result.json'
    $stdoutPath=Join-Path $output 'stdout.log';$stderrPath=Join-Path $output 'stderr.log'
    $manifest.arguments=@('--all',$unity,$childPath)
    WriteJson $manifest $resultPath
    $stdoutFile=[IO.File]::Open($stdoutPath,'CreateNew','Write','ReadWrite')
    $stderrFile=[IO.File]::Open($stderrPath,'CreateNew','Write','ReadWrite')
    $start=[Diagnostics.ProcessStartInfo]::new()
    $start.FileName=$fixture;$start.WorkingDirectory=$output;$start.UseShellExecute=$false;$start.CreateNoWindow=$true
    $start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true
    foreach ($argument in $manifest.arguments) { $start.ArgumentList.Add($argument) }
    $childProcess.StartInfo=$start
    Require ($childProcess.Start()) 'Owned relay preparation fixture failed to start.'
    $manifest.started=$true;$manifest.fixtureExecuted=$true;$manifest.pid=$childProcess.Id
    $stdoutTask=$childProcess.StandardOutput.BaseStream.CopyToAsync($stdoutFile)
    $stderrTask=$childProcess.StandardError.BaseStream.CopyToAsync($stderrFile)
    if (-not $childProcess.WaitForExit($TimeoutSeconds*1000)) {
        $manifest.timedOut=$true;$childProcess.Kill();$manifest.killed=$true
        Require ($childProcess.WaitForExit(10000)) 'Created child did not acknowledge termination.'
    }
    $manifest.exitCode=$childProcess.ExitCode
    $null=$stdoutTask.GetAwaiter().GetResult();$null=$stderrTask.GetAwaiter().GetResult()
    $stdoutFile.Dispose();$stdoutFile=$null;$stderrFile.Dispose();$stderrFile=$null
    Require (-not $manifest.timedOut -and $manifest.exitCode -eq 0) 'Actual owned preparation fixture failed or timed out.'
    Require (Test-Path -LiteralPath $childPath -PathType Leaf) 'Actual owned child JSON is missing.'
    $childText=[IO.File]::ReadAllText($childPath)
    Require ([IO.File]::ReadAllText($stdoutPath).Trim() -ceq $childText.Trim()) 'Actual child stdout and persisted JSON differ.'
    $child=$childText | ConvertFrom-Json
    Require ($child.schemaVersion -eq 1 -and $child.kind -eq 'engine-transport-relay-preparation' -and
        $child.passed -eq $true -and $child.failures -eq 0 -and $child.checks -gt 0 -and $child.pid -eq $manifest.pid) 'Child success/schema/PID is not the actual invoked fixture.'
    Require ($child.fixedDiskSha256 -ceq $manifest.unityDisk.expectedSha256 -and $child.fatal -ceq '') 'Actual child did not finish against the same frozen game disk hash.'
    foreach ($name in @('fixtureExecuted','mappedSourceUnchanged','mappedImageNeverExecutable')) {
        Require ($child.$name -eq $true) "Actual child proof is missing: $name"
    }
    foreach ($name in @('relayCodeExecuted','gameExecuted','installerExecuted','productionAdmissionVerified','sourcePatched')) {
        Require ($child.$name -eq $false) "Preparation-only boundary is missing: $name"
    }
    Require ($child.actualKernelEntryVerified -eq $false -and $child.functionTableRegistered -eq $false -and
        $child.ownedEntryLiteralsOnly -eq $true) 'Prepared owned literals must not claim actual Kernel identity or registered execution.'
    Require ($child.normal.passed -eq $true -and $child.normal.siteCount -eq 23 -and $child.normal.windowCount -eq 36 -and
        $child.normal.tableCount -eq 23 -and $child.normal.usedBytes -eq 6400 -and $child.normal.nearBytesWritten -eq 6400) 'Actual normal plan does not contain the full fixed 23/36 relay preparation.'
    $caseNames=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    Require (@($child.cases).Count -eq 14) 'Exactly fourteen real preparation cases are required.'
    foreach ($case in $child.cases) {
        Require (-not [string]::IsNullOrWhiteSpace($case.name) -and $caseNames.Add($case.name) -and $case.passed -eq $true -and
            $case.checks -gt 0 -and $case.result -eq $case.expected -and $case.sourceUnchanged -eq $true) 'An actual preparation case failed or has a missing/duplicate identity.'
    }
    Require ($caseNames.Contains('normal')) 'The cases must include the actual normal preparation.'
    Require ((Hash $fixture) -eq $manifest.executable.sha256) 'Owned executable changed during its actual run.'
    $manifest.child=$child;$manifest.childChecks=$child.checks
    $manifest.mappedSourceUnchanged=$true;$manifest.mappedImageNeverExecutable=$true;$manifest.passed=$true
} catch {
    $manifest.passed=$false;$manifest.issues+=$_.Exception.Message
    if ($manifest.started) {
        try {
            if (-not $childProcess.HasExited) {
                $childProcess.Kill();$manifest.killed=$true
                $null=$childProcess.WaitForExit(10000)
            }
        } catch { $manifest.issues+="Created child cleanup: $($_.Exception.Message)" }
    }
} finally {
    foreach ($item in @(@{task=$stdoutTask;file=$stdoutFile},@{task=$stderrTask;file=$stderrFile})) {
        try {
            if ($null -ne $item.task) {
                if (-not $item.task.Wait(10000)) { throw 'Created child output stream did not close within cleanup timeout.' }
                $null=$item.task.GetAwaiter().GetResult()
            }
        }
        catch { $manifest.issues+=$_.Exception.Message;$manifest.passed=$false }
        if ($null -ne $item.file) { $item.file.Dispose() }
    }
    try {
        $manifest.sourceAfter=@($sourcePaths | ForEach-Object { [ordered]@{path=$_;sha256=Hash $_;bytes=(Get-Item -LiteralPath $_).Length} })
        WriteJson $manifest.sourceAfter (Join-Path $output 'source-after.json')
        foreach ($row in $manifest.sources) {
            if ((Hash $row.path) -ne $row.sha256) { throw "Source changed: $($row.path)" }
        }
        $manifest.sourceHashesUnchanged=$manifest.sources.Count -eq 5
        $manifest.unityDiskAfter=[ordered]@{path=$unity;sha256=Hash $unity;bytes=(Get-Item -LiteralPath $unity).Length}
        WriteJson $manifest.unityDiskAfter (Join-Path $output 'unity-disk-after.json')
        $manifest.gameDiskHashUnchanged=$manifest.unityDiskAfter.sha256 -eq $manifest.unityDisk.sha256 -and
            $manifest.unityDiskAfter.sha256 -eq $manifest.unityDisk.expectedSha256 -and
            $manifest.unityDiskAfter.bytes -eq $manifest.unityDisk.bytes
        if (-not $manifest.sourceHashesUnchanged -or -not $manifest.gameDiskHashUnchanged) { throw 'Source/game disk integrity is incomplete.' }
    } catch { $manifest.passed=$false;$manifest.issues+=$_.Exception.Message }
    foreach ($item in $locks) { $item.Dispose() }
    $childProcess.Dispose();$timer.Stop();$manifest.elapsedMilliseconds=$timer.ElapsedMilliseconds
    $manifest.files=@(Get-ChildItem -LiteralPath $output -File | Where-Object Name -ne 'test-result.json' |
        ForEach-Object { [ordered]@{path=$_.FullName;sha256=Hash $_.FullName;bytes=$_.Length} })
    $manifest.completedUtc=[DateTime]::UtcNow.ToString('o');WriteJson $manifest $resultPath
}
if (-not $manifest.passed) { throw "Owned relay preparation failed; evidence retained: $resultPath" }
Write-Output "Relay preparation: PID=$($manifest.pid), childChecks=$($manifest.childChecks), parentChecks=$($manifest.checks); $resultPath"
