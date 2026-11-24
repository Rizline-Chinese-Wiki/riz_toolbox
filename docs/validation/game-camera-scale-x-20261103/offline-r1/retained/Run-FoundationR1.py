"""Fresh isolated solution, 28 actual managed suites, two SDK authors and Release build."""
from pathlib import Path
import ctypes
from ctypes import wintypes
import datetime
import difflib
import hashlib
import json
import os
import re
import shutil
import subprocess
import traceback

R = Path(__file__).resolve().parent
O = R / 'foundation-r1'
F = O / 'source'
SDK = O / 'sdk-source'
DOTNET = Path('C:/Program Files/dotnet/dotnet.exe')
PWSH = Path('C:/Users/71957/.cache/codex-runtimes/codex-primary-runtime/dependencies/native/powershell/pwsh.exe')
PROFILE = Path('C:/Users/71957/riz-build-cache/engine-production-r5-20261026/NativeEngineProductionProfile.Generated.cs')
TRANSPORT = Path('D:/riz-build-cache-next/engine-transport-production-native-r2-20261027/NativeEngineTransportProfile.Generated.cs')
FIXTURES = Path('C:/Users/71957/riz-build-cache/engine-production-audio-checks-r2-20261026')
OWNED = Path('D:/riz-build-cache-next/gameplay-occurrence-provider-r4-20261031/native/RizToolbox.PlaybackOwnedFixture.dll')
FEED = Path('D:/riz-build-cache-next/playback-progress-resource-continuity-r1-20261102/foundation-r7/environment/local-feed')
SUITES = ['Core','Adapter','Hosting','Game','Runtime','Sdk','Scope','Diagnostics','PackageLifecycle','Module','Mutation',
          'Data','Storage','Services','GameResources','GameAppearance','GameCamera','Hooks','Gameplay','GameplaySourceGeneration',
          'Settings','Visual','Performance','AudioContent','UiInput','RunModification','Judgements','SingleEvent']
now = lambda: datetime.datetime.now(datetime.timezone.utc).isoformat()
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
fact = lambda p: dict(path=str(p), sha256=sha(p), bytes=p.stat().st_size)
read = lambda p: json.loads(p.read_text(encoding='utf-8-sig'))


