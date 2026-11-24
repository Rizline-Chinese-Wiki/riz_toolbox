"""Read-only fixity audit of the completed Single subscription evidence; no build or test execution."""
from datetime import datetime, timezone
from pathlib import Path
from zipfile import ZipFile
import argparse
import hashlib
import json


parser = argparse.ArgumentParser()
parser.add_argument('--output', required=True)
parser.add_argument('--frozen', default='D:/riz-build-cache-next/single-subscriptions-frozen-r1-20261030')
args = parser.parse_args()
case = Path(__file__).resolve().parent
root = case.parents[2]
frozen, output = Path(args.frozen).resolve(), Path(args.output).resolve()
if output.exists():
    raise SystemExit('Use a fresh audit directory; earlier evidence is never overwritten.')
output.mkdir(parents=True)
digest = lambda path: hashlib.sha256(path.read_bytes()).hexdigest()
read_json = lambda path: json.loads(path.read_text(encoding='utf-8'))
checks = []
result = {
    'observedAtUtc': datetime.now(timezone.utc).isoformat(),
    'kind': 'single-subscriptions-completed-freeze-fixity-audit',
    'frozen': str(frozen),
    'worktree': str(root),
    'scriptSha256': digest(Path(__file__)),
    'buildExecuted': False,
    'testsExecuted': False,
    'gameExecuted': False,
    'installed': False,
    'checks': checks,
    'passed': False,
}


def check(name, passed, **details):
    checks.append({'name': name, 'passed': bool(passed), **details})


def verify_rows(name, directory, rows):
    mismatches = [row['path'] for row in rows if not (directory / row['path']).is_file()
                  or digest(directory / row['path']) != row['sha256']
                  or ('bytes' in row and (directory / row['path']).stat().st_size != row['bytes'])]
    check(name, not mismatches, count=len(rows), mismatches=mismatches)


def method(text, name):
    start = text.index('    public void ' + name + '(')
    opened, depth = text.index('{', start), 0
    for index in range(opened, len(text)):
        if text[index] == '{':
            depth += 1
        elif text[index] == '}':
            depth -= 1
            if depth == 0:
                return text[start:index + 1]
    raise ValueError('Unclosed owned method ' + name)


try:
    final = read_json(frozen / 'test-result.json')
    evidence = read_json(case / 'evidence-index.json')
    expected_result_hash = evidence['finalRun']['testResultSha256']
    check('original authoritative result matches the recorded final hash',
          digest(frozen / 'test-result.json') == expected_result_hash,
          sha256=digest(frozen / 'test-result.json'))
    status_keys = ['passed', 'sourceUnchanged', 'baseUnchanged', 'helperUnchanged', 'ownedWorktreeUnchanged']
    check('completed run reports every final invariant true', all(final[key] is True for key in status_keys))
    check('two successful builds and all 26 isolated run contracts are final',
          len(final['builds']) == 2 and len(final['tests']) == 26
          and all(row['exitCode'] == 0 for row in final['builds'] + final['tests'])
          and all(row['contractPassed'] is True for row in final['tests']))
    check('all seven source wiring supplements passed', len(final['sourceWiringChecks']) == 7
          and all(row['passed'] is True for row in final['sourceWiringChecks']))
    check('the frozen run retains its verification boundaries',
          all(final[key] is False for key in ['gameExecuted', 'installed', 'productionFirstInstallVerified',
                                              'singleProducerPositiveReadCurrentVerified', 'effectsCompletionVerified']))

    source = frozen / 'source-snapshot'
    before, after = read_json(frozen / 'source-before.json'), read_json(frozen / 'source-after.json')
    base, overlays = read_json(frozen / 'base-inputs.json'), read_json(frozen / 'overlay-inputs.json')
    check('source before and after manifests remain identical', before == after and len(before) == final['sourceFiles'])
    verify_rows('all frozen source bytes and lengths match', source, before)
    check('frozen source has no unrecorded file',
          {path.relative_to(source).as_posix() for path in source.rglob('*') if path.is_file()}
          == {row['path'] for row in before})
    verify_rows('all original dependency base bytes and lengths match', Path(final['base']), base)
    check('base and overlay manifests match the final result',
          digest(frozen / 'base-inputs.json') == final['baseManifestSha256']
          and digest(frozen / 'overlay-inputs.json') == final['overlayManifestSha256'])
    check('fixed native helper bytes match', digest(Path(final['helper'])) == final['helperSha256'])
    check('source archive bytes match', digest(frozen / 'source-snapshot.zip') == final['sourceArchiveSha256'])
    with ZipFile(frozen / 'source-snapshot.zip') as archive:
        zip_mismatches = [row['path'] for row in before
                          if hashlib.sha256(archive.read(row['path'])).hexdigest() != row['sha256']]
        check('source archive entries exactly match the frozen manifest', not zip_mismatches
              and len(archive.infolist()) == len(before)
              and set(archive.namelist()) == {row['path'] for row in before}, mismatches=zip_mismatches)

    for directory, label in [(root, 'current owned worktree'), (source, 'frozen owned overlay')]:
        full = [row for row in overlays if row['kind'] == 'whole-owned-file']
        verify_rows(label + ' full files match', directory, full)
        partial = [row for row in overlays if row['kind'] == 'owned-method-only']
        mismatches = [row['method'] for row in partial
                      if hashlib.sha256(method((directory / row['path']).read_bytes().decode('utf-8'),
                                               row['method']).encode('utf-8')).hexdigest() != row['sha256']]
        check(label + ' two Retirement methods match', not mismatches, count=len(partial), mismatches=mismatches)

    logs = [row[kind] for row in final['builds'] + final['tests'] for kind in ['stdout', 'stderr']]
    verify_rows('all original build and run logs match', frozen, logs)
    assemblies = {row['command'][1]: row['assemblySha256'] for row in final['tests']}
    check('both executed test assemblies still match',
          all(digest(Path(path)) == value for path, value in assemblies.items()), count=len(assemblies))
    verify_rows('all original case evidence artifacts match their index', case, evidence['artifacts'])
    check('copied result and manifests match the original final files',
          all(digest(case / name) == digest(frozen / name) for name in
              ['test-result.json', 'base-inputs.json', 'overlay-inputs.json', 'source-before.json', 'source-after.json']))
    result['summary'] = {
        'baseFiles': len(base), 'sourceFiles': len(before),
        'ownedWholeFiles': len(full), 'ownedMethods': len(partial),
        'originalLogFiles': len(logs), 'caseArtifacts': len(evidence['artifacts']),
        'singleManagedCases': 65, 'nativeOperationProcesses': 8, 'typedCompletionProcesses': 17,
        'testResultSha256': expected_result_hash, 'sourceArchiveSha256': final['sourceArchiveSha256'],
    }
    result['passed'] = all(row['passed'] for row in checks)
except Exception as error:
    result['error'] = type(error).__name__ + ': ' + str(error)

(output / 'fixity-result.json').write_text(json.dumps(result, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
print(json.dumps({'passed': result['passed'], 'checks': len(checks),
                  'failed': [row['name'] for row in checks if not row['passed']],
                  'error': result.get('error'), 'output': str(output / 'fixity-result.json')}, ensure_ascii=False))
raise SystemExit(0 if result['passed'] else 1)
