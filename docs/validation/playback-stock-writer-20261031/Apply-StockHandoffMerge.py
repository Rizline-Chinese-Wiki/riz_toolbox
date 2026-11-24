"""Apply the reviewed S5+S6 composition; execution acceptance is separate."""
import hashlib
import json
import subprocess
import zipfile
from datetime import datetime, timezone
from pathlib import Path

REPO = Path(__file__).resolve().parents[3]
ROOT = Path('D:/riz-build-cache-next/playback-stock-handoff-main-integration-r2-20261031')


def read(path):
    return json.loads(Path(path).read_text(encoding='utf-8-sig'))


def row(path):
    path = Path(path).resolve()
    data = path.read_bytes()
    return dict(path=str(path), sha256=hashlib.sha256(data).hexdigest(), bytes=len(data))


def write(path, value):
    Path(path).write_text(json.dumps(value, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')


def run():
    assert not (ROOT / 'test-result.json').exists()
    proof = read(ROOT / 'prepared-result.json')
    assert proof['passed'] and proof['sourceFiles'] == 778
    for key in ('beforeArchive', 'sourceArchive', 'patch', 's5Proof', 's6Snapshot'):
        assert row(proof[key]['path'])['sha256'] == proof[key]['sha256']
    with zipfile.ZipFile(proof['beforeArchive']['path']) as archive:
        assert len(archive.namelist()) == 766
        for entry in archive.infolist():
            assert (REPO / entry.filename).read_bytes() == archive.read(entry), entry.filename
    for item in read(ROOT / 'changed-files.json'):
        if item['beforeSha256'] is None:
            assert not (REPO / item['relativePath']).exists()
    for mode, argv in (('check', ['git', 'apply', '--check', '--whitespace=nowarn', proof['patch']['path']]),
                       ('apply', ['git', 'apply', '--whitespace=nowarn', proof['patch']['path']])):
        write(ROOT / ('main-' + mode + '.command.json'), dict(argv=argv, cwd=str(REPO)))
        result = subprocess.run(argv, cwd=REPO, capture_output=True)
        (ROOT / ('main-' + mode + '.stdout.log')).write_bytes(result.stdout)
        (ROOT / ('main-' + mode + '.stderr.log')).write_bytes(result.stderr)
        (ROOT / ('main-' + mode + '.exit.txt')).write_text(str(result.returncode), encoding='utf-8')
        assert result.returncode == 0, result.stderr.decode(errors='replace')
    with zipfile.ZipFile(proof['sourceArchive']['path']) as archive:
        assert len(archive.namelist()) == 778
        for entry in archive.infolist():
            assert (REPO / entry.filename).read_bytes() == archive.read(entry), entry.filename
    result = dict(kind='stock-joint-logical-handoff-main-source-integration', passed=True,
                  observedAtUtc=datetime.now(timezone.utc).isoformat(), sourceFilesCompared=778,
                  changedFiles=23, preparedProof=row(ROOT / 'prepared-result.json'),
                  sourceArchive=proof['sourceArchive'], beforeArchive=proof['beforeArchive'], patch=proof['patch'],
                  actualMainGitApplyCheck=0, actualMainGitApply=0,
                  s5ModuleAndFaultPumpPreserved=True, bridgeSehFixPreserved=True,
                  newIntegratedExecutionAccepted=False, formalModuleCrossRunAdoption=False,
                  productionAdmission=False, gameExecuted=False, installed=False)
    write(ROOT / 'test-result.json', result)
    print(json.dumps(dict(passed=True, sourceFiles=778, changedFiles=23, result=row(ROOT / 'test-result.json'))))


if __name__ == '__main__':
    run()
