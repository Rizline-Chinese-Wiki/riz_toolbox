"""Record the observed offline preview delivery and preserve its evidence.

No build, native call, game access, installation, environment update or Git
operation is performed. Existing validation archives and ZIPs are immutable.
"""
import datetime as dt
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import shutil
import tempfile
import uuid
import zipfile

ROOT = Path('C:/Users/71957/Desktop/riz_toolbox').resolve()
HERE = ROOT / 'work/delivery-20261113-preparation'
RUN = Path('D:/riz-build-cache-next/line-ring-endpoint-integration-20261113/main-r1')
DELIVERY = Path('D:/riz-build-cache-next/delivery-20261113-r1')
PUBLIC = ROOT / 'artifacts/delivery-20261113'
ENDPOINT = ROOT / 'docs/validation/line-ring-endpoint-local-20261113'
ARCHIVE = ROOT / 'docs/validation/delivery-preview-20261113'
ZIP_NAME = 'RizToolbox-0.3.0-sdk-runtime-20261113.zip'
ZIP_SHA = 'ffe59188b7fcd28df5b0501b15172f52871d0bdceccb9525909d8a2ec53f350a'
SCRIPTS = {
    HERE / 'Create-FinalSixMetadata.py': 'f7e63f0721f40b89e4dcc1be878f62aabfe5177c908dafe990956a600d90c501',
    HERE / 'Create-FinalSixReview.py': 'a7bbd09e0801998c5840002d78fe9978a5b5555532281e515237a92067d73991',
    HERE / 'Review-NativeAbiReuse.py': 'c39dfeef2face54a9d82857adee78e3782155b6eb8828ab37fb9378e5bd27e4e',
    DELIVERY / 'Capture-FinalSixInputs.targets': '9e18340a8e12a7aa9e991459b9e5175dfbb40b4e2966af6f2bf284ed19f3b7b4',
    DELIVERY / 'Package-Delivery.py': '94242f6ab8f1494932e094df5f9bc6c9dc595a40a1d5b50be2870406fb4b3d7e',
    DELIVERY / 'Verify-Delivery.py': '097a8dcc0bdb780d4a006f27926c2cadb110c19ede947f66b2427bb254d24563',
}


def require(ok, reason):
    if not ok:
        raise RuntimeError(reason)


def sha(path):
    path = Path(path)
    require(path.is_file() and not path.is_symlink(), 'Missing or linked file: ' + str(path))
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def read(path):
    return json.loads(Path(path).read_text(encoding='utf-8-sig'))


def encoded(value):
    return (json.dumps(value, ensure_ascii=False, indent=2) + '\n').encode('utf-8')


def fact(path):
    path = Path(path).resolve()
    return {'path': str(path), 'bytes': path.stat().st_size, 'sha256': sha(path)}


def exact(row):
    require(fact(row['path']) == {key: row[key] for key in ('path', 'bytes', 'sha256')}, 'Recorded bytes changed')


