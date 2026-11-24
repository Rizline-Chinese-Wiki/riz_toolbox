"""Archive the precise composite recovery, without accepting SDK/FullSeek.

No build/test/process/environment action is performed by this publisher.
"""
import argparse
import datetime as dt
import importlib.util
import json
from pathlib import Path
import re
import shutil
import uuid

ROOT = Path(__file__).resolve().parents[2]
WORK = Path(__file__).parent
RUN = Path('D:/riz-build-cache-next/line-ring-source-20261113/partitioned-r1')
SOURCE = RUN.parent / 'root-r3/source-r1'
FINAL = RUN.parent / 'root-r3/final-r1'
EVIDENCE = ROOT / 'docs/validation/line-ring-actual-source-20261113'
spec = importlib.util.spec_from_file_location('strict_source_publisher', WORK / 'publish_evidence.py')
old = importlib.util.module_from_spec(spec)
spec.loader.exec_module(old)
sha, read, require = old.sha, old.read, old.require


def check_archive(path, current=False):
    manifest = read(path / 'artifact-manifest.json')
    actual = {p.relative_to(path).as_posix() for p in path.rglob('*') if p.is_file()}
    require(actual == set(manifest) | {'artifact-manifest.json'}, 'Archive inventory differs.')
    for name, digest in manifest.items():
        old.relative_name(name)
        require(sha(path / name) == digest, 'Archived file changed: ' + name)
    summary = read(path / 'validation-summary.json')
    require(summary['acceptedThisBatch'] and not summary['sdkGoalComplete'] and not summary['completeSeekPermitted'], 'Scope flags differ.')
    if current:
        require(old.current_inputs() == read(path / 'inputs-after.json'), 'Main graph differs from archived recovery graph.')
    return summary


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--check-only', action='store_true')
    parser.add_argument('--check-current', action='store_true')
    opt = parser.parse_args()
    if opt.check_only:
        summary = check_archive(EVIDENCE, opt.check_current)
        print(json.dumps({'acceptedThisBatch': summary['acceptedThisBatch'], 'sdkGoalComplete': False, 'archiveVerified': True}))
        return
    require(not EVIDENCE.exists(), 'Accepted directory already exists; verify instead of overwriting.')
    result = read(RUN / 'result.json')
    require(result.get('passed') and result.get('finishedUtc') and not result.get('activeChild'), 'Recovery has no complete natural terminal.')
    require(result.get('inputsUnchanged') and result.get('legacy300UniqueNaturalPasses') and result.get('productionProductsByteIdentical'), 'Required recovery gates did not pass.')
    inputs = read(RUN / 'inputs-before.json')
    require(inputs == read(RUN / 'inputs-after.json') == old.current_inputs(), 'Current input graph is not exactly the completed graph.')
    old_inputs = read(SOURCE / 'inputs-before.json')
    require(old_inputs == read(SOURCE / 'inputs-after.json') == read(FINAL / 'inputs-before.json') == read(FINAL / 'inputs-after.json'),
            'The original source/final before/after manifests no longer describe one exact graph.')
    changed = [name for name in inputs if inputs[name] != old_inputs.get(name)]
    require(set(inputs) == set(old_inputs) and changed == ['tests/RizToolbox.Judgements.Tests/Program.cs'], 'Only reviewed runner delta is allowed.')
    review_path = ROOT / 'work/line-ring-source-review-20261113/independent-source-review.json'
    review = read(review_path)
    coverage = old.validate_review(review, old_inputs, check_current=False)
    partition_review_path = WORK / 'independent-partition-review.json'
    partition_review = read(partition_review_path)
    require(partition_review.get('scopeComplete') and partition_review.get('blockers') == [], 'Independent partition review is incomplete.')
    for row in partition_review['reviewedFiles']:
        require(sha(old.root_file(row['path'])) == row['sha256'], 'Partition reviewed file changed: ' + row['path'])
    require(partition_review['originalInputManifest']['sha256'] == sha(SOURCE / 'inputs-before.json'), 'Partition review graph differs.')
    for row in review['reviewedFiles']:
        if row['path'] != 'tests/RizToolbox.Judgements.Tests/Program.cs':
            require(sha(old.root_file(row['path'])) == row['sha256'], 'Source-reviewed file changed: ' + row['path'])
    source = read(SOURCE / 'result.json')
    final = read(FINAL / 'result.json')
    require(sha(SOURCE / 'result.json') == result['originalSourceResultSha256'] and sha(FINAL / 'result.json') == result['originalMatrixResultSha256'], 'Original terminal results changed.')
    require(source['passed'] and source['inputsUnchanged'] and not final['passed'] and final['inputsUnchanged'], 'Original result scopes differ.')
    stopped = final['steps'][-1]
    require(stopped == result['originalResourceStop'] and stopped['name'] == '7-judgements-without-basis'
            and stopped['exitCode'] != 0 and not stopped['timedOut'] and stopped['resourceStop']
            and stopped['resourceStop']['reason'] == 'Less than 512 MiB of system commit remains.'
            and not stopped['totals'] and not stopped['matchesExpected'], 'Original resource stop semantics differ.')
    stopped_log = Path(stopped['log']).read_text(encoding='utf-8-sig')
    stopped_passes = re.findall(r'^PASS (.+)$', stopped_log, re.MULTILINE)
    stopped_runs = re.findall(r'^RUN (.+)$', stopped_log, re.MULTILINE)
    require(len(stopped_passes) == 256 and len(stopped_runs) == 257 and not re.search(r'^FAIL ', stopped_log, re.MULTILINE),
            'Original failed 300 process does not have the recorded 256-pass resource boundary.')
    require(result['retainedSourceCases'] == source['steps'][1:], 'Retained source cases differ.')
    require(result['retainedBasisCases'] == [row for row in final['steps'] if row['name'].startswith('6-canvas-basis-case-')], 'Retained basis cases differ.')
    require(len(result['retainedSourceCases']) == len(result['retainedBasisCases']) == 16, 'Incomplete retained groups.')
    plan = old.Plan()
    def add(path, name, expected=None):
        if name not in plan.copies:
            plan.add(path, name, expected)
    for name, digest in inputs.items():
        plan.guard(old.root_file(name), digest)
    for label, run, report in (('original-source', SOURCE, source), ('original-final', FINAL, final), ('current', RUN, result)):
        for name in ('result.json', 'inputs-before.json', 'inputs-after.json'):
            add(run / name, label + '/' + name)
        for row in report['steps']:
            add(row['log'], label + '/logs/' + row['name'] + '.log', row['logSha256'])
    for name in ('inputs-before.json', 'inputs-after.json', 'legacy-case-list.json', 'original-compiled-manifest.json', 'recovery-control.py', 'runner-only-change.diff', 'runner-change-review.json'):
        add(RUN / name, name)
    for name in ('recover_partitioned.py', 'verify_runner_change.py', 'original-program-before-partition.cs.txt', 'independent-partition-review.json', 'README-partition-review.md', 'publish_partitioned.py'):
        add(WORK / name, 'controls/' + name)
    require(sha(RUN / 'recovery-control.py') == result['controlSha256'] == sha(WORK / 'recover_partitioned.py'), 'Executed recovery controller differs from reviewed controller.')
    add(review_path, 'reviews/independent-source-review.json')
    add(review_path.with_name('README.md'), 'reviews/source-review.md')
    # Current snapshots of every input and all static read dependencies make
    # this directory independent of temporary build caches for source review.
    for name, digest in inputs.items():
        add(old.root_file(name), 'source-snapshot/' + name, digest)
    for name, digest in coverage['listed'].items():
        if name != 'tests/RizToolbox.Judgements.Tests/Program.cs':
            add(old.root_file(name), 'source-snapshot/' + name, digest)
    static_evidence = read(old.root_file(old.CONTRACTS['line-ring-source-static-evidence.json']))
    static_verification = read(old.root_file(old.CONTRACTS['line-ring-source-profile-verification.json']))
    old.validate_static(static_evidence, static_verification, old_inputs)
    for name, digest in static_evidence['readSet'].items():
        add(old.root_file(name), 'source-snapshot/' + name, digest)
    old.validate_dependency_records(result)
    artifacts = Path(result['artifactsDirectory'])
    original_bin = Path(result['originalCompiledBackup'])
    require(result['products'] == result['originalProducts'], 'Six current production pairs differ from recovery-start fingerprint.')
    for component, values in result['products'].items():
        for ext, digest in values.items():
            product = artifacts / 'bin' / component / 'release' / (component + ext)
            require(sha(original_bin / component / 'release' / (component + ext)) == digest, 'Original product backup differs.')
            add(product, 'compiled-products/' + component + ext, digest)
    for row in result['dependencyCopies']:
        require(sha(artifacts / 'bin' / row['test'] / 'release' / (row['component'] + row['extension'])) == row['sha256'], 'Current test dependency changed.')
    for ext in ('.dll', '.pdb'):
        add(original_bin / 'RizToolbox.Judgements.Tests/release' / ('RizToolbox.Judgements.Tests' + ext),
            'original-test-assembly/RizToolbox.Judgements.Tests' + ext, result['originalTestAssembly'][ext])
    for test in old.TESTS:
        for ext in ('.dll', '.pdb'):
            add(artifacts / 'bin' / test / 'release' / (test + ext), 'current-test-assemblies/' + test + ext)
    listing = read(RUN / 'legacy-case-list.json')
    require([row['index'] for row in listing] == list(range(1, 301)) and len({row['name'] for row in listing}) == 300, 'Invalid original legacy listing.')
    require([row['name'] for row in listing[:256]] == stopped_passes and listing[256]['name'] == stopped_runs[-1],
            'Current legacy listing no longer binds the actual original interrupted order.')
    steps = result['steps']
    expected_names = ['build-solution', 'list-legacy-cases', 'invalid-legacy-0', 'invalid-legacy-301', 'invalid-legacy-not-an-int']
    expected_names += ['legacy-case-' + str(i).zfill(3) for i in list(range(257, 301)) + list(range(1, 257))]
    expected_names += ['renderer-abi', 'audio-content', 'exact-static-read']
    require([row['name'] for row in steps] == expected_names, 'Current step identities/order differ or are incomplete.')
    for row in steps:
        content = Path(row['log']).read_text(encoding='utf-8-sig')
        invalid = row['name'].startswith('invalid-legacy-')
        require(row['exitCode'] == (2 if invalid else 0) and not row['timedOut'] and not row['resourceStop'] and row['matchesExpected'], 'Current child did not naturally meet its gate: ' + row['name'])
        if row['name'].startswith('legacy-case-'):
            index = int(row['name'].rsplit('-', 1)[1])
            name = listing[index - 1]['name']
            require(row['arguments'][-2:] == ['--legacy-case', str(index)] and row['totals'] == [['1', '1']], 'Case selection/total differs.')
            require(re.findall(r'^RUN (.+)$', content, re.MULTILINE) == [name] and re.findall(r'^PASS (.+)$', content, re.MULTILINE) == [name], 'Case exact RUN/PASS differs.')
        elif invalid:
            require(not re.search(r'^(RUN|PASS|FAIL) ', content, re.MULTILINE) and not row['totals'], 'Invalid selection ran a case.')
    for row in result['retainedSourceCases'] + result['retainedBasisCases']:
        require(row['exitCode'] == 0 and not row['timedOut'] and not row['resourceStop'] and row['totals'] == [['1', '1']] and row['matchesExpected'], 'Retained case is not a natural 1/1 pass.')
    require([row['totals'] for row in steps[-3:]] == [[['51', '51']], [['1001', '1001']], [['6', '6']]], 'Current tail totals differ.')
    require(old.ZIP.stat().st_size == old.ZIP_BYTES and sha(old.ZIP) == old.ZIP_SHA, 'Historical ZIP changed.')
    stamp = dt.datetime.now(dt.timezone.utc).isoformat()
    summary = {'scope': 'Accepted offline source-capture batch with explicit original/current test-runner composite',
        'acceptedAtUtc': stamp, 'acceptedThisBatch': True, 'sdkGoalComplete': False, 'completeSeekPermitted': False,
        'gameStarted': False, 'installed': False, 'originalSourceGroups': 16, 'originalBasisGroups': 16,
        'currentIsolatedLegacyGroups': 300, 'rendererAbiChecks': 51, 'audioContentChecks': 1001, 'exactStaticReadChecks': 6,
        'oldInputCount': len(old_inputs), 'currentInputCount': len(inputs), 'onlyInputChange': changed,
        'oldGroupsExecutedByCurrentTestAssembly': False, 'default332SingleProcessExecuted': False,
        'productionPairsByteIdenticalAtRecovery': 12,
        'historicalPreRecordedProductionComponents': list(old.SIX[:4]),
        'recoveryStartOnlyProductionFingerprintComponents': list(old.SIX[4:]),
        'original300ResourceStopPreserved': True, 'sourceReviewCompleteForScope': True, 'partitionReviewCompleteForScope': True,
        'dependenciesMatch': True, 'dependencyCopies': 24, 'oldZipSha256': old.ZIP_SHA,
        'originalSourceResultSha256': sha(SOURCE / 'result.json'), 'originalFinalResultSha256': sha(FINAL / 'result.json'),
        'recoveryResultSha256': sha(RUN / 'result.json'), 'reviewedCompiledInputs': coverage['reviewedCompiledInputs'],
        'hashOnlyDependencyInputs': coverage['hashOnlyDependencyInputs'],
        'numericIntegrated': False, 'targetProjectionComplete': False, 'writerComplete': False}
    plan.json('validation-summary.json', summary)
    plan.text('README.md', f'''# Line/ring actual source offline acceptance

Accepted at {stamp}. This batch captures actual line/ring source on the original Canvas capture/Plan/NoteView. Full SDK, full target reconstruction, native writer and complete Seek remain unfinished.

| Evidence | Finding | Use |
|---|---|---|
| Original source 16 and basis 16 natural 1/1 logs, original test DLL/PDB and graph | Source/baseline, lifetime, cancellation and ownership checks passed | Original executing test assembly only |
| Current 300 isolated natural 1/1 logs with exact ordinal/name listing | All original legacy groups passed after separating process lifetimes | Current runner; unchanged test bodies and native fixtures |
| Current ABI51, Audio1001, static6, 24 dependency hashes | Required tail and dependency closure passed | Current production |
| Six product DLL/PDB pairs identical before/after runner rebuild | Test-runner change did not change production bytes | Four historical fingerprints plus two recovery-start fingerprints |

The original monolithic legacy process stopped after 256 PASS lines under the 512 MiB system-commit guard. That failed report/log is preserved in original-final and is not accepted as 300/300. Process isolation preserves each test's unknown-owner and native heap semantics; it changes cross-case process lifetime. No single-process default332 success is claimed.

Source review covers 28 compiled inputs and 4 stated evidence files; 843 dependencies are hash-only. The independent partition review covers the declared runner/recovery files and explicitly performs no execution. The only current input change is Program.cs, whose exact old bytes are reconstructed in controls and checked against the old frozen manifest.

Use validation-summary.json for scope, original-source/original-final/current for exact results and logs, source-snapshot for frozen inputs, compiled-products for production, and original-test-assembly/current-test-assemblies for explicit test identities. artifact-manifest.json hashes the archived inventory.

The numerical kernel remains an independently validated isolated SDK candidate. It is not part of this accepted source graph. Real Speed/Seek stay disabled; no game was installed or started by this recovery.
''')
    plan.verify(inputs)
    parent = EVIDENCE.parent.resolve()
    require(parent.is_relative_to(ROOT.resolve()), 'Archive parent escaped workspace.')
    temporary = parent / (EVIDENCE.name + '.preparing-' + uuid.uuid4().hex)
    require(temporary.resolve().parent == parent and EVIDENCE.resolve().parent == parent, 'Atomic move targets escaped archive parent.')
    temporary.mkdir()
    for name, (source_path, digest) in plan.copies.items():
        target = temporary / name
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(source_path, target)
        require(sha(target) == digest, 'Archive copy changed: ' + name)
    for name, content in plan.generated.items():
        target = temporary / name
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(content, encoding='utf-8')
    manifest = {path.relative_to(temporary).as_posix(): sha(path) for path in sorted(temporary.rglob('*')) if path.is_file()}
    (temporary / 'artifact-manifest.json').write_text(old.json_text(manifest), encoding='utf-8')
    check_archive(temporary, current=True)
    plan.verify(inputs)
    temporary.rename(EVIDENCE)
    check_archive(EVIDENCE, current=True)
    print(json.dumps({'acceptedThisBatch': True, 'sdkGoalComplete': False, 'archiveFileCount': len(manifest) + 1}))


if __name__ == '__main__':
    main()
