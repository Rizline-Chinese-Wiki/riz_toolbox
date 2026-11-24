"""Freeze completed M-batch evidence; grants no game or cancellation authority."""
import argparse
import hashlib
import json
from pathlib import Path
import re
import zipfile

REPO = Path(__file__).resolve().parents[3]
CACHE = Path('C:/Users/71957/riz-build-cache')


def sha(data):
    return hashlib.sha256(data).hexdigest()


def read(path):
    return json.loads(Path(path).read_text(encoding='utf-8-sig'))


def row(path):
    path = Path(path).resolve()
    return dict(path=str(path), sha256=sha(path.read_bytes()), bytes=path.stat().st_size)


def require(condition, reason):
    if not condition:
        raise ValueError(reason)


def verify_paths(value):
    if isinstance(value, dict):
        if 'path' in value and 'sha256' in value:
            path = Path(value['path'])
            require(path.is_absolute() and path.is_file(), 'Missing absolute evidence input: ' + str(path))
            require(sha(path.read_bytes()) == value['sha256'], 'Evidence SHA mismatch: ' + str(path))
        for child in value.values():
            verify_paths(child)
    elif isinstance(value, list):
        for child in value:
            verify_paths(child)


def archive(path, entries):
    require(not path.exists(), 'Use a fresh archive: ' + str(path))
    with zipfile.ZipFile(path, 'x', compression=zipfile.ZIP_DEFLATED) as z:
        for name, source in sorted(entries.items()):
            z.writestr(name, source.read_bytes())
    result = row(path)
    result['entries'] = len(entries)
    return result


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--managed', required=True)
    p.add_argument('--joint-native', required=True)
    p.add_argument('--joint-cases', required=True)
    p.add_argument('--output', required=True)
    args = p.parse_args()
    managed, joint_native, joint_cases, output = [Path(v).resolve() for v in
        (args.managed, args.joint_native, args.joint_cases, args.output)]
    require(not output.exists(), 'Use a fresh evidence directory.')
    kernel = CACHE / 'engine-scheduler-kernel-r2-20261026'
    fixture = CACHE / 'engine-scheduler-fixture-r2-20261026'
    native_cases = CACHE / 'engine-scheduler-cases-r1-20261026'
    receipts = [
        CACHE / 'engine-scheduler-contract-r1-20261026/fixed-sites.json',
        kernel / 'kernel-manifest.json', kernel / 'scheduler-pe-verification-r1.json',
        fixture / 'scheduler-fixture-build.json', native_cases / 'fixture-result.json',
        managed / 'test-result.json', joint_native / 'scheduler-managed-fixture-build.json',
        joint_cases / 'fixture-result.json']
    values = [read(path) for path in receipts]
    require(sha(Path(values[0]['image']).read_bytes()) == values[0]['imageSha256'], 'Fixed game image changed.')
    for value in values:
        require(value.get('passed', value.get('buildSucceeded', True)) is True, 'A required receipt failed.')
        for flag in ('gameExecuted', 'installed', 'productionAdmissionVerified', 'engineCancellationCompleted'):
            require(value.get(flag, False) is False, 'Unexpected game/admission authority: ' + flag)
        verify_paths(value)
    require(values[2]['checks'] == 33110, 'Unexpected verified kernel generation.')
    require(values[5]['sourceHashesUnchanged'] and values[5]['audioChecks'] == 742
        and values[5]['coordinatorChecks'] == 23 and values[5]['receiverChecks'] == 10,
        'Exact completed managed regression summaries are required.')
    for name, pattern, expected in [('audio', r'(\d+)/(\d+) audio/content checks passed\.', 742),
        ('coordinator', r'Engine managed checks: (\d+)/(\d+) passed\.', 23),
        ('receiver', r'Engine managed checks: (\d+)/(\d+) passed\.', 10)]:
        match = re.search(pattern, (managed / (name + '-tests.log')).read_text(encoding='utf-8-sig'))
        require(match is not None and tuple(map(int, match.groups())) == (expected, expected), 'Actual managed child summary mismatch: ' + name)
    native_children, joint_children = [], []
    for value, expected, children in [(values[4], 16, native_children), (values[7], 7, joint_children)]:
        require(len(value['cases']) == expected, 'Exact independent case count required.')
        for case in value['cases']:
            require(case['passed'] and case['exitCode'] == 0 and not case.get('timedOut', False), 'An actual independent process failed.')
            child = read(case['childJson']['path'])
            require(child['passed'] and child['checks'] > 0 and child['failures'] == 0, 'Child execution checks are absent.')
            children.append(child)
    for child in joint_children:
        require(child['actualNativeToClrCallbacksVerified'] and child['actualSchedulerRowLedgerVerified']
            and child['actualNodeIdentityLedgerVerified'] and child['trackerIdentityProvidersControlled']
            and child['nativeFailures'] == 0 and child['nativeChecks'] > 0,
            'The actual owned-DLL/CLR ledger path is unverified.')
        for flag in ('actualTrackerBackendExecuted', 'productionPublishedAdoptionUsed', 'schedulerClosureVerified',
            'futureProducerClosureVerified', 'engineCancelled', 'engineCancellationCompleted', 'gameExecuted', 'installed'):
            require(child[flag] is False, 'Owned evidence must not supply game/future/cancellation authority.')
        for image in child['files']:
            require(image['beforeSha256'] == image['afterSha256'] == sha(Path(image['path']).read_bytes()),
                'The actual loaded joint DLL changed.')
    source_entries = {}
    for value in (values[1], values[3], values[6]):
        for item in value['sources']:
            path = Path(item['path']).resolve()
            require(path.is_relative_to(REPO), 'Unexpected native source outside repository.')
            source_entries[path.relative_to(REPO).as_posix()] = path
    for path in (Path(__file__), Path(__file__).with_name('verify-scheduler-kernel.py'),
                 Path(__file__).with_name('inspect-scheduler-sites.py'),
                 Path(__file__).parent.parent / 'foundation-engine-inline-20261026/verify-inline-kernel.py'):
        source_entries[path.resolve().relative_to(REPO).as_posix()] = path
    artifact_entries = {}
    for directory in (kernel, fixture, native_cases, joint_native, managed, joint_cases):
        require(directory.is_relative_to(CACHE), 'Evidence must remain in the declared build cache.')
        for path in directory.rglob('*'):
            if path.is_file() and path.suffix != '.zip':
                artifact_entries[path.relative_to(CACHE).as_posix()] = path
    output.mkdir()
    archives = [archive(output / 'native-evidence-sources.zip', source_entries),
                archive(output / 'executed-artifacts.zip', artifact_entries), row(managed / 'managed-project-sources.zip')]
    for value in values:
        verify_paths(value)
    result = dict(schemaVersion=1, kind='engine-scheduler-frozen-evidence-index', passed=True,
        kernelDiskChecks=values[2]['checks'], nativeCases=16,
        nativeAssertions=sum(child['checks'] for child in native_children),
        audioChecks=742, coordinatorChecks=23, receiverChecks=10, managedInputs=len(values[5]['inputs']),
        actualNativeClrCases=7, jointManagedAssertions=sum(child['checks'] for child in joint_children),
        jointNativeAssertions=sum(child['nativeChecks'] for child in joint_children),
        sourceFixityVerified=True, trackerIdentityProvidersControlled=True,
        gameExecuted=False, installed=False, productionAdmissionVerified=False,
        engineCancellationCompleted=False, allFutureProducersClosed=False,
        receipts=[row(path) for path in receipts], archives=archives,
        limitations=['Owned fixture execution is not fixed Unity execution.',
            'Address reuse uses a retained owned reservation with decommit/recommit, not system address release/reacquisition.',
            'Current page state after CLR entry is diagnostic; old allocation release uses checked native operation receipts.',
            'No ordinary-wait, all-consumer retirement, future-producer closure or cancellation completion is granted.'])
    index = output / 'evidence-index.json'
    index.write_text(json.dumps(result, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(row(index)))
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
