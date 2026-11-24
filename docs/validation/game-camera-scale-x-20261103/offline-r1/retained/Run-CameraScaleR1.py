"""Freeze this isolated camera increment, then build and run its complete owned suite."""
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
S = R / 'source'
O = R / 'functional-r1'
F = O / 'source'
DOTNET = Path('C:/Program Files/dotnet/dotnet.exe')
U = Path('D:/riz-build-cache-next/playback-progress-resource-continuity-r1-20261102')
T = Path('D:/riz-build-cache-next/playback-stock-stop-occurrence-r1-20261102')
MAIN = Path('C:/Users/71957/Desktop/riz_toolbox')
FEED = U / 'foundation-r7/environment/local-feed'
now = lambda: datetime.datetime.now(datetime.timezone.utc).isoformat()
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
fact = lambda p: dict(path=str(p), sha256=sha(p), bytes=p.stat().st_size)
read = lambda p: json.loads(p.read_text(encoding='utf-8-sig'))


def save(p, value):
    p.parent.mkdir(parents=True, exist_ok=True)
    temporary = p.with_suffix(p.suffix + '.writing')
    temporary.write_text(json.dumps(value, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    temporary.replace(p)


def source_rows(root, paths):
    return [dict(path=p, sha256=sha(root / p), bytes=(root / p).stat().st_size) for p in sorted(paths)]


def unchanged(root, rows):
    return all((root / row['path']).is_file() and sha(root / row['path']) == row['sha256']
               and (root / row['path']).stat().st_size == row['bytes'] for row in rows)


def run(name, command, env, result):
    folder = O / name
    folder.mkdir()
    row = dict(name=name, command=[str(x) for x in command], cwd=str(F),
               startedAtUtc=now(), actualExitCode=None, passed=False)
    result['rows'].append(row)
    with (folder / 'stdout.log').open('xb') as stdout, (folder / 'stderr.log').open('xb') as stderr:
        process = subprocess.Popen(row['command'], cwd=F, env=env, stdout=stdout, stderr=stderr,
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
    assert not O.exists(), 'A scope is immutable: use a new scope after any correction.'
    O.mkdir()
    result = dict(kind='isolated-camera-local-scale-x-owned-functional-r1', passed=False,
                  startedAtUtc=now(), driverPid=os.getpid(), rows=[],
                  gameExecuted=False, installed=False, mainApplied=False,
                  runtimeCameraAcceptance=False, fullBCompleted=False)
    frozen = []
    guards = []
    try:
        kernel = ctypes.WinDLL('kernel32', use_last_error=True)
        kernel.GetCurrentProcess.restype = wintypes.HANDLE
        kernel.IsProcessInJob.argtypes = [wintypes.HANDLE, wintypes.HANDLE, ctypes.POINTER(wintypes.BOOL)]
        in_job = wintypes.BOOL()
        assert kernel.IsProcessInJob(kernel.GetCurrentProcess(), None, ctypes.byref(in_job))
        result['inJob'] = bool(in_job.value)
        assert not result['inJob'], 'Durable validation must survive a tool turn.'
        baseline = read(R / 'baseline-manifest.json')
        assert len(baseline['files']) == 913
        paths = {row['path'] for row in baseline['files']}
        for folder in ('src', 'tests', 'scripts', 'native'):
            for p in (S / folder).rglob('*'):
                if p.is_file() and not {'bin', 'obj', '.git'}.intersection(p.relative_to(S).parts):
                    paths.add(p.relative_to(S).as_posix())
        assert len(paths) == 915, len(paths)
        frozen = source_rows(S, paths)
        result['sourceFiles'] = len(frozen)
        result['managedInputs'] = sum(r['path'].startswith('src/') for r in frozen)
        result['nativeInputs'] = sum(r['path'].startswith('native/') for r in frozen)
        save(O / 'source-manifest.json', dict(source=str(S), frozen=str(F), files=frozen))
        result['sourceManifest'] = fact(O / 'source-manifest.json')
        for root, manifest in ((U / 'source', U / 'foundation-r7/source-manifest.json'),
                               (MAIN, T / 'positive-r3/source-manifest.json')):
            rows = read(manifest)['files']
            assert unchanged(root, rows), 'Baseline changed: ' + str(root)
            guards.append((root, rows))
        assert sha(MAIN / 'README.md') == 'cfbac4691d36edc5daba513aa62d5856ae61e29bf553d0dc2979832750847364'
        save(O / 'guarded-baselines.json', [dict(root=str(root), files=rows) for root, rows in guards])
        delta = []
        prior = {r['path']: r for r in baseline['files']}
        for row in frozen:
            before = prior.get(row['path'])
            if before and before['sha256'] == row['sha256']:
                continue
            delta.append(dict(path=row['path'], before=before, after=row))
            old = (U / 'source' / row['path']).read_text(encoding='utf-8-sig').splitlines(True) if before else []
            new = (S / row['path']).read_text(encoding='utf-8-sig').splitlines(True)
            patch = O / 'patches' / (row['path'] + '.patch')
            patch.parent.mkdir(parents=True, exist_ok=True)
            patch.write_text(''.join(difflib.unified_diff(old, new, 'U/' + row['path'], 'V/' + row['path'])), encoding='utf-8')
        save(O / 'delta.json', dict(paths=delta, count=len(delta), baseline=fact(R / 'baseline-manifest.json')))
        for row in frozen:
            target = F / row['path']
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(S / row['path'], target)
        assert unchanged(F, frozen)
        config = O / 'NuGet.Config'
        config.write_text('<configuration><packageSources><clear/><add key="OfflineReferencePackages" value="' + str(FEED) + '"/></packageSources></configuration>', encoding='utf-8')
        assert len(list(FEED.glob('*.nupkg'))) == 3
        environment = O / 'environment'
        for name in ('dotnet-home', 'packages', 'http-cache', 'temp'):
            (environment / name).mkdir(parents=True)
        env = os.environ.copy()
        env.update(DOTNET_CLI_HOME=str(environment / 'dotnet-home'), NUGET_PACKAGES=str(environment / 'packages'),
                   NUGET_HTTP_CACHE_PATH=str(environment / 'http-cache'), TEMP=str(environment / 'temp'), TMP=str(environment / 'temp'),
                   DOTNET_CLI_TELEMETRY_OPTOUT='1', DOTNET_NOLOGO='1', DOTNET_CLI_UI_LANGUAGE='en',
                   MSBUILDDISABLENODEREUSE='1', NuGetAudit='false')
        project = F / 'tests/RizToolbox.GameCamera.Tests/RizToolbox.GameCamera.Tests.csproj'
        text = run('build', [DOTNET, 'build', project, '-c', 'Release', '-m:1', '--configfile', config,
                            '-p:UseSharedCompilation=false', '-p:NuGetAudit=false', '--nologo', '-v:minimal'], env, result)
        assert re.search(r'\b0 Error\(s\)', text), 'Build error summary absent'
        output = project.parent / 'bin/Release/net8.0'
        save(O / 'build-products.json', [fact(p) for p in sorted(output.iterdir()) if p.is_file()])
        text = run('camera', [DOTNET, output / 'RizToolbox.GameCamera.Tests.dll'], env, result)
        match = re.search(r'Game camera checks: (\d+)/(\d+) passed\.', text)
        assert match and int(match[1]) == int(match[2]) and int(match[2]) >= 54, 'Full owned suite did not pass'
        result.update(passed=True, checks=int(match[2]), buildProducts=fact(O / 'build-products.json'),
                      delta=fact(O / 'delta.json'))
    except BaseException:
        result['failure'] = traceback.format_exc()
        result['passed'] = False
    finally:
        result['sourceUnchanged'] = bool(frozen) and unchanged(S, frozen) and unchanged(F, frozen)
        result['baselinesUnchanged'] = bool(guards) and all(unchanged(root, rows) for root, rows in guards)
        result['readmeUnchanged'] = sha(MAIN / 'README.md') == 'cfbac4691d36edc5daba513aa62d5856ae61e29bf553d0dc2979832750847364'
        result['passed'] = result['passed'] and result['sourceUnchanged'] and result['baselinesUnchanged'] and result['readmeUnchanged']
        result['finishedAtUtc'] = now()
        save(O / 'test-result.json', result)
        (O / 'driver-exit.txt').write_text('0' if result['passed'] else '1', encoding='utf-8')
    return 0 if result['passed'] else 1


if __name__ == '__main__':
    raise SystemExit(main())
