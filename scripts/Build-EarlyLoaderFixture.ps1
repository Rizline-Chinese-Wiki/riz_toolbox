#requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$HelperDirectory,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string]$DobbyPath,
    [string]$VcVars = ''
)
. (Join-Path $PSScriptRoot 'Toolchain.Common.ps1')
if (-not $VcVars) { $VcVars = Get-RizVcVarsPath }

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$source = Join-Path $repoRoot 'native/RizToolbox.EarlyLoaderFixture'
$output = [IO.Path]::GetFullPath($OutputDirectory)
$vcvarsPath = (Resolve-Path -LiteralPath $VcVars).Path
if (-not $DobbyPath) { $DobbyPath = Join-Path $repoRoot '.tools/bepinex-789/BepInEx/core/dobby.dll' }
$dobby = (Resolve-Path -LiteralPath $DobbyPath).Path
$helper = (Resolve-Path -LiteralPath (Join-Path $HelperDirectory 'RizToolbox.JudgeContext.dll')).Path
function Hash([string]$Path) { (Get-FileHash -Algorithm SHA256 -LiteralPath $Path).Hash.ToLowerInvariant() }
if ((Hash $dobby) -ne '8015de7d867245a1095d13947a63763878e4cf5fd3d3089b63cc39200b055ded') {
    throw 'The early loader fixture requires the fixed Dobby binary.'
}
foreach ($path in @($source, $output, $vcvarsPath, $dobby, $helper)) {
    if ($path -match '[\r\n"%&|<>^]') { throw 'Build paths contain unsupported cmd metacharacters.' }
}
$inputNames = @('EarlyLoaderShared.h','Recorder.cpp','Proxy.cpp','ImporterApi.h','Importer.cpp','Host.cpp','OwnedTargets.asm','EarlyLoaderDebugger.cpp')
foreach ($name in $inputNames) {
    if (-not (Test-Path -LiteralPath (Join-Path $source $name) -PathType Leaf)) { throw "Required early loader fixture input missing: $name" }
}
New-Item -ItemType Directory -Path $output -Force | Out-Null
Copy-Item -LiteralPath $dobby -Destination (Join-Path $output 'dobby.dll') -Force
Copy-Item -LiteralPath $helper -Destination (Join-Path $output 'RizToolbox.JudgeContext.dll') -Force
$helperExports = @('RizEngineNodeContext_GetAbiVersion','RizEngineNodeContext_GetLimit','RizEngineNodeContext_GetFrameSize',
    'RizEngineNodeContext_GetCompletionSize','RizEngineNodeContext_GetEntry','RizEngineNodeContext_Configure',
    'RizEngineNodeContext_Attach','RizEngineNodeContext_GetInfo')
