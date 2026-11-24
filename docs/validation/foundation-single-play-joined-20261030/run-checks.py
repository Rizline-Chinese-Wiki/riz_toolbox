"""Freeze the previous exact Play core plus owned joined-delivery inputs, and execute only new joint cases."""
from pathlib import Path
from datetime import datetime, timezone
from zipfile import ZipFile, ZIP_DEFLATED
import argparse
import hashlib
import json
import shutil
import subprocess

parser = argparse.ArgumentParser()
parser.add_argument('--output', required=True)
args = parser.parse_args()
root = Path(__file__).resolve().parents[3]
base = Path('D:/riz-build-cache-next/single-play-effects-frozen-r1-20261030/source-snapshot')
output = Path(args.output).resolve()
if output.exists():
    raise SystemExit('A fresh output directory is required; failed histories are retained.')
output.mkdir(parents=True)
source = output / 'source-snapshot'
digest = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
def write(path, value):
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
base_rows = []
for path in sorted(base.rglob('*')):
    if not path.is_file() or {'bin', 'obj'}.intersection(path.parts):
        continue
    relative = path.relative_to(base)
    target = source / relative
    target.parent.mkdir(parents=True, exist_ok=True)
    before = digest(path)
    shutil.copyfile(path, target)
    assert before == digest(path) == digest(target), relative
    base_rows.append({'path': relative.as_posix(), 'sha256': before, 'bytes': path.stat().st_size})
write(output / 'base-inputs.json', base_rows)
owned = [
    'src/RizToolbox.Runtime/Hooks/NativeTweenSingleEventBridge.cs',
    'src/RizToolbox.Runtime/Hooks/NativeTweenSingleEventBridge.Controlled.cs',
    'src/RizToolbox.Adapter/AudioContent/NativeLocalTweenBackend.SingleDispatch.cs',
    'src/RizToolbox.Adapter/AudioContent/NativeTweenSingleDeliveryReader.cs',
    'src/RizToolbox.Adapter/AudioContent/NativeLocalTweenWorkOwner.TypedListenerEffects.cs',
    'tests/RizToolbox.AudioContent.Tests/NativeTweenTypedPlayNativeReaderTests.cs',
    'tests/RizToolbox.AudioContent.Tests/NativeTweenCompletionTests.cs',
    'tests/RizToolbox.AudioContent.Tests/NativeTweenTypedListenerEffectTests.cs',
    'tests/RizToolbox.AudioContent.Tests/NativeTweenSinglePlayJoinedIdentity.cs',
    'tests/RizToolbox.AudioContent.Tests/NativeTweenSinglePlayJoinedTests.cs',
]
native = [
    'TweenSingleEventContext.h', 'TweenSingleEventContext.cpp', 'TweenSingleEventEntry.asm',
    'MidContext.h', 'OperationContext.cpp', 'OperationContext.h', 'TweenAdmission.cpp', 'TweenAdmission.h',
    'TweenRetirementContext.cpp', 'TweenRetirementContext.h', 'FirstInstall.h',
    'fixture/SinglePlayJoinedFixture.cpp', 'fixture/SinglePlayJoinedFixture.asm'
]
owned += ['native/RizToolbox.JudgeContext/' + path for path in native]
owned += ['docs/validation/foundation-single-play-joined-20261030/run-checks.py']
rows = []
for name in owned:
    path, target = root / name, source / name
    target.parent.mkdir(parents=True, exist_ok=True)
    before = digest(path)
    shutil.copyfile(path, target)
    assert before == digest(path) == digest(target), name
    rows.append({'path': name, 'sha256': before, 'bytes': path.stat().st_size,
                 'kind': 'production' if name.startswith('src/') else 'fixture' if name.startswith('native/') else 'test-or-runner'})
# Only the dedicated command route is owned in the shared test registration file.
name = 'tests/RizToolbox.AudioContent.Tests/Program.cs'
route = 'if (args.Length > 0 && args[0] == "--single-play-joined")\n    return NativeTweenTypedPlayNativeReaderTests.RunJoined(args);\n\n'
live = (root / name).read_text(encoding='utf-8').replace('\r\n', '\n')
assert route in live
fixed = (source / name).read_text(encoding='utf-8').replace('\r\n', '\n')
assert '--single-play-joined' not in fixed
fixed = fixed.replace('using static Assert;\n\n', 'using static Assert;\n\n' + route, 1)
(source / name).write_text(fixed, encoding='utf-8')
rows.append({'path': name, 'kind': 'owned-route-only', 'statement': route,
             'sha256': hashlib.sha256(route.encode()).hexdigest()})
