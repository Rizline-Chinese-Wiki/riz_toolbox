"""Apply the frozen S5 increment after checking its exact current-tree baseline."""
import hashlib
import json
import shutil
import subprocess
import zipfile
from datetime import datetime, timezone
from pathlib import Path

REPO = Path(__file__).resolve().parents[3]
CASE = Path('D:/riz-build-cache-next/playback-stock-provider-s5-r1-20261031/handoff-r1')
OUT = Path('D:/riz-build-cache-next/playback-stock-provider-main-integration-r1-20261031')
EXPECTED = '3ab341bf2624d40fa24a84a8c7106f66c41713fff3a7d48536b39358c3d5cf8b'


def read(path):
    return json.loads(Path(path).read_text(encoding='utf-8-sig'))


def row(path):
    path = Path(path).resolve()
    data = path.read_bytes()
    return dict(path=str(path), sha256=hashlib.sha256(data).hexdigest(), bytes=len(data))


def write(path, value):
    Path(path).write_text(json.dumps(value, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')


def verify(item):
    actual = row(item['path'])
    assert actual['sha256'] == item['sha256'], item['path']
    return actual


def inside(name):
    path = (REPO / name).resolve()
    assert path.is_relative_to(REPO), name
    return path


def git(*args, name):
    command = ['git', *args]
    write(OUT / (name + '.command.json'), dict(argv=command, cwd=str(REPO)))
    completed = subprocess.run(command, cwd=REPO, capture_output=True)
    (OUT / (name + '.stdout.log')).write_bytes(completed.stdout)
    (OUT / (name + '.stderr.log')).write_bytes(completed.stderr)
    (OUT / (name + '.exit.txt')).write_text(str(completed.returncode), encoding='utf-8')
    assert completed.returncode == 0, (name, completed.stderr.decode(errors='replace'))
    return completed.returncode


def run():
    assert not OUT.exists(), 'Use a fresh integration directory.'
    assert row(CASE / 'test-result.json')['sha256'] == EXPECTED
    proof = read(CASE / 'test-result.json')
    assert proof['passed'] and proof['changedFiles'] == 8 and not proof['sharedBridgeChanged']
    assert proof['executions']['checks-r4']['processes'] == 7
    assert proof['executions']['checks-r4']['checks'] == 1021
    assert proof['executions']['clock-regression-r3']['checks'] == 179
    assert proof['actualGitApplyCheckExit'] == proof['actualGitApplyExit'] == 0
    verified = []
    verify(proof['verificationIndex'])
    for index_name in ('verification-index.json', 'retained-evidence-manifest.json', 'native-input-manifest.json'):
        for item in read(CASE / index_name)['files']:
            verified.append(verify(item))
    before = read(CASE / 'baseline-manifest.json')
    after = read(CASE / 'source-manifest.json')
    changes = read(CASE / 'changed-files.json')
    assert len(before) == 756 and len(after) == 762 and len(changes) == 8
    before_map = {item['path']: item for item in before}
    after_map = {item['path']: item for item in after}
    assert len(before_map) == 756 and len(after_map) == 762
    with zipfile.ZipFile(CASE / 'before-source-snapshot.zip') as archive:
        assert len(archive.namelist()) == 756
        for item in before:
            assert hashlib.sha256(archive.read(item['path'])).hexdigest() == item['sha256']
            assert row(inside(item['path']))['sha256'] == item['sha256'], item['path']
    with zipfile.ZipFile(CASE / 'implemented-source-snapshot.zip') as archive:
        assert len(archive.namelist()) == 762
        for item in after:
            assert hashlib.sha256(archive.read(item['path'])).hexdigest() == item['sha256']
    actual_changes = {name for name, item in after_map.items()
                      if name not in before_map or item['sha256'] != before_map[name]['sha256']}
    assert actual_changes == {item['path'] for item in changes}
    for change in changes:
        path = inside(change['path'])
        if change['before'] is None:
            assert not path.exists(), str(path)
        else:
            assert row(path)['sha256'] == change['before']['sha256']
        assert after_map[change['path']]['sha256'] == change['after']['sha256']

    OUT.mkdir()
    backup = OUT / 'before-worktree-source.zip'
    with zipfile.ZipFile(backup, 'w', zipfile.ZIP_DEFLATED) as archive:
        for item in before:
            archive.writestr(item['path'], inside(item['path']).read_bytes())
    write(OUT / 'before-worktree-source.json', before)
    write(OUT / 'input-verification.json', dict(passed=True, files=verified,
                                               checkedAtUtc=datetime.now(timezone.utc).isoformat()))
    patch = CASE / 's5-stock-provider.patch'
    git('apply', '--check', '--whitespace=nowarn', str(patch), name='git-apply-check')
    git('apply', '--whitespace=nowarn', str(patch), name='git-apply')
    for item in after:
        assert row(inside(item['path']))['sha256'] == item['sha256'], item['path']
    assert row(inside('src/RizToolbox.Runtime/Hooks/NativePlaybackSingleReturnBridge.cs'))['sha256'] == proof['sharedBridgeHash']
    shutil.copy2(CASE / '2026-10-31_stock-provider-report.md',
                 REPO / 'docs/validation/playback-stock-writer-20261031/2026-10-31_stock-provider-report.md')
    result = dict(kind='stock-single-session-provider-main-source-integration', passed=True,
                  observedAtUtc=datetime.now(timezone.utc).isoformat(),
                  stageProof=row(CASE / 'test-result.json'), beforeArchive=row(backup),
                  sourceArchive=row(CASE / 'implemented-source-snapshot.zip'), patch=row(patch),
                  actualGitApplyCheckExit=0, actualGitApplyExit=0,
                  sourceFilesCompared=762, changedFiles=changes,
                  targetedProviderProcesses=7, targetedProviderChecks=1021,
                  targetedClockProcesses=10, targetedClockChecks=179,
                  mainSourceEqualsTestedStage=True, newIntegratedFullMatrixAccepted=False,
                  formalCrossRunAdoption=False, productionAdmission=False,
                  gameExecuted=False, installed=False)
    write(OUT / 'test-result.json', result)
    print(json.dumps(dict(passed=True, changedFiles=len(changes), sourceFiles=762,
                          result=row(OUT / 'test-result.json'))))


if __name__ == '__main__':
    run()
