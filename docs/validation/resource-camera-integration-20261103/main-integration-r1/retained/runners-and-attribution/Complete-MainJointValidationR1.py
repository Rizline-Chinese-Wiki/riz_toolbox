"""Continue only the unexecuted SDK and Release stages after the retained path error."""
from pathlib import Path
import datetime
import difflib
import hashlib
import json
import os
import shutil
import subprocess
import traceback

P = Path(__file__).resolve().parent
OLD = P / 'main-joint-validation-r1'
O = P / 'main-joint-validation-continuation-r1'
F = OLD / 'source'
MAIN = Path('C:/Users/71957/Desktop/riz_toolbox')
SDK = O / 'sdk-source'
DOTNET = Path('C:/Program Files/dotnet/dotnet.exe')
PWSH = Path('C:/Users/71957/.cache/codex-runtimes/codex-primary-runtime/dependencies/native/powershell/pwsh.exe')
FEED = Path('D:/riz-build-cache-next/playback-progress-resource-continuity-r1-20261102/foundation-r7/environment/local-feed')
PROFILE = Path('C:/Users/71957/riz-build-cache/engine-production-r5-20261026/NativeEngineProductionProfile.Generated.cs')
TRANSPORT = Path('D:/riz-build-cache-next/engine-transport-production-native-r2-20261027/NativeEngineTransportProfile.Generated.cs')
read = lambda p: json.loads(p.read_text(encoding='utf-8-sig'))
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
fact = lambda p: dict(path=str(p), sha256=sha(p), bytes=p.stat().st_size)
now = lambda: datetime.datetime.now(datetime.timezone.utc).isoformat()


