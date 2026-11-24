"""Read-only verification and seal of the successful focused output; no build or tests run."""
from pathlib import Path
from datetime import datetime, timezone
from zipfile import ZipFile
import argparse
import hashlib
import json

parser = argparse.ArgumentParser()
parser.add_argument('--output', required=True)
parser.add_argument('--frozen', default='D:/riz-build-cache-next/single-play-effects-frozen-r1-20261030')
args = parser.parse_args()
root = Path(__file__).resolve().parents[3]
frozen, output = Path(args.frozen).resolve(), Path(args.output).resolve()
if output.exists():
    raise SystemExit('Use a fresh seal directory.')
output.mkdir(parents=True)
digest = lambda path: hashlib.sha256(path.read_bytes()).hexdigest()
read_json = lambda path: json.loads(path.read_text(encoding='utf-8'))
result = read_json(frozen / 'test-result.json')
before, after = read_json(frozen / 'source-before.json'), read_json(frozen / 'source-after.json')
base, overlays = read_json(frozen / 'base-inputs.json'), read_json(frozen / 'overlay-inputs.json')
checks = []


def check(name, condition, count=None):
    checks.append({'name': name, 'passed': bool(condition), 'count': count})


def rows_match(directory, rows):
    return all((directory / row['path']).is_file() and digest(directory / row['path']) == row['sha256']
               and ('bytes' not in row or (directory / row['path']).stat().st_size == row['bytes']) for row in rows)


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
    raise ValueError('Unclosed method ' + name)


check('one final successful build and 39 complete isolated contracts', result['passed'] is True
      and len(result['builds']) == 1 and len(result['tests']) == 39
      and all(row['exitCode'] == 0 for row in result['builds'] + result['tests'])
      and all(row['contractPassed'] is True for row in result['tests']))
check('source before/after manifests agree', before == after)
check('all frozen source bytes and lengths match', rows_match(frozen / 'source-snapshot', before), len(before))
check('original dependency base bytes and lengths match', rows_match(Path(result['base']), base), len(base))
check('base and overlay manifest hashes match', digest(frozen / 'base-inputs.json') == result['baseManifestSha256']
      and digest(frozen / 'overlay-inputs.json') == result['overlayManifestSha256'])
full = [row for row in overlays if row['kind'] == 'whole-owned-file']
check('all owned whole worktree files match', rows_match(root, full), len(full))
partial = [row for row in overlays if row['kind'] != 'whole-owned-file']
check('two owned methods and one completion statement still match', all(
    hashlib.sha256(method((root / row['path']).read_bytes().decode('utf-8'), row['method']).encode('utf-8')).hexdigest() == row['sha256']
    if row['kind'] == 'owned-method-only'
    else hashlib.sha256(row['statement'].encode('utf-8')).hexdigest() == row['sha256']
        and row['statement'] in (root / row['path']).read_bytes().decode('utf-8').replace('\r\n', '\n')
    for row in partial), len(partial))
check('source archive file hash matches', digest(frozen / 'source-snapshot.zip') == result['sourceArchiveSha256'])
with ZipFile(frozen / 'source-snapshot.zip') as archive:
    check('every source archive entry matches', len(archive.infolist()) == len(before)
          and set(archive.namelist()) == {row['path'] for row in before}
          and all(hashlib.sha256(archive.read(row['path'])).hexdigest() == row['sha256'] for row in before), len(before))
logs = [row[kind] for row in result['builds'] + result['tests'] for kind in ['stdout', 'stderr']]
check('all final build/run logs match', rows_match(frozen, logs), len(logs))
check('all tests name the same still-matching executed assembly', len({row['assemblySha256'] for row in result['tests']}) == 1
      and all(digest(Path(row['command'][1])) == row['assemblySha256'] for row in result['tests']))
seal = {'observedAtUtc': datetime.now(timezone.utc).isoformat(), 'passed': all(row['passed'] for row in checks),
        'kind': 'single-exact-play-effects-focused-freeze-seal', 'frozen': str(frozen),
        'buildExecuted': False, 'testsExecuted': False, 'gameExecuted': False, 'installed': False,
        'testResultSha256': digest(frozen / 'test-result.json'), 'sourceArchiveSha256': result['sourceArchiveSha256'],
        'overlayManifestSha256': result['overlayManifestSha256'], 'checks': checks}
(output / 'freeze-seal.json').write_text(json.dumps(seal, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
print(json.dumps({'passed': seal['passed'], 'checks': len(checks), 'testResultSha256': seal['testResultSha256']}))
raise SystemExit(0 if seal['passed'] else 1)
