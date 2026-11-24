#requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ManagedAssemblyPath,
    [Parameter(Mandatory)][string]$FixturePath,
    [Parameter(Mandatory)][string]$HelperDirectory,
    [Parameter(Mandatory)][string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$managed = (Resolve-Path -LiteralPath $ManagedAssemblyPath).Path
$fixture = (Resolve-Path -LiteralPath $FixturePath).Path
$helper = (Resolve-Path -LiteralPath (Join-Path $HelperDirectory 'RizToolbox.JudgeContext.dll')).Path
$output = [IO.Path]::GetFullPath($OutputDirectory)
if ([IO.Path]::GetFileName($managed) -ne 'RizToolbox.Runtime.Tests.dll') { throw 'Use the Runtime.Tests assembly.' }
New-Item -ItemType Directory -Path $output -Force | Out-Null
function Hash([string]$File) { (Get-FileHash -LiteralPath $File -Algorithm SHA256).Hash.ToLowerInvariant() }
$before = @($managed, $fixture, $helper) | ForEach-Object { @{path=$_;sha256=(Hash $_)} }
$rows = [Collections.Generic.List[object]]::new()
$processes = [Collections.Generic.List[object]]::new()
foreach ($mode in @('normal','original-seh','original-cpp','detach','observer-fault')) {
    $start = [Diagnostics.ProcessStartInfo]::new((Get-Command dotnet -CommandType Application).Source)
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    foreach ($argument in @($managed, '--operation-managed-callbacks', $mode)) { $start.ArgumentList.Add($argument) }
    $start.Environment['RIZ_OPERATION_MANAGED_FIXTURE'] = $fixture
    $start.Environment['RIZ_INITIALIZATION_CONTEXT_HELPER'] = $helper
    $child = [Diagnostics.Process]::new()
    $child.StartInfo = $start
    if (-not $child.Start()) { throw "Could not start owned operation child $mode." }
    try {
        $stdout = $child.StandardOutput.ReadToEndAsync()
        $stderr = $child.StandardError.ReadToEndAsync()
        if (-not $child.WaitForExit(60000)) { $child.Kill($true); $child.WaitForExit(); throw "Owned operation child $mode exceeded its timeout." }
        $exitCode = $child.ExitCode
        $outText = $stdout.GetAwaiter().GetResult()
        $errText = $stderr.GetAwaiter().GetResult()
    } finally { $child.Dispose() }
    [IO.File]::WriteAllText((Join-Path $output "$mode.stdout.log"), $outText, [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText((Join-Path $output "$mode.stderr.log"), $errText, [Text.UTF8Encoding]::new($false))
    $lines = @($outText -split '\r?\n' | Where-Object { $_.StartsWith('{') })
    if ($exitCode -ne 0 -or $lines.Count -ne 1) { throw "Operation CLR $mode failed (exit $exitCode): $errText $outText" }
    $value = $lines[0] | ConvertFrom-Json -AsHashtable
    if ($value.kind -ne 'native-operation-managed-fixture' -or $value.mode -ne $mode -or $value.passed -ne $true -or
        $value.failures -ne 0 -or $value.checks -le 0 -or $value.modeVerified -ne $true -or
        $value.productionBridgeAndBrokerVerified -ne $true -or $value.nativeExceptionsCaughtBeforePInvoke -ne $true -or
        $value.actualOriginalCalls -ne $(if ($mode -eq 'detach') { 6 } else { 3 }) -or $value.nativeInFlight -ne 0 -or
        $value.diskHashesUnchanged -ne $true -or $value.gameCodeExecuted -ne $false -or $value.installed -ne $false -or
        $value.gameRuntimeVerified -ne $false -or $value.firstInstallSafetyVerified -ne $false -or
        $value.actualIl2CppMetadataResolved -ne $false -or $value.actualLedgerCaptureVerified -ne $false -or $value.dobbyPublicationVerified -ne $false) {
        throw "Operation CLR $mode produced an incomplete result."
    }
    [IO.File]::WriteAllText((Join-Path $output "$mode-result.json"), $lines[0] + "`n", [Text.UTF8Encoding]::new($false))
    $rows.Add($value)
    $processes.Add(@{mode=$mode;exitCode=$exitCode;stdoutSha256=(Hash (Join-Path $output "$mode.stdout.log"));stderrSha256=(Hash (Join-Path $output "$mode.stderr.log"))})
    Write-Output "PASS native operation CLR: $mode ($($value.checks) checks)."
}
$files = @($before | ForEach-Object { @{path=$_.path;beforeSha256=$_.sha256;afterSha256=(Hash $_.path)} })
$unchanged = @($files | Where-Object { $_.beforeSha256 -ne $_.afterSha256 }).Count -eq 0
@{schemaVersion=1;files=$files;allMatched=$unchanged;installed=$false;gameCodeExecuted=$false} |
    ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $output 'source-file-check.json') -Encoding utf8
if (-not $unchanged) { throw 'Operation fixture inputs changed during execution.' }
@{schemaVersion=1;kind='native-operation-managed-summary';passed=$true;independentProcesses=$rows.Count;
    checks=($rows | Measure-Object -Property checks -Sum).Sum;modes=@($rows);processes=@($processes);
    gameCodeExecuted=$false;gameRuntimeVerified=$false;installed=$false;files=$before} |
    ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $output 'fixture-result.json') -Encoding utf8
Write-Output "Native operation CLR evidence: $output"
