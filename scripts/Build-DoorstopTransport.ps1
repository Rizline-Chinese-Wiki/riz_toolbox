#requires -Version 7.2
[CmdletBinding()]
param([Parameter(Mandatory)][string]$OutputDirectory,[string]$InputDirectory,[switch]$Owned)
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
if(-not [IO.Path]::IsPathFullyQualified($OutputDirectory)){throw 'An absolute fresh output directory is required.'}
$output=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $output){throw 'Preserve prior builds; use a nonexistent output directory.'}
if(-not $InputDirectory){$InputDirectory=Join-Path $repo '.tools/doorstop-preinit-inputs'}
$inputs=[IO.Path]::GetFullPath($InputDirectory)
foreach($path in @($repo,$output,$inputs)){if($path -match '[\r\n"%&|<>^]'){throw 'Unsupported command path characters.'}}
function Hash([string]$Path){(Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()}
function Swap([string]$Body,[string]$Before,[string]$After){
    if($Body.Split(@($Before),[StringSplitOptions]::None).Count -ne 2){throw "The fixed Doorstop build seam changed: $Before"}
    $Body.Replace($Before,$After)
}
$legacy=Join-Path $PSScriptRoot 'Build-Doorstop.ps1'
$patch=Join-Path $repo 'native/RizToolbox.Doorstop'
$sources=@($legacy,$PSCommandPath,(Join-Path $patch 'riz_engine_transport.cpp'),(Join-Path $patch 'riz_engine_transport.h'),
    (Join-Path $repo 'native/RizToolbox.EngineBootstrap/EngineTransportPublication.h'),
    (Join-Path $repo 'native/RizToolbox.EngineBootstrap/EngineTransportGameProfile.h'),
    (Join-Path $repo 'native/RizToolbox.EngineBootstrap/EngineTransportAdmissionFacts.h'),
    (Join-Path $repo 'native/RizToolbox.EngineBootstrap/EngineTransportAdmissionSha256.h'))
$locks=[Collections.Generic.List[IDisposable]]::new()
$manifest=[ordered]@{schemaVersion=1;kind='engine-transport-doorstop-build';passed=$false;owned=[bool]$Owned
    installed=$false;gameExecuted=$false;productionAdmissionVerified=$false;inputs=@();sourceHashesUnchanged=$false;failure=$null}
$null=New-Item -ItemType Directory -Path $output
try{
    foreach($path in $sources){$locks.Add([IO.File]::Open($path,'Open','Read','Read'))}
    $manifest.inputs=@($sources|ForEach-Object{[ordered]@{path=$_;sha256=Hash $_}})
    & $legacy -OutputDirectory $output -InputDirectory $inputs -FixedSystemWinHttp -EngineTransport -Owned:$Owned
    $legacyManifest=Get-Content -LiteralPath (Join-Path $output 'doorstop-manifest.json') -Raw|ConvertFrom-Json
    $expectedProfile=if($Owned){1}else{2}
    if($legacyManifest.engineBootstrapPublicationVersion -ne 5 -or $legacyManifest.engineBootstrapPublicationBytes -ne 91376 -or
        $legacyManifest.engineBootstrapPrivateRecordBytes -ne 274288 -or
        $legacyManifest.engineBootstrapProfile -ne $expectedProfile -or $legacyManifest.publicExportCount -ne 82){throw 'The actual V5 Doorstop build omitted its fixed profile.'}
    $manifest.productionAdmissionVerified = [bool]$legacyManifest.engineBootstrapProductionAdmissionVerified
    foreach($row in $manifest.inputs){if((Hash $row.path) -ne $row.sha256){throw "Build source changed: $($row.path)"}}
    $manifest.sourceHashesUnchanged=$true;$manifest.passed=$true
}catch{$manifest.failure=$_.Exception.Message;throw}
finally{
    foreach($item in $locks){$item.Dispose()}
    [IO.File]::WriteAllText((Join-Path $output 'transport-doorstop-build.json'),($manifest|ConvertTo-Json -Depth 12),[Text.UTF8Encoding]::new($false))
}
Write-Output "V5 Doorstop built (82 public exports; no game installation): $output"
