"""Compile the staged composition; this never applies it or accepts pending behavior gates."""
from pathlib import Path
import ctypes
from ctypes import wintypes
import datetime
import hashlib
import json
import os
import re
import subprocess
import traceback

P = Path(__file__).resolve().parent
O = P / 'joint-compile-r1'
S = P / 'candidate-915'
MAIN = Path('C:/Users/71957/Desktop/riz_toolbox')
DOTNET = Path('C:/Program Files/dotnet/dotnet.exe')
PROFILE = Path('C:/Users/71957/riz-build-cache/engine-production-r5-20261026/NativeEngineProductionProfile.Generated.cs')
FEED = Path('D:/riz-build-cache-next/playback-progress-resource-continuity-r1-20261102/foundation-r7/environment/local-feed')
read = lambda p: json.loads(p.read_text(encoding='utf-8-sig'))
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
fact = lambda p: dict(path=str(p), sha256=sha(p), bytes=p.stat().st_size)
now = lambda: datetime.datetime.now(datetime.timezone.utc).isoformat()


def save(p, value):
    temporary = p.with_suffix(p.suffix + '.writing')
    temporary.write_text(json.dumps(value, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    temporary.replace(p)


def unchanged(root, rows):
    return all(sha(root / row['path']) == row['sha256'] and (root / row['path']).stat().st_size == row['bytes'] for row in rows)


def main():
    assert not O.exists(), 'Preserve original compilation scopes.'
    O.mkdir()
    result = dict(kind='resource-camera-joint-staged-compilation-only', passed=False, startedAtUtc=now(),
                  driverPid=os.getpid(), actualBuildExitCode=None, jointBehaviorValidationComplete=False,
                  integrationAdmissionVerified=False, mainApplicationReady=False, mainApplied=False,
                  gameExecuted=False, installed=False, fullBCompleted=False)
    rows = []
    before = []
    try:
        kernel = ctypes.WinDLL('kernel32', use_last_error=True)
        kernel.GetCurrentProcess.restype = wintypes.HANDLE
        kernel.IsProcessInJob.argtypes = [wintypes.HANDLE, wintypes.HANDLE, ctypes.POINTER(wintypes.BOOL)]
        in_job = wintypes.BOOL()
        assert kernel.IsProcessInJob(kernel.GetCurrentProcess(), None, ctypes.byref(in_job)) and not in_job.value
        result['inJob'] = False
        preparation = read(P / 'preparation-result.json')
        assert preparation['passed'] and preparation['stageOnly'] and preparation['lifecycleDisposePreserved']
        rows = read(P / 'joint-candidate-manifest.json')['files']
        before = read(P / 'main-before-manifest.json')['files']
        assert len(rows) == 915 and len(before) == 894 and unchanged(S, rows) and unchanged(MAIN, before)
        assert sha(PROFILE) == 'bf5d5cf89f20e068b9a7a7cc8f5f055cd5c19cad3aa1db0f053f6649e78aa63b'
        config = O / 'NuGet.Config'
        config.write_text('<configuration><packageSources><clear/><add key="OfflineReferencePackages" value="' + str(FEED) + '"/></packageSources></configuration>', encoding='utf-8')
        environment = O / 'environment'
        for name in ['dotnet-home','packages','http-cache','temp']:
            (environment / name).mkdir(parents=True)
        env = os.environ.copy()
        env.update(DOTNET_CLI_HOME=str(environment / 'dotnet-home'), NUGET_PACKAGES=str(environment / 'packages'),
                   NUGET_HTTP_CACHE_PATH=str(environment / 'http-cache'), TEMP=str(environment / 'temp'), TMP=str(environment / 'temp'),
                   DOTNET_CLI_TELEMETRY_OPTOUT='1', DOTNET_NOLOGO='1', DOTNET_CLI_UI_LANGUAGE='en',
                   MSBUILDDISABLENODEREUSE='1', NuGetAudit='false')
        command = [str(DOTNET),'build',str(S / 'RizToolbox.sln'),'-c','Release','-m:1','--configfile',str(config),
                   '-p:UseSharedCompilation=false','-p:NuGetAudit=false','-p:RizEngineProductionProfileSource=' + str(PROFILE),
                   '--nologo','-v:minimal']
        result.update(command=command, cwd=str(S), sourceManifest=fact(P / 'joint-candidate-manifest.json'),
                      preparation=fact(P / 'preparation-result.json'), generatedProfile=fact(PROFILE), runner=fact(Path(__file__).resolve()))
        with (O / 'stdout.log').open('xb') as stdout, (O / 'stderr.log').open('xb') as stderr:
            process = subprocess.Popen(command, cwd=S, env=env, stdout=stdout, stderr=stderr, creationflags=subprocess.CREATE_NO_WINDOW)
            result['buildPid'] = process.pid
            save(O / 'launch.json', result)
            save(O / 'test-result.json', result)
            result['actualBuildExitCode'] = process.wait()
        (O / 'build-exit.txt').write_text(str(result['actualBuildExitCode']), encoding='utf-8')
        result.update(stdout=fact(O / 'stdout.log'), stderr=fact(O / 'stderr.log'))
        text = (O / 'stdout.log').read_text(encoding='utf-8-sig')
        warnings = re.findall(r'(?m)^\s*(\d+) Warning\(s\)\s*$', text)
        errors = re.findall(r'(?m)^\s*(\d+) Error\(s\)\s*$', text)
        assert result['actualBuildExitCode'] == 0 and warnings and errors and int(errors[-1]) == 0
        result.update(warnings=int(warnings[-1]), errors=0)
        products = [fact(p) for p in sorted(S.rglob('*')) if p.is_file() and 'bin' in p.relative_to(S).parts and p.suffix in {'.dll','.pdb','.json'}]
        save(O / 'products.json', dict(files=products, passed=True, compilationOnly=True))
        result.update(passed=True, products=fact(O / 'products.json'))
    except BaseException:
        result['failure'] = traceback.format_exc()
        result['passed'] = False
    finally:
        result['stagedSourcesUnchanged'] = bool(rows) and unchanged(S, rows)
        result['mainBaselineUnchanged'] = bool(before) and unchanged(MAIN, before)
        result['mainReadmeUnchanged'] = sha(MAIN / 'README.md') == 'cfbac4691d36edc5daba513aa62d5856ae61e29bf553d0dc2979832750847364'
        result['passed'] = result['passed'] and result['stagedSourcesUnchanged'] and result['mainBaselineUnchanged'] and result['mainReadmeUnchanged']
        result['finishedAtUtc'] = now()
        save(O / 'test-result.json', result)
        (O / 'driver-exit.txt').write_text('0' if result['passed'] else '1', encoding='utf-8')
    return 0 if result['passed'] else 1


if __name__ == '__main__':
    raise SystemExit(main())
