"""Archive an actually completed production cold candidate without execution."""
from pathlib import Path
import hashlib
import importlib.util
import json
import shutil
import zipfile

batch=Path(__file__).resolve().parent
repo=batch.parents[2]
def read(path):return json.loads(path.read_text(encoding='utf-8-sig'))
def sha(path):return hashlib.sha256(path.read_bytes()).hexdigest()
def require(value,message):
    if not value:raise ValueError(message)
def write(name,value):
    path=batch/name;require(not path.exists(),'Preserve final artifact '+name)
    path.write_text(json.dumps(value,ensure_ascii=False,indent=2)+'\n',encoding='utf-8',newline='\n')
terminal=read(batch/'terminal.json')
require(terminal['actualTerminalObserved'] and terminal['exitCode']==0,'Root actual terminal success required')
attempt=batch/terminal['evidenceDirectory']
require(attempt.resolve().parent==(batch/'attempts').resolve() and not attempt.is_symlink(),'Selected attempt leaves this batch')
command=read(attempt/'command-result.json')
require(command['completed'] and command['buildCompleted'] and command['coldCompleted'] and command['standaloneCompleted'] and command['legacyBuildCompleted'],'Four command stages must actually complete')
cache=Path(command['cache']);require(cache.is_absolute() and cache.resolve().is_relative_to(Path('C:/Users/71957/riz-build-cache').resolve()),'Unexpected build cache')
frozen=read(attempt/'inputs-before.json');require(len(frozen['files'])==626 and len(frozen['previousSourcesChanged'])==4 and len(frozen['newInputs'])==13,'Wrong declared source set')
for row in frozen['files']:require(sha(repo/row['path'])==row['sha256'],'Frozen source changed: '+row['path'])
source_zip=attempt/'source-inputs.zip';require(sha(source_zip)==frozen['sourceArchiveSha256'],'Frozen source ZIP changed')
with zipfile.ZipFile(source_zip) as archive:
    require(len(archive.namelist())==626 and len(set(archive.namelist()))==626,'Wrong source ZIP entries')
    for row in frozen['files']:require(hashlib.sha256(archive.read(row['path'])).hexdigest()==row['sha256'],'Source ZIP byte mismatch')
selected=[('cases',5),('standalone-cases',21)]
totals={}
for directory,count in selected:
    result=read(cache/directory/'fixture-result.json')
    require(result['passed'] and result['verifiedCases']==result['independentProcesses']==count and result['allInputHashesUnchanged'],'Incomplete actual '+directory)
    require(len(result['caseResults'])==len(result['processes'])==count,'Wrong physical case rows')
    for row in result['files']:require(row['beforeSha256']==row['afterSha256']==sha(Path(row['path'])),'Held input changed')
    total_checks=total_calls=total_pairs=0
    seen=set()
    for row in result['caseResults']:
        require(row['name'] not in seen,'Duplicate case');seen.add(row['name'])
        case=cache/directory/row['name'];process=read(case/'process.json');proof=read(case/'result.json');host=read(case/'case.json')
        require(process==row['process'] and proof==row['result'] and proof['passed'],'Aggregate differs from actual case')
        expected=process.get('expectedExit',0)
        require(process['started'] and not process['timedOut'] and process['exitCode']==expected,'Actual owned child failed')
        require(process['pid']==proof['actualChildPid']==host['actualProcessId'] and host['sehCode']==0,'Wrong process or SEH result')
        require(proof['hostRawSha256']==sha(case/'case.json'),'Case raw hash changed')
        if expected==0:require(host['passed'] and host['finished'] and not host['failures'] and not host['error'],'Normal host failed')
        else:require(expected==1 and not host['passed'] and not host['finished'] and host['error'] and host['failures'],'Expected clean refusal missing')
        total_checks+=proof['checks'];total_calls+=proof['apiCalls'];total_pairs+=proof['directForwardPairs']
    expected_names={'normal-1','normal-2','normal-3','missing-proxy','legacy-proxy'} if directory=='cases' else {'production-normal','fixture-normal'}|{f'fault-{stage}-{i}' for stage in range(1,11) for i in (range(4) if stage in (5,6,10) else (0,))}
    require(seen==expected_names,'Actual selector matrix differs')
    totals[directory]=dict(processes=count,parentChecks=total_checks,apiCalls=total_calls,directForwardPairs=total_pairs,heldInputs=len(result['files']))
# Check legacy build export compatibility independently of the new 82-name DEF.
spec=importlib.util.spec_from_file_location('cold_prepare_final',batch/'prepare-cold-contract.py')
prepare=importlib.util.module_from_spec(spec);spec.loader.exec_module(prepare)
parse=prepare.reader().pinned_pe_reader()
legacy_manifest=read(cache/'legacy-doorstop/doorstop-manifest.json')
require(legacy_manifest['profile']=='legacy-default' and legacy_manifest['gameRuntimeVerified'] is False,'Default path profile changed')
for row in legacy_manifest['files']:require(sha(cache/'legacy-doorstop'/row['path'])==row['sha256'],'Legacy build output changed')
legacy=parse(cache/'legacy-doorstop/winhttp.dll');baseline=parse(Path('D:/SteamLibrary/steamapps/common/Rizline/winhttp.dll'))
require({(r['name'],r['ordinal']) for r in legacy['exports']}=={(r['name'],r['ordinal']) for r in baseline['exports']} and len(legacy['exports'])==82,'Legacy default exports drifted')
environment=read(attempt/'environment-before.json');game=Path('D:/SteamLibrary/steamapps/common/Rizline')
for key in ('installedFiles','originalFiles'):
    for row in environment['realGame'][key]:require(sha(game/row['path'])==row['sha256'],'Actual game file changed')
