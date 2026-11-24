#requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ManagedAssemblyPath,
    [Parameter(Mandatory)][string]$HelperDirectory,
    [Parameter(Mandatory)][string]$FixturePath,
    [Parameter(Mandatory)][string]$DobbyPath,
    [Parameter(Mandatory)][string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$managedPath = [IO.Path]::GetFullPath($ManagedAssemblyPath)
$managedRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot 'tests/RizToolbox.Runtime.Tests/bin')) + [IO.Path]::DirectorySeparatorChar
if (-not $managedPath.StartsWith($managedRoot, [StringComparison]::OrdinalIgnoreCase) -or
    [IO.Path]::GetFileName($managedPath) -ne 'RizToolbox.Runtime.Tests.dll') {
    throw 'The owned callback child must use this repository Runtime test executable.'
}
$helperPath = [IO.Path]::GetFullPath((Join-Path $HelperDirectory 'RizToolbox.JudgeContext.dll'))
$fixtureFile = [IO.Path]::GetFullPath($FixturePath)
$dobbyFile = [IO.Path]::GetFullPath($DobbyPath)
$outputPath = [IO.Path]::GetFullPath($OutputDirectory)
function Hash([string]$File) { (Get-FileHash -LiteralPath $File -Algorithm SHA256).Hash.ToLowerInvariant() }
$inputPaths = @($managedPath, (Join-Path (Split-Path -Parent $managedPath) 'RizToolbox.Runtime.dll'), $helperPath, $fixtureFile, $dobbyFile)
foreach ($path in $inputPaths) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing callback fixture input: $path" }
}
if ((Hash $dobbyFile) -ne '8015de7d867245a1095d13947a63763878e4cf5fd3d3089b63cc39200b055ded') {
    throw 'The callback fixture requires the inspected Dobby image.'
}
$beforeHashes = @{}
foreach ($path in $inputPaths) { $beforeHashes[$path] = Hash $path }
New-Item -ItemType Directory -Path $outputPath -Force | Out-Null
$rows = [Collections.Generic.List[object]]::new()
$processes = [Collections.Generic.List[object]]::new()
try {
    foreach ($mode in @('happy', 'fault', 'late-attach')) {
        $start = [Diagnostics.ProcessStartInfo]::new((Get-Command dotnet -CommandType Application).Source)
        $start.UseShellExecute = $false
        $start.CreateNoWindow = $true
        $start.RedirectStandardOutput = $true
        $start.RedirectStandardError = $true
        foreach ($argument in @($managedPath, '--adopted-broker-callbacks', $mode)) { $start.ArgumentList.Add($argument) }
        $start.Environment['RIZ_INITIALIZATION_CONTEXT_HELPER'] = $helperPath
        $start.Environment['RIZ_INITIALIZATION_ABI_FIXTURE'] = $fixtureFile
        $start.Environment['RIZ_INITIALIZATION_ABI_DOBBY'] = $dobbyFile
        $child = [Diagnostics.Process]::new()
        $child.StartInfo = $start
        if (-not $child.Start()) { throw "Could not start the owned adopted-broker $mode child." }
        try {
            $stdout = $child.StandardOutput.ReadToEndAsync()
            $stderr = $child.StandardError.ReadToEndAsync()
            if (-not $child.WaitForExit(60000)) {
                # Only this test process created above is terminated on timeout.
                $child.Kill($true)
                $child.WaitForExit()
                $captured = $stdout.GetAwaiter().GetResult() + $stderr.GetAwaiter().GetResult()
                [IO.File]::WriteAllText((Join-Path $outputPath "$mode.log"), $captured, [Text.UTF8Encoding]::new($false))
                throw "The owned adopted-broker $mode child exceeded its finite timeout."
            }
            $exitCode = $child.ExitCode
            $lines = @(($stdout.GetAwaiter().GetResult() + $stderr.GetAwaiter().GetResult()) -split '\r?\n' | Where-Object { $_.Length -gt 0 })
        } finally { $child.Dispose() }
        $lines | Set-Content -LiteralPath (Join-Path $outputPath "$mode.log") -Encoding utf8
        $jsonLines = @($lines | Where-Object { $_.StartsWith('{') })
        if ($exitCode -ne 0 -or $jsonLines.Count -ne 1) {
            throw "Adopted-broker $mode failed (exit $exitCode): $($lines -join [Environment]::NewLine)"
        }
        $value = $jsonLines[0] | ConvertFrom-Json -AsHashtable
        if ($value.kind -ne 'native-adopted-broker-callback-fixture' -or $value.mode -ne $mode -or
            $value.passed -ne $true -or $value.failures -ne 0 -or $value.checks -le 0 -or
            $value.nativeToClrCallbackVerified -ne $true -or $value.metadataIdentityControlled -ne $true -or
            $value.gameRuntimeVerified -ne $false -or $value.gameCodeExecuted -ne $false -or
            $value.firstInstallReceiptUsed -ne $false) {
            throw "Adopted-broker $mode returned an invalid evidence contract."
        }
        $jsonLines[0] | Set-Content -LiteralPath (Join-Path $outputPath "$mode-result.json") -Encoding utf8
        $rows.Add($value)
        $processes.Add(@{mode=$mode;exitCode=$exitCode;logSha256=(Hash (Join-Path $outputPath "$mode.log"))})
    }
} finally {
    $fileChecks = @($inputPaths | ForEach-Object { @{path=$_;beforeSha256=$beforeHashes[$_];afterSha256=(Hash $_)} })
    $allMatched = @($fileChecks | Where-Object { $_.beforeSha256 -ne $_.afterSha256 }).Count -eq 0
    @{schemaVersion=1;files=$fileChecks;allMatched=$allMatched;installed=$false;gameCodeExecuted=$false} |
        ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $outputPath 'source-file-check.json') -Encoding utf8
    if (-not $allMatched) { throw 'Callback fixture inputs changed during the owned child runs.' }
}
@{schemaVersion=1;kind='native-adopted-broker-callback-summary';passed=$true;failures=0;
    checks=($rows | Measure-Object -Property checks -Sum).Sum;independentProcesses=$rows.Count;
    modes=@($rows);processes=@($processes);nativeToClrCallbackVerified=$true;
    firstInstallReceiptUsed=$false;metadataIdentityControlled=$true;gameCodeExecuted=$false;
    gameRuntimeVerified=$false;installed=$false} |
    ConvertTo-Json -Depth 14 | Set-Content -LiteralPath (Join-Path $outputPath 'fixture-result.json') -Encoding utf8
Write-Output "PASS adopted native broker callbacks in $($rows.Count) independent fixture processes; no game code executed."
Write-Output "Adopted-broker evidence: $outputPath"
