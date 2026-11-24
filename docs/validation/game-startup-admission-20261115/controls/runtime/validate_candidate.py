"""Validate the complete accepted graph plus the fixed internal tick admission candidate."""
import datetime as dt
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess

WORK=Path(__file__).resolve().parent
ROOT=WORK.parents[1]
BASE=ROOT / 'docs/validation/foundation-current-integration-20261114'
MODULE=ROOT / 'work/module-manifest-startup-next-20261115'
OUT=Path('D:/riz-build-cache-next/runtime-tick-admission-20261115/run-r1')
DOTNET='C:/Program Files/dotnet/dotnet.exe'
sha=lambda path:hashlib.sha256(Path(path).read_bytes()).hexdigest()
read=lambda path:json.loads(Path(path).read_text(encoding='utf-8-sig'))
now=lambda:dt.datetime.now(dt.timezone.utc).isoformat()


def require(value,reason):
    if not value:raise RuntimeError(reason)


def main():
    require(not OUT.exists(),'Preserve candidate validation attempts')
    manifest=read(WORK / 'overlay-manifest.json')
    module_manifest=read(MODULE / 'overlay-manifest.json')
    require(manifest.get('ready') is True and manifest.get('frozen') is True,'Runtime tick candidate not frozen')
    require(module_manifest.get('ready') is True and module_manifest.get('frozen') is True,'Module startup candidate not frozen')
    accepted=read(BASE / 'run/result.json')
    require(sha(BASE / 'validation-summary.json')=='e7a1540f75185a9714f935bec18520edfd8cada522df017b9f17d5efabf87a61'
            and accepted['passed'] and accepted['fullFrozenInputsUnchanged'],'Accepted complete source graph differs')
    expected=dict(accepted['fullFrozenInputs']);auxiliary=dict(accepted['auxiliaryInputs'])
    require(len(expected)==1171,'Expected current complete source closure')
    rows=[dict(row,ownerDirectory=WORK) for row in manifest['files']]+[dict(row,ownerDirectory=MODULE) for row in module_manifest['files']]
    require(len(rows)==7 and len({row['targetPath'] for row in rows})==7,'Expected distinct runtime and module overlays')
    for row in rows:
        name=row['targetPath']
        if row['baseSha256'] is None:
            require(name not in expected and not (BASE / 'source' / name).exists(),'New candidate compiler input already existed')
        else:require(name in expected and sha(BASE / 'source' / name)==row['baseSha256']==expected[name],'Runtime candidate base differs')
        require(sha(row['ownerDirectory'] / row['overlayPath'])==row['sha256'],'Runtime candidate overlay differs')
        expected[name]=row['sha256']
    require(len(expected)==1172,'Expected six existing-file edits and one aggregate test compiler input')
    OUT.mkdir(parents=True);source=OUT / 'source';controls=OUT / 'controls';controls.mkdir()
    overlay={row['targetPath']:row['ownerDirectory'] / row['overlayPath'] for row in rows}
    for name,digest in {**expected,**auxiliary}.items():
        original=overlay.get(name,BASE / 'source' / name);target=source / name
        target.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(original,target)
        require(sha(target)==digest,'Frozen runtime candidate source differs')
    for path in (Path(__file__),WORK / 'prepare_overlay.py',WORK / 'tick-admission-tests.cs.txt',WORK / 'overlay-manifest.json'):
        shutil.copyfile(path,controls / path.name)
    shutil.copyfile(MODULE / 'overlay-manifest.json',controls / 'module-overlay-manifest.json')
    control_hashes={path.name:sha(path) for path in controls.iterdir() if path.is_file()}
    config=OUT / 'NuGet.Config';config.write_text('<configuration><packageSources><clear/></packageSources></configuration>',encoding='utf-8')
    env=os.environ.copy();env.update(DOTNET_GENERATE_ASPNET_CERTIFICATE='false',DOTNET_ADD_GLOBAL_TOOLS_TO_PATH='false',
        DOTNET_CLI_TELEMETRY_OPTOUT='1',DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1',DOTNET_NOLOGO='1')
    fixtures={}
    for key,row in accepted['nativeFixtures'].items():
        original=Path(row['path']);require(original.is_file() and sha(original)==row['sha256'],'Accepted owned native fixture differs')
        target=OUT / 'fixtures' / key / original.name;target.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(original,target)
        env[key]=str(target);fixtures[key]=dict(path=str(target),sha256=sha(target))
    main_state=read(Path('D:/riz-build-cache-next/line-producer-control-20261114/main-r5/result.json'))
    parent_inputs={**main_state['inputsBefore'],**main_state['additionalSolutionInputsBefore']}
    require(len(parent_inputs)==1160 and all(sha(ROOT / name)==digest for name,digest in parent_inputs.items()),'Active parent managed graph differs')
    state=dict(schema='combined-game-startup-validation/v1',controllerPid=os.getpid(),startedUtc=now(),passed=False,
        overlayManifestSha256=sha(WORK / 'overlay-manifest.json'),fullFrozenInputs=expected,auxiliaryInputs=auxiliary,
        moduleOverlayManifestSha256=sha(MODULE / 'overlay-manifest.json'),
        completeSolutionInputs=1172,changedExistingInputs=6,addedCompilerInputs=1,parentManagedInputs=parent_inputs,
        controls=control_hashes,nativeFixtures=fixtures,steps=[],
        fullManagedMatrix=False,fullJudgementsRerun=False,mainApplied=False,installed=False,gameStarted=False,
        sdkGoalComplete=False,completeSeekPermitted=False)
    def save():
        pending=OUT / 'result.json.pending';pending.write_text(json.dumps(state,indent=2)+'\n',encoding='utf-8');os.replace(pending,OUT / 'result.json')
    def stable():return set(path.relative_to(source).as_posix() for path in source.rglob('*') if path.is_file())==set(expected)|set(auxiliary) and all(sha(source / name)==digest for name,digest in {**expected,**auxiliary}.items()) and all(sha(controls / name)==digest for name,digest in control_hashes.items())
    artifacts=OUT / 'artifacts'
    def products():return {name:{ext:sha(artifacts / 'bin' / name / 'release' / (name+ext)) for ext in ('.dll','.pdb')} for name in ('RizToolbox.Sdk','RizToolbox.Core','RizToolbox.Runtime','RizToolbox.Adapter','RizToolbox.Hosting','RizToolbox.Loader')}
    def run(name,argv,expected_cases=None):
        require(stable(),'Frozen candidate changed before '+name)
        row=dict(id=name,argv=list(map(str,argv)),startedUtc=now(),naturalExit=False);state['steps'].append(row);save()
        with (OUT / (name+'.log')).open('xb') as stream:
            child=subprocess.Popen(row['argv'],cwd=source,env=env,stdin=subprocess.DEVNULL,stdout=stream,stderr=subprocess.STDOUT)
            row['pid']=child.pid;save();row['exitCode']=child.wait();row['naturalExit']=True
        text=(OUT / (name+'.log')).read_text(encoding='utf-8-sig',errors='replace')
        row.update(finishedUtc=now(),logSha256=sha(OUT / (name+'.log')),passNames=re.findall(r'^PASS (.+)$',text,re.M),failNames=re.findall(r'^FAIL (.+)$',text,re.M));save()
        require(row['exitCode']==0 and not row['failNames'] and stable(),'Runtime tick candidate step failed: '+name)
        if expected_cases is not None:require(len(row['passNames'])==expected_cases,'Unexpected scoped case count: '+name)
        print(name+': actual0; cases='+str(len(row['passNames'])),flush=True)
    def build(target):return [DOTNET,'build',source / target,'-c','Release','--artifacts-path',artifacts,'--verbosity','minimal','--disable-build-servers','-m:1','-p:UseSharedCompilation=false','-p:NuGetAudit=false','--configfile',config]
    save()
    try:
        run('six-release',build('src/RizToolbox.Loader/RizToolbox.Loader.csproj'))
        state['productionAfterSix']=products();save()
        run('solution',build('RizToolbox.sln'))
        require(products()==state['productionAfterSix'],'Solution rewrote six production bytes')
        testdll=artifacts / 'bin/RizToolbox.Runtime.Tests/release/RizToolbox.Runtime.Tests.dll'
        run('tick-admission',[DOTNET,testdll,manifest['testSelector']],manifest['expectedFocusedCases'])
        if module_manifest.get('testSelector','').startswith('--'):
            run('builtin-module-contract',[DOTNET,artifacts / 'bin/RizToolbox.Module.Tests/release/RizToolbox.Module.Tests.dll',module_manifest['testSelector']],module_manifest['expectedFocusedCases'])
        for suite in ('Runtime','Hosting','Adapter','Hooks','Gameplay','Module','GameCamera'):
            baseline=next(row for row in accepted['steps'] if row['id']=='suite-'+suite)
            expected_cases=len(baseline['passNames'])+({'Runtime':8,'Module':2}.get(suite,0))
            run('suite-'+suite,[DOTNET,artifacts / 'bin' / ('RizToolbox.'+suite+'.Tests') / 'release' / ('RizToolbox.'+suite+'.Tests.dll')],expected_cases)
        require(products()==state['productionAfterSix'],'Tests rewrote production bytes')
        state['fullFrozenInputsUnchanged']=stable()
        state['parentManagedInputsUnchanged']=all(sha(ROOT / name)==digest for name,digest in parent_inputs.items())
        require(state['parentManagedInputsUnchanged'],'Parent managed source changed during isolated validation')
        state.update(passed=True,products={path.relative_to(OUT).as_posix():sha(path) for path in (artifacts / 'bin').rglob('*') if path.is_file() and path.suffix in ('.dll','.pdb','.json')})
    except Exception as error:state['failure']=str(error);raise
    finally:
        state['finishedUtc']=now();save()
        print(json.dumps({key:state.get(key) for key in ('passed','failure','fullFrozenInputsUnchanged','parentManagedInputsUnchanged')}),flush=True)


if __name__=='__main__':main()
