#requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ManagedAssemblyPath,
    [Parameter(Mandatory)][string]$HelperPath,
    [switch]$NativeOnly
)

$ErrorActionPreference = 'Stop'
$managed = (Resolve-Path -LiteralPath $ManagedAssemblyPath).Path
$helper = (Resolve-Path -LiteralPath $HelperPath).Path

if (-not $NativeOnly) {
    & dotnet $managed '--runtime-tick-admission'
    if ($LASTEXITCODE -ne 0) { throw "Managed Tick admission checks failed with exit code $LASTEXITCODE." }
}

# Each native channel has one permanent observer and one owning process.
foreach ($mode in 'standard', 'developer', 'foreign-thread', 'native-unknown') {
    & dotnet $managed '--runtime-tick-native-admission' $mode $helper
    if ($LASTEXITCODE -ne 0) { throw "Owned native Tick fixture '$mode' failed with exit code $LASTEXITCODE." }
}