write(output / 'overlay-inputs.json', rows)
frozen = [{'path': p.relative_to(source).as_posix(), 'sha256': digest(p), 'bytes': p.stat().st_size}
          for p in sorted(source.rglob('*')) if p.is_file()]
write(output / 'source-before.json', frozen)
with ZipFile(output / 'source-snapshot.zip', 'w', ZIP_DEFLATED) as archive:
    for row in frozen:
        archive.write(source / row['path'], row['path'])
result = {'kind': 'single-native-delivery-play-effects-joined', 'observedAtUtc': datetime.now(timezone.utc).isoformat(),
          'base': str(base), 'baseManifestSha256': digest(output / 'base-inputs.json'),
          'overlayManifestSha256': digest(output / 'overlay-inputs.json'), 'sourceArchiveSha256': digest(output / 'source-snapshot.zip'),
          'baseFiles': len(base_rows), 'sourceFiles': len(frozen), 'nativeBuild': None, 'managedBuild': None,
          'tests': [], 'gameExecuted': False, 'installed': False, 'old39CasesRerun': False, 'passed': False}
write(output / 'test-result.json', result)
def run(command, label, cwd=source, timeout=120):
    ran = subprocess.run(command, cwd=cwd, capture_output=True, encoding='utf-8', errors='replace', timeout=timeout)
    row = {'command': command, 'exitCode': ran.returncode}
    for name in ['stdout', 'stderr']:
        path = output / (label + '.' + name + '.log')
        path.write_text(getattr(ran, name), encoding='utf-8')
        row[name] = {'path': path.name, 'sha256': digest(path)}
    if ran.returncode:
        print(ran.stdout[-6000:] + ran.stderr[-6000:], flush=True)
    return ran, row
native_output = output / 'native'
native_output.mkdir()
native_source = source / 'native/RizToolbox.JudgeContext'
vcvars = Path('C:/Program Files (x86)/Microsoft Visual Studio/2019/BuildTools/VC/Auxiliary/Build/vcvars64.bat')
commands = ['@echo off', f'call "{vcvars}" >nul', 'if errorlevel 1 exit /b 1']
for name, obj in [('TweenSingleEventEntry.asm', 'entry'), ('fixture/SinglePlayJoinedFixture.asm', 'fixture-entry')]:
    commands += [f'ml64 /nologo /c /Zi /Fo"{native_output / (obj + ".obj")}" "{native_source / name}"', 'if errorlevel 1 exit /b 1']
flags = '/nologo /std:c++17 /O2 /W4 /WX /EHa /MD /Zi /DRIZ_JUDGE_CONTEXT_BUILD /DRIZ_TWEEN_ADMISSION_FIXTURE /DRIZ_TWEEN_RETIREMENT_RESERVATIONS_ONLY /DRIZ_TWEEN_SINGLE_EVENT_FIXTURE'
objects = []
for name in ['OperationContext', 'TweenAdmission', 'TweenRetirementContext', 'TweenSingleEventContext']:
    objects.append(native_output / (name + '.obj'))
    commands += [f'cl {flags} /c /Fo"{objects[-1]}" /Fd"{native_output / (name + ".pdb")}" "{native_source / (name + ".cpp")}"', 'if errorlevel 1 exit /b 1']
helper = native_output / 'RizToolbox.SinglePlayJoinedFixture.dll'
links = ' '.join('"' + str(path) + '"' for path in objects + [native_output / 'entry.obj', native_output / 'fixture-entry.obj'])
commands += [f'cl {flags} /LD /Fo"{native_output / "fixture.obj"}" /Fd"{native_output / "fixture.pdb"}" "{native_source / "fixture/SinglePlayJoinedFixture.cpp"}" {links} /link /OUT:"{helper}" /PDB:"{native_output / "joined.pdb"}" /INCREMENTAL:NO /OPT:NOICF /DYNAMICBASE /NXCOMPAT /CETCOMPAT', 'if errorlevel 1 exit /b 1']
build = native_output / 'build.cmd'
build.write_text('\n'.join(commands) + '\n', encoding='utf-8')
ran, record = run(['cmd.exe', '/d', '/c', str(build)], 'native-build', native_output)
result['nativeBuild'] = record
write(output / 'test-result.json', result)
print('NATIVE BUILD', ran.returncode, flush=True)
if ran.returncode:
    raise SystemExit(1)
