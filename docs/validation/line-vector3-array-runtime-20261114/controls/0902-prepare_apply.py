"""Stage or apply only the actual shadow-tested Vector3 source files.

The accepted dynamic main is a prerequisite. No build, game or installation is
performed here. The scoped test entry point is the exact one compiled in r2.
"""
import argparse
import datetime as dt
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import tempfile

WORK = Path(__file__).resolve().parent
ROOT = WORK.parents[1]
BASIS = ROOT / 'docs/validation/line-dynamic-consumer-20261113'
CANDIDATE = ROOT / 'work/line-vector3-array-next-20261113'
SHADOW_WORK = ROOT / 'work/line-vector3-array-root-20261113'
SHADOW = SHADOW_WORK / 'root-r2-snapshot'
SCANNER = ROOT / 'work/line-ring-source-validation-20261113/run_validation.py'
PROGRAM = 'tests/RizToolbox.Runtime.Tests/Program.cs'
NEW = (
    'src/RizToolbox.Runtime/NativeRuntime.Vector3Array.cs',
    'tests/RizToolbox.Runtime.Tests/FakeNative.Vector3Array.cs',
    'tests/RizToolbox.Runtime.Tests/Vector3ArrayPublicationRegression.cs',
)
MANIFEST_SHA = '1823779180ec65632cdd79f0c42d60d26638e56bbd31fc7c4940f25d9327a776'
SHADOW_RESULT_SHA = '9e2144ad810124db23595e831023d1bf2f63c7d23b9f137379d2abe3a8a1d745'
SHADOW_HANDLE_SHA = 'd44e937940312fd8f87264ae08909f1f5055271fc6c0d42ccf6a6d9951ae993b'
SHADOW_ROOT_SHA = 'e0a6ba7e6861a631793e2a1f74f722ae6169c83d6f90bf76c2e15e1b9cdee07b'
ROUTED_PROGRAM_SHA = '5644833d6ab42f495066727d76b5408c8d745feb2a3a88c9e8bc3a32297238a4'


def require(ok, why):
    if not ok:
        raise RuntimeError(why)


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def read(path):
    return json.loads(Path(path).read_text(encoding='utf-8-sig'))


def write_new(path, data):
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open('xb') as stream:
        stream.write(data)


def encoded(value):
    return (json.dumps(value, ensure_ascii=False, indent=2) + '\n').encode('utf-8')


def scanner():
    require(sha(SCANNER) == '61c2983ae117322b25ff96ad75b659b870fdadf9aada9274727248cd5ca98fd4', 'Input scanner changed')
    spec = importlib.util.spec_from_file_location('vector_array_main_scanner', SCANNER)
    value = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(value)
    return value


