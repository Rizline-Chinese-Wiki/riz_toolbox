"""Install only exact fresh combined startup products after the original supervised validation exits zero."""
import datetime as dt
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess

WORK=Path(__file__).resolve().parent;ROOT=WORK.parents[1]
CACHE=Path('D:/riz-build-cache-next/runtime-tick-admission-20261115')
RUN=CACHE / 'run-r2';OUT=CACHE / 'install-r2'
BASE=Path('D:/riz-build-cache-next/doorstop-process-state-20261115/install-r4')
GAME=Path('D:/SteamLibrary/steamapps/common/Rizline')
sha=lambda path:hashlib.sha256(Path(path).read_bytes()).hexdigest()
read=lambda path:json.loads(Path(path).read_text(encoding='utf-8-sig'))
now=lambda:dt.datetime.now(dt.timezone.utc).isoformat()


def require(value,reason):
    if not value:raise RuntimeError(reason)


def owned_path(relative):
    parts=relative.replace('\\','/').split('/')
    require(all(part and part not in ('.','..') and ':' not in part for part in parts),'Unsafe owned path')
    require((len(parts)==1 and parts[0] in ('winhttp.dll','doorstop_config.ini','.doorstop_version','changelog.txt','RizToolbox.EngineBootstrap.dll','RizToolbox.EngineNodeKernel.dll'))
        or (len(parts)>1 and parts[0] in ('BepInEx','dotnet')),'Unowned loader path')
    path=GAME.joinpath(*parts);require(path.resolve().is_relative_to(GAME.resolve()),'Owned path escaped game root')
    for cursor in (path,*path.parents):
        if cursor==GAME:break
        require(not cursor.is_symlink() and not(getattr(cursor.lstat(),'st_file_attributes',0)&0x400),'Linked install path refused')
    return path