result['nativeHelperSha256'] = digest(helper)
project = source / 'tests/RizToolbox.AudioContent.Tests/RizToolbox.AudioContent.Tests.csproj'
ran, record = run(['dotnet', 'build', str(project), '-c', 'Release', '--artifacts-path', str(output / 'artifacts'), '--verbosity', 'minimal'], 'managed-build')
result['managedBuild'] = record
write(output / 'test-result.json', result)
print('MANAGED BUILD', ran.returncode, flush=True)
if ran.returncode:
    raise SystemExit(1)
dll = output / 'artifacts/bin/RizToolbox.AudioContent.Tests/release/RizToolbox.AudioContent.Tests.dll'
for mode in ['identity', 'instant', 'duplicates', 'withdraw', 'unknown', 'missing-post', 'copied-read']:
    ran, record = run(['dotnet', str(dll), '--single-play-joined', mode, str(helper)], mode)
    try:
        record['result'] = json.loads(ran.stdout.strip().splitlines()[-1])
    except (ValueError, IndexError):
        record['result'] = None
    record['mode'] = mode
    record['assemblySha256'] = digest(dll)
    actual = record['result']
    active, positive, duplicates = mode != 'identity', mode in ['instant', 'duplicates', 'withdraw', 'copied-read'], mode in ['duplicates', 'withdraw']
    record['contractPassed'] = bool(actual and actual.get('kind') == result['kind'] and actual.get('mode') == mode
        and actual.get('passed') is True and actual.get('helperSha256') == result['nativeHelperSha256']
        and actual.get('nativeOriginals') == (0 if not active else 2 if mode == 'withdraw' else 1)
        and actual.get('nativeListeners') == (0 if not active else 2 if mode == 'withdraw' else 1)
        and actual.get('nativePlays') == (0 if not active else 2 if mode == 'withdraw' else 1)
        and actual.get('Deliveries') == (3 if active else 0)
        and actual.get('ConsumerReads') == (0 if not active else 1 if mode in ['unknown', 'missing-post'] else 2)
        and all(actual.get(key) is active for key in ['actualNativeSingleProducerExecuted', 'actualNativeTlsReadCurrentVerified',
            'actualBridgeDeliveryAndPhysicalScopeVerified', 'productionSingleDispatchReaderExecuted', 'productionPlayConsumerReaderExecuted',
            'controlledSourceAdmissionAndDeath', 'fixtureOwnedSecImageCodeExecuted'])
        and all(actual.get(key) is positive for key in ['privatePlayTransferVerified', 'PrivateLedgerBorrowed'])
        and all(actual.get(key) is duplicates for key in ['distinctDuplicateQueueExitsVerified', 'WaitedForFirstDuplicateExit', 'CompletedSecondDuplicateExit'])
        and actual.get('NativeBeforeReadReject') == (1 if mode == 'missing-post' else 0)
        and actual.get('NativeCopiedReadRejected') is (mode == 'copied-read')
        and actual.get('NativeCopiedReadInvalidatedTransfer') is (mode == 'copied-read')
        and all(actual.get(key) is False for key in ['productionFirstInstallVerified', 'productionGameObjectsVerified',
            'productionReadyIssued', 'installed', 'gameExecuted']))
    result['tests'].append(record)
    write(output / 'test-result.json', result)
    print('TEST', mode, ran.returncode, flush=True)
after = [{'path': row['path'], 'sha256': digest(source / row['path']), 'bytes': (source / row['path']).stat().st_size} for row in frozen]
write(output / 'source-after.json', after)
result['sourceUnchanged'] = frozen == after
result['baseUnchanged'] = all(digest(base / row['path']) == row['sha256'] for row in base_rows)
result['ownedInputsUnchanged'] = all(digest(root / row['path']) == row['sha256'] if row['kind'] != 'owned-route-only'
    else row['statement'] in (root / row['path']).read_text(encoding='utf-8').replace('\r\n', '\n') for row in rows)
result['passed'] = result['nativeBuild']['exitCode'] == result['managedBuild']['exitCode'] == 0 and len(result['tests']) == 7 \
    and all(row['exitCode'] == 0 and row['contractPassed'] for row in result['tests']) \
    and all(result[key] for key in ['sourceUnchanged', 'baseUnchanged', 'ownedInputsUnchanged'])
write(output / 'test-result.json', result)
print(json.dumps({key: result[key] for key in ['passed', 'sourceUnchanged', 'baseUnchanged', 'ownedInputsUnchanged']}), flush=True)
raise SystemExit(0 if result['passed'] else 1)
