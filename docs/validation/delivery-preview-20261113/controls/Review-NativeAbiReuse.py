"""Prove exact reuse of the 2026-11-07 five native payload bytes for final managed sources.

This is a static byte-identity ABI gate. It never loads a DLL, builds, installs, or
touches the game. Changed helper declarations or interop layout require a new
native stage and a separate review; this script intentionally cannot bless it.
"""
import argparse
import datetime
import hashlib
import json
import os
import re
from pathlib import Path

ROOT = Path('C:/Users/71957/Desktop/riz_toolbox').resolve()
OLD = Path('D:/riz-build-cache-next/playback-lower-time-r1-20261104/seek-judge-state-view-six-component-build-r1')
OLD_GRAPH = OLD / 'metadata/graph/result.json'
OLD_GRAPH_SHA = 'c7407da86c1e36b6c4e31b47b2c67dae7a4b7cb60ef6074a67e5a6667206d30b'
NATIVE = Path('D:/riz-build-cache-next/delivery-native-20261107-r1')
NATIVE_MANIFEST_SHA = '98f5decb9984a74c4c3fd1656d70d941d4f2fa5a30466e5abfae7ec8f8726d13'
SOURCE_INPUTS_SHA = '34097f6ae3adb368e23a0e2cd77c2d9a130e2a4d657555edfe10c8d4a84cc5a8'
PRODUCTS = {f'RizToolbox.{name}' for name in ('Sdk', 'Core', 'Runtime', 'Adapter', 'Hosting', 'Loader')}
DIRECT = re.compile(r'RizToolbox\.(?:JudgeContext|EngineNodeKernel)\.dll|'
                    r'Riz(?:Judge|EngineNode|EngineScheduler|EngineWait|Coroutine|EventMid|FirstInstall|'
                    r'Initialization|Listener|Operation|RuntimeInvoke|Tween|Void)[A-Za-z0-9]*_')
LAYOUT = re.compile(r'\[StructLayout\(|Marshal\.SizeOf|Size\s*=\s*(?:0x[0-9a-fA-F]+|\d+)')
SYMBOL = re.compile(r'Riz[A-Za-z0-9]+_[A-Za-z0-9_]+')


def require(value, reason):
    if not value:
        raise ValueError(reason)


def fact(path):
    path = Path(path).resolve()
    raw = path.read_bytes()
    return {'path': str(path), 'bytes': len(raw), 'sha256': hashlib.sha256(raw).hexdigest()}


def read(path):
    return json.loads(Path(path).read_text(encoding='utf-8-sig'))


def same(row):
    actual = fact(row['path'])
    require(actual == {name: row[name] for name in ('path', 'bytes', 'sha256')},
            'Native reuse file changed: ' + row['path'])


def key(path):
    return os.path.normcase(str(Path(path).resolve()))