def audit():
    for path, digest in SCRIPTS.items():
        require(sha(path) == digest, 'Reviewed script changed: ' + path.name)
    require(sha(PUBLIC / ZIP_NAME) == ZIP_SHA, 'New ZIP changed')
    old = ROOT / 'artifacts/delivery-20261107/RizToolbox-0.3.0-sdk-runtime-20261107.zip'
    require(sha(old) == '24c5ab5008027fc574992b1688ba399fc61b930925c213f001256a206b23ac0e', 'Old ZIP changed')
    result = read(RUN / 'result.json')
    require(sha(RUN / 'result.json') == '3a45cd9bded930640375cc3a756d2c634c2c240962b60a0fa734940e87fbec02'
            and result['passed'] is True and result['compilerInputs'] == 881
            and result['old300ExecutedThisBatch'] is False and len(result['steps']) == 11,
            'Final focused result changed or overclaims scope')
    helper = ROOT / 'work/line-ring-source-validation-20261113/run_validation.py'
    require(sha(helper) == '61c2983ae117322b25ff96ad75b659b870fdadf9aada9274727248cd5ca98fd4', 'Input helper changed')
    spec = importlib.util.spec_from_file_location('delivery_input_audit', helper)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    require(module.inputs() == read(RUN / 'inputs-after.json'), 'Compiler inputs changed after validation')
    accepted = read(ENDPOINT / 'validation-summary.json')
    require(sha(ENDPOINT / 'validation-summary.json') == '004d421af4985d78ef9c13aa709b7d7470ced8ed27e902826969c786a0166957'
            and accepted['acceptedThisBatch'] is True and accepted['focusedPassedCases'] == 29
            and accepted['sdkGoalComplete'] is False, 'Endpoint archive is not accepted')
    graph = read(RUN / 'metadata/graph/result.json')
    require(sha(RUN / 'metadata/graph/result.json') == '91242f8b8172efa16ece71a0cd98f0ffa60ec3bc7bbd8ab61e27fc8d0c218c23'
            and graph['totals'] == {'projects': 6, 'authored': 619, 'generated': 18, 'pdbDocuments': 637,
                                    'freshProductReferenceEdges': 11, 'copyLocalPePdbFiles': 22}, 'Final graph changed')
    require(result['productsAfterSix'] == result['productsBefore'] == result['productsAfter'], 'Product byte closure lost')
    manifest = read(PUBLIC / 'delivery-manifest.json')
    verified = read(PUBLIC / 'delivery-verification.json')
    require(sha(PUBLIC / 'delivery-manifest.json') == '6c24137bc20b9fbf9e2f354f781cdef483b00690d618219213567d7d84042197'
            and sha(PUBLIC / 'delivery-verification.json') == '833cc0ed0a5aa231fbd54a65e7ce74b742f4b400ca237e1b4800b93e63118bab', 'Delivery receipts changed')
    require(verified['passed'] is True and verified['archiveFileCount'] == 64
            and verified['runtimeManifestFilesVerified'] == 23 and verified['sdkReferenceOnly'] is True
            and verified['completeSpeedEnabled'] is False and verified['completeSeekEnabled'] is False
            and verified['gameExecuted'] is False and verified['productionAdmission'] is False,
            'Preview verification scope differs')
    require(manifest['fullLegacy300RunOnFinal881Bytes'] is False, 'Old matrix applied to new DLLs')
    for row in verified['unpackedQuickstartBuilds']:
        require(row['exitCode'] == 0, 'Independent QUICKSTART exit differs')
        exact(row['stdout']); exact(row['stderr'])
        require(Path(row['stderr']['path']).stat().st_size == 0, 'Independent QUICKSTART stderr is nonempty')
        if row['name'].endswith('-build'):
            body = Path(row['stdout']['path']).read_text(encoding='utf-8-sig')
            require(body.count('已成功生成。') == 1
                    and re.findall(r'^\s*(\d+) 个警告\s*$', body, re.M) == ['0']
                    and re.findall(r'^\s*(\d+) 个错误\s*$', body, re.M) == ['0'], 'Independent example did not build W0/E0')
    for project in graph['projects']:
        exact(project['dll']); exact(project['pdb'])
        for source in project['sources']:
            exact(source['frozen'])
    with zipfile.ZipFile(PUBLIC / ZIP_NAME) as archive:
        require(archive.testzip() is None and len(archive.namelist()) == 64, 'ZIP integrity changed')
        for row in manifest['files']:
            raw = archive.read(row['path'])
            require(len(raw) == row['bytes'] and hashlib.sha256(raw).hexdigest() == row['sha256'], 'ZIP member changed')
    return result, graph, manifest, verified


