#requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ManagedAssemblyPath,
    [Parameter(Mandatory)][string]$FixturePath,
    [Parameter(Mandatory)][string]$HelperDirectory,
    [Parameter(Mandatory)][string]$DobbyPath,
    [Parameter(Mandatory)][string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$managed = (Resolve-Path -LiteralPath $ManagedAssemblyPath).Path
$fixture = (Resolve-Path -LiteralPath $FixturePath).Path
$helper = (Resolve-Path -LiteralPath (Join-Path $HelperDirectory 'RizToolbox.JudgeContext.dll')).Path
$dobby = (Resolve-Path -LiteralPath $DobbyPath).Path
$output = [IO.Path]::GetFullPath($OutputDirectory)
function Hash([string]$File) { (Get-FileHash -LiteralPath $File -Algorithm SHA256).Hash.ToLowerInvariant() }
if ((Hash $dobby) -ne '8015de7d867245a1095d13947a63763878e4cf5fd3d3089b63cc39200b055ded') { throw 'Listener callback fixture requires the pinned Dobby image.' }
if ([IO.Path]::GetFileName($managed) -ne 'RizToolbox.Runtime.Tests.dll') { throw 'Use the Runtime.Tests assembly for native callback validation.' }
New-Item -ItemType Directory -Path $output -Force | Out-Null
$before = @($managed, $fixture, $helper, $dobby) | ForEach-Object { @{path=$_;sha256=(Hash $_)} }
$rows = [Collections.Generic.List[object]]::new()
$processes = [Collections.Generic.List[object]]::new()
try {
    foreach ($mode in @('normal','original-seh','original-cpp','target-read-av','late-attach','detach','worker','nested-same','nested-cross','foreign-original','fault-before','fault-after')) {
        $start = [Diagnostics.ProcessStartInfo]::new((Get-Command dotnet -CommandType Application).Source)
        $start.UseShellExecute = $false
        $start.CreateNoWindow = $true
        $start.RedirectStandardOutput = $true
        $start.RedirectStandardError = $true
        foreach ($argument in @($managed, '--listener-managed-callbacks', $mode)) { $start.ArgumentList.Add($argument) }
        $start.Environment['RIZ_LISTENER_MANAGED_FIXTURE'] = $fixture
        $start.Environment['RIZ_INITIALIZATION_CONTEXT_HELPER'] = $helper
        $start.Environment['RIZ_INITIALIZATION_ABI_DOBBY'] = $dobby
        $child = [Diagnostics.Process]::new()
        $child.StartInfo = $start
        if (-not $child.Start()) { throw "Could not start the owned listener callback $mode child." }
        $stdout = $child.StandardOutput.ReadToEndAsync()
        $stderr = $child.StandardError.ReadToEndAsync()
        if (-not $child.WaitForExit(60000)) {
            $child.Kill($true)
            $child.WaitForExit()
            $stdout.GetAwaiter().GetResult() | Set-Content -LiteralPath (Join-Path $output "$mode.stdout.log") -Encoding utf8
            $stderr.GetAwaiter().GetResult() | Set-Content -LiteralPath (Join-Path $output "$mode.stderr.log") -Encoding utf8
            $child.Dispose()
            throw "The owned listener callback $mode child exceeded its finite timeout."
        }
        $exitCode = $child.ExitCode
        $outText = $stdout.GetAwaiter().GetResult()
        $errorText = $stderr.GetAwaiter().GetResult()
        $child.Dispose()
        $outText | Set-Content -LiteralPath (Join-Path $output "$mode.stdout.log") -Encoding utf8
        $errorText | Set-Content -LiteralPath (Join-Path $output "$mode.stderr.log") -Encoding utf8
        $lines = @($outText -split '\r?\n' | Where-Object { $_.StartsWith('{') })
        if ($exitCode -ne 0 -or $lines.Count -ne 1) { throw "Listener callback $mode failed (exit $exitCode): $errorText $outText" }
        $value = $lines[0] | ConvertFrom-Json -AsHashtable
        if ($value.kind -ne 'native-listener-managed-callback-fixture' -or $value.mode -ne $mode -or
            $value.passed -ne $true -or $value.failures -ne 0 -or $value.checks -le 0 -or $value.modeVerified -ne $true -or
            $value.productionGameFrontRejected -ne $true -or $value.firstInstallReceiptUsed -ne $false -or
            $value.metadataGraphRootsControlled -ne $true -or $value.actualIl2CppMetadataResolved -ne $false -or
            $value.gameCodeExecuted -ne $false -or $value.gameRuntimeVerified -ne $false -or
            $value.firstInstallSafetyVerified -ne $false -or $value.brokerInstallCalls -ne 0 -or
            $value.nativeFailures -ne 0 -or $value.nativeInFlight -ne 0 -or $value.diskHashesUnchanged -ne $true) {
            throw "Listener callback $mode produced an incomplete result."
        }
        if ($mode -ne 'target-read-av' -and $value.sharedProductionListenerCoreVerified -ne $true) { throw "Listener callback $mode did not use the shared broker core." }
        if ($mode -in @('normal','original-seh','original-cpp') -and $value.actualLedgerCaptureVerified -ne $true) { throw "Listener callback $mode did not verify actual ledger capture." }
        $lines[0] | Set-Content -LiteralPath (Join-Path $output "$mode-result.json") -Encoding utf8
        $rows.Add($value)
        $processes.Add(@{mode=$mode;exitCode=$exitCode;stdoutSha256=(Hash (Join-Path $output "$mode.stdout.log"));stderrSha256=(Hash (Join-Path $output "$mode.stderr.log"))})
        Write-Output "PASS native listener CLR callbacks: $mode ($($value.checks) checks)."
    }
} finally {
    $sourceCheck = @($before | ForEach-Object { @{path=$_.path;expected=$_.sha256;actual=(Hash $_.path)} })
    $unchanged = @($sourceCheck | Where-Object { $_.expected -ne $_.actual }).Count -eq 0
    @{schemaVersion=1;files=$sourceCheck;allMatched=$unchanged;installed=$false;gameCodeExecuted=$false} |
        ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $output 'source-file-check.json') -Encoding utf8
    if (-not $unchanged) { throw 'A listener callback fixture input changed during execution.' }
}
@{schemaVersion=1;kind='native-listener-managed-callback-summary';passed=$true;failures=0;checks=($rows | Measure-Object -Property checks -Sum).Sum;
    independentProcesses=$rows.Count;modes=@($rows);processes=@($processes);firstInstallReceiptUsed=$false;gameCodeExecuted=$false;
    gameRuntimeVerified=$false;firstInstallSafetyVerified=$false;installed=$false;files=$before} |
    ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $output 'fixture-result.json') -Encoding utf8
Write-Output "Native listener CLR callback evidence: $output"
