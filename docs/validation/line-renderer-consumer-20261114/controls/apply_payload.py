"""Apply the reviewed, freshly tested candidate while preserving original bytes."""
import datetime as dt
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import shutil

ROOT = Path(__file__).resolve().parents[2]
WORK = Path(__file__).resolve().parent


def sha(path):
    with Path(path).open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def read(path):
    return json.loads(Path(path).read_text(encoding='utf-8-sig'))


def save(path, value):
    temporary = Path(str(path) + '.pending')
    temporary.write_text(json.dumps(value, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    os.replace(temporary, path)


def require(ok, reason):
    if not ok:
        raise RuntimeError(reason)


def main():
    record_path = WORK / 'application-record.json'
    require(not record_path.exists(), 'Preserve prior applications')
    manifest_path = WORK / 'payload-manifest.json'
    manifest = read(manifest_path)
    require(manifest['schema'] == 'renderer-consumer-main-payload/v1'
            and len(manifest['baseInputs']) == 913 and len(manifest['expectedInputs']) == 922
            and manifest['freshMainPassed'] is False, 'Payload scope differs')
    require(sha(WORK / 'shadow-root-exit.json') == manifest['rootExitSha256'], 'Candidate root witness changed')
    scanner_path = ROOT / 'work/line-ring-source-validation-20261113/run_validation.py'
    require(sha(scanner_path) == '61c2983ae117322b25ff96ad75b659b870fdadf9aada9274727248cd5ca98fd4', 'Scanner changed')
    spec = importlib.util.spec_from_file_location('renderer_apply_scanner', scanner_path)
    scanner = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(scanner)
    require(scanner.inputs() == manifest['baseInputs'], 'Accepted main changed before application')
    require(sha(ROOT / 'README.md') == manifest['readmeSha256'], 'Preserve user README')
    for row in manifest['files']:
        destination = (ROOT / row['target']).resolve()
        source = (ROOT / row['candidate']).resolve()
        require(destination.is_relative_to(ROOT) and source.is_relative_to(WORK), 'Payload escaped its workspace')
        require(sha(source) == row['sha256'], 'Candidate bytes changed')
        require(sha(destination) == row['baseSha256'] if row['baseSha256'] is not None else not destination.exists(),
                'Destination changed before application')
    backup = WORK / 'main-backup'
    require(not backup.exists(), 'Preserve existing backups')
    backup.mkdir()
    for row in manifest['files']:
        if row['baseSha256'] is not None:
            target = backup / row['target']
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(ROOT / row['target'], target)
            require(sha(target) == row['baseSha256'], 'Original backup differs')
    state = dict(schema='renderer-consumer-main-application/v1', startedUtc=dt.datetime.now(dt.timezone.utc).isoformat(),
                 payloadManifestSha256=sha(manifest_path), appliedFiles=[],
                 compilerInputsBefore=913, compilerInputsAfter=None, applied=False,
                 freshMainPassed=False, installed=False, gameStarted=False, sdkGoalComplete=False)
    save(record_path, state)
    try:
        for row in manifest['files']:
            target = ROOT / row['target']
            target.parent.mkdir(parents=True, exist_ok=True)
            staging = target.with_name(target.name + '.renderer-applying')
            require(not staging.exists(), 'Preserve existing staging file')
            shutil.copyfile(ROOT / row['candidate'], staging)
            require(sha(staging) == row['sha256'], 'Staged payload differs')
            os.replace(staging, target)
            state['appliedFiles'].append(row['target'])
            save(record_path, state)
        current = scanner.inputs()
        require(current == manifest['expectedInputs'], 'Applied compiler graph differs')
        require(sha(ROOT / 'README.md') == manifest['readmeSha256'], 'User README changed during application')
        state.update(applied=True, compilerInputsAfter=len(current), finishedUtc=dt.datetime.now(dt.timezone.utc).isoformat())
        save(record_path, state)
    except Exception as error:
        state['failure'] = str(error)
        save(record_path, state)
        raise
    print(json.dumps(dict(appliedFiles=len(state['appliedFiles']), inputs=state['compilerInputsAfter'], freshMainPassed=False)))


if __name__ == '__main__':
    main()
