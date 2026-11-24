#requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$OutputDirectory,
    [Parameter(Mandatory)][string]$TargetDirectory,
    [Parameter(Mandatory)][string]$DoorstopDirectory,
    [string]$KernelDirectory='D:/riz-build-cache-next/engine-wait-kernel-r2-20261026',
    [switch]$ManagedHost,
    [switch]$VerifyBenchmark,
    [string]$HostHeaders = '',
    [string]$VcVars = '',
    [string]$Python = ''
)
. (Join-Path $PSScriptRoot 'Toolchain.Common.ps1')
if (-not $VcVars) { $VcVars = Get-RizVcVarsPath }
if (-not $Python) { $Python = Get-RizPythonPath }
if (-not $HostHeaders) { $HostHeaders = Get-RizHostHeadersDirectory }

$ErrorActionPreference='Stop';$PSNativeCommandUseErrorActionPreference=$false
$repo=Split-Path -Parent $PSScriptRoot
function Absolute([string]$Path){if(-not [IO.Path]::IsPathFullyQualified($Path)){throw 'Absolute paths required.'};$full=[IO.Path]::GetFullPath($Path);if($full -match '[\r\n"%&|<>^]'){throw 'Unsupported command path characters.'};$full}
function Hash([string]$Path){(Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()}
function WriteJson([object]$Value,[string]$Path){[IO.File]::WriteAllText($Path,($Value|ConvertTo-Json -Depth 20),[Text.UTF8Encoding]::new($false))}
$output=Absolute $OutputDirectory;$targetDirectory=Absolute $TargetDirectory;$proxyDirectory=Absolute $DoorstopDirectory;$kernelDirectory=Absolute $KernelDirectory;$vcvarsPath=Absolute $VcVars
if(Test-Path -LiteralPath $output){throw 'Use a fresh nonexistent candidate directory.'}
$targetManifestPath=Join-Path $targetDirectory 'owned-target-image.json'
$targetManifest=Get-Content -LiteralPath $targetManifestPath -Raw|ConvertFrom-Json
$targetSource=Absolute $targetManifest.output.path
if((Hash $targetSource) -ne $targetManifest.output.sha256 -or [IO.Path]::GetFileName($targetSource) -in @('UnityPlayer.dll','GameAssembly.dll')){throw 'An exact independently generated owned target is required.'}
$proxyManifestPath=Join-Path $proxyDirectory 'transport-doorstop-build.json';$proxyManifest=Get-Content -LiteralPath $proxyManifestPath -Raw|ConvertFrom-Json
if($proxyManifest.passed -ne $true -or $proxyManifest.owned -ne $true){throw 'A successful compile-owned V5 Doorstop is required.'}
$proxySource=Join-Path $proxyDirectory 'winhttp.dll';$kernelSource=Join-Path $kernelDirectory 'RizToolbox.EngineNodeKernel.dll';$kernelLib=Join-Path $kernelDirectory 'RizToolbox.EngineNodeKernel.lib'
if((Hash $kernelSource) -ne '1541feaa8fe7b37d0d3a544fbb315387ea93e3f6b96a4dc955dc76dc9c30e38a'){throw 'The exact verified 35-export Kernel is required.'}
$native=Join-Path $repo 'native/RizToolbox.EngineBootstrap'
if($ManagedHost -and $VerifyBenchmark){throw 'Select only one owned host mode.'}
$hostName=if($ManagedHost){'ManagedHost.cpp'}elseif($VerifyBenchmark){'VerifyBenchmark.cpp'}else{'Host.cpp'}
$hostSource=Join-Path $repo ('native/RizToolbox.EngineTransportColdFixture/'+$hostName)
$hostHeaderOption=''
$hostHeaderPaths=@()
if($ManagedHost){
    $hostHeadersPath=Absolute $HostHeaders
    $hostHeaderPaths=@('hostfxr.h','coreclr_delegates.h'|ForEach-Object{Join-Path $hostHeadersPath $_})
    foreach($path in $hostHeaderPaths){if(-not (Test-Path -LiteralPath $path -PathType Leaf)){throw "Missing installed CLR hosting header: $path"}}
    $hostHeaderOption=" /I`"$hostHeadersPath`""
}
$sourcePaths=@($PSCommandPath,$hostSource,(Join-Path $PSScriptRoot 'generate-engine-transport-profile.py'))+
    @('EngineTransportCompileContract.h','EngineTransportProfile.h','EngineTransportFirstInstall.cpp','EngineTransportPublication.h','EngineTransportGameProfile.h',
      'EngineTransportAdmissionFacts.h','EngineTransportAdmissionSha256.h',
      'EngineTransportRelays.cpp','EngineTransportRelays.h','EngineTransportFamilies.cpp','EngineTransportFamilies.h','EngineTransportSourceWrites.cpp','EngineTransportSourceWrites.h',
      'EngineOriginals.asm','EngineTransportOriginals.asm','EngineTransportOriginals.h'|ForEach-Object{Join-Path $native $_})+
    @('EngineNodeContext.h','EngineInlineProbe.h','EngineSchedulerContext.h','EngineWaitContext.h'|ForEach-Object{Join-Path $repo ('native/RizToolbox.JudgeContext/'+$_)})
$inputPaths=@($sourcePaths+$hostHeaderPaths+@($targetSource,$targetManifestPath,$proxySource,$proxyManifestPath,$kernelSource,$kernelLib)|Sort-Object -Unique)
$locks=[Collections.Generic.List[IDisposable]]::new();$null=New-Item -ItemType Directory -Path $output
$manifest=[ordered]@{schemaVersion=1;kind='engine-transport-owned-candidate-build';managedHost=[bool]$ManagedHost;verifyBenchmark=[bool]$VerifyBenchmark;passed=$false;buildExitCode=$null;sourceHashesUnchanged=$false
    fixtureExecuted=$false;gameExecuted=$false;installed=$false;productionAdmissionVerified=$false;inputs=@();files=@();failure=$null}
try{
    foreach($path in $inputPaths){$locks.Add([IO.File]::Open($path,'Open','Read','Read'))}
    $manifest.inputs=@($inputPaths|ForEach-Object{[ordered]@{path=$_;sha256=Hash $_;bytes=(Get-Item -LiteralPath $_).Length}})
    WriteJson $manifest.inputs (Join-Path $output 'source-before.json')
    $archive=Join-Path $output 'owned-candidate-inputs.zip';$zip=[IO.Compression.ZipFile]::Open($archive,'Create')
    try{$index=0;foreach($row in $manifest.inputs){$relative=[IO.Path]::GetRelativePath($repo,$row.path).Replace('\','/');$entryName=if($relative.StartsWith('../') -or [IO.Path]::IsPathRooted($relative)){'external/'+('{0:D4}-' -f $index++)+[IO.Path]::GetFileName($row.path)}else{'repo/'+$relative};
        $entry=$zip.CreateEntry($entryName,[IO.Compression.CompressionLevel]::Optimal);$to=$entry.Open();$from=[IO.File]::OpenRead($row.path);try{$from.CopyTo($to)}finally{$from.Dispose();$to.Dispose()}}}finally{$zip.Dispose()}
    $manifest.inputArchive=[ordered]@{path=$archive;sha256=Hash $archive;entries=$manifest.inputs.Count}
    $target=Join-Path $output ([IO.Path]::GetFileName($targetSource));$proxy=Join-Path $output 'winhttp.dll';$kernel=Join-Path $output 'RizToolbox.EngineNodeKernel.dll'
    Copy-Item -LiteralPath $targetSource -Destination $target;Copy-Item -LiteralPath $proxySource -Destination $proxy;Copy-Item -LiteralPath $kernelSource -Destination $kernel
    Copy-Item -LiteralPath $kernelLib -Destination (Join-Path $output 'kernel.lib')
    [IO.File]::WriteAllLines((Join-Path $output 'owned-target.def'),@(('LIBRARY '+[IO.Path]::GetFileName($target)),'EXPORTS','UnityMain2'),[Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllLines((Join-Path $output 'seh.def'),@('LIBRARY ntdll.dll','EXPORTS','__C_specific_handler'),[Text.UTF8Encoding]::new($false))
    $exe=Join-Path $output 'RizTransportOwnedHost.exe'
    $hostCmd=@('@echo off','setlocal DisableDelayedExpansion',"call `"$vcvarsPath`" >nul",'if errorlevel 1 exit /b 1',
        "lib /nologo /machine:x64 /def:`"$output\owned-target.def`" /out:`"$output\owned-target.lib`"",'if errorlevel 1 exit /b 1',
        "lib /nologo /machine:x64 /def:`"$output\seh.def`" /out:`"$output\seh.lib`"",'if errorlevel 1 exit /b 1',
        "cl /nologo /std:c++17 /O2 /W4 /WX /EHa /MD /Zi /guard:cf$hostHeaderOption /Fo`"$output\Host.obj`" /Fd`"$output\Host.compiler.pdb`" `"$hostSource`" `"$output\owned-target.lib`" /link /OUT:`"$exe`" /PDB:`"$output\Host.pdb`" /MAP:`"$output\host.map`" /INCREMENTAL:NO /DYNAMICBASE /NXCOMPAT /CETCOMPAT /GUARD:CF",'if errorlevel 1 exit /b 1','exit /b 0')
    $hostCommandPath=Join-Path $output 'build-owned-host.cmd';[IO.File]::WriteAllLines($hostCommandPath,$hostCmd,[Text.UTF8Encoding]::new($false))
    & $env:ComSpec /d /v:off /c $hostCommandPath 2>&1|Tee-Object -FilePath (Join-Path $output 'host-build.log');if($LASTEXITCODE){throw "Owned host build failed: $LASTEXITCODE"}
    & $Python -B (Join-Path $PSScriptRoot 'generate-engine-transport-profile.py') --target $target --executable $exe --doorstop $proxy --kernel $kernel --profile owned --output-header (Join-Path $output 'EngineTransportProfile.Generated.h') --output-json (Join-Path $output 'native-profile.json')
    if($LASTEXITCODE){throw 'Actual compile-profile generation failed.'}
    $commands=@('@echo off','setlocal DisableDelayedExpansion',"call `"$vcvarsPath`" >nul",'if errorlevel 1 exit /b 1')
    $objects=@();foreach($name in @('EngineTransportFirstInstall','EngineTransportRelays','EngineTransportFamilies','EngineTransportSourceWrites')){
        $object=Join-Path $output ($name+'.obj');$objects+=('"'+$object+'"')
        $commands+="cl /nologo /c /std:c++17 /O2 /Oi /W4 /WX /GS- /GR- /Zl /EHa /Zi /DRIZ_ENGINE_TRANSPORT_OWNED=1 /DRIZ_ENGINE_TRANSPORT_OWNED_IMAGE=1 /I`"$output`" /Fo`"$object`" /Fd`"$output\$name.compiler.pdb`" `"$native\$name.cpp`"";$commands+='if errorlevel 1 exit /b 1'
    }
    foreach($name in @('EngineOriginals','EngineTransportOriginals')){$object=Join-Path $output ($name+'.obj');$objects+=('"'+$object+'"');$commands+="ml64 /nologo /c /Zi /Fo`"$object`" `"$native\$name.asm`"";$commands+='if errorlevel 1 exit /b 1'}
    $bootstrap=Join-Path $output 'RizToolbox.EngineBootstrap.dll';$objectArgs=$objects -join ' '
    $commands+="link /nologo /DLL /NODEFAULTLIB /MACHINE:X64 /ENTRY:RizEngineTransportBootstrapEntry /INCREMENTAL:NO /OPT:REF /OPT:NOICF /DYNAMICBASE /NXCOMPAT /CETCOMPAT /GUARD:NO /DEBUG /MAP:`"$output\bootstrap.map`" $objectArgs `"$output\kernel.lib`" `"$output\seh.lib`" kernel32.lib /OUT:`"$bootstrap`" /IMPLIB:`"$output\bootstrap.lib`" /PDB:`"$output\Bootstrap.pdb`"";$commands+='if errorlevel 1 exit /b 1'
    $commands+="dumpbin /nologo /headers /imports /exports /unwindinfo `"$bootstrap`" > `"$output\bootstrap.pe.txt`"";$commands+='if errorlevel 1 exit /b 1';$commands+='exit /b 0'
    $commandPath=Join-Path $output 'build-transport-bootstrap.cmd';[IO.File]::WriteAllLines($commandPath,$commands,[Text.UTF8Encoding]::new($false))
    & $env:ComSpec /d /v:off /c $commandPath 2>&1|Tee-Object -FilePath (Join-Path $output 'bootstrap-build.log');$manifest.buildExitCode=$LASTEXITCODE
    if($LASTEXITCODE){throw "V5 Bootstrap strict build failed: $LASTEXITCODE"}
    foreach($row in $manifest.inputs){if((Hash $row.path) -ne $row.sha256){throw "Candidate input changed: $($row.path)"}}
    $manifest.sourceHashesUnchanged=$true;$manifest.passed=$true
}catch{$manifest.failure=$_.Exception.Message;throw}
finally{foreach($lock in $locks){$lock.Dispose()};$manifest.files=@(Get-ChildItem -LiteralPath $output -File|Where-Object Name -ne 'owned-candidate-build.json'|ForEach-Object{[ordered]@{path=$_.FullName;sha256=Hash $_.FullName;bytes=$_.Length}});WriteJson $manifest (Join-Path $output 'owned-candidate-build.json')}
Write-Output "V5 owned native candidate built; no executable started: $output"
