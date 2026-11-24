"""Apply the nine frozen writer files only to the accepted 885 source graph."""
import datetime as dt
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import tempfile

WORK = Path(__file__).resolve().parent
ROOT = WORK.parents[1]
PRIOR = ROOT / 'docs/validation/line-ring-target-provider-20261113'
CANDIDATE = ROOT / 'work/line-endpoint-writer-candidate-20261113'
SHADOW_RUN = Path('D:/riz-build-cache-next/line-endpoint-writer-candidate-20261113/root-r4')


def require(ok, why):
    if not ok:
        raise RuntimeError(why)


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def read(path):
    return json.loads(Path(path).read_text(encoding='utf-8-sig'))


def write(path, data):
    with Path(path).open('x', encoding='utf-8') as stream:
        stream.write(json.dumps(data, ensure_ascii=False, indent=2) + '\n')


def replace_exact(target, data, expected):
    require(target.resolve().is_relative_to(ROOT) and not any(p.is_symlink() for p in (target, *target.parents)), 'Target escaped root')
    require((sha(target) if target.exists() else None) == expected, 'Concurrent target change')
    if expected is None:
        with target.open('xb') as stream:
            stream.write(data)
    else:
        with tempfile.NamedTemporaryFile(dir=target.parent, delete=False) as stream:
            staged = Path(stream.name); stream.write(data)
        try:
            require(sha(target) == expected, 'Concurrent target change during staging')
            os.replace(staged, target)
        finally:
            staged.unlink(missing_ok=True)


def main():
    require(not (WORK / 'application-record.json').exists(), 'Preserve original application record')
    require(sha(WORK / 'payload-manifest.json') == 'bf10a76f416e1fcf5d0437a8b6ea8e27036d77f5ae2d9951667ddfca5b23f5a7'
            and sha(WORK / 'payload.patch') == '405103c5cb2b94d311c91cc642813dbf568d0a8f6713fda8987d2925b0c1ff65', 'Frozen payload changed')
    manifest = read(WORK / 'payload-manifest.json')
    require(sha(PRIOR / 'validation-summary.json') == manifest['acceptedBase']['validationSummarySha256']
            and read(PRIOR / 'validation-summary.json')['acceptedThisBatch'] is True, 'Prior 885 batch is not accepted')
    helper = ROOT / 'work/line-ring-source-validation-20261113/run_validation.py'
    require(sha(helper) == '61c2983ae117322b25ff96ad75b659b870fdadf9aada9274727248cd5ca98fd4', 'Source scanner changed')
    spec = importlib.util.spec_from_file_location('writer_apply_inputs', helper)
    module = importlib.util.module_from_spec(spec); spec.loader.exec_module(module)
    before = read(PRIOR / 'run/inputs-after.json')
    require(module.inputs() == before and len(before) == 885, 'Current source is not the accepted 885 graph')
    rows = manifest['files']
    whitelist = {
        'src/RizToolbox.Adapter/Gameplay/NativeSeekLineEndpointCommands.cs',
        'src/RizToolbox.Adapter/Gameplay/NativeSeekLineEndpointAccess.cs',
        'src/RizToolbox.Adapter/Gameplay/NativeSeekLineRingSourceCapture.EndpointAccess.cs',
        'src/RizToolbox.Adapter/Judgements/NativeJudgementBackend.LineRingEndpointAccess.cs',
        'src/RizToolbox.Runtime/NativeRuntime.LineEndpointPublication.cs',
        'tests/RizToolbox.Judgements.Tests/LineRingSource/NativeSeekLineRingSourceChecks.EndpointWriter.cs',
        'src/RizToolbox.Adapter/Gameplay/NativeSeekLineRingSourceCapture.Lifecycle.cs',
        'tests/RizToolbox.Judgements.Tests/Program.cs', 'scripts/Build.ps1',
    }
    require(len(rows) == 9 and {row['path'] for row in rows} == whitelist, 'Unexpected integration target set')
    shadow = read(SHADOW_RUN / 'result.json')
    require(sha(SHADOW_RUN / 'result.json') == 'e43ceeea7874dd67f20373e8c5bf8da981176a5b872a4842dacb1be28293dcca'
            and shadow['passed'] is True and shadow['inputsUnchanged'] is True
            and shadow['compiledUnchanged'] is True and len(shadow['passNames']) == 17
            and all(row['exitCode'] == 0 and row['naturalExit'] for row in shadow['steps']), 'Fresh root candidate did not pass')
    for row in rows:
        name = row['path']; target = ROOT / name
        require((sha(target) if target.exists() else None) == row['baseSha256']
                and sha(WORK / 'files' / name) == row['payloadSha256'], 'Target or payload byte differs: ' + name)
        if 'candidateSha256' in row:
            candidate_name = name if name.startswith('src/') else 'tests/NativeSeekLineRingSourceChecks.EndpointWriter.cs'
            require(sha(CANDIDATE / candidate_name) == row['candidateSha256'], 'Candidate source changed')
            if name.startswith('tests/'):
                candidate = (CANDIDATE / candidate_name).read_bytes()
                payload = (WORK / 'files' / name).read_bytes()
                require(payload == candidate.replace(b'"actual endpoint writer ', b'"Line ring source endpoint writer '),
                        'Writer test bodies changed while converting registry labels')
    after = dict(before)
    for row in rows:
        if row['path'] != 'scripts/Build.ps1':
            after[row['path']] = row['payloadSha256']
    require(len(after) == 891, 'Expected six new compiler inputs')
    changed = []
    try:
        expected = dict(before)
        for row in rows:
            require(module.inputs() == expected, 'Concurrent compiler input edit')
            replace_exact(ROOT / row['path'], (WORK / 'files' / row['path']).read_bytes(), row['baseSha256'])
            changed.append(row)
            if row['path'] != 'scripts/Build.ps1':
                expected[row['path']] = row['payloadSha256']
        require(module.inputs() == after, 'Written source graph differs')
        write(WORK / 'inputs-before.json', before); write(WORK / 'inputs-after.json', after)
        write(WORK / 'application-record.json', {
            'appliedUtc': dt.datetime.now(dt.timezone.utc).isoformat(), 'appliedToMain': True,
            'priorInputs': 885, 'newInputs': 891, 'newValidationPassed': False,
            'candidateObservedRootExec': {'sessionId': 23504, 'completionChunkId': '92fa40', 'actualExitCode': 0},
            'candidateResultSha256': sha(SHADOW_RUN / 'result.json'), 'manifestSha256': sha(WORK / 'payload-manifest.json'),
            'sdkGoalComplete': False, 'completeSeekPermitted': False, 'atomicConditionalWrites': False,
            'gameStarted': False, 'installed': False})
    except Exception as failure:
        rollback = []
        for row in reversed(changed):
            try:
                target = ROOT / row['path']
                require(sha(target) == row['payloadSha256'], 'Concurrent user edit left untouched')
                if row['baseSha256'] is None:
                    target.unlink()
                else:
                    old = (WORK / 'before' / row['path']).read_bytes()
                    require(hashlib.sha256(old).hexdigest() == row['baseSha256'], 'Backup byte differs')
                    replace_exact(target, old, row['payloadSha256'])
                rollback.append({'path': row['path'], 'restored': True})
            except Exception as error:
                rollback.append({'path': row['path'], 'restored': False, 'reason': str(error)})
        write(WORK / 'failed-application.json', {'failure': str(failure), 'rollback': rollback})
        raise
    print('Applied the nine frozen files to main; compiler inputs 885 -> 891. Fresh main validation remains required.')


if __name__ == '__main__':
    main()
