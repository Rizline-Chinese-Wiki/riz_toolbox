#requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string]$VcVars = 'C:/Program Files (x86)/Microsoft Visual Studio/2019/BuildTools/VC/Auxiliary/Build/vcvars64.bat'
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($OutputDirectory)
$sourceRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$allowed = [IO.Path]::GetFullPath((Join-Path $sourceRoot '..')) + [IO.Path]::DirectorySeparatorChar
if (-not $root.StartsWith($allowed, [StringComparison]::OrdinalIgnoreCase) -or (Test-Path -LiteralPath $root)) {
    throw 'Use a fresh output directory beside this continuation source.'
}
if (($root + $VcVars) -match '[\r\n"%&|<>^]') { throw 'Unsupported native build path characters.' }
function Fact([string]$Path) {
    $item = Get-Item -LiteralPath $Path
    [ordered]@{ path = $item.FullName; sha256 = (Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash.ToLowerInvariant(); bytes = $item.Length }
}
$null = New-Item -ItemType Directory -Path $root
$frozen = Join-Path $root 'frozen-source'
$null = New-Item -ItemType Directory -Path $frozen
$inputs = @(
    'native/PlaybackLogicalHandoff.h', 'native/PlaybackInitialization.h',
    'native/RizToolbox.PlaybackSourcePrototype/PlaybackSourceContext.cpp',
    'native/RizToolbox.PlaybackSourcePrototype/PlaybackSourceContext.h',
    'native/RizToolbox.PlaybackSourcePrototype/PlaybackPlayerWatch.inc',
    'native/RizToolbox.PlaybackSourcePrototype/PlaybackProgressWatch.inc',
    'native/RizToolbox.PlaybackSourcePrototype/PlaybackUnityBackingWatch.inc','native/RizToolbox.PlaybackSourcePrototype/PlaybackLowerPitch.inc',
    'native/RizToolbox.PlaybackSourcePrototype/PlaybackWriterCall.asm',
    'native/RizToolbox.PlaybackSourcePrototype/PlayerWatchNativeFixture.cpp',
    'native/RizToolbox.PlaybackSourcePrototype/PlayerWatchFirstCaptureFixture.asm',
    'native/RizToolbox.SingleReturnPrototype/SingleReturnContext.cpp',
    'native/RizToolbox.SingleReturnPrototype/SingleReturnContext.h',
    'native/RizToolbox.SingleReturnPrototype/SingleReturnHandoff.inc',
    'native/RizToolbox.SingleReturnPrototype/SingleReturnInitialCapture.inc','native/RizToolbox.SingleReturnPrototype/SingleReturnLowerPitch.inc',
    'native/RizToolbox.PlaybackSourcePrototype/Build-PlayerConstructorNative.ps1'
)
$sources = @($inputs | ForEach-Object {
    $relative = $_.Replace('/', [IO.Path]::DirectorySeparatorChar)
    $destination = Join-Path $frozen $relative
    $null = New-Item -ItemType Directory -Force -Path (Split-Path -Parent $destination)
    Copy-Item -LiteralPath (Join-Path $sourceRoot $relative) -Destination $destination
    Fact $destination
})
$flags = '/nologo /c /std:c++17 /O2 /W4 /WX /EHa /fp:strict /MD /Z7 /guard:cf'
$link = '/nologo /DLL /DEBUG /INCREMENTAL:NO /OPT:NOICF /DYNAMICBASE /NXCOMPAT /CETCOMPAT /GUARD:CF'
$commands = @('@echo off', "call `"$VcVars`" >nul", 'if errorlevel 1 exit /b %errorlevel%')
$commands += "cl $flags /Fo`"$root/SingleReturnContext.obj`" /DRIZ_SINGLE_RETURN_PROTOTYPE_BUILD `"$frozen/native/RizToolbox.SingleReturnPrototype/SingleReturnContext.cpp`""
$commands += 'if errorlevel 1 exit /b %errorlevel%'
$commands += "cl $flags /Fo`"$root/PlaybackSourceContext.obj`" `"$frozen/native/RizToolbox.PlaybackSourcePrototype/PlaybackSourceContext.cpp`""
$commands += 'if errorlevel 1 exit /b %errorlevel%'
$commands += "ml64 /nologo /c /Zi /Fo`"$root/PlaybackWriterCall.obj`" `"$frozen/native/RizToolbox.PlaybackSourcePrototype/PlaybackWriterCall.asm`""
$commands += 'if errorlevel 1 exit /b %errorlevel%'
$commands += "cl $flags /Fo`"$root/PlayerWatchNativeFixture.obj`" `"$frozen/native/RizToolbox.PlaybackSourcePrototype/PlayerWatchNativeFixture.cpp`""
$commands += 'if errorlevel 1 exit /b %errorlevel%'
$commands += "ml64 /nologo /c /Zi /Fo`"$root/PlayerWatchFirstCaptureFixture.obj`" `"$frozen/native/RizToolbox.PlaybackSourcePrototype/PlayerWatchFirstCaptureFixture.asm`""
$commands += 'if errorlevel 1 exit /b %errorlevel%'
$commands += "link $link `"$root/SingleReturnContext.obj`" /OUT:`"$root/RizToolbox.SingleReturn.Prototype.dll`" /PDB:`"$root/SingleReturn.Prototype.pdb`" /IMPLIB:`"$root/SingleReturn.Prototype.lib`""
$commands += 'if errorlevel 1 exit /b %errorlevel%'
$commands += "link $link `"$root/PlaybackSourceContext.obj`" `"$root/PlaybackWriterCall.obj`" /OUT:`"$root/RizToolbox.PlaybackSourcePrototype.dll`" /PDB:`"$root/PlaybackSourcePrototype.pdb`" /IMPLIB:`"$root/PlaybackSourcePrototype.lib`""
$commands += 'if errorlevel 1 exit /b %errorlevel%'
$commands += "link /nologo /DEBUG /INCREMENTAL:NO /OPT:NOICF /DYNAMICBASE /NXCOMPAT /CETCOMPAT /GUARD:CF `"$root/PlayerWatchNativeFixture.obj`" `"$root/PlayerWatchFirstCaptureFixture.obj`" /OUT:`"$root/PlayerWatchNativeFixture.exe`" /PDB:`"$root/PlayerWatchNativeFixture.pdb`""
$commands += 'if errorlevel 1 exit /b %errorlevel%'
foreach ($name in @('RizToolbox.SingleReturn.Prototype', 'RizToolbox.PlaybackSourcePrototype')) {
    foreach ($kind in @('headers', 'exports', 'unwindinfo', 'pdbpath:verbose')) {
        $suffix = $kind.Replace(':', '-')
        $commands += "dumpbin /nologo /$kind `"$root/$name.dll`" > `"$root/$name.$suffix.txt`""
        $commands += 'if errorlevel 1 exit /b %errorlevel%'
    }
}
[IO.File]::WriteAllLines((Join-Path $root 'build.cmd'), $commands, [Text.UTF8Encoding]::new($false))
$result = [ordered]@{
    kind = 'owned-player-constructor-native-build'; passed = $false; actualExitCode = $null
    sourceVersion = 4; frameBytes = 168; infoBytes = 136; playerWatchVersion = 2
    initializationProtocol = 2; initialCaptureProtocol = 1; compilerFlags = $flags
    sources = $sources; files = @(); gameExecuted = $false; installed = $false; productionAdmission = $false
}
try {
    & $env:ComSpec /d /c (Join-Path $root 'build.cmd') 1> (Join-Path $root 'build.stdout.log') 2> (Join-Path $root 'build.stderr.log')
    $result.actualExitCode = $LASTEXITCODE
    [IO.File]::WriteAllText((Join-Path $root 'build.exit.txt'), [string]$LASTEXITCODE)
    if ($LASTEXITCODE -ne 0) { throw 'Actual native helper compilation failed.' }
    foreach ($row in $sources) { if ((Fact $row.path).sha256 -cne $row.sha256) { throw 'Frozen native source changed.' } }
    $result.passed = $true
} finally {
    $result.files = @(Get-ChildItem -LiteralPath $root -File | Sort-Object FullName | ForEach-Object { Fact $_.FullName })
    [IO.File]::WriteAllText((Join-Path $root 'native-build-result.json'), ($result | ConvertTo-Json -Depth 12), [Text.UTF8Encoding]::new($false))
}
Write-Output 'Actual Source V4 / PlayerWatch V2 native helpers built.'
