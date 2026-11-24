#requires -Version 7.2
[CmdletBinding()]
param([Parameter(Mandatory)][string]$GameDirectory,[Parameter(Mandatory)][string]$CandidateDirectory,
    [string]$Python = '',[switch]$ValidateOnly)
. (Join-Path $PSScriptRoot 'Toolchain.Common.ps1')
if (-not $Python) { $Python = Get-RizPythonPath }

$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Install.Common.ps1')
. (Join-Path $PSScriptRoot 'EngineTransportPackage.Common.ps1')
$repo=Split-Path -Parent $PSScriptRoot
$game=Assert-EngineOrdinaryPath ($ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($GameDirectory)) -Directory
if(Get-Process -Name Rizline -ErrorAction SilentlyContinue){throw 'Close Rizline before installation.'}
$lock=Enter-InstallLock $game
try{
    $dependencies=Get-Content -LiteralPath (Join-Path $repo 'dependencies.lock.json') -Raw|ConvertFrom-Json
    $gameAssembly=Assert-EngineOrdinaryPath (Join-Path $game 'GameAssembly.dll')
    if((Get-InstallHash $gameAssembly) -ne $dependencies.supportedBuild.gameAssemblySha256){throw 'Unsupported actual game build.'}
    $manifestPath=Get-InstallTarget $game 'BepInEx\RizToolbox\installation.json'
    $existing=Test-Path -LiteralPath $manifestPath
    if($existing){
        if($ValidateOnly){
            $pending=Get-Content -LiteralPath $manifestPath -Raw|ConvertFrom-Json
            if(($pending.PSObject.Properties.Name -contains 'transaction') -or
                (($pending.PSObject.Properties.Name -contains 'pending') -and @($pending.pending).Count -gt 0)){
                throw 'ValidateOnly refuses an unfinished installation journal; recovery requires an explicit mutating install or uninstall.'
            }
        }
        $manifest=Read-InstallManifest $game $manifestPath
        # Recover solely through the established persistent journal/backups,
        # before requiring the new candidate or dependency cache to exist.
        if(-not $ValidateOnly){$manifest=Restore-InstallTransaction $game $manifestPath $manifest}
    }else{
        foreach($name in @('winhttp.dll','doorstop_config.ini','.doorstop_version','BepInEx','dotnet','MelonLoader','version.dll','dinput8.dll','doorstop_libs','RizToolbox.EngineBootstrap.dll','RizToolbox.EngineNodeKernel.dll')){
            if(Test-Path -LiteralPath (Join-Path $game $name)){throw "Foreign loader entry refused: $name"}
        }
        $manifest=[pscustomobject]@{owner='riz_toolbox';gameRoot=$game;installedAt=(Get-Date).ToUniversalTime().ToString('o');files=@();pending=@()}
    }
    $owned=Get-OwnedInstallFiles $manifest
    if($existing){foreach($name in @('winhttp.dll','doorstop_config.ini','.doorstop_version','MelonLoader','version.dll','dinput8.dll','doorstop_libs','RizToolbox.EngineBootstrap.dll','RizToolbox.EngineNodeKernel.dll')){
        if((Test-Path -LiteralPath (Join-Path $game $name)) -and -not $owned.ContainsKey($name)){throw "Foreign loader entry refused: $name"}
    }}
    $candidatePath=$ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($CandidateDirectory)
    $candidate=Get-EngineTransportCandidateInstallPlan $candidatePath $game $owned $Python
    if($ValidateOnly){
        $admitted = [bool]$candidate.Package.Manifest.productionAdmissionGranted
        [ordered]@{schemaVersion=1;kind='engine-transport-candidate-install-validation';passed=$true;validateOnly=$true;
            payloadFiles=@($candidate.Plan).Count;publicationVersion=5;nativeProfile=2;gameAssemblyVerified=$true;
            finalGraphComplete=$candidate.Validation.finalGraphComplete;dependencyGraphSha256=$candidate.Validation.dependencyGraphSha256;
            journalRecoveryPerformed=$false;dependenciesPrepared=$false;payloadWritten=$false;nativeExecuted=$false;
            productionAdmissionGranted=$admitted;gameExecuted=$false;installed=$false}|ConvertTo-Json -Compress
        return
    }
    & (Join-Path $PSScriptRoot 'Prepare-Dependencies.ps1')
    if(-not $?){throw 'Pinned dependency preparation failed.'}
    $loader=Join-Path $repo '.tools/bepinex-789'
    $plan=[Collections.Generic.List[object]]::new()
    foreach($item in $candidate.Plan){$plan.Add($item)}
    function Add-TransportInstallFile([string]$Source,[string]$Relative){
        $sourcePath=Assert-EngineOrdinaryPath $Source
        $plan.Add([pscustomobject]@{source=$sourcePath;path=$Relative;sha256=Get-InstallHash $sourcePath})
    }
    Add-TransportInstallFile (Join-Path $loader 'BepInEx/core/dobby.dll') 'BepInEx\core\dobby.dll'
    foreach($item in Get-ChildItem -LiteralPath (Join-Path $loader 'dotnet') -File -Recurse){
        Add-TransportInstallFile $item.FullName ([IO.Path]::GetRelativePath($loader,$item.FullName))
    }
    $staging=Join-Path $repo ('artifacts/transport-install-'+[Guid]::NewGuid().ToString('N'))
    $null=New-Item -ItemType Directory -Path $staging
    try{
        $config=Get-Content -LiteralPath (Join-Path $loader 'doorstop_config.ini') -Raw
        $config=$config -replace '(?m)^target_assembly\s*=.*$', 'target_assembly = BepInEx\plugins\RizToolbox\RizToolbox.Loader.dll'
        $configPath=Join-Path $staging 'doorstop_config.ini';[IO.File]::WriteAllText($configPath,$config,[Text.UTF8Encoding]::new($false))
        Add-TransportInstallFile $configPath 'doorstop_config.ini'
        if(-not (Test-Path -LiteralPath (Get-InstallTarget $game 'BepInEx\RizToolbox\settings.json'))){
            $settings=Join-Path $staging 'settings.json'
            [IO.File]::WriteAllText($settings,'{"enableExamples":false,"runSelfValidation":false}',[Text.UTF8Encoding]::new($false))
            Add-TransportInstallFile $settings 'BepInEx\RizToolbox\settings.json'
        }
        # The same bounded transaction owns upgrades from the old nested Kernel,
        # persistent recovery and complete rollback. It grants no startup gate.
        Invoke-InstallTransaction $game $manifestPath $manifest $plan $owned $candidate.RemovePaths
    }finally{
        foreach($item in Get-ChildItem -LiteralPath $staging -File){Remove-Item -LiteralPath $item.FullName}
        Remove-Item -LiteralPath $staging
    }
    $admittedInstall = [bool]$candidate.Package.Manifest.productionAdmissionGranted
    Write-Output "Explicit V5 candidate installed (productionAdmissionGranted=$admittedInstall). Journal: $manifestPath"
}finally{$lock.Dispose()}
