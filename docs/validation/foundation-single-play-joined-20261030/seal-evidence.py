"""Copy verified joined-delivery evidence into this case and index original, copied and owned artifacts."""
from pathlib import Path
from datetime import datetime, timezone
import argparse
import hashlib
import json
import shutil

parser = argparse.ArgumentParser()
parser.add_argument('--source', required=True)
args = parser.parse_args()
case = Path(__file__).resolve().parent
root = case.parents[2]
source = Path(args.source).resolve()
digest = lambda path: hashlib.sha256(path.read_bytes()).hexdigest()
read = lambda path: json.loads(path.read_text(encoding='utf-8'))
result = read(source / 'test-result.json')
assert result['passed'] and len(result['tests']) == 7 and all(row['contractPassed'] for row in result['tests'])
assert all(result[key] for key in ['sourceUnchanged', 'baseUnchanged', 'ownedInputsUnchanged'])
owned = read(source / 'overlay-inputs.json')
for row in owned:
    live = root / row['path']
    if row['kind'] == 'owned-route-only':
        assert row['statement'] in live.read_text(encoding='utf-8').replace('\r\n', '\n')
    else:
        assert digest(live) == row['sha256'], row['path']
for row in read(source / 'source-before.json'):
    assert digest(source / 'source-snapshot' / row['path']) == row['sha256'], row['path']

copies = []
def copy(path, target):
    target.parent.mkdir(parents=True, exist_ok=True)
    before = digest(path)
    if target.exists():
        assert digest(target) == before, 'An earlier evidence copy would be overwritten: ' + str(target)
    else:
        shutil.copyfile(path, target)
    assert before == digest(path) == digest(target)
    copies.append({'path': target.relative_to(case).as_posix(), 'source': str(path),
                   'sha256': before, 'bytes': target.stat().st_size})
for name in ['test-result.json', 'overlay-inputs.json', 'base-inputs.json', 'source-before.json', 'source-after.json']:
    copy(source / name, case / name)
for path in sorted(source.glob('*.log')):
    copy(path, case / 'logs' / path.name)
copy(source / 'source-snapshot.zip', case / 'artifacts/source-snapshot.zip')
copy(source / 'native/RizToolbox.SinglePlayJoinedFixture.dll', case / 'artifacts/RizToolbox.SinglePlayJoinedFixture.dll')
assert digest(case / 'artifacts/source-snapshot.zip') == result['sourceArchiveSha256']
assert digest(case / 'artifacts/RizToolbox.SinglePlayJoinedFixture.dll') == result['nativeHelperSha256']
history = []
for path in sorted(source.parent.glob('single-play-joined-*')):
    proof = path / 'test-result.json'
    if path == source or not proof.is_file():
        continue
    value = read(proof)
    history.append({'path': str(path), 'testResultSha256': digest(proof), 'passed': value['passed'],
                    'nativeBuildExitCode': value.get('nativeBuild', {}).get('exitCode') if value.get('nativeBuild') else None,
                    'managedBuildExitCode': value.get('managedBuild', {}).get('exitCode') if value.get('managedBuild') else None})
reports = [{'path': path.name, 'sha256': digest(path), 'bytes': path.stat().st_size}
           for path in sorted(case.glob('*')) if path.is_file() and path.suffix in ['.md', '.py']]
index = {'schemaVersion': 1, 'kind': result['kind'], 'generatedAtUtc': datetime.now(timezone.utc).isoformat(),
         'finalOutput': str(source), 'passed': True, 'testResultSha256': digest(source / 'test-result.json'),
         'sourceArchiveSha256': result['sourceArchiveSha256'], 'nativeHelperSha256': result['nativeHelperSha256'],
         'newProcesses': 7, 'old39CasesRerun': False, 'gameExecuted': False, 'installed': False,
         'productionReadyIssued': False, 'productionFirstInstallVerified': False,
         'controlledPlayOperationCapture': True, 'controlledSourceAdmissionAndDeath': True,
         'actualProductionPlayOperationHookVerified': False,
         'productionOwnedFiles': [row for row in owned if row['kind'] == 'production'],
         'fixtureOnlyNativeFiles': [row for row in owned if row['path'].startswith('native/')],
         'copiedArtifacts': copies, 'reportsAndRunners': reports, 'retainedEarlierRuns': history}
(case / 'evidence-index.json').write_text(json.dumps(index, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
print(json.dumps({'passed': True, 'copiedArtifacts': len(copies), 'productionOwnedFiles': len(index['productionOwnedFiles']),
    'indexSha256': digest(case / 'evidence-index.json'), 'resultSha256': index['testResultSha256']}, ensure_ascii=False))
