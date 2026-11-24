"""Freeze exact V7 core plus owned Single/effects overlays and run isolated managed checks, without a game."""
from pathlib import Path
from datetime import datetime, timezone
from zipfile import ZipFile, ZIP_DEFLATED
import argparse
import hashlib
import json
import re
import shutil
import subprocess

parser = argparse.ArgumentParser()
parser.add_argument('--output', required=True)
args = parser.parse_args()
root = Path(__file__).resolve().parents[3]
base = Path('D:/riz-build-cache-next/foundation-single-v7-core-r1-20261029/source-snapshot')
output = Path(args.output).resolve()
if output.exists():
    raise SystemExit('A fresh output directory is required; failed histories are retained.')
output.mkdir(parents=True)
source = output / 'source-snapshot'
source.mkdir()
digest = lambda path: hashlib.sha256(path.read_bytes()).hexdigest()
write_json = lambda path, value: path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')


def method(text, name, visibility='public'):
    start = text.index('    ' + visibility + ' void ' + name + '(')
    opened, depth = text.index('{', start), 0
    for index in range(opened, len(text)):
        if text[index] == '{':
            depth += 1
        elif text[index] == '}':
            depth -= 1
            if depth == 0:
                return text[start:index + 1]
    raise ValueError('Unclosed owned method: ' + name)


base_paths = [base / name for name in ['Directory.Build.props', 'global.json', 'dependencies.lock.json']]
for name in ['src', 'tests']:
    base_paths.extend(path for path in (base / name).rglob('*') if path.is_file()
                      and path.suffix in ['.cs', '.csproj', '.json'] and not {'bin', 'obj'}.intersection(path.parts))
base_rows = []
for path in sorted(set(base_paths)):
    relative = path.relative_to(base)
    target = source / relative
    target.parent.mkdir(parents=True, exist_ok=True)
    before = digest(path)
    shutil.copyfile(path, target)
    assert before == digest(path) == digest(target), relative
    base_rows.append({'path': relative.as_posix(), 'sha256': before, 'bytes': path.stat().st_size})
write_json(output / 'base-inputs.json', base_rows)

previous = json.loads((root / 'docs/validation/foundation-single-subscriptions-20261030/overlay-inputs.json').read_text(encoding='utf-8'))
owned = [row['path'] for row in previous if row['kind'] == 'whole-owned-file'] + [
    'src/RizToolbox.Adapter/AudioContent/NativeLocalTweenSingleEventTracker.cs',
    'src/RizToolbox.Adapter/AudioContent/NativeLocalTweenBackend.TypedListenerEffects.cs',
    'src/RizToolbox.Adapter/AudioContent/NativeTweenTypedPlayConsumerReader.cs',
    'src/RizToolbox.Adapter/AudioContent/NativeLocalTweenWorkOwner.TypedListenerEffects.cs',
    'tests/RizToolbox.AudioContent.Tests/NativeTweenCompletionTests.cs',
    'tests/RizToolbox.AudioContent.Tests/NativeTweenTypedListenerEffectTests.cs',
]
owned.extend(path.relative_to(root).as_posix() for path in sorted((root / 'tests/RizToolbox.AudioContent.Tests').glob('NativeTweenTypedPlay*.cs')))
overlays = []
for name in sorted(set(owned)):
    path, target = root / name, source / name
    target.parent.mkdir(parents=True, exist_ok=True)
    before = digest(path)
    shutil.copyfile(path, target)
    assert before == digest(path) == digest(target), name
    overlays.append({'path': name, 'kind': 'whole-owned-file', 'sha256': before, 'bytes': path.stat().st_size})

retirement = 'src/RizToolbox.Adapter/AudioContent/NativeLocalTweenBackend.Retirement.cs'
live, fixed = (root / retirement).read_bytes().decode('utf-8'), (source / retirement).read_bytes().decode('utf-8')
for name in ['WithdrawTweenObservationBindings', 'VerifyTweenCompletionBindingsWithdrawn']:
    replacement = method(live, name)
    fixed = fixed.replace(method(fixed, name), replacement, 1)
    overlays.append({'path': retirement, 'kind': 'owned-method-only', 'method': name,
                     'sha256': hashlib.sha256(replacement.encode('utf-8')).hexdigest()})
(source / retirement).write_bytes(fixed.encode('utf-8'))
completion = 'src/RizToolbox.Adapter/AudioContent/NativeLocalTweenWorkOwner.Completion.cs'
old = '        foreach (var typed in _typedEffects) pending.Add(TypedEffectPending(typed));'
new = '        foreach (var typed in _typedEffects)\n            if (TypedEffectPending(typed) is { Length: > 0 } reason) pending.Add(reason);'
fixed = (source / completion).read_bytes().decode('utf-8')
live = (root / completion).read_bytes().decode('utf-8').replace('\r\n', '\n')
assert fixed.count(old) == 1 and new in live
fixed = fixed.replace(old, new.replace('\n', '\r\n') if '\r\n' in fixed else new)
(source / completion).write_bytes(fixed.encode('utf-8'))
overlays.append({'path': completion, 'kind': 'owned-statement-only', 'statement': new,
                 'sha256': hashlib.sha256(new.encode('utf-8')).hexdigest()})