def main():
    require(not OUT.exists(),'Preserve combined startup install evidence')
    result=read(RUN / 'result.json');supervision=read(CACHE / 'run-r2-supervision/controller-exit.json')
    require(result['passed'] and result['fullFrozenInputsUnchanged'] and result['parentManagedInputsUnchanged']
        and len(result['fullFrozenInputs'])==1172 and len(result['steps'])==6
        and all(row['naturalExit'] and row['exitCode']==0 for row in result['steps']),'Complete combined startup validation not passed')
    require(supervision['controllerExited'] and supervision['actualControllerExitCode']==0 and supervision['resultPassed']
        and supervision['controllerPid']==supervision['resultControllerPid']==result['controllerPid']
        and supervision['resultSha256']==sha(RUN / 'result.json')
        and supervision['controllerControlSha256']==sha(WORK / 'validate_candidate_r2.py')
        and supervision['supervisorControlSha256']==sha(WORK / 'supervise_candidate_r2.py'),'Original exact validation wait not zero')
    base=read(BASE / 'result.json')
    require(base['passed'] and base['installed'] and base['actualInstallerExitCode']==0,'Native diagnostic package basis not verified')
    require(all(sha(BASE / 'source/artifacts/package' / row['path'])==row['sha256'] for row in base['packageFiles']),'Native/previous package basis changed')
    require(sha(GAME / 'GameAssembly.dll')==base['gameAssemblySha256'],'Actual game fingerprint changed')
    ledger=GAME / 'BepInEx/RizToolbox/installation.json';old=read(ledger)
    require(old['owner']=='riz_toolbox' and not old.get('pending') and Path(old['gameRoot']).resolve()==GAME.resolve(),'Owned install not clean')
    OUT.mkdir(parents=True);source=OUT / 'source';scripts=source / 'scripts';scripts.mkdir(parents=True)
    for name in ('Install.ps1','Install.Common.ps1','Prepare-Dependencies.ps1'):
        shutil.copyfile(BASE / 'source/scripts' / name,scripts / name)
    shutil.copyfile(RUN / 'source/dependencies.lock.json',source / 'dependencies.lock.json')
    shutil.copytree(BASE / 'source/.tools',source / '.tools')
    package=source / 'artifacts/package';shutil.copytree(BASE / 'source/artifacts/package',package)
    for name,pair in result['productionAfterSix'].items():
        original=RUN / 'artifacts/bin' / name / 'release' / (name+'.dll')
        require(sha(original)==pair['.dll'],'Fresh six production byte changed')
        shutil.copyfile(original,package / 'BepInEx/plugins/RizToolbox' / (name+'.dll'))
    examples=RUN / 'artifacts/bin/RizToolbox.Examples/release/RizToolbox.Examples.dll'
    require(sha(examples)==result['products']['artifacts/bin/RizToolbox.Examples/release/RizToolbox.Examples.dll'],'Fresh examples byte changed')
    for directory in sorted((RUN / 'source/examples/packages').iterdir()):
        if not directory.is_dir():continue
        target=package / 'BepInEx/RizToolbox/mods' / directory.name
        shutil.copyfile(examples,target / 'RizToolbox.Examples.dll')
        shutil.copyfile(directory / 'plugin.json',target / 'plugin.json')
    shutil.copyfile(RUN / 'source/src/RizToolbox.Adapter/profiles/steam-25977441.json',package / 'BepInEx/plugins/RizToolbox/profiles/steam-25977441.json')
    rows=sorted([dict(path=path.relative_to(package).as_posix(),sha256=sha(path)) for path in package.rglob('*') if path.is_file()],key=lambda row:row['path'])
    require(len(rows)==23,'Combined startup package scope differs')
    manifest=source / 'artifacts/package-manifest.json';manifest.write_text(json.dumps(dict(schemaVersion=2,version='0.3.0',loader='Doorstop modular compatibility host',files=rows),indent=2)+'\n',encoding='utf-8')
    before=OUT / 'before';backups=[];drift=[]
    for row in old['files']:
        name=row['path'].replace('\\','/');original=owned_path(name);digest=sha(original)
        if digest!=row['sha256'].lower():
            require(name=='BepInEx/config/BepInEx.cfg','Unexpected changed owned file');drift.append(name)
        target=before / 'owned' / name;target.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(original,target)
        require(sha(target)==digest,'Current owned backup differs');backups.append(dict(path=name,actualSha256=digest,ledgerSha256=row['sha256'].lower()))
    shutil.copyfile(ledger,before / 'installation.json')
    for name in ('settings.json','activation.json','command.json','status.json','toolbox.log','native-preinit.log'):
        original=GAME / 'BepInEx/RizToolbox' / name
        if original.is_file():
            target=before / 'runtime-data' / name;target.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(original,target)
    controls=OUT / 'controls';controls.mkdir();shutil.copyfile(__file__,controls / Path(__file__).name)
    state=dict(schema='combined-startup-candidate-install/v1',controllerPid=os.getpid(),startedUtc=now(),passed=False,installed=False,
        userAuthorization='有必要的话可以动游戏',validationResultSha256=sha(RUN / 'result.json'),originalValidationExitSha256=sha(CACHE / 'run-r2-supervision/controller-exit.json'),
        packageManifestSha256=sha(manifest),packageFiles=rows,ownedBackups=backups,preservedUserModifiedNonTargets=drift,
        nativeBasisInstallResultSha256=sha(BASE / 'result.json'),gameAssemblySha256=base['gameAssemblySha256'],
        allManagedProductsFromCurrentCombinedBuild=True,gameStarted=False,mainApplied=False,sdkGoalComplete=False,completeSeekPermitted=False)
    def save():(OUT / 'result.json').write_text(json.dumps(state,indent=2)+'\n',encoding='utf-8')
    save()
    try:
        env=os.environ.copy();env.update(DOTNET_GENERATE_ASPNET_CERTIFICATE='false',DOTNET_ADD_GLOBAL_TOOLS_TO_PATH='false',
            DOTNET_CLI_TELEMETRY_OPTOUT='1',DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1',DOTNET_NOLOGO='1')
        argv=[shutil.which('pwsh'),'-NoProfile','-File',str(scripts / 'Install.ps1'),'-GameDirectory',str(GAME)]
        state['installerArgv']=argv;save()
        with (OUT / 'installer.log').open('xb') as stream:
            child=subprocess.Popen(argv,cwd=source,env=env,stdin=subprocess.DEVNULL,stdout=stream,stderr=subprocess.STDOUT)
            state['installerPid']=child.pid;save();state['actualInstallerExitCode']=child.wait();state['naturalExit']=True;save()
        require(state['actualInstallerExitCode']==0,'Installer failed; preserve recovery ledger')
        current=read(ledger);owned={row['path'].replace('\\','/'):row['sha256'].lower() for row in current['files']}
        require(not current.get('pending') and current['owner']=='riz_toolbox','Install did not commit cleanly')
        require(all(sha(owned_path(row['path']))==row['sha256']==owned[row['path']] for row in rows),'Installed current package byte differs')
        require(all(sha(owned_path(name))==next(row['actualSha256'] for row in backups if row['path']==name) for name in drift),'User modified config changed')
        require(sha(GAME / 'BepInEx/RizToolbox/settings.json')==sha(before / 'runtime-data/settings.json'),'Existing SDK settings changed')
        require(sha(GAME / 'GameAssembly.dll')==state['gameAssemblySha256'],'Original game binary changed')
        state.update(passed=True,installed=True,cleanCommit=True,verifiedPackageFiles=23,userConfigurationPreserved=True,installationManifestSha256=sha(ledger))
    except Exception as error:state['failure']=str(error);raise
    finally:
        state['finishedUtc']=now();save();print(json.dumps({key:state.get(key) for key in ('passed','installed','actualInstallerExitCode','verifiedPackageFiles','failure')}),flush=True)


if __name__=='__main__':main()