require(len(environment['realGame']['installedFiles'])==18 and len(environment['realGame']['originalFiles'])==4,'Game preservation identity set differs')
for row in environment['originalPackage']:require(sha(repo/'artifacts/package'/row['path'])==row['sha256'],'Existing package changed')
require(len(environment['originalPackage'])==23,'Package preservation count differs')
require(sha(Path(environment['systemWinHttp']['path']))==environment['systemWinHttp']['sha256'],'Actual system WinHTTP changed')
raw=batch/'raw';require(not raw.exists(),'Preserve existing raw archive')
raw_rows=[];excluded=[]
for path in sorted(cache.rglob('*')):
    if not path.is_file():continue
    relative=path.relative_to(cache)
    # Re-extracted compiler tooling is already pinned as .tools input archives;
    # keep its exact hashes, while archiving all source/build/case artifacts.
    if 'tools' in relative.parts:
        excluded.append(dict(path=relative.as_posix(),sha256=sha(path),bytes=path.stat().st_size));continue
    require(not path.is_symlink(),'Linked raw cache input')
    destination=raw/relative;destination.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(path,destination)
    require(sha(path)==sha(destination),'Raw copy mismatch')
    raw_rows.append(dict(path=relative.as_posix(),sha256=sha(path),bytes=path.stat().st_size))
with zipfile.ZipFile(batch/'raw-inputs.zip','x',compression=zipfile.ZIP_DEFLATED) as archive:
    for row in raw_rows:archive.write(raw/row['path'],row['path'])
with zipfile.ZipFile(batch/'raw-inputs.zip') as archive:
    require(len(archive.namelist())==len(raw_rows),'Raw ZIP entry mismatch')
    for row in raw_rows:require(hashlib.sha256(archive.read(row['path'])).hexdigest()==row['sha256'],'Raw ZIP hash mismatch')
shutil.copyfile(source_zip,batch/'source-final-inputs.zip');require(sha(batch/'source-final-inputs.zip')==sha(source_zip),'Final source ZIP copy mismatch')
binary_paths=['binaries/winhttp.dll','binaries/WinHttpCold.Importer.dll','binaries/WinHttpCold.Host.exe',
    'standalone-binaries/production/RizToolbox.WinHttpForward.dll','standalone-binaries/fixture/RizToolbox.WinHttpForward.dll',
    'standalone-binaries/RizWinHttpOwnedImporter.dll','standalone-binaries/WinHttpForward.Fixture.exe','legacy-doorstop/winhttp.dll']
binaries=[dict(path=p,sha256=sha(cache/p),bytes=(cache/p).stat().st_size) for p in binary_paths]
with zipfile.ZipFile(batch/'binaries.zip','x',compression=zipfile.ZIP_DEFLATED) as archive:
    for row in binaries:archive.write(cache/row['path'],row['path'])
write('source-final-manifest.json',dict(files=frozen['files'],previousSourcesChanged=frozen['previousSourcesChanged'],newInputs=frozen['newInputs'],sourceArchiveSha256=sha(source_zip),evidenceDirectory=terminal['evidenceDirectory']))
write('raw-manifest.json',dict(files=raw_rows,excludedPinnedToolFiles=excluded));write('binary-manifest.json',dict(files=binaries))
write('real-game-preservation-check.json',dict(**environment['realGame'],allMatched=True,gameExecuted=False,installed=False))
summary=dict(kind='production-doorstop-cold-final',passed=True,rootSession=terminal['sessionId'],actualTerminalExitCode=0,sourceInputs=626,changedPreviousSources=4,newInputs=13,
    cold=totals['cases'],standalone=totals['standalone-cases'],legacyDefaultExports=82,binaryFiles=8,rawFiles=len(raw_rows),pinnedToolFilesExcluded=len(excluded),
    sourceArchiveSha256=sha(source_zip),rawArchiveSha256=sha(batch/'raw-inputs.zip'),binaryArchiveSha256=sha(batch/'binaries.zip'),
    installedFilesUnchanged=18,originalGameFilesUnchanged=4,existingPackageFilesUnchanged=23,productionCandidateBuilt=True,
    actualProductionDllEntryTested=True,wholeSystemLoaderSafetyVerified=False,all82AbiBehaviorsVerified=False,negativeLoadedModulePathProved=False,
    gameExecuted=False,installed=False,engineV4Issued=False,fullManagedRegressionRerun=False,engineCancellationCompleted=False)
write('summary.json',summary);print(json.dumps(summary))
