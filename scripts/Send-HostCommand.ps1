#requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$GameDirectory,
    [Parameter(Mandatory)][ValidateSet('status','enable','disable','set-language','shutdown')][string]$Action,
    [string]$PluginId,
    [string]$Language
)
$ErrorActionPreference='Stop'
$Action=$Action.ToLowerInvariant()
if($Action -in @('enable','disable')){
    if([string]::IsNullOrWhiteSpace($PluginId)){throw 'enable/disable requires PluginId.'}
}elseif($PSBoundParameters.ContainsKey('PluginId')){throw 'PluginId is only used by enable/disable.'}
if($Action -eq 'set-language'){
    if([string]::IsNullOrWhiteSpace($Language)){throw 'set-language requires Language.'}
}elseif($PSBoundParameters.ContainsKey('Language')){throw 'Language is only used by set-language.'}
$gameRoot=(Resolve-Path -LiteralPath $GameDirectory -ErrorAction Stop).Path
$dataRoot=Join-Path $gameRoot 'BepInEx/RizToolbox'
if(-not(Test-Path -LiteralPath $dataRoot -PathType Container)){throw 'The installed Toolbox data directory is missing.'}
$id=[Guid]::NewGuid().ToString('N')
$command=[ordered]@{id=$id;action=$Action}
if($PSBoundParameters.ContainsKey('PluginId')){$command.pluginId=$PluginId}
if($PSBoundParameters.ContainsKey('Language')){$command.language=$Language}
$target=Join-Path $dataRoot 'command.json'
$temporary=Join-Path $dataRoot ('command.'+$id+'.tmp')
[IO.File]::WriteAllText($temporary,($command|ConvertTo-Json),[Text.UTF8Encoding]::new($false))
[IO.File]::Move($temporary,$target,$true)
[pscustomobject]@{Id=$id;Action=$Action;CommandPath=$target;StatusPath=(Join-Path $dataRoot 'status.json');Dispatched=$false;
    Message='Command saved. The running host must process it on the game thread; check status.json and toolbox.log for the result.'}
