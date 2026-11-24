#requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$BinaryDirectory,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [Parameter(Mandatory)][string]$ExpectedManifestPath,
    [string]$Python = '',
    [ValidateRange(5,60)][int]$TimeoutSeconds = 20,
    [switch]$SkipDebugger
)
. (Join-Path $PSScriptRoot 'Toolchain.Common.ps1')
if (-not $Python) { $Python = Get-RizPythonPath }

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$verifier = Join-Path $repoRoot 'docs/validation/foundation-early-loader-20261025/verify-early-loader.py'
$binaryRoot = (Resolve-Path -LiteralPath $BinaryDirectory).Path
$expectedPath = (Resolve-Path -LiteralPath $ExpectedManifestPath).Path
$pythonPath = (Resolve-Path -LiteralPath $Python).Path
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
if (Test-Path -LiteralPath (Join-Path $outputRoot 'fixture-result.json')) { throw 'Choose a fresh evidence output directory; an earlier result is already present.' }
$utf8 = [Text.UTF8Encoding]::new($false)
function Save-Json([string]$Path, $Value) { [IO.File]::WriteAllText($Path, ($Value | ConvertTo-Json -Depth 35)+"`n", $utf8) }
function Hash-Stream([IO.FileStream]$Stream) {
    $Stream.Position = 0
    $algorithm = [Security.Cryptography.SHA256]::Create()
    try { [Convert]::ToHexString($algorithm.ComputeHash($Stream)).ToLowerInvariant() }
    finally { $algorithm.Dispose(); $Stream.Position = 0 }
}
function Invoke-Verifier([string[]]$Arguments, [string]$LogPrefix) {
    & $pythonPath $verifier @Arguments 1>($LogPrefix+'.stdout.log') 2>($LogPrefix+'.stderr.log')
    if ($LASTEXITCODE -ne 0) { throw "Independent verifier failed: $LogPrefix (exit $LASTEXITCODE)" }
}
$held = [Collections.Generic.List[object]]::new()
$results = [Collections.Generic.List[object]]::new()
$records = [Collections.Generic.List[object]]::new()
$completed = $false
$caught = $null
try {
    $expected = Get-Content -LiteralPath $expectedPath -Raw | ConvertFrom-Json -AsHashtable
    if (-not $expected.files -or $expected.files.Count -lt 6) { throw 'The completed build manifest must contain hashed fixture binaries.' }
    $required = @('RizOwnedHost.exe','RizOwnedImporter.dll','RizOwnedProxy.dll','RizOwnedRecorder.dll','RizToolbox.JudgeContext.dll','dobby.dll')
    if (-not $SkipDebugger) { $required += 'RizOwnedDebugger.exe' }
    foreach ($name in $required) {
        $manifestRows = @($expected.files | Where-Object { $_.name -eq $name })
        if ($manifestRows.Count -ne 1 -or $manifestRows[0].sha256 -notmatch '^[a-fA-F0-9]{64}$') { throw "Missing or ambiguous completed-build hash for $name" }
        $path = (Resolve-Path -LiteralPath (Join-Path $binaryRoot $name)).Path
        if ([IO.Path]::GetFullPath($manifestRows[0].path) -ne [IO.Path]::GetFullPath($path)) { throw "Manifest file path differs from the exact fixture directory: $name" }
        # Hold the same handle used for hashing through every child. FileShare.Read
        # permits loader reads and prevents competing writes, renames and deletes.
        $stream = [IO.FileStream]::new($path,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
        $item = @{name=$name;path=$path;stream=$stream;sha256=(Hash-Stream $stream)}
        $held.Add($item)
        if ($item.sha256 -ne $manifestRows[0].sha256.ToLowerInvariant()) { throw "Input hash differs from completed build manifest: $name" }
        if ($name -eq 'dobby.dll' -and $item.sha256 -ne '8015de7d867245a1095d13947a63763878e4cf5fd3d3089b63cc39200b055ded') { throw 'Fixture requires the inspected pinned Dobby DLL.' }
    }
    Save-Json (Join-Path $outputRoot 'held-inputs.json') @{
        kind='owned-early-loader-parent-input-lock';files=@($held | ForEach-Object { @{name=$_.name;path=$_.path;sha256=$_.sha256;fileShare='Read';writeAndDeleteDenied=$true} });
        expectedManifestPath=$expectedPath;expectedManifestSha256=(Get-FileHash -LiteralPath $expectedPath -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    $peProof = Join-Path $outputRoot 'pe-imports.json'
    Invoke-Verifier @('--inspect-images',$binaryRoot,'--host-name','RizOwnedHost.exe','--output',$peProof) (Join-Path $outputRoot 'pe-imports')
    $selectors = [Collections.Generic.List[object]]::new()
    for ($mode=0; $mode -lt 14; ++$mode) { $selectors.Add(@{name=('mode-'+$mode);mode=$mode;stage=0;index=0;debug=$false}) }
    for ($stage=1; $stage -le 6; ++$stage) {
        for ($index=0; $index -lt 4; ++$index) { $selectors.Add(@{name="fault-$stage-$index";mode=0;stage=$stage;index=$index;debug=$false}) }
    }
    if (-not $SkipDebugger) { $selectors.Add(@{name='normal-debugger';mode=0;stage=0;index=0;debug=$true}) }
    foreach ($selector in $selectors) {
        $runRoot = Join-Path $outputRoot $selector.name
        New-Item -ItemType Directory -Path $runRoot -Force | Out-Null
        $initialPath = Join-Path $runRoot 'mapping-initial.bin'
        Invoke-Verifier @('--initialize','--pe-proof',$peProof,'--mode',[string]$selector.mode,'--fault-stage',[string]$selector.stage,
            '--fault-index',[string]$selector.index,'--output',$initialPath) (Join-Path $runRoot 'initialize')
        $initialBytes = [IO.File]::ReadAllBytes($initialPath)
        if ($initialBytes.Length -ne 427208) { throw 'Parent mapping initializer size mismatch.' }
        $mappingName = 'Local\RizOwnedEarly-'+$PID+'-'+[Guid]::NewGuid().ToString('N')
        $mapping = [IO.MemoryMappedFiles.MemoryMappedFile]::CreateNew($mappingName,427208,[IO.MemoryMappedFiles.MemoryMappedFileAccess]::ReadWrite)
        $view = $null
        $child = $null
        $record = @{name=$selector.name;mode=$selector.mode;faultStage=$selector.stage;faultIndex=$selector.index;
            debugger=[bool]$selector.debug;mappingName=$mappingName;started=$false;timedOut=$false;exitCode=$null;pid=$null;exactOwnedProcess=$true}
        $records.Add($record)
        try {
            $view = $mapping.CreateViewAccessor(0,427208,[IO.MemoryMappedFiles.MemoryMappedFileAccess]::ReadWrite)
            $view.WriteArray([long]0,$initialBytes,0,$initialBytes.Length)
            $hostPath = Join-Path $binaryRoot 'RizOwnedHost.exe'
            $executable = if ($selector.debug) { Join-Path $binaryRoot 'RizOwnedDebugger.exe' } else { $hostPath }
            $start = [Diagnostics.ProcessStartInfo]::new($executable)
            $start.WorkingDirectory = $binaryRoot
            $start.UseShellExecute = $false
            $start.CreateNoWindow = $true
            $start.RedirectStandardOutput = $true
            $start.RedirectStandardError = $true
            $start.Environment['RIZ_EARLY_MAPPING_NAME'] = $mappingName
            if ($selector.debug) { $start.ArgumentList.Add($hostPath); $start.ArgumentList.Add((Join-Path $runRoot 'debug-events.jsonl')) }
            $child = [Diagnostics.Process]::new()
            $child.StartInfo = $start
            if (-not $child.Start()) { throw 'Could not start the owned early-loader child.' }
            $record.started = $true
            $record.pid = $child.Id
            $stdout = $child.StandardOutput.ReadToEndAsync()
            $stderr = $child.StandardError.ReadToEndAsync()
            if (-not $child.WaitForExit($TimeoutSeconds*1000)) {
                $record.timedOut = $true
                # Only the Process object started above is terminated. Never find
                # or stop other processes by name, window or executable path.
                $child.Kill($false)
                $child.WaitForExit()
            }
            $record.exitCode = $child.ExitCode
            [IO.File]::WriteAllText((Join-Path $runRoot 'child.stdout.log'),$stdout.GetAwaiter().GetResult(),$utf8)
            [IO.File]::WriteAllText((Join-Path $runRoot 'child.stderr.log'),$stderr.GetAwaiter().GetResult(),$utf8)
            $raw = [byte[]]::new(427208)
            $read = $view.ReadArray([long]0,$raw,0,$raw.Length)
            if ($read -ne $raw.Length) { throw 'Could not preserve the complete owned shared mapping.' }
            $rawPath = Join-Path $runRoot 'mapping-final.bin'
            [IO.File]::WriteAllBytes($rawPath,$raw)
            Save-Json (Join-Path $runRoot 'process.json') $record
            if ($record.timedOut) { throw 'Owned early-loader child exceeded finite timeout; not a passing case.' }
            $ownedPid = if ($selector.debug) { [BitConverter]::ToInt32($raw,48) } else { $record.pid }
            $arguments = @('--mapping',$rawPath,'--initial',$initialPath,'--pe-proof',$peProof,'--child-pid',[string]$ownedPid,
                '--child-exit',[string]$record.exitCode,'--output',(Join-Path $runRoot 'result.json'))
            if ($selector.debug) { $arguments += @('--debug-events',(Join-Path $runRoot 'debug-events.jsonl')) }
            Invoke-Verifier $arguments (Join-Path $runRoot 'verify')
            $value = Get-Content -LiteralPath (Join-Path $runRoot 'result.json') -Raw | ConvertFrom-Json -AsHashtable
            if ($value.passed -ne $true -or $value.checks -le 0) { throw 'Independent owned event verifier returned an incomplete result.' }
            $results.Add($value)
            Write-Output "Early loader case $($selector.name): $($value.checks) exact log assertions passed."
        } finally {
            if ($child) { $child.Dispose() }
            if ($view) { $view.Dispose() }
            $mapping.Dispose()
            Save-Json (Join-Path $runRoot 'process.json') $record
        }
    }
    $completed = $true
} catch { $caught = $_; throw }
finally {
    $files = @($held | ForEach-Object { @{name=$_.name;path=$_.path;beforeSha256=$_.sha256;afterSha256=(Hash-Stream $_.stream)} })
    $unchanged = @($files | Where-Object { $_.beforeSha256 -ne $_.afterSha256 }).Count -eq 0
    foreach ($item in $held) { $item.stream.Dispose() }
    $sourcePaths = @($PSCommandPath,$verifier,(Join-Path $repoRoot 'native/RizToolbox.EarlyLoaderFixture/EarlyLoaderShared.h'))
    $sources = @($sourcePaths | ForEach-Object { @{path=$_;sha256=(Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant()} })
    $aggregate = @{schemaVersion=1;kind='owned-early-loader-parent-evidence';passed=($completed -and $unchanged);checks=($results | Measure-Object -Property checks -Sum).Sum;
        processes=@($records);caseResults=@($results);independentProcesses=$records.Count;verifiedCases=$results.Count;files=$files;allInputHashesUnchanged=$unchanged;sources=$sources;
        expectedCases=$(if($SkipDebugger){38}else{39});debuggerSupplementIncluded=(-not $SkipDebugger);rootSoleBuilder=$true;sharedProjectsRebuiltByRunner=$false;
        realSystemWinHttpVerified=$false;gameFirstInstallVerified=$false;productionV4Issued=$false;allTransitiveLoaderDependenciesSafeVerified=$false;
        schedulerClosureVerified=$false;engineCancellationCompleted=$false;installed=$false;gameCodeExecuted=$false}
    if ($caught) { $aggregate.error = $caught.ToString() }
    Save-Json (Join-Path $outputRoot 'fixture-result.json') $aggregate
    if ($completed -and -not $unchanged) { throw 'An owned fixture input changed while held read-only.' }
}