def managed_index(graph, scope):
    rows = {}
    require(graph['passed'] is True and len(graph['projects']) == 6
            and {p['projectName'] for p in graph['projects']} == PRODUCTS,
            'Six-component graph required for ABI comparison.')
    for project in graph['projects']:
        for source in project['sources']:
            if not source['generated']:
                path = Path(source['path']).resolve()
                relative = path.relative_to(Path(graph.get('sourceRoot', str(scope / 'source'))))
                identity = key(ROOT / relative)
                require(identity not in rows, 'Duplicate managed compiler source.')
                rows[identity] = source
    return rows


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--final-graph', required=True, help='Fresh six-product metadata/graph/result.json')
    parser.add_argument('--native-manifest', default=str(NATIVE / 'native-delivery-files.json'))
    parser.add_argument('--output', required=True, help='New one-shot ABI proof JSON path')
    args = parser.parse_args()
    native_fact = fact(args.native_manifest)
    require(native_fact['sha256'] == NATIVE_MANIFEST_SHA, 'This reuse proof only applies to the exact old five native bytes.')
    native = read(native_fact['path'])
    require(native['passed'] is True and len(native['files']) == 5
            and native['doorstopVersion'] == '4.5.0-riz-preinit.2', 'Native payload manifest differs.')
    for row in native['files']:
        same(row['source'])
    source_fact = fact(NATIVE / 'source-inputs.json')
    require(source_fact['sha256'] == SOURCE_INPUTS_SHA, 'Old native source inventory changed.')
    source_inputs = read(source_fact['path'])
    require(len(source_inputs['sourceFiles']) == 126, 'Native source input inventory differs.')
    for row in source_inputs['sourceFiles']:
        same(row)
        require(key(row['path']) == key(ROOT / row['relativePath']), 'Native source path mapping differs.')
    old_fact = fact(OLD_GRAPH)
    require(old_fact['sha256'] == OLD_GRAPH_SHA, 'Accepted baseline managed graph changed.')
    old = read(OLD_GRAPH)
    require(old['passed'] is True and old['totals']['authored'] == 517
            and old['allCscFrozenPdbCompleteAndSha256Equal'] is True,
            'Accepted baseline managed graph lost its raw PDB gate.')
    final_path = Path(args.final_graph).resolve()
    require(final_path.as_posix().lower().endswith('/metadata/graph/result.json')
            and final_path != OLD_GRAPH.resolve(), 'An actual new final six graph is required.')
    final_fact = fact(final_path)
    final = read(final_path)
    require(final['passed'] is True and final['allCscFrozenPdbCompleteAndSha256Equal'] is True,
            'Final managed graph has no accepted raw PDB/source closure.')
    old_sources = managed_index(old, OLD)
    final_sources = managed_index(final, final_path.parents[2])
    selected = []
    symbols = set()
    for name in sorted(PRODUCTS):
        for path in (ROOT / 'src' / name).rglob('*.cs'):
            if {'bin', 'obj'}.intersection(path.relative_to(ROOT / 'src' / name).parts):
                continue
            data = path.read_text(encoding='utf-8-sig')
            direct = bool(DIRECT.search(data))
            layout = (name == 'RizToolbox.Runtime' and 'Hooks' in path.parts
                      and path.name.startswith('Native') and bool(LAYOUT.search(data)))
            if not (direct or layout):
                continue
            identity = key(path)
            require(identity in old_sources and identity in final_sources,
                    'New helper ABI touchpoint needs a native rebuild: ' + str(path))
            current = fact(path)
            baseline = old_sources[identity]
            captured = final_sources[identity]
            require((current['bytes'], current['sha256']) ==
                    (baseline['bytes'], baseline['sha256']) ==
                    (captured['bytes'], captured['sha256']),
                    'Managed helper ABI touchpoint changed: ' + str(path))
            symbols.update(SYMBOL.findall(data))
            selected.append({'current': current, 'baseline': baseline['sha256'],
                             'finalCompilerSource': {field: captured[field] for field in ('path', 'bytes', 'sha256')},
                             'directExportOrHelperReference': direct, 'interopLayoutOrSize': layout})
    require(len(selected) >= 30 and any(row['interopLayoutOrSize'] for row in selected),
            'Managed helper ABI touchpoint inventory unexpectedly shrank.')
    profile = fact(ROOT / 'src/RizToolbox.Adapter/profiles/steam-25977441.json')
    require(profile['sha256'] == '17fdb95efeac14e67c2a870a270e713c5667e37fa8cb78f493f848d7aab86a51',
            'Target GameAssembly profile changed.')
    exports = fact(NATIVE / 'judge-context-sdk/actual-export-validation.json')
    export_review = read(exports['path'])
    require(export_review['passed'] is True and export_review['helperExportCount'] == 85
            and export_review['publicationVersion'] == 7
            and export_review['playbackAttestationVersion'] == 1
            and export_review['playbackAttestationHeaderBytes'] == 120
            and export_review['playbackAttestationRangeBytes'] == 184,
            'Accepted native Judge exports, versions or sizes differ.')
    kernel_fact = fact(NATIVE / 'engine-node-kernel/kernel-manifest.json')
    kernel = read(kernel_fact['path'])
    require(kernel['inlineSideAbi'] == 1 and kernel['legacyNodeTransportAbi'] == 2
            and kernel['schedulerObservationAbi'] == 1 and kernel['schedulerFrameSize'] == 256
            and kernel['schedulerRowFrameSize'] == 224 and kernel['waitObservationAbi'] == 1
            and kernel['waitFrameSize'] == 240 and kernel['waitPhaseFrameSize'] == 288,
            'Accepted native Engine ABI versions or frame sizes differ.')
    result = {'kind': 'delivery-native-abi-reuse-v1', 'passed': True, 'reuseAccepted': True,
              'reviewedUtc': datetime.datetime.now(datetime.timezone.utc).isoformat(),
              'method': 'All selected managed helper export/delegate/layout/size source bytes equal accepted baseline and final Csc source; native inputs and five payload bytes equal accepted manifest.',
              'oldManagedGraph': old_fact, 'finalManagedGraph': final_fact,
              'nativeManifest': native_fact, 'nativeSourceInputs': source_fact,
              'nativeInputCount': 126, 'nativePayloadCount': 5,
              'managedTouchpointCount': len(selected), 'managedTouchpoints': selected,
              'referencedHelperSymbols': sorted(symbols),
              'judgeExportValidation': exports, 'engineKernelManifest': kernel_fact,
              'targetProfile': profile, 'nativeRecompiled': False,
              'gameExecuted': False, 'installed': False, 'reviewerScript': fact(__file__)}
    output = Path(args.output).resolve()
    require(not output.exists() and output.parent.is_dir(), 'ABI output must be a new path in an existing scope.')
    with output.open('xb') as stream:
        stream.write((json.dumps(result, ensure_ascii=False, indent=2) + '\n').encode('utf-8'))
        stream.flush(); os.fsync(stream.fileno())
    print(json.dumps({'passed': True, 'abiReview': fact(output),
                      'managedTouchpoints': len(selected), 'nativeInputs': 126, 'nativePayloads': 5}))


if __name__ == '__main__':
    main()
