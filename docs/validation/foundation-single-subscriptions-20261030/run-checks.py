"""Freeze the precise V7 core dependency base, overlay owned Single inputs, and execute actual callbacks."""
from pathlib import Path
from datetime import datetime, timezone
from zipfile import ZipFile, ZIP_DEFLATED
import argparse, hashlib, json, os, re, shutil, subprocess

parser = argparse.ArgumentParser()
parser.add_argument('--output', required=True)
parser.add_argument('--base', default='D:/riz-build-cache-next/foundation-single-v7-core-r1-20261029/source-snapshot')
parser.add_argument('--helper', default='D:/riz-build-cache-next/single-v7-native-a-r3-20261029/RizToolbox.JudgeContext.dll')
args = parser.parse_args()
root = Path(__file__).resolve().parents[3]
base, output, helper = (Path(value).resolve() for value in [args.base, args.output, args.helper])
if output.exists(): raise SystemExit('Use a fresh output directory; earlier runs are never overwritten.')
if not (base/'src/RizToolbox.Runtime/Hooks/NativeTweenSingleEventBridge.cs').is_file(): raise SystemExit('Exact frozen V7 core dependency base missing.')
if not helper.is_file(): raise SystemExit('Fixed prebuilt helper is required; this script never builds native code.')
output.mkdir(parents=True)
source = output/'source-snapshot'; source.mkdir()
digest = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
hash_text = lambda text: hashlib.sha256(text.encode('utf-8')).hexdigest()
write_json = lambda path, value: path.write_text(json.dumps(value, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')
paths = [base/name for name in ['Directory.Build.props', 'global.json', 'dependencies.lock.json']]
for directory in ['src', 'tests']:
    paths.extend(p for p in (base/directory).rglob('*') if p.is_file() and p.suffix in ['.cs', '.csproj', '.json'] and not {'obj', 'bin'}.intersection(p.parts))
base_rows = []
for path in sorted(set(paths)):
    relative = path.relative_to(base); target = source/relative; target.parent.mkdir(parents=True, exist_ok=True)
    before = digest(path); shutil.copyfile(path, target)
    assert digest(target) == before and digest(path) == before, relative
    base_rows.append({'path': relative.as_posix(), 'sha256': before, 'bytes': path.stat().st_size})
write_json(output/'base-inputs.json', base_rows)
owned = [
    'src/RizToolbox.Runtime/Hooks/NativeTweenSingleEventBridge.cs',
    'src/RizToolbox.Runtime/Hooks/NativeTweenSingleEventSubscriptions.cs',
    'src/RizToolbox.Runtime/Hooks/NativeTweenSingleEventScope.cs',
    'src/RizToolbox.Runtime/Hooks/NativeTweenSingleEventPhysicalParent.cs',
    'src/RizToolbox.Adapter/AudioContent/NativeLocalTweenBackend.SingleDispatch.cs',
    'src/RizToolbox.Adapter/AudioContent/NativeLocalTweenBackend.TypedEvents.cs',
    'src/RizToolbox.Adapter/AudioContent/NativeLocalTweenWorkOwner.TypedEvents.cs',
    'src/RizToolbox.Adapter/AudioContent/NativeLocalTweenWorkOwner.TypedEffects.cs',
    'src/RizToolbox.Adapter/AudioContent/NativeLocalTweenWorkOwner.TypedDispatch.cs',
]
owned.extend(p.relative_to(root).as_posix() for p in sorted((root/'tests/RizToolbox.SingleEvent.Tests').glob('*')) if p.suffix in ['.cs', '.csproj'])
overlays = []
for name in owned:
    path, target = root/name, source/name
    before = digest(path); target.parent.mkdir(parents=True, exist_ok=True); shutil.copyfile(path, target)
    assert digest(path) == before and digest(target) == before, name
    overlays.append({'path': name, 'kind': 'whole-owned-file', 'sha256': before, 'bytes': path.stat().st_size})

def method(text, name):
    start = text.index('    public void '+name+'('); opened = text.index('{', start); depth = 0
    for i in range(opened, len(text)):
        if text[i] == '{': depth += 1
        elif text[i] == '}':
            depth -= 1
            if depth == 0: return text[start:i+1]
    raise ValueError('Unclosed owned method '+name)

# Only these two functions are owned. Every other retirement byte comes from the
# exact frozen base, so concurrent root work cannot enter this focused evidence.
retirement = 'src/RizToolbox.Adapter/AudioContent/NativeLocalTweenBackend.Retirement.cs'
retirement_live = (root/retirement).read_bytes().decode('utf-8')
retirement_frozen = (source/retirement).read_bytes().decode('utf-8')
for name in ['WithdrawTweenObservationBindings', 'VerifyTweenCompletionBindingsWithdrawn']:
    replacement = method(retirement_live, name)
    retirement_frozen = retirement_frozen.replace(method(retirement_frozen, name), replacement, 1)
    overlays.append({'path': retirement, 'kind': 'owned-method-only', 'method': name, 'sha256': hash_text(replacement)})
(source/retirement).write_bytes(retirement_frozen.encode('utf-8'))
write_json(output/'overlay-inputs.json', overlays)

rows = [{'path': p.relative_to(source).as_posix(), 'sha256': digest(p), 'bytes': p.stat().st_size}
        for p in sorted(source.rglob('*')) if p.is_file()]
write_json(output/'source-before.json', rows)
with ZipFile(output/'source-snapshot.zip', 'w', ZIP_DEFLATED) as archive:
    for row in rows: archive.write(source/row['path'], row['path'])
helper_hash = digest(helper)
manifest = {'observedAtUtc': datetime.now(timezone.utc).isoformat(), 'kind': 'single-run-subscription-controlled-callbacks',
    'base': str(base), 'baseManifestSha256': digest(output/'base-inputs.json'), 'baseFiles': len(base_rows),
    'overlayManifestSha256': digest(output/'overlay-inputs.json'), 'sourceFiles': len(rows),
    'sourceArchiveSha256': digest(output/'source-snapshot.zip'), 'helper': str(helper), 'helperSha256': helper_hash,
    'gameExecuted': False, 'installed': False, 'productionFirstInstallVerified': False,
    'singleProducerPositiveReadCurrentVerified': False, 'effectsCompletionVerified': False,
    'builds': [], 'tests': [], 'sourceWiringChecks': [], 'passed': False}
write_json(output/'test-result.json', manifest)

# Static wiring supplements the actual callback executables; it is not presented
# as positive native Delivery/metadata/effects verification.
def wiring(name, condition):
    manifest['sourceWiringChecks'].append({'name': name, 'passed': bool(condition)})
    assert condition, name
typed = (source/'src/RizToolbox.Adapter/AudioContent/NativeLocalTweenBackend.TypedEvents.cs').read_text(encoding='utf-8')
single = (source/'src/RizToolbox.Adapter/AudioContent/NativeLocalTweenBackend.SingleDispatch.cs').read_text(encoding='utf-8')
bridge = (source/'src/RizToolbox.Runtime/Hooks/NativeTweenSingleEventBridge.cs').read_text(encoding='utf-8')
physical = (source/'src/RizToolbox.Runtime/Hooks/NativeTweenSingleEventPhysicalParent.cs').read_text(encoding='utf-8')
wiring('production Capture reserves exact subscriber before rooted membership/metadata/capture',
       typed.index('bridge.BorrowParent(subscription, physical)') < typed.index('owner.MatchTypedEvent(this, frame') < typed.index('_api.ObserveRootedSingleEventInvocation'))
wiring('production Capture returns actual bound cleanup scope and Complete consumes that scope',
       'return new NativeTweenSingleEventScope<' in typed and 'actual.Complete(frame, flags, complete)' in typed)
wiring('selected callback losing actual physical context raises a source fault',
       'if (!TypedEventBinding.WithCurrentSingleContext' in single and 'captured Single callback lost its exact retained physical cleanup context' in single)
wiring('already rooted child cleanup precedes known barrier and new metadata reads',
       single.index('owner.CompleteTypedDispatch(actual.Inner, frame)') < single.index('NativeTweenSingleEventRootBatch.Capture'))
wiring('retirement withdraws Single before the operation sinks and requires drained subscription',
       'RetireSingleSubscription(owner); _typedEventBinding.Dispose();' in retirement_frozen and '!SingleSubscriptionWithdrawnAndDrained' in retirement_frozen)
wiring('object association remains guarded by current native Delivery and private scalar core',
       'delivery.VerifyCurrent();' in physical and 'private IDisposable? CurrentSingleContext' in physical)
wiring('permanent observer owns a hub with actual Delivery callbacks and one retained Attach attempt',
       'NativeTweenSingleEventSubscriptions<Delivery> _subscriptions' in bridge and 'RetainedAttempts.Add(this)' in bridge and '_subscriptions.Dispatch(' in bridge)
artifacts = output/'artifacts'
env = os.environ.copy(); env['RIZ_SINGLE_SUBSCRIPTION_HELPER'] = str(helper)

def run(command, label, timeout=120):
    result = subprocess.run(command, cwd=source, env=env, capture_output=True, text=True, encoding='utf-8', errors='replace', timeout=timeout)
    stdout, stderr = output/(label+'.stdout.log'), output/(label+'.stderr.log')
    stdout.write_text(result.stdout, encoding='utf-8'); stderr.write_text(result.stderr, encoding='utf-8')
    return result, {'command': command, 'exitCode': result.returncode,
        'stdout': {'path': stdout.name, 'sha256': digest(stdout)}, 'stderr': {'path': stderr.name, 'sha256': digest(stderr)}}

for project in ['SingleEvent', 'AudioContent']:
    name = 'RizToolbox.'+project+'.Tests'
    command = ['dotnet', 'build', str(source/'tests'/name/(name+'.csproj')), '-c', 'Release', '--artifacts-path', str(artifacts), '--verbosity', 'minimal']
    print('BUILD', name, flush=True); built, record = run(command, project+'.build')
    record['project'] = name; record['warnings'] = next((int(n) for n in re.findall(r'(\d+) 个警告', built.stdout)[-1:]), None)
    manifest['builds'].append(record); write_json(output/'test-result.json', manifest)
    if built.returncode:
        print(built.stdout[-6000:], flush=True); break
    dll = artifacts/'bin'/name/'release'/(name+'.dll')
    if project == 'SingleEvent':
        cases = [(None, [])]+[(mode, ['--single-subscription-native', mode]) for mode in
            ['withdraw-original', 'withdraw-capture', 'nested-foreign', 'two-runs', 'observer-fault', 'completion-fault', 'context-fault', 'collected-run']]
    else:
        text = (source/'tests/RizToolbox.AudioContent.Tests/NativeTweenTypedEventTests.cs').read_text(encoding='utf-8').split('private static void CapturePlay()')[0]
        names = re.findall(r'\("([^"]+)",\s*\w+\)', text)+['Tween completion refuses an unassociated returned inline child and a typed invocation despite native return and disposal']
        cases = [(name, ['--isolated-case', name]) for name in names]
    for i, (case, arguments) in enumerate(cases):
        command = ['dotnet', str(dll)]+arguments
        ran, record = run(command, project+'.'+str(i).zfill(2), timeout=45)
        record.update({'project': project, 'case': case, 'assemblySha256': digest(dll),
            'summary': [line for line in ran.stdout.splitlines() if re.search(r'\d+/\d+|"passed":', line)]})
        if project == 'SingleEvent' and case is None:
            record['contractPassed'] = '65/65 Single managed source checks passed.' in ran.stdout
        elif project == 'SingleEvent':
            try:
                native = json.loads(ran.stdout.strip().splitlines()[-1])
                record['contractPassed'] = all([
                    native['passed'] is True, native['mode'] == case,
                    native['kind'] == 'single-subscription-native-operation-callbacks',
                    native['actualNativeOriginals'] > 0, native['actualManagedCallbacks'] > 0,
                    native['actualOperationCaptureAndExitVerified'] is True,
                    native['privateScalarParentAssociationVerified'] is True,
                    native['parentAssociationViaPrivateReflection'] is True,
                    native['actualSingleDeliveryReadCurrentVerified'] is False,
                    native['productionFirstInstallVerified'] is False,
                    native['actualIl2CppMetadataVerified'] is False,
                    native['effectsCompletionVerified'] is False,
                    native['nativeObserverAttached'] == 1, native['nativeInFlight'] == 0,
                    native['gameExecuted'] is False, native['installed'] is False,
                    native['helperSha256'] == helper_hash])
            except (ValueError, KeyError, IndexError): record['contractPassed'] = False
        else:
            record['contractPassed'] = '1/1 audio/content checks passed.' in ran.stdout
        manifest['tests'].append(record)
        print('TEST', project, i, ran.returncode, record['summary'], flush=True)
        if ran.returncode: print(ran.stderr[-6000:], flush=True)
        write_json(output/'test-result.json', manifest)

after = [{'path': r['path'], 'sha256': digest(source/r['path']), 'bytes': (source/r['path']).stat().st_size} for r in rows]
manifest['sourceUnchanged'] = rows == after
manifest['baseUnchanged'] = all(digest(base/r['path']) == r['sha256'] for r in base_rows)
manifest['helperUnchanged'] = digest(helper) == helper_hash
manifest['ownedWorktreeUnchanged'] = all(
    digest(root/r['path']) == r['sha256'] if r['kind'] == 'whole-owned-file'
    else hash_text(method((root/r['path']).read_bytes().decode('utf-8'), r['method'])) == r['sha256'] for r in overlays)
manifest['passed'] = len(manifest['builds']) == 2 and len(manifest['tests']) == 26 and all(
    r['exitCode'] == 0 for r in manifest['builds']+manifest['tests']) and all(
    manifest[key] for key in ['sourceUnchanged', 'baseUnchanged', 'helperUnchanged', 'ownedWorktreeUnchanged']) and all(
    r['contractPassed'] for r in manifest['tests'])
write_json(output/'source-after.json', after); write_json(output/'test-result.json', manifest)
print(json.dumps({key: manifest[key] for key in ['passed', 'sourceFiles', 'sourceUnchanged', 'baseUnchanged', 'helperUnchanged', 'ownedWorktreeUnchanged', 'sourceArchiveSha256']}), flush=True)
raise SystemExit(0 if manifest['passed'] else 1)
