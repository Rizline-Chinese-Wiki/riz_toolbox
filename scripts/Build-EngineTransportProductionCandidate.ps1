#requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$OutputDirectory,
    [Parameter(Mandatory)][string]$DoorstopDirectory,
    [string]$GameDirectory='D:/SteamLibrary/steamapps/common/Rizline',
    [string]$KernelDirectory='D:/riz-build-cache-next/engine-wait-kernel-r2-20261026',
    [string]$InteriorClosurePath='',
    [string]$ColdTracePath='',
    [string]$Python = '',
    [string]$VcVars = ''
)
. (Join-Path $PSScriptRoot 'Toolchain.Common.ps1')
if (-not $VcVars) { $VcVars = Get-RizVcVarsPath }
if (-not $Python) { $Python = Get-RizPythonPath }

$ErrorActionPreference='Stop';$PSNativeCommandUseErrorActionPreference=$false
$repo=Split-Path -Parent $PSScriptRoot
function Absolute([string]$Path){if(-not [IO.Path]::IsPathFullyQualified($Path)){throw 'Absolute paths required.'};$full=[IO.Path]::GetFullPath($Path);if($full -match '[\r\n"%&|<>^]'){throw 'Unsupported command path characters.'};$full}
function Hash([string]$Path){(Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()}
function WriteJson([object]$Value,[string]$Path){[IO.File]::WriteAllText($Path,($Value|ConvertTo-Json -Depth 20),[Text.UTF8Encoding]::new($false))}
$output=Absolute $OutputDirectory;$proxyDirectory=Absolute $DoorstopDirectory;$game=Absolute $GameDirectory;$kernelDirectory=Absolute $KernelDirectory;$vcvarsPath=Absolute $VcVars
if(Test-Path -LiteralPath $output){throw 'Preserve previous candidates; use a fresh output directory.'}
if($output.TrimEnd('\','/').Equals($game.TrimEnd('\','/'),[StringComparison]::OrdinalIgnoreCase) -or
    $output.StartsWith($game.TrimEnd('\','/')+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Candidate output cannot be in the game installation.'}
$proxyManifestPath=Join-Path $proxyDirectory 'transport-doorstop-build.json';$proxyManifest=Get-Content -LiteralPath $proxyManifestPath -Raw|ConvertFrom-Json
if($proxyManifest.kind -ne 'engine-transport-doorstop-build' -or $proxyManifest.passed -ne $true -or $proxyManifest.owned -ne $false){throw 'A successful fixed-production V5 Doorstop build is required.'}
$native=Join-Path $repo 'native/RizToolbox.EngineBootstrap';$generator=Join-Path $PSScriptRoot 'generate-engine-transport-profile.py'
$target=Join-Path $game 'UnityPlayer.dll';$executable=Join-Path $game 'Rizline.exe';$proxySource=Join-Path $proxyDirectory 'winhttp.dll'
$kernelSource=Join-Path $kernelDirectory 'RizToolbox.EngineNodeKernel.dll';$kernelLib=Join-Path $kernelDirectory 'RizToolbox.EngineNodeKernel.lib'
$sourcePaths=@($PSCommandPath,$generator)+@('EngineTransportCompileContract.h','EngineTransportProfile.h','EngineTransportFirstInstall.cpp',
    'EngineTransportPublication.h','EngineTransportGameProfile.h','EngineTransportAdmissionFacts.h','EngineTransportAdmissionSha256.h',
    'EngineTransportRelays.cpp','EngineTransportRelays.h',
    'EngineTransportFamilies.cpp','EngineTransportFamilies.h','EngineTransportSourceWrites.cpp','EngineTransportSourceWrites.h',
    'EngineOriginals.asm','EngineTransportOriginals.asm','EngineTransportOriginals.h'|ForEach-Object{Join-Path $native $_})+
    @('EngineNodeContext.h','EngineInlineProbe.h','EngineSchedulerContext.h','EngineWaitContext.h'|ForEach-Object{Join-Path $repo ('native/RizToolbox.JudgeContext/'+$_)})
$hasClosure = -not [string]::IsNullOrWhiteSpace($InteriorClosurePath)
$hasTrace = -not [string]::IsNullOrWhiteSpace($ColdTracePath)
if ($hasClosure -ne $hasTrace) { throw 'Provide both -InteriorClosurePath and -ColdTracePath, or neither.' }
$extraInputs = @($target,$executable,$proxySource,$proxyManifestPath,$kernelSource,$kernelLib)
if ($hasClosure) {
    $icFull = Absolute $InteriorClosurePath
    $ctFull = Absolute $ColdTracePath
    if (-not (Test-Path -LiteralPath $icFull -PathType Leaf) -or -not (Test-Path -LiteralPath $ctFull -PathType Leaf)) {
        throw 'Stage A closure and cold-trace files must exist.'
    }
    $extraInputs += @($icFull, $ctFull)
}
$paths=@($sourcePaths+$extraInputs|Sort-Object -Unique)
$locks=[Collections.Generic.List[IDisposable]]::new();$null=New-Item -ItemType Directory -Path $output
$manifest=[ordered]@{schemaVersion=1;kind='engine-transport-production-native-candidate-build';passed=$false;buildExitCode=$null
    inputs=@();sourceHashesUnchanged=$false;gameFilesUnchanged=$false;files=@();failure=$null
    fixtureExecuted=$false;gameExecuted=$false;installed=$false;productionAdmissionGranted=$false;productionAdmissionVerified=$false;packaged=$false}
try{
    foreach($path in $paths){$locks.Add([IO.File]::Open($path,'Open','Read','Read'))}
    if((Hash $kernelSource) -ne '1541feaa8fe7b37d0d3a544fbb315387ea93e3f6b96a4dc955dc76dc9c30e38a'){throw 'The exact verified 35-export Kernel is required.'}
    $manifest.inputs=@($paths|ForEach-Object{[ordered]@{path=$_;sha256=Hash $_;bytes=(Get-Item -LiteralPath $_).Length}})
    WriteJson $manifest.inputs (Join-Path $output 'source-before.json')
    $archive=Join-Path $output 'production-native-sources.zip';$zip=[IO.Compression.ZipFile]::Open($archive,'Create')
    try{foreach($path in $sourcePaths){$entry=$zip.CreateEntry([IO.Path]::GetRelativePath($repo,$path).Replace('\','/'),[IO.Compression.CompressionLevel]::Optimal)
        $from=[IO.File]::OpenRead($path);$to=$entry.Open();try{$from.CopyTo($to)}finally{$from.Dispose();$to.Dispose()}}}finally{$zip.Dispose()}
    $manifest.sourceArchive=[ordered]@{path=$archive;sha256=Hash $archive}
    $proxy=Join-Path $output 'winhttp.dll';$kernel=Join-Path $output 'RizToolbox.EngineNodeKernel.dll'
    Copy-Item -LiteralPath $proxySource -Destination $proxy;Copy-Item -LiteralPath $kernelSource -Destination $kernel
    Copy-Item -LiteralPath $kernelLib -Destination (Join-Path $output 'kernel.lib')
    foreach($name in @('doorstop-corresponding-source.zip','UnityDoorstop.LGPL-2.1.txt','.doorstop_version')){
        $path=Join-Path $proxyDirectory $name;if(Test-Path -LiteralPath $path -PathType Leaf){Copy-Item -LiteralPath $path -Destination (Join-Path $output $name)}
    }
    $genArgs = @('-B',$generator,'--target',$target,'--executable',$executable,'--doorstop',$proxy,'--kernel',$kernel,'--profile','production','--output-header',(Join-Path $output 'EngineTransportProfile.Generated.h'),'--output-json',(Join-Path $output 'native-profile.json'))
    if ($hasClosure) { $genArgs += @('--interior-closure',$icFull,'--cold-trace',$ctFull) }
    & $Python @genArgs
    if($LASTEXITCODE){throw 'The fixed-production compile profile failed exact independent PE validation.'}
    $nativeProfile = Get-Content -LiteralPath (Join-Path $output 'native-profile.json') -Raw | ConvertFrom-Json
    $admitted = ($nativeProfile.productionAdmissionGranted -eq $true)
    [IO.File]::WriteAllLines((Join-Path $output 'seh.def'),@('LIBRARY ntdll.dll','EXPORTS','__C_specific_handler'),[Text.UTF8Encoding]::new($false))
    $commands=@('@echo off','setlocal DisableDelayedExpansion',"call `"$vcvarsPath`" >nul",'if errorlevel 1 exit /b 1',
        "lib /nologo /machine:x64 /def:`"$output\seh.def`" /out:`"$output\seh.lib`"",'if errorlevel 1 exit /b 1')
    $objects=@();foreach($name in @('EngineTransportFirstInstall','EngineTransportRelays','EngineTransportFamilies','EngineTransportSourceWrites')){
        $object=Join-Path $output ($name+'.obj');$objects+=('"'+$object+'"')
        $commands+="cl /nologo /c /std:c++17 /O2 /Oi /W4 /WX /GS- /GR- /Zl /EHa /Zi /DRIZ_ENGINE_TRANSPORT_PRODUCTION=1 /I`"$output`" /Fo`"$object`" /Fd`"$output\$name.compiler.pdb`" `"$native\$name.cpp`"";$commands+='if errorlevel 1 exit /b 1'
    }
    foreach($name in @('EngineOriginals','EngineTransportOriginals')){$object=Join-Path $output ($name+'.obj');$objects+=('"'+$object+'"');$commands+="ml64 /nologo /c /Zi /Fo`"$object`" `"$native\$name.asm`"";$commands+='if errorlevel 1 exit /b 1'}
    $bootstrap=Join-Path $output 'RizToolbox.EngineBootstrap.dll';$objectArguments=$objects -join ' '
    $commands+="link /nologo /DLL /NODEFAULTLIB /MACHINE:X64 /ENTRY:RizEngineTransportBootstrapEntry /INCREMENTAL:NO /OPT:REF /OPT:NOICF /DYNAMICBASE /NXCOMPAT /CETCOMPAT /GUARD:NO /DEBUG /MAP:`"$output\bootstrap.map`" $objectArguments `"$output\kernel.lib`" `"$output\seh.lib`" kernel32.lib /OUT:`"$bootstrap`" /IMPLIB:`"$output\bootstrap.lib`" /PDB:`"$output\Bootstrap.pdb`"";$commands+='if errorlevel 1 exit /b 1'
    $commands+="dumpbin /nologo /headers /imports /exports /unwindinfo `"$bootstrap`" > `"$output\bootstrap.pe.txt`"";$commands+='if errorlevel 1 exit /b 1';$commands+='exit /b 0'
    $commandPath=Join-Path $output 'build-production-transport.cmd';[IO.File]::WriteAllLines($commandPath,$commands,[Text.UTF8Encoding]::new($false))
    & $env:ComSpec /d /v:off /c $commandPath 2>&1|Tee-Object -FilePath (Join-Path $output 'bootstrap-build.log');$manifest.buildExitCode=$LASTEXITCODE
    if($LASTEXITCODE){throw "The strict fixed-production V5 native build failed: $LASTEXITCODE"}
    $hashes=@($bootstrap,$kernel,$target,$executable,$proxy|ForEach-Object{Hash $_})
    $rvas=@('0x792240','0x78f840','0x78f680','0x793840','0x44d630','0x447ad0','0x447c30','0x447d60','0x447750','0x78f5e0','0x78fc50','0x5a5260','0x5da510') -join ', '
    $ctorArgs = (@($hashes|ForEach-Object{'"'+$_+'"'}) -join ', ') + ", new uint[] { $rvas }"
    if ($admitted) {
        $ctorArgs += ", true, `"$($nativeProfile.fixedContractSha256)`", `"$($nativeProfile.interiorClosureSha256)`", `"$($nativeProfile.coldTraceSha256)`""
    }
    $source="namespace RizToolbox.Runtime.Hooks;`ninternal static partial class NativeEngineTransportProfile {`n static partial void Populate(ref NativeEngineTransportProfileData? value) {`n value = new($ctorArgs);`n }`n}`n"
    $managedProfile=Join-Path $output 'NativeEngineTransportProfile.Generated.cs';[IO.File]::WriteAllText($managedProfile,$source,[Text.UTF8Encoding]::new($false))
    $manifest.managedProfile=[ordered]@{path=$managedProfile;sha256=Hash $managedProfile;finalImageHashes=$hashes;productionAdmissionGranted=$admitted;fixedContractSha256=$nativeProfile.fixedContractSha256;interiorClosureSha256=$nativeProfile.interiorClosureSha256;coldTraceSha256=$nativeProfile.coldTraceSha256}
    $manifest.interiorEntryClosureVerified = ($nativeProfile.interiorEntryClosureVerified -eq $true)
    $manifest.productionColdTraceVerified = ($nativeProfile.productionColdTraceVerified -eq $true)
    $manifest.productionAdmissionGranted = $admitted
    $manifest.productionAdmissionVerified = $admitted
    foreach($row in $manifest.inputs){if((Hash $row.path) -ne $row.sha256){throw "Fixed production candidate input changed: $($row.path)"}}
    $manifest.sourceHashesUnchanged=$true;$manifest.gameFilesUnchanged=$true;$manifest.passed=$true
}catch{$manifest.failure=$_.Exception.Message;throw}
finally{foreach($lock in $locks){$lock.Dispose()};$manifest.files=@(Get-ChildItem -LiteralPath $output -File|Where-Object Name -ne 'production-native-build.json'|ForEach-Object{[ordered]@{path=$_.FullName;sha256=Hash $_.FullName;bytes=$_.Length}});WriteJson $manifest (Join-Path $output 'production-native-build.json')}
if ($manifest.productionAdmissionGranted) {
    Write-Output "Fixed-production V5 native candidate built with verified Stage A admission; no game process started: $output"
} else {
    Write-Output "Fixed-production V5 native candidate built with admission closed; no game process started: $output"
}
