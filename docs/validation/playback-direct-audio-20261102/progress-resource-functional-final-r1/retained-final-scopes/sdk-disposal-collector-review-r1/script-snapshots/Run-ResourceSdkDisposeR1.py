"""Freshly verify the SDK case after ending its previous prepared transaction."""
from pathlib import Path
from ctypes import wintypes
import ctypes
import datetime
import difflib
import hashlib
import json
import os
import shutil
import subprocess
import sys
import traceback

R = Path(__file__).resolve().parent
O = R / 'sdk-current-dispose-r1'
S = O / 'source'
LIVE = R / 'source'
DOTNET = 'C:/Program Files/dotnet/dotnet.exe'
REL = 'tests/RizToolbox.PlaybackStartInitialization.Tests/ProgressClipResourceLifecycleChecks.cs'
now = lambda: datetime.datetime.now(datetime.timezone.utc).isoformat()
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
read = lambda p: json.loads(p.read_text(encoding='utf-8-sig'))
fact = lambda p: dict(path=str(p), sha256=sha(p), bytes=p.stat().st_size)


def save(path, value):
    pending = path.with_suffix(path.suffix + '.writing')
    pending.write_text(json.dumps(value, indent=2), encoding='utf-8')
    pending.replace(path)


def run(mode, command):
    row = dict(mode=mode, command=list(map(str, command)), startedAtUtc=now(), actualExitCode=None)
    result['rows'].append(row)
    with (O / (mode + '.stdout.log')).open('xb') as stdout, (O / (mode + '.stderr.log')).open('xb') as stderr:
        process = subprocess.Popen(row['command'], cwd=S, env=env, stdout=stdout, stderr=stderr,
            creationflags=subprocess.CREATE_NO_WINDOW)
        row['pid'] = process.pid
        save(O / (mode + '.launch.json'), row)
        save(O / 'test-result.json', result)
        row['actualExitCode'] = process.wait()
    row.update(finishedAtUtc=now(), stdout=fact(O / (mode + '.stdout.log')), stderr=fact(O / (mode + '.stderr.log')))
    (O / (mode + '.exit.txt')).write_text(str(row['actualExitCode']), encoding='utf-8')
    if mode != 'build':
        try:
            row['actual'] = json.loads((O / (mode + '.stdout.log')).read_text(encoding='utf-8-sig').strip().splitlines()[-1])
        except (ValueError, IndexError):
            pass
    row['passed'] = row['actualExitCode'] == 0 and (mode == 'build' or row.get('actual', {}).get('passed') is True)
    save(O / (mode + '.result.json'), row)
    save(O / 'test-result.json', result)
    assert row['passed'], 'Actual case failed: ' + mode
    return row


assert not O.exists(), 'Preserve previous attempts and use a fresh scope.'
O.mkdir()
sys.stdout = (O / 'driver.stdout.log').open('x', encoding='utf-8', buffering=1)
sys.stderr = (O / 'driver.stderr.log').open('x', encoding='utf-8', buffering=1)
kernel = ctypes.WinDLL('kernel32', use_last_error=True)
kernel.GetCurrentProcess.restype = wintypes.HANDLE
kernel.IsProcessInJob.argtypes = [wintypes.HANDLE, wintypes.HANDLE, ctypes.POINTER(wintypes.BOOL)]
inside = wintypes.BOOL()
assert kernel.IsProcessInJob(kernel.GetCurrentProcess(), None, ctypes.byref(inside))
result = dict(kind='resource-sdk-current-explicit-prepared-disposal-fresh-case', passed=False,
    runnerPid=os.getpid(), parentPid=os.getppid(), inJob=bool(inside.value), startedAtUtc=now(), rows=[],
    singleCaseOnly=True, previousFailedResultRetained=True, gameExecuted=False, installed=False,
    gameUiAccessed=False, accountAccessed=False, productionAdmission=False, fullBCompleted=False,
    currentMainModified=False, candidateSourceModified=False, physicalHelpersModified=False)
env = {**os.environ, 'DOTNET_NOLOGO': '1', 'DOTNET_CLI_TELEMETRY_OPTOUT': '1',
    'MSBUILDDISABLENODEREUSE': '1', 'DOTNET_CLI_UI_LANGUAGE': 'en', 'RIZ_INITIALIZER_TRACE': '1'}
