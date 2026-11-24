#requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string]$GameDirectory = 'D:/SteamLibrary/steamapps/common/Rizline',
    [string]$Python = '',
    [string]$VcVars = '',
    [switch]$SkipManaged
)
. (Join-Path $PSScriptRoot 'Toolchain.Common.ps1')
if (-not $VcVars) { $VcVars = Get-RizVcVarsPath }
if (-not $Python) { $Python = Get-RizPythonPath }

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$output = [IO.Path]::GetFullPath($OutputDirectory)
$game = [IO.Path]::GetFullPath($GameDirectory)
if (Test-Path -LiteralPath $output) { throw 'Use a new candidate directory; preserve every previous build.' }
foreach ($path in @($output,$game,$repo,$VcVars,$Python)) {
    if ($path -match '[\r\n"%&|<>^]') { throw 'Unsupported compiler path metacharacter.' }
}
if ($output.TrimEnd('\','/').Equals($game.TrimEnd('\','/'),[StringComparison]::OrdinalIgnoreCase) -or
    $output.StartsWith($game.TrimEnd('\','/') + [IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) {
    throw 'Build output must be separate from the game installation.'
}
$utf8 = [Text.UTF8Encoding]::new($false)
function Hash([string]$Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }
$kernel = Join-Path $repo 'artifacts/package/BepInEx/plugins/RizToolbox/RizToolbox.EngineNodeKernel.dll'
if ((Hash $kernel) -ne '923486d2901ab6f66ea6d90d99cc65b1afafbc3275c338f0fd71ea5d22bc5662') { throw 'The verified fixed Kernel changed.' }
$sourcePaths = @('scripts/Build-EngineProductionCandidate.ps1','scripts/Build-Doorstop.ps1',
    'docs/validation/foundation-engine-production-20261025/generate-production-profile.py')
$sourcePaths += @(Get-ChildItem -LiteralPath (Join-Path $repo 'native/RizToolbox.EngineBootstrap'),(Join-Path $repo 'native/RizToolbox.Doorstop') -File -Recurse |
    ForEach-Object { [IO.Path]::GetRelativePath($repo,$_.FullName) })
$sources = @($sourcePaths | ForEach-Object { [ordered]@{path=(Join-Path $repo $_);sha256=(Hash (Join-Path $repo $_))} })
$managedPaths = @('Directory.Build.props','global.json')
$managedPaths += @(Get-ChildItem -LiteralPath (Join-Path $repo 'src') -File -Recurse |
    Where-Object { $_.Extension -in @('.cs','.csproj','.props','.targets') -and $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } |
    ForEach-Object { [IO.Path]::GetRelativePath($repo,$_.FullName) })
$managedSources = @($managedPaths | Sort-Object -Unique | ForEach-Object { [ordered]@{path=(Join-Path $repo $_);sha256=(Hash (Join-Path $repo $_))} })
New-Item -ItemType Directory -Path $output | Out-Null
$sources | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $output 'source-before-native-build.json') -Encoding utf8
$managedSources | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $output 'source-before-managed-build.json') -Encoding utf8
$doorstop = Join-Path $output 'doorstop'
& (Join-Path $PSScriptRoot 'Build-Doorstop.ps1') -OutputDirectory $doorstop -FixedSystemWinHttp -EngineBootstrap
if ($LASTEXITCODE -ne 0) { throw 'Engine candidate Doorstop build failed.' }
foreach ($name in @('winhttp.dll','doorstop.map')) { Copy-Item -LiteralPath (Join-Path $doorstop $name) -Destination $output }
Copy-Item -LiteralPath $kernel -Destination (Join-Path $output 'RizToolbox.EngineNodeKernel.dll')
$generator = Join-Path $repo 'docs/validation/foundation-engine-production-20261025/generate-production-profile.py'
& $Python $generator native --game $game --doorstop (Join-Path $output 'winhttp.dll') --kernel (Join-Path $output 'RizToolbox.EngineNodeKernel.dll') --header (Join-Path $output 'EngineBootstrapProductionProfile.h') --output (Join-Path $output 'production-native-profile.json')
if ($LASTEXITCODE -ne 0) { throw 'Fixed production native facts generation failed.' }
$kernelNames = @('RizEngineNodeContext_GetAbiVersion','RizEngineNodeContext_GetLimit','RizEngineNodeContext_GetFrameSize','RizEngineNodeContext_GetCompletionSize',
    'RizEngineNodeContext_GetEntry','RizEngineNodeContext_Configure','RizEngineNodeContext_Attach','RizEngineNodeContext_GetInfo')
[IO.File]::WriteAllLines((Join-Path $output 'kernel.def'),@('LIBRARY RizToolbox.EngineNodeKernel.dll','EXPORTS') + $kernelNames,$utf8)
[IO.File]::WriteAllLines((Join-Path $output 'seh.def'),@('LIBRARY ntdll.dll','EXPORTS','__C_specific_handler'),$utf8)
$native = Join-Path $repo 'native/RizToolbox.EngineBootstrap'
$commands = @('@echo off','chcp 65001 >nul',"call `"$VcVars`" >nul",'if errorlevel 1 exit /b 1',
    "lib /nologo /machine:x64 /def:`"$output\kernel.def`" /out:`"$output\kernel.lib`"",'if errorlevel 1 exit /b 1',
    "lib /nologo /machine:x64 /def:`"$output\seh.def`" /out:`"$output\seh.lib`"",'if errorlevel 1 exit /b 1',
    "ml64 /nologo /c /Zi /Fo`"$output\EngineOriginals.obj`" `"$native\EngineOriginals.asm`"",'if errorlevel 1 exit /b 1',
    "cl /nologo /c /std:c++17 /O2 /Oi /W4 /WX /GS- /Zl /EHa /Zi /DRIZ_ENGINE_BOOTSTRAP_PRODUCTION=1 /I`"$output`" /Fo`"$output\EngineFirstInstall.obj`" /Fd`"$output\EngineFirstInstall.compiler.pdb`" `"$native\EngineFirstInstall.cpp`"",'if errorlevel 1 exit /b 1',
    "link /nologo /DLL /NODEFAULTLIB /MACHINE:X64 /ENTRY:RizEngineBootstrapEntry /INCREMENTAL:NO /OPT:REF /OPT:NOICF /DYNAMICBASE /NXCOMPAT /CETCOMPAT /GUARD:NO /DEBUG /MAP:`"$output\bootstrap.map`" `"$output\EngineFirstInstall.obj`" `"$output\EngineOriginals.obj`" `"$output\kernel.lib`" `"$output\seh.lib`" kernel32.lib /OUT:`"$output\RizToolbox.EngineBootstrap.dll`" /IMPLIB:`"$output\bootstrap.lib`" /PDB:`"$output\Bootstrap.pdb`"",'if errorlevel 1 exit /b 1')
[IO.File]::WriteAllLines((Join-Path $output 'build-bootstrap.cmd'),$commands,$utf8)
& $env:ComSpec /d /c (Join-Path $output 'build-bootstrap.cmd') 2>&1 | Tee-Object -FilePath (Join-Path $output 'build-bootstrap.log')
if ($LASTEXITCODE -ne 0) { throw 'Production-contract Bootstrap compilation failed.' }
& $Python $generator managed --profile (Join-Path $output 'production-native-profile.json') --bootstrap (Join-Path $output 'RizToolbox.EngineBootstrap.dll') --source (Join-Path $output 'NativeEngineProductionProfile.Generated.cs') --output (Join-Path $output 'production-managed-profile.json')
if ($LASTEXITCODE -ne 0) { throw 'Fixed managed facts generation failed.' }
if (-not $SkipManaged) {
    & dotnet build (Join-Path $repo 'src/RizToolbox.Loader/RizToolbox.Loader.csproj') -c Release --no-restore --nologo -m:1 -p:UseSharedCompilation=false -v:minimal -o (Join-Path $output 'managed') "-p:RizEngineProductionProfileSource=$(Join-Path $output 'NativeEngineProductionProfile.Generated.cs')" 2>&1 | Tee-Object -FilePath (Join-Path $output 'build-managed.log')
    if ($LASTEXITCODE -ne 0) { throw 'Production candidate managed build failed.' }
    foreach ($row in $managedSources) { if ((Hash $row.path) -ne $row.sha256) { throw "Declared managed build source changed: $($row.path)" } }
    $managedProfile = Get-Content -LiteralPath (Join-Path $output 'production-managed-profile.json') -Raw | ConvertFrom-Json
    $managedFiles = @('Sdk','Core','Runtime','Adapter','Hosting','Loader') | ForEach-Object {
        $path = Join-Path $output "managed/RizToolbox.$_.dll"
        [ordered]@{path=$path;sha256=(Hash $path)}
    }
    [ordered]@{kind='fixed-game-engine-managed-candidate-build';schemaVersion=1;generatedSource=$managedProfile.generatedSource;
        sources=$managedSources;files=@($managedFiles);productionAdmissionProved=$false;gameExecuted=$false;installed=$false} |
        ConvertTo-Json -Depth 7 | Set-Content -LiteralPath (Join-Path $output 'managed-build-manifest.json') -Encoding utf8
}
foreach ($row in $sources) { if ((Hash $row.path) -ne $row.sha256) { throw "Declared native build source changed: $($row.path)" } }
$files = @('winhttp.dll','RizToolbox.EngineBootstrap.dll','RizToolbox.EngineNodeKernel.dll','doorstop.map','bootstrap.map','production-native-profile.json','production-managed-profile.json') |
    ForEach-Object { [ordered]@{path=(Join-Path $output $_);sha256=(Hash (Join-Path $output $_))} }
[ordered]@{kind='fixed-game-engine-production-candidate-build';schemaVersion=1;sources=$sources;files=@($files);
    nativeProfile='production-native-profile.json';managedProfile='production-managed-profile.json';managedBuilt=[bool](-not $SkipManaged);
    productionAdmissionProved=$false;gameExecuted=$false;installed=$false;testProcessesStarted=0} |
    ConvertTo-Json -Depth 7 | Set-Content -LiteralPath (Join-Path $output 'production-build-manifest.json') -Encoding utf8
Write-Output "Fixed production candidate built (startup permission remains unavailable): $output"