def save(p, value):
    p.parent.mkdir(parents=True, exist_ok=True)
    temporary = p.with_suffix(p.suffix + '.writing')
    temporary.write_text(json.dumps(value, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    temporary.replace(p)


def unchanged(root, rows):
    return all((root / row['path']).is_file() and sha(root / row['path']) == row['sha256']
               and (root / row['path']).stat().st_size == row['bytes'] for row in rows)


def copy_source(target, rows):
    target.mkdir()
    for row in rows:
        destination = target / row['path']
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(R / 'source' / row['path'], destination)
    assert unchanged(target, rows)


def run(name, command, env, result, cwd=F):
    folder = O / name
    folder.mkdir()
    row = dict(name=name, command=[str(x) for x in command], cwd=str(cwd),
               startedAtUtc=now(), actualExitCode=None, passed=False)
    result['rows'].append(row)
    with (folder / 'stdout.log').open('xb') as stdout, (folder / 'stderr.log').open('xb') as stderr:
        process = subprocess.Popen(row['command'], cwd=cwd, env=env, stdout=stdout, stderr=stderr,
                                   creationflags=subprocess.CREATE_NO_WINDOW)
        row['pid'] = process.pid
        save(folder / 'launch.json', row)
        save(O / 'test-result.json', result)
        row['actualExitCode'] = process.wait()
    (folder / 'exit.txt').write_text(str(row['actualExitCode']), encoding='utf-8')
    row.update(finishedAtUtc=now(), passed=row['actualExitCode'] == 0,
               stdout=fact(folder / 'stdout.log'), stderr=fact(folder / 'stderr.log'))
    save(folder / 'process-result.json', row)
    save(O / 'test-result.json', result)
    assert row['passed'], name + ' actual exit ' + str(row['actualExitCode'])
    return (folder / 'stdout.log').read_text(encoding='utf-8-sig')


def main():
    assert not O.exists(), 'Use a fresh scope after a failure.'
    O.mkdir()
    result = dict(kind='isolated-camera-scale-x-current-foundation-r1', passed=False, startedAtUtc=now(),
                  driverPid=os.getpid(), rows=[], projects=[], gameExecuted=False, installed=False,
                  mainApplied=False, runtimeCameraAcceptance=False, fullBCompleted=False)
    rows = []
    guard_rows = []
    try:
        kernel = ctypes.WinDLL('kernel32', use_last_error=True)
        kernel.GetCurrentProcess.restype = wintypes.HANDLE
        kernel.IsProcessInJob.argtypes = [wintypes.HANDLE, wintypes.HANDLE, ctypes.POINTER(wintypes.BOOL)]
        in_job = wintypes.BOOL()
        assert kernel.IsProcessInJob(kernel.GetCurrentProcess(), None, ctypes.byref(in_job)) and not in_job.value
        result['inJob'] = False
        functional = read(R / 'functional-r1/test-result.json')
        assert functional['passed'] and functional['checks'] == 55 and functional['sourceUnchanged']
        rows = read(R / 'functional-r1/source-manifest.json')['files']
        assert len(rows) == 915 and unchanged(R / 'source', rows)
        save(O / 'source-manifest.json', dict(source=str(R / 'source'), frozen=str(F), files=rows))
        result.update(sourceFiles=len(rows), managedInputs=sum(r['path'].startswith('src/') for r in rows),
                      nativeInputs=sum(r['path'].startswith('native/') for r in rows),
                      functionalProof=fact(R / 'functional-r1/test-result.json'), sourceManifest=fact(O / 'source-manifest.json'))
        guard_rows = read(R / 'functional-r1/guarded-baselines.json')
        assert all(unchanged(Path(g['root']), g['files']) for g in guard_rows)
        assert sha(PROFILE) == 'bf5d5cf89f20e068b9a7a7cc8f5f055cd5c19cad3aa1db0f053f6649e78aa63b'
        assert sha(TRANSPORT) == '60a05f096646a31c99b00b1f1d96e961dd6761e2ed42019bc3c39838e40cd06f'
        fixture_specs = [(FIXTURES / 'RizToolbox.InitializationAbiFixture.dll', '1655533a3aa18c04b39b3a6c785a887dc6f31950db220e02608e5f786159a59b'),
                         (FIXTURES / 'RizToolbox.JudgeContext.dll', '398d1c1ba90cbf55442c42a0b2b0197804b82ef1f575f0a26ee7942063809ec9'),
                         (FIXTURES / 'dobby.dll', '8015de7d867245a1095d13947a63763878e4cf5fd3d3089b63cc39200b055ded'),
                         (OWNED, '88bacc51b2c759c3c650a87a8a10f73f2ae38f37e553a18203037aed2737b7a8')]
        for p, expected in fixture_specs:
            assert sha(p) == expected
        save(O / 'fixed-inputs.json', [fact(p) for p in [DOTNET, PWSH, PROFILE, TRANSPORT] + [p for p, _ in fixture_specs] + sorted(FEED.glob('*.nupkg'))])
        copy_source(F, rows)
        config = O / 'NuGet.Config'
        config_text = '<configuration><packageSources><clear/><add key="OfflineReferencePackages" value="' + str(FEED) + '"/></packageSources></configuration>'
        config.write_text(config_text, encoding='utf-8')
        environment = O / 'environment'
        for name in ('dotnet-home', 'packages', 'http-cache', 'temp'):
            (environment / name).mkdir(parents=True)
        env = os.environ.copy()
        env.update(PATH='C:/Program Files/dotnet;C:/Users/71957/.cache/opencode/bin;' + env.get('PATH', ''),
                   DOTNET_CLI_HOME=str(environment / 'dotnet-home'), NUGET_PACKAGES=str(environment / 'packages'),
                   NUGET_HTTP_CACHE_PATH=str(environment / 'http-cache'), TEMP=str(environment / 'temp'), TMP=str(environment / 'temp'),
                   DOTNET_CLI_TELEMETRY_OPTOUT='1', DOTNET_NOLOGO='1', DOTNET_CLI_UI_LANGUAGE='en',
                   MSBUILDDISABLENODEREUSE='1', NuGetAudit='false',
                   RIZ_INITIALIZATION_ABI_FIXTURE=str(fixture_specs[0][0]),
                   RIZ_INITIALIZATION_CONTEXT_HELPER=str(fixture_specs[1][0]),
                   RIZ_INITIALIZATION_ABI_DOBBY=str(fixture_specs[2][0]), RIZ_PLAYBACK_OWNED_FIXTURE=str(OWNED))
        text = run('solution', [DOTNET, 'build', F / 'RizToolbox.sln', '-c', 'Release', '-m:1', '--configfile', config,
                               '-p:UseSharedCompilation=false', '-p:NuGetAudit=false',
                               '-p:RizEngineProductionProfileSource=' + str(PROFILE), '--nologo', '-v:minimal'], env, result)
        warnings = re.findall(r'(?m)^\s*(\d+) Warning\(s\)\s*$', text)
        errors = re.findall(r'(?m)^\s*(\d+) Error\(s\)\s*$', text)
        assert warnings and errors and int(errors[-1]) == 0
        result.update(solutionWarnings=int(warnings[-1]), solutionErrors=0)
        save(O / 'solution-products.json', [fact(p) for p in sorted(F.rglob('*')) if p.is_file() and 'bin' in p.relative_to(F).parts and p.suffix in {'.dll','.pdb','.json'}])
        for name in SUITES:
            assembly_name = 'Gameplay' if name == 'GameplaySourceGeneration' else name
            assembly = F / ('tests/RizToolbox.' + name + '.Tests/bin/Release/net8.0/RizToolbox.' + assembly_name + '.Tests.dll')
            assert assembly.is_file(), 'Solution omitted the actual suite ' + name
            text = run('suite-' + name, [DOTNET, assembly], env, result)
            lines = [line for line in text.splitlines() if line]
            if name == 'GameplaySourceGeneration':
                actual = json.loads(lines[-1])
                assert actual['kind'] == 'gameplay-source-generation-checks' and actual['passed'] and actual['checks'] > 0
                checks = actual['checks']
            else:
                matches = list(re.finditer(r'(?m)(\d+)/(\d+)[^\r\n]*passed\.?\s*$', text))
                assert matches and int(matches[-1][1]) == int(matches[-1][2]) and int(matches[-1][1]) > 0, name + ' lacks an actual full summary'
                checks = int(matches[-1][1])
            result['projects'].append(dict(name=name, passed=True, checks=checks, summary=lines[-1], assembly=fact(assembly),
                                           process=fact(O / ('suite-' + name) / 'process-result.json')))
            save(O / 'test-result.json', result)
        assert len(result['projects']) == 28
        result['managedChecks'] = sum(p['checks'] for p in result['projects'])
        assert next(p for p in result['projects'] if p['name'] == 'GameCamera')['checks'] == 55
        save(O / 'managed-matrix-result.json', dict(kind='camera-scale-x-fresh-28-suite-matrix', passed=True,
             projects=result['projects'], projectCount=28, checks=result['managedChecks'], sourceManifest=fact(O / 'source-manifest.json'),
             solutionBuild=fact(O / 'solution/process-result.json'), gameExecuted=False, installed=False))
        copy_source(SDK, rows)
        (SDK / 'NuGet.Config').write_text(config_text, encoding='utf-8')
        script = SDK / 'scripts/Test-SdkDistribution.ps1'
        original = script.read_text(encoding='utf-8-sig')
        assert original.count('https://api.nuget.org/v3/index.json') == 1
        offline = original.replace('https://api.nuget.org/v3/index.json', str(FEED))
        script.write_text(offline, encoding='utf-8')
        patch = O / 'sdk-offline-feed-only.patch'
        patch.write_text(''.join(difflib.unified_diff(original.splitlines(True), offline.splitlines(True),
                         'frozen/scripts/Test-SdkDistribution.ps1', 'sdk-source/scripts/Test-SdkDistribution.ps1')), encoding='utf-8')
        save(O / 'sdk-offline-transformation.json', dict(passed=True, onlyPackageSourceChanged=True, allAssertionsUnmodified=True,
             original=fact(R / 'source/scripts/Test-SdkDistribution.ps1'), runner=fact(script), patch=fact(patch)))
        run('sdk', [PWSH, '-NoProfile', '-File', script], env, result, SDK)
        sdk_paths = list((SDK / 'artifacts').glob('sdk-distribution-*/result.json'))
        assert len(sdk_paths) == 1
        sdk = read(sdk_paths[0])
        assert sdk['checksPassed'] == 14 and sdk['runtimeGameVerification'] is False
        for name in ('AuthorOne', 'AuthorTwo'):
            assert (Path(sdk['generatedProjects']) / name / ('bin/Release/net6.0/' + name + '.dll')).is_file()
        assert unchanged(SDK, [r for r in rows if r['path'] != 'scripts/Test-SdkDistribution.ps1'])
        result.update(sdkChecks=14, sdkResult=fact(sdk_paths[0]))
        text = run('release', [DOTNET, 'build', F / 'src/RizToolbox.Loader/RizToolbox.Loader.csproj', '-c', 'Release', '-m:1',
                              '--configfile', config, '-p:UseSharedCompilation=false', '-p:NuGetAudit=false',
                              '-p:RizEngineProductionProfileSource=' + str(PROFILE), '-p:RizEngineTransportProfileSource=' + str(TRANSPORT),
                              '--nologo', '-v:minimal', '-o', O / 'release-managed'], env, result)
        assert re.search(r'\b0 Error\(s\)', text)
        products = [fact(O / 'release-managed' / ('RizToolbox.' + name + '.dll')) for name in ['Sdk','Core','Runtime','Adapter','Hosting','Loader']]
        save(O / 'release-products.json', dict(passed=True, assemblies=products, process=fact(O / 'release/process-result.json'),
             sourceManifest=fact(O / 'source-manifest.json'), sourceHashesUnchanged=unchanged(F, rows), productionAdmission=False,
             gameExecuted=False, installed=False))
        result.update(passed=True, releaseAssemblyCount=len(products), releaseProducts=fact(O / 'release-products.json'))
    except BaseException:
        result['passed'] = False
        result['failure'] = traceback.format_exc()
    finally:
        result['sourceUnchanged'] = bool(rows) and unchanged(R / 'source', rows) and unchanged(F, rows)
        result['baselinesUnchanged'] = bool(guard_rows) and all(unchanged(Path(g['root']), g['files']) for g in guard_rows)
        result['passed'] = result['passed'] and result['sourceUnchanged'] and result['baselinesUnchanged']
        result['finishedAtUtc'] = now()
        save(O / 'test-result.json', result)
        (O / 'driver-exit.txt').write_text('0' if result['passed'] else '1', encoding='utf-8')
    return 0 if result['passed'] else 1


if __name__ == '__main__':
    raise SystemExit(main())