def main():
    require(not ARCHIVE.exists(), 'Preserve existing delivery evidence')
    result, graph, manifest, verified = audit()
    now = dt.datetime.now(dt.timezone.utc).isoformat()
    receipt = {
        'schema': 'root-observed-offline-delivery-exits/v1', 'observedUtc': now,
        'observations': [
            {'step': 'metadata', 'sessionId': 63150, 'chunkId': 'f48945', 'actualExitCode': 0},
            {'step': 'root-six-review', 'sessionId': 19687, 'chunkId': '44e25a', 'actualExitCode': 0},
            {'step': 'native-abi-reuse', 'chunkId': '85f8c2', 'actualExitCode': 0},
            {'step': 'package', 'sessionId': 12056, 'chunkId': 'd5407d', 'actualExitCode': 0},
            {'step': 'independent-unpacked-verification', 'sessionId': 7199, 'chunkId': 'd33ddb', 'actualExitCode': 0},
            {'step': 'endpoint-evidence-publication', 'sessionId': 27033, 'chunkId': 'bc9dc6', 'actualExitCode': 0},
        ],
        'observationKind': 'Root tool completions; no kernel handle witness claimed',
        'firstMetadataAttempt': {'exitCode': 1, 'reason': 'English-only summary parser rejected real Chinese W0/E0 log before any metadata output directory was created', 'rootToolChunk': '3934e4'},
        'firstSixReview': {'outputPreserved': 'final-six-review.json', 'exitNotObservedAfterSessionReset': True,
                          'repeatedReadOnlyToNewOutput': 'final-six-review-root-r1.json'},
        'archive': fact(PUBLIC / ZIP_NAME), 'sdkGoalComplete': False, 'completeSeekPermitted': False,
        'installed': False, 'gameStarted': False,
    }
    receipt_path = HERE / 'execution-receipt.json'
    require(not receipt_path.exists(), 'Preserve original execution receipt')
    receipt_path.write_bytes(encoded(receipt))
    staging = ARCHIVE.parent / ('.delivery-preview-staging-' + uuid.uuid4().hex)
    staging.mkdir()
    inventory = {}
    def add(path, name):
        require(name not in inventory and ':' not in name and '..' not in Path(name).parts, 'Archive path collision')
        before = fact(path)
        require(before['bytes'] < 64 * 1024 * 1024, 'Oversized evidence member')
        target = staging / name
        target.parent.mkdir(parents=True, exist_ok=True)
        require(not target.exists(), 'Archive target already exists')
        shutil.copyfile(path, target)
        require(sha(target) == before['sha256'] and fact(path) == before, 'Evidence changed during copy')
        inventory[name] = before['sha256']
    for path in SCRIPTS:
        add(path, 'controls/' + path.name)
    for name in ('ManagedMetadataReader.py', 'HANDOFF.md', 'execution-receipt.json', 'record_delivery.py'):
        add(HERE / name, 'controls/' + name)
    for name in ('delivery-manifest.json', 'delivery-verification.json', 'SHA256SUMS.txt'):
        add(PUBLIC / name, 'delivery/' + name)
    for name in ('result.json', 'inputs-before.json', 'inputs-after.json'):
        add(RUN / name, 'focused/' + name)
    add(RUN / 'metadata/graph/result.json', 'graph/result.json')
    for project in graph['projects']:
        for ordinal, source in enumerate(project['sources']):
            add(source['frozen']['path'], 'graph/frozen/' + project['projectName'] + f'/{ordinal:04}.cs')
    for name in ('final-six-review.json', 'final-six-review-root-r1.json', 'native-abi-review.json'):
        add(DELIVERY / name, 'reviews/' + name)
    for row in manifest['commands']:
        for field in ('stdout', 'stderr'):
            add(row[field]['path'], 'package-logs/' + Path(row[field]['path']).name)
    for row in verified['unpackedQuickstartBuilds']:
        for field in ('stdout', 'stderr'):
            add(row[field]['path'], 'verify-logs/' + Path(row[field]['path']).name)
    add(ENDPOINT / 'validation-summary.json', 'endpoint/validation-summary.json')
    add(ENDPOINT / 'artifact-manifest.json', 'endpoint/artifact-manifest.json')
    summary = {
        'schema': 'offline-preview-delivery-20261113/v1', 'acceptedAtUtc': now,
        'previewPackaged': True, 'independentUnpackedVerificationPassed': True,
        'zip': fact(PUBLIC / ZIP_NAME), 'zipMembers': 64, 'runtimeInstallPaths': 23,
        'sixReleaseComponents': 6, 'sixReleaseWarnings': 0, 'sixReleaseErrors': 0,
        'solutionWarnings': 56, 'solutionErrors': 0,
        'authoredCompilerSources': 619, 'generatedCompilerSources': 18, 'pdbDocuments': 637,
        'solutionCompilerInputs': 881, 'focusedCases': 29, 'invalidSelectors': 2,
        'fullLegacy300RunOnFinal881Bytes': False, 'prior879FullMatrixSeparate': True,
        'sdkContractVersion': '1.2.0', 'runtimeVersion': '0.3.0',
        'sdkReferenceOnly': True, 'nativeManagedTouchpointsReviewed': 65,
        'nativeSourceInputsUnchanged': 126, 'nativePayloadsReused': 5,
        'sdkGoalComplete': False, 'completeSpeedEnabled': False, 'completeSeekEnabled': False,
        'productionAdmission': False, 'runtimeGameVerified': False, 'installed': False, 'gameStarted': False,
        'oldZipUnchanged': True,
    }
    (staging / 'validation-summary.json').write_bytes(encoded(summary))
    inventory['validation-summary.json'] = sha(staging / 'validation-summary.json')
    require(len(inventory) < 1000 and sum(p.stat().st_size for p in staging.rglob('*') if p.is_file()) < 128 * 1024 * 1024,
            'Delivery evidence bound exceeded')
    (staging / 'artifact-manifest.json').write_bytes(encoded(inventory))
    require(all(sha(staging / name) == digest for name, digest in inventory.items()), 'Staged evidence byte differs')
    require(not ARCHIVE.exists(), 'Concurrent delivery evidence appeared')
    staging.rename(ARCHIVE)
    print(json.dumps({'previewAccepted': True, 'evidenceFiles': len(inventory) + 1,
                      'summary': fact(ARCHIVE / 'validation-summary.json'), 'zip': fact(PUBLIC / ZIP_NAME),
                      'sdkGoalComplete': False}), flush=True)


if __name__ == '__main__':
    main()