def preflight(summary_sha):
    require(len(summary_sha) == 64 and sha(BASIS / 'validation-summary.json') == summary_sha, 'Explicit accepted dynamic summary differs')
    summary = read(BASIS / 'validation-summary.json')
    basis_result = read(BASIS / 'run/result.json')
    before = read(BASIS / 'run/inputs-after.json')
    require(summary['acceptedThisBatch'] and summary['compilerInputs'] == len(before) == 898
            and summary['focusedPassedCases'] == 155 and summary['naturalSteps'] == 78
            and summary['sourceCases'] == 30 and summary['writerCases'] == 36
            and summary['resultSha256'] == sha(BASIS / 'run/result.json')
            and summary['inputGraphSha256'] == sha(BASIS / 'run/inputs-after.json')
            and basis_result['passed'] and basis_result['fullSource30ExecutedThisBatch'], 'Dynamic main is not accepted')
    require(sha(CANDIDATE / 'MANIFEST.sha256') == MANIFEST_SHA, 'Frozen array manifest differs')
    require(sha(SHADOW / 'result.json') == SHADOW_RESULT_SHA
            and sha(SHADOW / 'controller-handle-exit.json') == SHADOW_HANDLE_SHA
            and sha(SHADOW_WORK / 'root-shadow-exit.json') == SHADOW_ROOT_SHA, 'Actual array shadow exit chain differs')
    result = read(SHADOW / 'result.json')
    handle = read(SHADOW / 'controller-handle-exit.json')
    root_exit = read(SHADOW_WORK / 'root-shadow-exit.json')
    require(result['passed'] and len(result['steps']) == 4
            and sum(len(row['passNames']) for row in result['steps']) == 61
            and all(row['naturalExit'] and row['exitCode'] == 0 and row['matchesExpected'] for row in result['steps'])
            and handle['actualExitCode'] == 0 and handle['handleKeptUntilExit']
            and handle['resultSha256AtExit'] == SHADOW_RESULT_SHA
            and root_exit['observedBy'] == 'root'
            and root_exit['observedRootExec']['actualExitCode'] == 0
            and root_exit['resultSha256'] == SHADOW_RESULT_SHA
            and root_exit['handleExitSha256'] == SHADOW_HANDLE_SHA
            and result['mainInputsBefore'] == result['mainInputsAfter'] == before, 'Shadow is not the original successful 898-base execution')
    # Three candidate files were compiled directly; the actual routed Program
    # came from a separate frozen control. Do not certify the uncompiled route.
    sources = {name: CANDIDATE / name for name in NEW}
    sources[PROGRAM] = SHADOW / 'controls/Program.cs'
    require(sha(sources[PROGRAM]) == result['controlsBefore'][str(SHADOW_WORK / 'Program.cs')] == ROUTED_PROGRAM_SHA, 'Actual compiled scoped entry point differs')
    for name in NEW:
        require(name not in before and sha(sources[name]) == result['candidateFilesSha256'][name]
                and sha(SHADOW / 'candidate' / name) == result['candidateFilesSha256'][name], 'Actual compiled candidate source differs: ' + name)
    for row in result['steps']:
        for stream in ('stdout', 'stderr'):
            require(sha(SHADOW / 'logs' / (row['name'] + '.' + stream + '.log')) == row[stream + 'Sha256'], 'Shadow log changed')
    after = dict(before)
    after.update({name: sha(path) for name, path in sources.items()})
    require(len(after) == 901, 'Exactly three new compiler inputs are required')
    return summary, before, sources, after