write_json(output / 'overlay-inputs.json', overlays)
rows = [{'path': path.relative_to(source).as_posix(), 'sha256': digest(path), 'bytes': path.stat().st_size}
        for path in sorted(source.rglob('*')) if path.is_file()]
write_json(output / 'source-before.json', rows)
with ZipFile(output / 'source-snapshot.zip', 'w', ZIP_DEFLATED) as archive:
    for row in rows:
        archive.write(source / row['path'], row['path'])
result = {'observedAtUtc': datetime.now(timezone.utc).isoformat(), 'kind': 'single-exact-play-listener-effects-transfer',
          'runnerSha256': digest(Path(__file__)),
          'base': str(base), 'baseManifestSha256': digest(output / 'base-inputs.json'),
          'overlayManifestSha256': digest(output / 'overlay-inputs.json'), 'baseFiles': len(base_rows),
          'sourceFiles': len(rows), 'sourceArchiveSha256': digest(output / 'source-snapshot.zip'),
          'gameExecuted': False, 'installed': False, 'productionGameObjectsVerified': False,
          'singleLifecycleRerun': False, 'builds': [], 'tests': [], 'passed': False}
write_json(output / 'test-result.json', result)


def run(command, label):
    ran = subprocess.run(command, cwd=source, capture_output=True, encoding='utf-8', errors='replace', timeout=120)
    record = {'command': command, 'exitCode': ran.returncode}
    for name in ['stdout', 'stderr']:
        path = output / (label + '.' + name + '.log')
        path.write_text(getattr(ran, name), encoding='utf-8')
        record[name] = {'path': path.name, 'sha256': digest(path)}
    return ran, record


project = source / 'tests/RizToolbox.AudioContent.Tests/RizToolbox.AudioContent.Tests.csproj'
ran, record = run(['dotnet', 'build', str(project), '-c', 'Release', '--artifacts-path', str(output / 'artifacts'), '--verbosity', 'minimal'], 'AudioContent.build')
result['builds'].append(record)
write_json(output / 'test-result.json', result)
print('BUILD', ran.returncode, flush=True)
if ran.returncode == 0:
    names = []
    for file in ['NativeTweenTypedListenerEffectTests.cs', 'NativeTweenCompletionTests.cs', 'NativeTweenTypedEventTests.cs', 'NativeTweenTypedPlayNativeReaderTests.cs']:
        path = source / 'tests/RizToolbox.AudioContent.Tests' / file
        if not path.is_file():
            continue
        text = path.read_text(encoding='utf-8').split('private static')[0].split('private sealed')[0]
        names.extend(re.findall(r'\("([^"]+)",\s*\w+\)', text))
    dll = output / 'artifacts/bin/RizToolbox.AudioContent.Tests/release/RizToolbox.AudioContent.Tests.dll'
    for index, name in enumerate(dict.fromkeys(names)):
        ran, record = run(['dotnet', str(dll), '--isolated-case', name], 'AudioContent.' + str(index).zfill(2))
        record.update({'case': name, 'assemblySha256': digest(dll),
                       'contractPassed': '1/1 audio/content checks passed.' in ran.stdout})
        result['tests'].append(record)
        write_json(output / 'test-result.json', result)
        print('TEST', index, ran.returncode, name, flush=True)
        if ran.returncode:
            print(ran.stdout[-2500:] + ran.stderr[-2500:], flush=True)
else:
    print(ran.stdout[-5000:], flush=True)

after = [{'path': row['path'], 'sha256': digest(source / row['path']), 'bytes': (source / row['path']).stat().st_size} for row in rows]
write_json(output / 'source-after.json', after)
result['sourceUnchanged'] = rows == after
result['baseUnchanged'] = all(digest(base / row['path']) == row['sha256'] for row in base_rows)
result['ownedWholeFilesUnchanged'] = all(digest(root / row['path']) == row['sha256'] for row in overlays if row['kind'] == 'whole-owned-file')
result['ownedPartialInputsUnchanged'] = all(
    hashlib.sha256(method((root / row['path']).read_bytes().decode('utf-8'), row['method']).encode('utf-8')).hexdigest() == row['sha256']
    if row['kind'] == 'owned-method-only' else row['statement'] in (root / row['path']).read_bytes().decode('utf-8').replace('\r\n', '\n')
    for row in overlays if row['kind'] != 'whole-owned-file')
result['passed'] = len(result['builds']) == 1 and len(result['tests']) == 39 and all(row['exitCode'] == 0 for row in result['builds'] + result['tests']) and all(row['contractPassed'] for row in result['tests']) and all(result[key] for key in ['sourceUnchanged', 'baseUnchanged', 'ownedWholeFilesUnchanged', 'ownedPartialInputsUnchanged'])
write_json(output / 'test-result.json', result)
print(json.dumps({key: result[key] for key in ['passed', 'sourceFiles', 'sourceUnchanged', 'baseUnchanged', 'ownedWholeFilesUnchanged']}), flush=True)
raise SystemExit(0 if result['passed'] else 1)
