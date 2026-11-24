#requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)][Alias('KernelPath')][string]$HelperPath,
    [Parameter(Mandatory)][string]$FixturePath,
    [Parameter(Mandatory)][string]$ManagedFixturePath,
    [Parameter(Mandatory)][string]$ManagedAssemblyPath,
    [Parameter(Mandatory)][string]$DobbyPath,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string]$EntryFixturePath,
    [string]$ExpectedManifestPath,
    [string]$Python = ''
)
. (Join-Path $PSScriptRoot 'Toolchain.Common.ps1')
if (-not $Python) { $Python = Get-RizPythonPath }

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$verifier = Join-Path $repo 'docs/validation/foundation-engine-kernel-20261025/verify-engine-kernel.py'
$nativeRunner = Join-Path $PSScriptRoot 'Test-EngineNodeNative.ps1'
$managedRunner = Join-Path $PSScriptRoot 'Test-EngineNodeManaged.ps1'
if (-not [IO.Path]::IsPathFullyQualified($HelperPath)) { throw 'Use the absolute real Kernel DLL path.' }
$kernel = (Resolve-Path -LiteralPath $HelperPath).Path
if ([IO.Path]::GetFileName($kernel) -ne 'RizToolbox.EngineNodeKernel.dll') { throw 'Use the real Kernel filename without renaming a legacy helper.' }
if (-not $ExpectedManifestPath) { $ExpectedManifestPath = Join-Path (Split-Path -Parent $kernel) 'kernel-manifest.json' }
$manifestPath = (Resolve-Path -LiteralPath $ExpectedManifestPath).Path
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json -AsHashtable
$kernelRows = @($manifest.files | Where-Object { $_.role -eq 'kernel' })
$entryRows = @($manifest.files | Where-Object { $_.role -eq 'entry-fixture' })
if ($kernelRows.Count -ne 1 -or $entryRows.Count -ne 1 -or $manifest.files.Count -ne 2) { throw 'Use a completed kernel build manifest with one Kernel and one entry fixture.' }
if (-not $EntryFixturePath) { $EntryFixturePath = $entryRows[0].path }
$entryFixture = (Resolve-Path -LiteralPath $EntryFixturePath).Path
$nativeFixture = (Resolve-Path -LiteralPath $FixturePath).Path
$managedFixture = (Resolve-Path -LiteralPath $ManagedFixturePath).Path
$managed = (Resolve-Path -LiteralPath $ManagedAssemblyPath).Path
$dobby = (Resolve-Path -LiteralPath $DobbyPath).Path
$pythonPath = (Resolve-Path -LiteralPath $Python).Path
$output = [IO.Path]::GetFullPath($OutputDirectory)
if ((Test-Path -LiteralPath $output) -and @(Get-ChildItem -LiteralPath $output -Force).Count) { throw 'Choose a fresh evidence output directory.' }
New-Item -ItemType Directory -Path $output -Force | Out-Null
$utf8 = [Text.UTF8Encoding]::new($false)
$held = [Collections.Generic.Dictionary[string,object]]::new([StringComparer]::OrdinalIgnoreCase)
$processes = [Collections.Generic.List[object]]::new()
$completed = $false
$errorRecord = $null
$nativeResult = $null
$managedResult = $null
$entryProof = $null
function Save-Json([string]$Path, $Value) { [IO.File]::WriteAllText($Path,($Value | ConvertTo-Json -Depth 35)+"`n",$utf8) }
function Hash-Stream([IO.FileStream]$Stream) {
    $Stream.Position = 0
    $algorithm = [Security.Cryptography.SHA256]::Create()
    try { [Convert]::ToHexString($algorithm.ComputeHash($Stream)).ToLowerInvariant() }
    finally { $algorithm.Dispose(); $Stream.Position = 0 }
}
function Hold-Input([string]$Path, [string]$Role, [string]$ExpectedSha = '') {
    $resolved = (Resolve-Path -LiteralPath $Path).Path
    if (-not $held.ContainsKey($resolved)) {
        $stream = [IO.FileStream]::new($resolved,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
        $held.Add($resolved,@{path=$resolved;role=$Role;stream=$stream;beforeSha256=(Hash-Stream $stream)})
    }
    if ($ExpectedSha -and ($ExpectedSha -notmatch '^[a-fA-F0-9]{64}$' -or $held[$resolved].beforeSha256 -ne $ExpectedSha.ToLowerInvariant())) {
        throw "Actual held input differs from completed build manifest: $resolved"
    }
}
function Invoke-Verifier([string[]]$Arguments, [string]$Prefix) {
    & $pythonPath $verifier @Arguments 1>($Prefix+'.stdout.log') 2>($Prefix+'.stderr.log')
    if ($LASTEXITCODE -ne 0) { throw "Independent Kernel PE/entry verifier failed: $Prefix (exit $LASTEXITCODE)" }
}
try {
    Hold-Input $manifestPath 'completed-build-manifest'
    foreach ($pair in @(@{row=$kernelRows[0];actual=$kernel},@{row=$entryRows[0];actual=$entryFixture})) {
        if (-not [IO.Path]::IsPathFullyQualified($pair.row.path) -or
            [IO.Path]::GetFullPath($pair.row.path) -ne $pair.actual -or $pair.row.name -ne [IO.Path]::GetFileName($pair.actual) -or
            $pair.row.sha256 -notmatch '^[a-fA-F0-9]{64}$') {
            throw 'Completed manifest absolute path/name differs from the actual Kernel or entry fixture.'
        }
        Hold-Input $pair.actual $pair.row.role $pair.row.sha256
    }
    foreach ($source in $manifest.sources) {
        if (-not [IO.Path]::IsPathFullyQualified($source.path) -or $source.sha256 -notmatch '^[a-fA-F0-9]{64}$') { throw 'Kernel manifest source identities must be absolute with a completed SHA256.' }
        Hold-Input $source.path 'declared-kernel-build-source' $source.sha256
    }
    foreach ($item in @(@{path=$nativeFixture;role='native-regression-fixture'},@{path=$managedFixture;role='native-managed-regression-fixture'},
            @{path=$dobby;role='fixture-only-dobby'},@{path=$pythonPath;role='offline-python-host'},
            @{path=$nativeRunner;role='existing-native-runner'},@{path=$managedRunner;role='existing-managed-runner'},
            @{path=$verifier;role='independent-pe-verifier'},@{path=$PSCommandPath;role='kernel-parent-runner'})) {
        Hold-Input $item.path $item.role
    }
    # Hold the complete supplied managed output tree, not only Runtime.Tests.dll.
    # The system .NET runtime and Windows remain external prerequisites.
    foreach ($file in Get-ChildItem -LiteralPath (Split-Path -Parent $managed) -File -Recurse) {
        Hold-Input $file.FullName 'supplied-managed-output-file'
    }
    if ($held[$dobby].beforeSha256 -ne '8015de7d867245a1095d13947a63763878e4cf5fd3d3089b63cc39200b055ded') { throw 'Use the inspected fixture-only Dobby image.' }
    Save-Json (Join-Path $output 'held-inputs.json') @{
        files=@($held.Values | ForEach-Object { @{path=$_.path;role=$_.role;sha256=$_.beforeSha256;fileShare='Read';writeAndDeleteDenied=$true} });
        kernelPath=$kernel;manifestPath=$manifestPath;sharedProjectsRebuilt=$false;systemDotnetAndWindowsArchived=$false
    }
    $staticProof = Join-Path $output 'kernel-pe-before.json'
    Invoke-Verifier @('--kernel',$kernel,'--manifest',$manifestPath,'--output',$staticProof) (Join-Path $output 'kernel-pe-before')
    $entriesRoot = Join-Path $output 'entries'
    New-Item -ItemType Directory -Path $entriesRoot -Force | Out-Null
    $entryOutput = Join-Path $entriesRoot 'fixture-result.json'
    $start = [Diagnostics.ProcessStartInfo]::new($entryFixture)
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    foreach ($argument in @('--kernel',$kernel,'--output',$entryOutput)) { $start.ArgumentList.Add($argument) }
    $child = [Diagnostics.Process]::new()
    $child.StartInfo = $start
    $record = @{role='entry-table-reader';pid=$null;exitCode=$null;timedOut=$false;exactOwnedChild=$true}
    $processes.Add($record)
    try {
        if (-not $child.Start()) { throw 'Could not start the exact owned entry-table fixture.' }
        $record.pid = $child.Id
        $stdout = $child.StandardOutput.ReadToEndAsync()
        $stderr = $child.StandardError.ReadToEndAsync()
        if (-not $child.WaitForExit(60000)) { $record.timedOut=$true; $child.Kill($false); $child.WaitForExit() }
        $record.exitCode = $child.ExitCode
        [IO.File]::WriteAllText((Join-Path $entriesRoot 'stdout.log'),$stdout.GetAwaiter().GetResult(),$utf8)
        [IO.File]::WriteAllText((Join-Path $entriesRoot 'stderr.log'),$stderr.GetAwaiter().GetResult(),$utf8)
        if ($record.timedOut -or $record.exitCode -ne 0 -or -not (Test-Path -LiteralPath $entryOutput)) {
            throw 'Actual entry-table fixture did not complete successfully; compilation/execution failure is not evidence.'
        }
    } finally {
        $child.Dispose()
        Save-Json (Join-Path $entriesRoot 'process.json') $record
    }
    $entryProofPath = Join-Path $output 'kernel-entry-proof.json'
    Invoke-Verifier @('--kernel',$kernel,'--manifest',$manifestPath,'--entries',$entryOutput,'--output',$entryProofPath) (Join-Path $output 'kernel-entry-proof')
    $entryProof = Get-Content -LiteralPath $entryProofPath -Raw | ConvertFrom-Json -AsHashtable
    if ($entryProof.passed -ne $true -or $entryProof.staticEntryTablesVerified -ne $true) { throw 'Actual static entry table proof is incomplete.' }

    # Both existing runners keep every original assertion. Kernel is passed by
    # its exact absolute filename; no copying or renaming to JudgeContext.
    & $nativeRunner -HelperPath $kernel -FixturePath $nativeFixture -ManagedFixturePath $managedFixture -DobbyPath $dobby -OutputDirectory (Join-Path $output 'native') *>&1 |
        Tee-Object -FilePath (Join-Path $output 'native-runner.log')
    $nativeResult = Get-Content -LiteralPath (Join-Path $output 'native/fixture-result.json') -Raw | ConvertFrom-Json -AsHashtable
    & $managedRunner -HelperPath $kernel -ManagedAssemblyPath $managed -FixturePath $managedFixture -DobbyPath $dobby -OutputDirectory (Join-Path $output 'managed') *>&1 |
        Tee-Object -FilePath (Join-Path $output 'managed-runner.log')
    $managedResult = Get-Content -LiteralPath (Join-Path $output 'managed/fixture-result.json') -Raw | ConvertFrom-Json -AsHashtable
    if ($nativeResult.passed -ne $true -or $nativeResult.actualOriginalCalls -ne 136 -or
        $managedResult.passed -ne $true -or $managedResult.independentProcesses -ne 5 -or $managedResult.actualOriginalCalls -ne 44) {
        throw 'Existing native136/five managed-mode regression evidence is incomplete.'
    }
    $completed = $true
} catch { $errorRecord = $_; throw }
finally {
    $files = @($held.Values | ForEach-Object { @{path=$_.path;role=$_.role;beforeSha256=$_.beforeSha256;afterSha256=(Hash-Stream $_.stream)} })
    $unchanged = @($files | Where-Object { $_.beforeSha256 -ne $_.afterSha256 }).Count -eq 0
    foreach ($item in $held.Values) { $item.stream.Dispose() }
    $result = @{
        schemaVersion=1;kind='engine-node-kernel-runner';passed=($completed -and $unchanged);kernelPath=$kernel;
        manifestPath=$manifestPath;allHeldInputHashesUnchanged=$unchanged;files=$files;entryProcesses=@($processes);
        nativeRegression=$nativeResult;managedRegression=$managedResult;entryProof=$entryProof;
        actualOriginalCalls=$(if($completed){$nativeResult.actualOriginalCalls+$managedResult.actualOriginalCalls}else{$null});
        nativeRegressionProcesses=$(if($nativeResult){1}else{0});managedRegressionProcesses=$(if($managedResult){5}else{0});
        fixtureOnlyDobbyUsed=$true;kernelHasDobbyDirectImport=$false;sharedProjectsRebuiltByRunner=$false;
        allTransitiveOsDependenciesSafeVerified=$false;firstInstallSafetyVerified=$false;productionV4Issued=$false;
        schedulerClosureVerified=$false;engineCancellationCompleted=$false;gameCodeExecuted=$false;installed=$false
    }
    if ($errorRecord) { $result.error = $errorRecord.ToString() }
    Save-Json (Join-Path $output 'fixture-result.json') $result
    if ($completed -and -not $unchanged) { throw 'An input changed while held read-only for the entire Kernel regression.' }
}
Write-Output "Kernel exact-path regression evidence: $output"