[IO.File]::WriteAllLines((Join-Path $output 'helper-import.def'), @('LIBRARY RizToolbox.JudgeContext.dll','EXPORTS') + $helperExports, [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllLines((Join-Path $output 'dobby-import.def'), @('LIBRARY dobby.dll','EXPORTS','DobbyPrepare','DobbyCommit'), [Text.UTF8Encoding]::new($false))
$common = '/nologo /std:c++17 /O2 /W4 /WX /EHa /MD /Zi /guard:cf'
$link = '/INCREMENTAL:NO /OPT:NOICF /DYNAMICBASE /NXCOMPAT /CETCOMPAT /GUARD:CF'
$commands = @(
    '@echo off','chcp 65001 >nul',"call `"$vcvarsPath`" >nul",'if errorlevel 1 exit /b 1',
    "lib /nologo /machine:x64 /def:`"$output\helper-import.def`" /out:`"$output\EarlyHelper.lib`"",'if errorlevel 1 exit /b 1',
    "lib /nologo /machine:x64 /def:`"$output\dobby-import.def`" /out:`"$output\EarlyDobby.lib`"",'if errorlevel 1 exit /b 1',
    "cl /nologo /c /std:c++17 /O2 /Oi /W4 /WX /GS- /Zl /Fo`"$output\Recorder.obj`" `"$source\Recorder.cpp`"",'if errorlevel 1 exit /b 1',
    "link /nologo /DLL /ENTRY:DllMain /NODEFAULTLIB /MACHINE:X64 `"$output\Recorder.obj`" kernel32.lib /OUT:`"$output\RizOwnedRecorder.dll`" /IMPLIB:`"$output\RizOwnedRecorder.lib`" /INCREMENTAL:NO /OPT:NOICF /DYNAMICBASE /NXCOMPAT /CETCOMPAT /GUARD:NO",'if errorlevel 1 exit /b 1',
    "cl $common /LD /Fo`"$output\Proxy.obj`" /Fd`"$output\Proxy.compiler.pdb`" `"$source\Proxy.cpp`" /link `"$output\RizOwnedRecorder.lib`" `"$output\EarlyHelper.lib`" `"$output\EarlyDobby.lib`" /OUT:`"$output\RizOwnedProxy.dll`" /IMPLIB:`"$output\RizOwnedProxy.lib`" /PDB:`"$output\Proxy.pdb`" $link",'if errorlevel 1 exit /b 1',
    "ml64 /nologo /c /Zi /Fo`"$output\OwnedTargets.obj`" `"$source\OwnedTargets.asm`"",'if errorlevel 1 exit /b 1',
    "cl $common /LD /Fo`"$output\Importer.obj`" /Fd`"$output\Importer.compiler.pdb`" `"$source\Importer.cpp`" /link `"$output\OwnedTargets.obj`" `"$output\RizOwnedProxy.lib`" `"$output\RizOwnedRecorder.lib`" `"$output\EarlyHelper.lib`" /OUT:`"$output\RizOwnedImporter.dll`" /IMPLIB:`"$output\RizOwnedImporter.lib`" /PDB:`"$output\Importer.pdb`" $link",'if errorlevel 1 exit /b 1',
    "cl $common /Fo`"$output\Host.obj`" /Fd`"$output\Host.compiler.pdb`" `"$source\Host.cpp`" /link `"$output\RizOwnedImporter.lib`" `"$output\RizOwnedProxy.lib`" `"$output\RizOwnedRecorder.lib`" `"$output\EarlyHelper.lib`" /OUT:`"$output\RizOwnedHost.exe`" /PDB:`"$output\Host.pdb`" $link",'if errorlevel 1 exit /b 1',
    "cl $common /Fo`"$output\EarlyLoaderDebugger.obj`" /Fd`"$output\EarlyLoaderDebugger.compiler.pdb`" `"$source\EarlyLoaderDebugger.cpp`" /link /OUT:`"$output\RizOwnedDebugger.exe`" /PDB:`"$output\EarlyLoaderDebugger.pdb`" $link",'if errorlevel 1 exit /b 1'
)
foreach ($name in @('RizOwnedRecorder.dll','RizOwnedProxy.dll','RizOwnedImporter.dll','RizOwnedHost.exe','RizOwnedDebugger.exe','RizToolbox.JudgeContext.dll','dobby.dll')) {
    $commands += "dumpbin /nologo /headers /imports /exports /loadconfig `"$output\$name`" > `"$output\$name.pe.txt`""
    $commands += 'if errorlevel 1 exit /b 1'
}
$cmdPath = Join-Path $output 'build-early-loader.cmd'
[IO.File]::WriteAllLines($cmdPath, $commands, [Text.UTF8Encoding]::new($false))
& $env:ComSpec /d /c $cmdPath 2>&1 | Tee-Object -FilePath (Join-Path $output 'build.log')
if ($LASTEXITCODE -ne 0) { throw "Owned early loader fixture build failed: $LASTEXITCODE" }
$files = @('RizOwnedRecorder.dll','RizOwnedProxy.dll','RizOwnedImporter.dll','RizOwnedHost.exe','RizOwnedDebugger.exe','RizToolbox.JudgeContext.dll','dobby.dll') |
    ForEach-Object { $path = Join-Path $output $_; [ordered]@{name=$_;path=$path;sha256=(Hash $path)} }
$sources = @($inputNames | ForEach-Object { $path = Join-Path $source $_; [ordered]@{path=$path;sha256=(Hash $path)} })
$sources += [ordered]@{path=$PSCommandPath;sha256=(Hash $PSCommandPath)}
[ordered]@{schemaVersion=1;kind='owned-early-loader-fixture-build';files=@($files);sources=$sources;compilerEnvironment=$vcvarsPath;
    pinnedHelperInput=$helper;pinnedHelperSha256=(Hash $helper);pinnedDobbySha256=(Hash $dobby);
    fixturesExecuted=$false;gameCodeExecuted=$false;gameFirstInstallVerified=$false;installed=$false} |
    ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $output 'fixture-build-manifest.json') -Encoding utf8
Write-Output "Owned early loader fixture: $output"
