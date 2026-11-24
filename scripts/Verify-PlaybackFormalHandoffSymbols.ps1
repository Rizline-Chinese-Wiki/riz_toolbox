#requires -Version 7.2
[CmdletBinding()]
param([Parameter(Mandatory)][string]$ExecutionResult,[Parameter(Mandatory)][string]$BuildDirectory,
    [Parameter(Mandatory)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
function Hash([string]$Path){(Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()}
function Fact([string]$Path){$p=Get-Item -LiteralPath $Path;[ordered]@{path=$p.FullName;sha256=Hash $p.FullName;bytes=$p.Length}}
$out=[IO.Path]::GetFullPath($OutputDirectory);$build=[IO.Path]::GetFullPath($BuildDirectory)
if(Test-Path -LiteralPath $out){throw 'Use a fresh portable-symbol proof directory.'}
$source=Split-Path -Parent $PSScriptRoot;$result=Get-Content -LiteralPath $ExecutionResult -Raw|ConvertFrom-Json
if($result.passed -ne $true){throw 'Actual current-source occurrence execution is required.'}
$executionFiles=@{}
foreach($row in @($result.inputs)+@($result.managedArtifacts)){
    if((Hash $row.path) -cne $row.sha256){throw 'An actual execution input or managed output changed.'}
    $executionFiles[[IO.Path]::GetFullPath($row.path)]=$row.sha256
}
$frozen=@{};foreach($row in $result.sources){if((Hash $row.path) -cne $row.sha256){throw 'A compiled source changed.'};$frozen[[IO.Path]::GetFullPath($row.path)]=$row.sha256}
New-Item -ItemType Directory -Path $out|Out-Null
$binary=Join-Path $build 'managed'
if([IO.Path]::GetFullPath($result.inputs[0].path) -cne [IO.Path]::GetFullPath((Join-Path $binary 'RizToolbox.Gameplay.Tests.dll'))){throw 'The supplied build is not the actual executed test assembly.'}
$images=@();$count=0
foreach($image in Get-ChildItem -LiteralPath $binary -File -Filter 'RizToolbox.*.dll'|Sort-Object Name){
    $pdb=[IO.Path]::ChangeExtension($image.FullName,'.pdb')
    foreach($path in @($image.FullName,$pdb)){
        if(-not $executionFiles.ContainsKey($path) -or (Hash $path) -cne $executionFiles[$path]){throw 'PE/PDB was not an exact retained artifact of this execution.'}
    }
    $peStream=[IO.File]::OpenRead($image.FullName);$pdbStream=[IO.File]::OpenRead($pdb)
    $pe=[Reflection.PortableExecutable.PEReader]::new($peStream);$provider=[Reflection.Metadata.MetadataReaderProvider]::FromPortablePdbStream($pdbStream)
    try{
        $reader=$provider.GetMetadataReader();$id=[byte[]]$reader.DebugMetadataHeader.Id;$entries=@($pe.ReadDebugDirectory()|Where-Object Type -eq 'CodeView')
        if($entries.Count -ne 1){throw 'Expected one actual CodeView record.'};$cv=$pe.ReadCodeViewDebugDirectoryData($entries[0])
        if($id.Length -ne 20 -or $cv.Age -ne 1 -or $cv.Guid -ne [guid]::new([byte[]]$id[0..15]) -or $entries[0].Stamp -ne [BitConverter]::ToUInt32($id,16)){throw 'PE/PDB identity mismatch.'}
        $documents=@()
        foreach($handle in $reader.Documents){
            $doc=$reader.GetDocument($handle);$name=$reader.GetString($doc.Name);$prefix='/_/riz_toolbox/'
            if($name.StartsWith($prefix,[StringComparison]::Ordinal)){$path=[IO.Path]::GetFullPath((Join-Path $source $name.Substring($prefix.Length)))}
            elseif([IO.Path]::IsPathFullyQualified($name)){$path=[IO.Path]::GetFullPath($name)}else{throw "Unmapped PDB source: $name"}
            $expected=[Convert]::ToHexString($reader.GetBlobBytes($doc.Hash)).ToLowerInvariant()
            $generated=$path.StartsWith($build+'\',[StringComparison]::OrdinalIgnoreCase) -and $path -match '[\\/]artifacts[\\/]obj[\\/]'
            if(-not $frozen.ContainsKey($path) -and -not $generated){throw "Source is neither frozen nor generated in this build: $name"}
            if($reader.GetGuid($doc.HashAlgorithm).ToString() -ne '8829d00f-11b8-4213-878b-770e8597ac16' -or (Hash $path) -cne $expected -or $frozen.ContainsKey($path) -and $frozen[$path] -cne $expected){throw "PDB source checksum mismatch: $name"}
            $documents+=@{document=$name;path=$path;sha256=$expected;generatedByThisBuild=$generated;frozenSource=$frozen.ContainsKey($path)}
        }
        $images+=@{image=Fact $image.FullName;pdb=Fact $pdb;codeViewGuid=$cv.Guid.ToString();portablePdbId=[Convert]::ToHexString($id).ToLowerInvariant();codeViewMatches=$true;sourceChecksumsMatch=$true;documents=$documents}
        $count+=$documents.Count
    }finally{$provider.Dispose();$pe.Dispose();$pdbStream.Dispose();$peStream.Dispose()}
}
if($images.Count -ne 5){throw 'Expected the test assembly and its four actual managed dependencies.'}
$proof=[ordered]@{kind='actual-formal-handoff-portable-symbol-source-proof';passed=$true;pePdbPairs=$images.Count;documents=$count;
    execution=Fact $ExecutionResult;executionKind=$result.kind;executedAssembly=Fact $result.inputs[0].path;
    verifier=Fact $PSCommandPath;images=$images;gameExecuted=$false;controlledProviderIssued=($result.controlledProvider -eq $true);
    completeProductionProvider=$false;productionAdmission=$false;crossRunHandoff=($result.crossRunHandoff -eq $true)}
[IO.File]::WriteAllText((Join-Path $out 'test-result.json'),($proof|ConvertTo-Json -Depth 18),[Text.UTF8Encoding]::new($false))
Write-Output "Actual staged source proof: $($images.Count) PE/PDB pairs; $count documents."
