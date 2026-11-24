"""Archive the actual completed fixed forwarding test; never execute target code."""
from pathlib import Path
import hashlib
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
    path=batch/name;require(not path.exists(),'Do not overwrite an existing final artifact: '+name)
    path.write_text(json.dumps(value,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
def unlinked(path):
    require(not path.is_symlink() and not path.is_junction(),'Linked evidence input: '+str(path))
def selected_attempt(terminal):
    # The root directory intentionally retains the first failed freeze. It is
    # never a fallback source for a successful revision's command/environment.
    value=terminal.get('evidenceDirectory')
    require(isinstance(value,str) and value,'Terminal must select the actual successful attempt evidenceDirectory.')
    supplied=Path(value)
    candidate=supplied if supplied.is_absolute() else batch/supplied
    attempts=batch/'attempts'
    unlinked(attempts)
    resolved=candidate.resolve(strict=True)
    require(resolved.parent==attempts.resolve(strict=True) and resolved.is_dir(),
            'Successful evidenceDirectory must be one direct child of this batch attempts directory.')
    # Check lexical components as well as the resolved target, so a link cannot
    # escape or alias another successful attempt while resolving inside it.
    require(candidate.absolute()==resolved,'Successful attempt path must be canonical without links or parent traversal.')
    unlinked(resolved)
    for name in ('command-result.json','inputs-before.json','environment-before.json','source-inputs.zip'):
        path=resolved/name;require(path.is_file(),'Missing selected attempt evidence: '+name);unlinked(path)
    return resolved
terminal=read(batch/'terminal.json')
require(terminal['exitCode']==0 and terminal['terminalExitCodeObserved'] is True and terminal['rootSoleBuilder'] is True,'Actual root terminal success is required.')
evidence=selected_attempt(terminal)
command_path=evidence/'command-result.json'
inputs_path=evidence/'inputs-before.json'
environment_path=evidence/'environment-before.json'
attempt_source_archive=evidence/'source-inputs.zip'
command=read(command_path)
require(command['completed'] is True and command['buildCompleted'] is True and command['rootSoleBuilder'] is True
        and command['cache']==terminal['cache'],'Selected attempt command did not really complete.')
cache=Path(command['cache']);require(cache.is_absolute(),'Cache must be absolute.')
require(command['gameExecuted'] is False and command['installed'] is False and command['productionLoaderStageAdopted'] is False,'Unexpected expanded execution claim.')
inputs=read(inputs_path)
sources=inputs['files'];require(len(sources)==613 and len({row['path'] for row in sources})==613,'Wrong or duplicate source freeze.')
for row in sources:
    source=repo/row['path']
    require(not Path(row['path']).is_absolute() and '..' not in Path(row['path']).parts
            and source.resolve().is_relative_to(repo.resolve()),'Source input leaves the repository.')
    unlinked(source)
    require(sha(source)==row['sha256'],'Frozen source changed: '+row['path'])
with zipfile.ZipFile(attempt_source_archive) as archive:
    require(len(archive.namelist())==613 and set(archive.namelist())=={row['path'] for row in sources},'Source archive entries differ.')
    for row in sources:require(hashlib.sha256(archive.read(row['path'])).hexdigest()==row['sha256'],'Source archive hash differs.')
aggregate=read(cache/'cases/fixture-result.json')
require(aggregate['passed'] is True and aggregate['expectedCases']==aggregate['verifiedCases']==aggregate['independentProcesses']==21,'Incomplete actual test matrix.')
require(aggregate['allInputHashesUnchanged'] is True,'Input handles changed.')
expected={'production-normal','fixture-normal'}|{f'fault-{stage}-{index}' for stage in range(1,11) for index in (range(4) if stage in (5,6,10) else (0,))}
require(len(expected)==21 and len(aggregate['processes'])==21
        and {row['name'] for row in aggregate['processes']}==expected
        and len(aggregate['caseResults'])==21 and {row['name'] for row in aggregate['caseResults']}==expected,
        'Physical selector matrix differs or contains duplicate/missing cases.')
held=read(cache/'cases/held-inputs.json')['files']
require(len(held)==len(aggregate['files']) and len({row['path'] for row in held})==len(held),'Ambiguous held input set.')
for row in aggregate['files']:
    before=next(value for value in held if value['path']==row['path'])
    require(before['writeAndDeleteDenied'] is True and before['sha256']==row['beforeSha256']==row['afterSha256']==sha(Path(row['path'])),'Held input changed.')
total_checks=total_calls=total_pairs=host_checks=0
normal=[]
for item in aggregate['caseResults']:
    directory=cache/'cases'/item['name'];process=read(directory/'process.json');proof=read(directory/'result.json');host=read(directory/'case.json')
    require(process==item['process'] and proof==item['result'] and proof['passed'] is True,'Raw case differs from aggregate.')
    require(process['started'] and not process['timedOut'] and process['exitCode']==0,'Owned child did not complete.')
    require(process['pid']==proof['actualChildPid']==host['actualProcessId'] and host['passed'] is True,'Wrong host identity/result.')
    require(proof['hostRawSha256']==sha(directory/'case.json') and proof['profile']==process['profile'] and proof['faultStage']==process['stage'] and proof['faultIndex']==process['index'],'Selector or raw identity differs.')
    if item['name'] in ('production-normal','fixture-normal'):
        expected_profile='production' if item['name']=='production-normal' else 'fixture'
        expected_stage=expected_index=0
    else:
        _,stage,index=item['name'].split('-');expected_profile='fixture';expected_stage=int(stage);expected_index=int(index)
    require((process['profile'],process['stage'],process['index'])==(expected_profile,expected_stage,expected_index),
            'Actual process selector does not match its independently required matrix name.')
    require(host['finished'] is True and not host['failures'] and not host['error'] and host['sehCode']==0,'Host contains a native failure.')
    total_checks+=proof['checks'];total_calls+=proof['apiCalls'];total_pairs+=proof['directForwardPairs'];host_checks+=host['checks']
    if process['stage']==0:normal.append(dict(name=item['name'],checks=proof['checks'],apiCalls=proof['apiCalls'],directForwardPairs=proof['directForwardPairs'],systemBeforeInit=host['systemBeforeInit']))
require(len(normal)==2,'Missing normal production/test shape.')
environment=read(environment_path);system=environment['systemWinHttp']
require(environment['sourceArchiveSha256']==sha(attempt_source_archive),
        'Selected attempt environment is not bound to its actual successful source ZIP.')
require(sha(Path(system['path']))==system['sha256'],'System WinHTTP changed.')
game_root=Path('D:/SteamLibrary/steamapps/common/Rizline');preservation={}
for name,count in (('installedFiles',18),('originalFiles',4)):
    rows=environment['realGame'][name];require(len(rows)==count,'Wrong prior game identity shape.')
    preservation[name]=[]
    for row in rows:
        actual=sha(game_root/row['path']);require(actual==row['expected'],'Game file changed.')
        preservation[name].append(dict(path=row['path'],sha256=actual))
preservation.update(allMatched=True,gameExecuted=False,gameModified=False)
old_package=read(batch.parent/'foundation-engine-kernel-20261025/package-manifest.json')['files']
require(len(old_package)==23,'Wrong existing package count.')
for row in old_package:require(sha(repo/'artifacts/package'/row['path'])==row['sha256'],'Existing package changed.')
build=read(cache/'binaries/fixture-build-manifest.json');require(len(build['files'])==4,'Wrong built image set.')
binaries=[]
for row in build['files']:
    path=Path(row['path']);require(sha(path)==row['sha256'],'Completed image changed.')
    binaries.append(dict(path=path.relative_to(cache).as_posix(),sha256=row['sha256'],profile=row['profile'],role=row['role']))
raw_directory=batch/'raw';require(not raw_directory.exists(),'Preserve previous raw evidence.')
raw_rows=[]
for path in sorted(cache.rglob('*')):
    if not path.is_file():continue
    require(not path.is_symlink(),'Linked cache input.')
    relative=path.relative_to(cache).as_posix();destination=raw_directory/relative;destination.parent.mkdir(parents=True,exist_ok=True)
    shutil.copyfile(path,destination);require(sha(path)==sha(destination),'Raw copy mismatch.')
    raw_rows.append(dict(path=relative,sha256=sha(path),bytes=path.stat().st_size))
for archive_name,rows in (('raw-inputs.zip',raw_rows),('binaries.zip',binaries)):
    with zipfile.ZipFile(batch/archive_name,'x',compression=zipfile.ZIP_DEFLATED) as archive:
        for row in rows:archive.write(raw_directory/row['path'],row['path'])
    with zipfile.ZipFile(batch/archive_name) as archive:
        require(len(archive.namelist())==len(rows),'Wrong archive count.')
        for row in rows:require(hashlib.sha256(archive.read(row['path'])).hexdigest()==row['sha256'],'Archive hash differs.')
final_source_archive=batch/'source-final-inputs.zip'
require(not final_source_archive.exists(),'Preserve an existing final source archive.')
with attempt_source_archive.open('rb') as source,final_source_archive.open('xb') as destination:
    shutil.copyfileobj(source,destination)
require(sha(final_source_archive)==sha(attempt_source_archive),'Final source archive copy differs from successful attempt.')
source_manifest=dict(files=sources,sourceInputs=613,evidenceDirectory=evidence.relative_to(batch).as_posix(),
    inputsBeforeSha256=sha(inputs_path),sourceArchive='source-final-inputs.zip',sourceArchiveSha256=sha(final_source_archive),
    selectedAttemptSourceArchiveSha256=sha(attempt_source_archive),initialFailedArtifactsPreserved=True)
write('source-final-manifest.json',source_manifest)
# Preserve the conventional manifest name for readers, with the same explicit
# successful-revision provenance rather than the failed root ZIP.
write('source-manifest.json',source_manifest);write('binary-manifest.json',dict(files=binaries));write('raw-manifest.json',dict(files=raw_rows))
write('real-game-preservation-check.json',preservation);write('package-preservation-check.json',dict(files=old_package,allMatched=True))
summary=dict(kind='fixed-system-winhttp-forward-final',passed=True,sessionId=terminal['sessionId'],actualTerminalExitCode=0,
    cases=21,parentChecks=total_checks,hostChecks=host_checks,apiCalls=total_calls,directForwardPairs=total_pairs,normal=normal,
    sourceInputs=613,previousSourcesChanged=inputs['previousSourcesChanged'],binaryFiles=4,heldInputs=len(held),rawFiles=len(raw_rows),
    sourceArchive='source-final-inputs.zip',sourceArchiveSha256=sha(final_source_archive),
    selectedEvidenceDirectory=evidence.relative_to(batch).as_posix(),selectedAttemptSourceArchiveSha256=sha(attempt_source_archive),
    inputsBeforeSha256=sha(inputs_path),environmentBeforeSha256=sha(environment_path),initialFailedArtifactsPreserved=True,
    binaryArchiveSha256=sha(batch/'binaries.zip'),rawArchiveSha256=sha(batch/'raw-inputs.zip'),
    systemWinHttp=system,terminalSha256=sha(batch/'terminal.json'),commandSha256=sha(command_path),
    sourceVerifierSha256=sha(batch/'verify-winhttp-forward.py'),previousFullBuildRerun=False,packageFilesUnchanged=23,
    systemCodeExecuted=True,gameExecuted=False,installed=False,productionLoaderStageAdopted=False,engineV4Issued=False,
    networkRequestsIssuedByFixture=False,networkTraceCaptured=False,allOsLoaderDependenciesVerified=False,engineCancellationCompleted=False)
write('summary.json',summary)
print(json.dumps(summary,ensure_ascii=False))