def replace(target, data, expected):
    require(target.resolve().is_relative_to(ROOT.resolve())
            and not any(p.is_symlink() for p in (target, *target.parents) if p.exists()), 'Unsafe main target')
    require(not target.exists() if expected is None else target.is_file() and sha(target) == expected, 'Concurrent target change')
    with tempfile.NamedTemporaryFile(dir=target.parent, delete=False) as stream:
        temp = Path(stream.name)
        stream.write(data)
    try:
        if expected is None:
            os.link(temp, target)
        else:
            require(sha(target) == expected, 'Concurrent target change before replacement')
            os.replace(temp, target)
    finally:
        temp.unlink(missing_ok=True)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('action', choices=('stage', 'apply'))
    parser.add_argument('--basis-summary-sha256', required=True)
    parser.add_argument('--manifest-sha256')
    args = parser.parse_args()
    summary, before, sources, after = preflight(args.basis_summary_sha256)
    helper = scanner()
    require(helper.inputs() == before, 'Main must remain the accepted 898-input graph')
    require(not (WORK / 'application-record.json').exists() and not (WORK / 'failed-application.json').exists(), 'Preserve prior application history')
    if args.action == 'stage':
        require(args.manifest_sha256 is None and not (WORK / 'payload-manifest.json').exists(), 'Preserve prior stage')
        rows = []
        for name, source in sources.items():
            write_new(WORK / 'files' / name, source.read_bytes())
            if name in before:
                write_new(WORK / 'before' / name, (ROOT / name).read_bytes())
            rows.append({'path': name, 'sourcePath': str(source), 'baseSha256': before.get(name), 'payloadSha256': sha(source)})
        control_paths = [Path(__file__).resolve(), SCANNER, CANDIDATE / 'MANIFEST.sha256',
                         BASIS / 'validation-summary.json', BASIS / 'run/result.json', BASIS / 'run/inputs-after.json',
                         SHADOW / 'result.json', SHADOW / 'controller-handle-exit.json', SHADOW_WORK / 'root-shadow-exit.json', *sources.values()]
        manifest = {'schema': 'line-vector3-array-main-payload/v1', 'stagedUtc': dt.datetime.now(dt.timezone.utc).isoformat(),
                    'basisSummarySha256': args.basis_summary_sha256, 'basisResultSha256': summary['resultSha256'],
                    'acceptedInputs': 898, 'expectedInputs': 901, 'files': rows,
                    'controls': {str(p): sha(p) for p in control_paths}, 'inputsBefore': before, 'inputsAfter': after,
                    'shadowResultSha256': SHADOW_RESULT_SHA, 'shadowPositiveChecks': 61,
                    'mainApplied': False, 'freshMainPassed': False, 'sdkGoalComplete': False, 'completeSeekPermitted': False}
        write_new(WORK / 'payload-manifest.json', encoded(manifest))
        print(json.dumps({'staged': True, 'manifestSha256': sha(WORK / 'payload-manifest.json'), 'mainChanged': False}))
        return
    require(args.manifest_sha256 and sha(WORK / 'payload-manifest.json') == args.manifest_sha256, 'Explicit reviewed payload SHA differs')
    manifest = read(WORK / 'payload-manifest.json')
    require(manifest['inputsBefore'] == before and manifest['inputsAfter'] == after
            and manifest['basisSummarySha256'] == args.basis_summary_sha256
            and all(sha(path) == digest for path, digest in manifest['controls'].items()), 'Staged input or control changed')
    require({row['path'] for row in manifest['files']} == set(sources) and len(manifest['files']) == 4, 'Exact four-file payload required')
    for row in manifest['files']:
        name = row['path']
        require(row['baseSha256'] == before.get(name) and row['payloadSha256'] == sha(sources[name])
                and sha(WORK / 'files' / name) == row['payloadSha256']
                and (sha(WORK / 'before' / name) == row['baseSha256'] if name in before else not (ROOT / name).exists()), 'Staged bytes differ')
    changed = []
    try:
        for row in manifest['files']:
            replace(ROOT / row['path'], (WORK / 'files' / row['path']).read_bytes(), row['baseSha256'])
            changed.append(row)
        require(helper.inputs() == after, 'Written graph differs from reviewed 901 inputs')
        write_new(WORK / 'application-record.json', encoded({'schema': 'line-vector3-array-main-application/v1',
                  'appliedUtc': dt.datetime.now(dt.timezone.utc).isoformat(), 'payloadManifestSha256': args.manifest_sha256,
                  'basisSummarySha256': args.basis_summary_sha256, 'mainApplied': True, 'newInputs': 901,
                  'freshMainPassed': False, 'sdkGoalComplete': False, 'completeSeekPermitted': False}))
    except Exception as error:
        rollback = []
        for row in reversed(changed):
            target = ROOT / row['path']
            try:
                require(sha(target) == row['payloadSha256'], 'Concurrent user change left untouched')
                if row['baseSha256'] is None:
                    target.unlink()
                else:
                    replace(target, (WORK / 'before' / row['path']).read_bytes(), row['payloadSha256'])
                rollback.append({'path': row['path'], 'restored': True})
            except Exception as restore_error:
                rollback.append({'path': row['path'], 'restored': False, 'reason': str(restore_error)})
        write_new(WORK / 'failed-application.json', encoded({'failure': str(error), 'rollback': rollback}))
        raise
    print('Applied the three actual shadow-tested files and exact compiled scoped Program. Fresh 901-input validation is required.')


if __name__ == '__main__':
    main()