origin_rows = []
rows = []
try:
    assert not inside.value, 'Launch the persistent WMI driver outside the exec job.'
    origin_path = R / 'foundation-r7/source-manifest.json'
    origin_rows = read(origin_path)['files']
    assert len(origin_rows) == 913
    assert all(sha(LIVE / row['path']) == row['sha256'] for row in origin_rows)
    S.mkdir()
    for row in origin_rows:
        source = LIVE / row['path']; target = S / row['path']
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(source, target)
        assert sha(target) == row['sha256']
        rows.append(dict(row))
    original = (S / REL).read_bytes()
    needle = b'                    sdk.Speed(1.25);'
    assert original.count(needle) == 1
    newline = b'\r\n' if b'\r\n' in original else b'\n'
    insertion = newline.join([
        b'                    // Confirmation retains rollback ownership until this prepared transaction is disposed.',
        b'                    // Release that reservation before starting the next independent SDK transaction.',
        b'                    prepared.Dispose();', needle])
    changed = original.replace(needle, insertion)
    (S / REL).write_bytes(changed)
    for row in rows:
        if row['path'] == REL:
            row.update(sha256=sha(S / REL), bytes=len(changed))
    (O / 'basis').mkdir()
    (O / 'basis/Lifecycle.before.cs').write_bytes(original)
    (O / 'basis/Lifecycle.after.cs').write_bytes(changed)
    patch = ''.join(difflib.unified_diff(original.decode('utf-8-sig').splitlines(True), changed.decode('utf-8-sig').splitlines(True),
        'r2/' + REL, 'sdk-disposal/' + REL))
    (O / 'basis/prepared-disposal-only.patch').write_text(patch, encoding='utf-8')
    basis = read(R / 'functional-matrix-r2/source-manifest.json')['files']
    old = {row['path']: row['sha256'] for row in basis}
    deltas = sorted(row['path'] for row in rows if old[row['path']] != row['sha256'])
    expected = sorted(['tests/RizToolbox.GameCamera.Tests/NativeCameraChecks.cs',
        'tests/RizToolbox.InitializationRootFree.Tests/Program.cs',
        'tests/RizToolbox.InitializationRootFree.Tests/RizToolbox.InitializationRootFree.Tests.csproj', REL])
    assert deltas == expected
    assert all(old[row['path']] == row['sha256'] for row in rows if row['path'].startswith(('src/', 'native/')))
    save(O / 'source-manifest.json', dict(source=str(S), origin=str(LIVE), files=rows))
    result['sourceManifest'] = fact(O / 'source-manifest.json')
    save(O / 'basis/test-only-delta.json', dict(kind='resource-sdk-prepared-disposal-test-only-delta', passed=True,
        origin=fact(origin_path), functionalR2=fact(R / 'functional-matrix-r2/source-manifest.json'),
        originalFailedCase=fact(R / 'functional-matrix-r2/resource-sdk-current.result.json'),
        originDeltaPaths=[REL], functionalR2DeltaPaths=deltas, allManaged484AndNative48InputsUnchanged=True,
        candidateSourceUnmodified=True, onlyExplicitDisposalAdded=True, existingAssertionsUnmodified=True,
        patch=fact(O / 'basis/prepared-disposal-only.patch')))
    result['testOnlyDelta'] = fact(O / 'basis/test-only-delta.json')
    native_manifest = R / 'functional-inputs-r2.json'
    native = read(native_manifest)['files']
    assert len(native) == 4
    (O / 'inputs').mkdir(); inputs = []
    for row in native:
        source = Path(row['path']); assert sha(source) == row['sha256']
        target = O / 'inputs' / source.name
        shutil.copy2(source, target); assert sha(target) == row['sha256']; inputs.append(target)
    result['nativeInputs'] = [fact(p) for p in inputs]
    result['nativeInputManifest'] = fact(native_manifest)
    config = O / 'NuGet.Config'
    config.write_text('<configuration><packageSources><clear/></packageSources></configuration>', encoding='utf-8')
    project = S / 'tests/RizToolbox.PlaybackStartInitialization.Tests/RizToolbox.PlaybackStartInitialization.Tests.csproj'
    run('build', [DOTNET, 'build', project, '-c', 'Release', '-m:1', '--configfile', config,
        '-p:UseSharedCompilation=false', '--nologo', '-v:minimal'])
    app = project.parent / 'bin/Release/net8.0/RizToolbox.Gameplay.Tests.dll'
    save(O / 'build-products.json', [fact(p) for p in sorted(app.parent.iterdir()) if p.is_file()])
    result['buildProducts'] = fact(O / 'build-products.json')
    case = run('resource-sdk-current', [DOTNET, app, 'resource-sdk-current', *inputs])
    result.update(passed=True, checks=case['actual']['checks'], actualCases=1)
except BaseException:
    result['failure'] = traceback.format_exc(); traceback.print_exc()
finally:
    result['frozenSourcesUnchanged'] = bool(rows) and all(sha(S / row['path']) == row['sha256'] for row in rows)
    result['candidateSourcesUnchanged'] = bool(origin_rows) and all(sha(LIVE / row['path']) == row['sha256'] for row in origin_rows)
    result['passed'] = result['passed'] and result['frozenSourcesUnchanged'] and result['candidateSourcesUnchanged']
    result['finishedAtUtc'] = now()
    save(O / 'test-result.json', result)
    (O / 'driver.exit.txt').write_text('0' if result['passed'] else '1', encoding='utf-8')
raise SystemExit(0 if result['passed'] else 1)