def save(p, value):
    p.parent.mkdir(parents=True, exist_ok=True)
    temporary = p.with_suffix(p.suffix + '.writing')
    temporary.write_text(json.dumps(value, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    temporary.replace(p)


def check(root, rows):
    return all(sha(root / row['path']) == row['sha256'] and (root / row['path']).stat().st_size == row['bytes'] for row in rows)


def run(name, command, cwd, env, result):
    folder = O / name
    folder.mkdir()
    row = dict(name=name, command=[str(p) for p in command], cwd=str(cwd), startedAtUtc=now(), actualExitCode=None, passed=False)
    result['rows'].append(row)
    with (folder / 'stdout.log').open('xb') as stdout, (folder / 'stderr.log').open('xb') as stderr:
        process = subprocess.Popen(row['command'], cwd=cwd, env=env, stdout=stdout, stderr=stderr, creationflags=subprocess.CREATE_NO_WINDOW)
        row['pid'] = process.pid
        save(folder / 'launch.json', row)
        save(O / 'test-result.json', result)
        row['actualExitCode'] = process.wait()
    (folder / 'exit.txt').write_text(str(row['actualExitCode']), encoding='utf-8')
    row.update(passed=row['actualExitCode'] == 0, finishedAtUtc=now(), stdout=fact(folder / 'stdout.log'), stderr=fact(folder / 'stderr.log'))
    save(folder / 'process-result.json', row)
    save(O / 'test-result.json', result)
    assert row['passed'], name + ' actual exit ' + str(row['actualExitCode'])


def main():
    assert not O.exists(), 'Preserve previous continuations.'
    prior = read(OLD / 'test-result.json')
    matrix = read(OLD / 'managed-matrix-result.json')
    rows = read(OLD / 'source-manifest.json')['files']
    assert prior['passed'] is False and 'FileNotFoundError' in prior['failure'] and 'source' in prior['failure']
    assert matrix['passed'] and matrix['projectCount'] == 28 and matrix['checks'] == 2620
    assert len(prior['rows']) == 29 and all(row['passed'] and row['actualExitCode'] == 0 for row in prior['rows'])
    assert len(rows) == 915 and check(MAIN, rows) and check(F, rows)
    assert not any(row['name'] in ['sdk','release'] for row in prior['rows'])
    assert sha(PROFILE) == 'bf5d5cf89f20e068b9a7a7cc8f5f055cd5c19cad3aa1db0f053f6649e78aa63b'
    assert sha(TRANSPORT) == '60a05f096646a31c99b00b1f1d96e961dd6761e2ed42019bc3c39838e40cd06f'
    O.mkdir()
    result = dict(kind='fresh-main-joint-only-remaining-sdk-and-release-continuation', passed=False,
                  startedAtUtc=now(), runnerPid=os.getpid(), rows=[], newManagedSuitesRun=0,
                  inheritedActualManagedSuites=28, inheritedActualManagedChecks=2620,
                  successfulMatrix=fact(OLD / 'managed-matrix-result.json'), originalFailedAggregate=fact(OLD / 'test-result.json'),
                  originalDriverRewritten=False, oldPartialSdkSourcePreserved=True, sourceManifest=fact(OLD / 'source-manifest.json'),
                  mainApplied=True, gameExecuted=False, installed=False, fullBCompleted=False)
    try:
        environment = O / 'environment'
        for name in ['dotnet-home','packages','temp','http-cache']:
            (environment / name).mkdir(parents=True)
        config = O / 'NuGet.Config'
        config_text = '<configuration><packageSources><clear/><add key="OfflineReferencePackages" value="' + str(FEED) + '"/></packageSources></configuration>'
        config.write_text(config_text, encoding='utf-8')
        env = os.environ.copy()
        env.update(PATH='C:/Program Files/dotnet;' + env.get('PATH',''), DOTNET_CLI_HOME=str(environment / 'dotnet-home'),
                   NUGET_PACKAGES=str(environment / 'packages'), NUGET_HTTP_CACHE_PATH=str(environment / 'http-cache'),
                   TEMP=str(environment / 'temp'), TMP=str(environment / 'temp'), DOTNET_CLI_TELEMETRY_OPTOUT='1',
                   DOTNET_NOLOGO='1', DOTNET_CLI_UI_LANGUAGE='en', MSBUILDDISABLENODEREUSE='1', NuGetAudit='false')
        for row in rows:
            destination = SDK / row['path']
            destination.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(MAIN / row['path'], destination)
        assert check(SDK, rows)
        (SDK / 'NuGet.Config').write_text(config_text, encoding='utf-8')
        script = SDK / 'scripts/Test-SdkDistribution.ps1'
        original = script.read_text(encoding='utf-8-sig')
        assert original.count('https://api.nuget.org/v3/index.json') == 1
        offline = original.replace('https://api.nuget.org/v3/index.json', str(FEED))
        script.write_text(offline, encoding='utf-8')
        patch = O / 'sdk-offline-feed-only.patch'
        patch.write_text(''.join(difflib.unified_diff(original.splitlines(True), offline.splitlines(True), 'main/scripts/Test-SdkDistribution.ps1','sdk-source/scripts/Test-SdkDistribution.ps1')),encoding='utf-8')
        save(O / 'sdk-offline-transformation.json',dict(passed=True, onlyPackageSourceChanged=True, allAssertionsUnmodified=True,
             original=fact(MAIN / 'scripts/Test-SdkDistribution.ps1'), runner=fact(script), patch=fact(patch)))
        run('sdk',[PWSH,'-NoProfile','-File',script],SDK,env,result)
        sdk_paths=list((SDK / 'artifacts').glob('sdk-distribution-*/result.json'))
        assert len(sdk_paths) == 1
        sdk=read(sdk_paths[0])
        assert sdk['checksPassed'] == 14 and sdk['runtimeGameVerification'] is False
        for name in ['AuthorOne','AuthorTwo']:
            assert (Path(sdk['generatedProjects']) / name / ('bin/Release/net6.0/' + name + '.dll')).is_file()
        assert check(SDK,[row for row in rows if row['path'] != 'scripts/Test-SdkDistribution.ps1'])
        result.update(sdkChecks=14,sdkResult=fact(sdk_paths[0]))
        run('release',[DOTNET,'build',F / 'src/RizToolbox.Loader/RizToolbox.Loader.csproj','-c','Release','-m:1',
            '--configfile',config,'-p:UseSharedCompilation=false','-p:NuGetAudit=false',
            '-p:RizEngineProductionProfileSource=' + str(PROFILE),'-p:RizEngineTransportProfileSource=' + str(TRANSPORT),
            '--nologo','-v:minimal','-o',O / 'release-managed'],F,env,result)
        files=[fact(O / 'release-managed' / ('RizToolbox.' + name + '.dll')) for name in ['Sdk','Core','Runtime','Adapter','Hosting','Loader']]
        save(O / 'release-products.json',dict(passed=True,assemblies=files,process=fact(O / 'release/process-result.json'),
             sourceManifest=fact(OLD / 'source-manifest.json'),sourceHashesUnchanged=check(F,rows),productionAdmission=False,gameExecuted=False,installed=False))
        result.update(passed=True,releaseAssemblyCount=6,releaseProducts=fact(O / 'release-products.json'))
    except BaseException:
        result['failure']=traceback.format_exc()
    finally:
        result.update(main915Unchanged=check(MAIN,rows),frozen915Unchanged=check(F,rows),finishedAtUtc=now())
        result['passed']=result['passed'] and result['main915Unchanged'] and result['frozen915Unchanged']
        save(O / 'test-result.json',result)
        (O / 'driver-exit.txt').write_text('0' if result['passed'] else '1',encoding='utf-8')
    if result['passed']:
        summary=dict(kind='fresh-main-joint-explicit-successful-build-and-test-chain',passed=True,finishedAtUtc=now(),
             projects=prior['projects'],managedChecks=2620,sdkChecks=14,releaseAssemblyCount=6,sourceFiles=915,
             solutionErrors=prior['solutionErrors'],solutionWarnings=prior['solutionWarnings'],
             sources=[dict(role='fresh-main-solution-and-28-terminal-successes',aggregate=fact(OLD / 'test-result.json'),aggregatePassed=False,
                           successfulChildCommands=29,childCommandsActualExitZero=True),
                      dict(role='only-new-SDK14-and-six-Release-terminal-successes',aggregate=fact(O / 'test-result.json'),aggregatePassed=True,successfulChildCommands=2)],
             matrix=fact(OLD / 'managed-matrix-result.json'),sourceManifest=fact(OLD / 'source-manifest.json'),
             main915Unchanged=True,symbolProofComplete=False,mainApplied=True,
             fullIncrementAcceptanceComplete=False,productionAdmission=False,gameExecuted=False,installed=False,fullBCompleted=False)
        save(P / 'main-joint-build-chain-r1.json',summary)
    return 0 if result['passed'] else 1


if __name__ == '__main__':
    raise SystemExit(main())
