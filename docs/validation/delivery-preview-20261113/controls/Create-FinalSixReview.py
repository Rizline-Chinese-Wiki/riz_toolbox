"""Review an actual final six-product raw-metadata graph; never builds or runs it.

Input must be a fresh metadata/graph/result.json from the captured-Csc pipeline.
The 881-source solution graph and the earlier 879-source regression are separate.
"""
import argparse
import datetime
import hashlib
import json
import os
from pathlib import Path

from ManagedMetadataReader import codeview, portable

ROOT = Path('C:/Users/71957/Desktop/riz_toolbox').resolve()
PRODUCTS = {f'RizToolbox.{name}' for name in ('Sdk', 'Core', 'Runtime', 'Adapter', 'Hosting', 'Loader')}
PDB_SHA256 = '8829d00f-11b8-4213-878b-770e8597ac16'


def require(value, reason):
    if not value:
        raise ValueError(reason)


def read(path):
    return json.loads(Path(path).read_text(encoding='utf-8-sig'))


def fact(path):
    path = Path(path).resolve()
    raw = path.read_bytes()
    return {'path': str(path), 'bytes': len(raw), 'sha256': hashlib.sha256(raw).hexdigest()}


def same(row):
    require(fact(row['path']) == {name: row[name] for name in ('path', 'bytes', 'sha256')},
            'File fact changed: ' + row['path'])


def key(path):
    return os.path.normcase(str(Path(path).resolve()))


def contained(path, parent):
    return Path(parent).resolve() in Path(path).resolve().parents


def document_path(value, source):
    normalized = value.replace('\\', '/')
    prefix = '/_/riz_toolbox/'
    return source / normalized[len(prefix):] if normalized.startswith(prefix) else Path(value)


def walk_facts(value, rows):
    if isinstance(value, dict):
        if {'path', 'bytes', 'sha256'} <= value.keys():
            row = {name: value[name] for name in ('path', 'bytes', 'sha256')}
            identity = key(row['path'])
            require(identity not in rows or rows[identity] == row, 'Conflicting file facts: ' + row['path'])
            rows[identity] = row
        for child in value.values():
            walk_facts(child, rows)
    elif isinstance(value, list):
        for child in value:
            walk_facts(child, rows)


def csc_arguments(project, source_rows):
    row = project['actualCscArguments']
    same(row['file'])
    lines = Path(row['file']['path']).read_text(encoding='utf-8-sig').splitlines()
    require(len(lines) == row['argumentCount'], 'Actual Csc argument count differs.')
    options = {'/reference:': 'references', '/analyzer:': 'analyzers',
               '/additionalfile:': 'additional', '/analyzerconfig:': 'editorconfigs'}
    observed = {name: [] for name in ('compile', *options.values())}
    out = []
    for raw in lines:
        arg = raw.strip().strip('"')
        require(not arg.startswith('@'), 'Unexpanded Csc response file.')
        if not arg.startswith('/') and arg.lower().endswith('.cs'):
            path = Path(arg) if Path(arg).is_absolute() else Path(project['project']['path']).parent / arg
            observed['compile'].append(key(path))
        for prefix, name in options.items():
            if arg.lower().startswith(prefix):
                observed[name].append(key(arg[len(prefix):].strip('"')))
        if arg.lower().startswith('/out:'):
            out.append(key(arg[5:].strip('"')))
    flags = {line.strip().strip('"').lower() for line in lines}
    for required in ('/debug:portable', '/checksumalgorithm:sha256', '/deterministic+', '/optimize+'):
        require(required in flags, 'Actual Csc flag absent: ' + required)
    require(len(out) == 1 and out[0] == key(project['objDll']['path'])
            and key(row['objDll']) == out[0], 'Actual Csc output differs from graph obj DLL.')
    require(len(observed['compile']) == len(set(observed['compile']))
            and set(observed['compile']) == set(source_rows), 'Actual Csc argv source set differs.')
    for name in options.values():
        expected = {key(item['path']) for item in project['dependencies'][name]}
        require(len(observed[name]) == len(set(observed[name]))
                and set(observed[name]) == expected
                and len(expected) == row['inputCounts'][name],
                'Actual Csc argv ' + name + ' differs from captured inputs.')
    require(row['inputCounts']['compile'] == len(source_rows), 'Captured compile count differs.')


def review_project(project, scope, graph):
    name = project['projectName']
    require(name in PRODUCTS and project['targetFramework'] == 'net6.0'
            and project['exactCompleteSourceSet'] is True, 'Product identity or source closure differs.')
    source_root = Path(graph['sourceRoot']).resolve()
    artifact_root = Path(graph['artifactRoot']).resolve()
    require(source_root == ROOT and artifact_root == scope / 'artifacts'
            and contained(project['project']['path'], source_root)
            and key(project['project']['path']) == key(source_root / 'src' / name / (name + '.csproj')),
            'Project is outside final source graph.')
    require((fact(ROOT / 'src' / name / (name + '.csproj'))['bytes'],
             fact(ROOT / 'src' / name / (name + '.csproj'))['sha256']) ==
            (project['project']['bytes'], project['project']['sha256']), 'Current project file differs.')
    for field in ('project', 'dll', 'objDll', 'pdb', 'objPdb'):
        same(project[field])
    require(contained(project['dll']['path'], artifact_root / 'bin')
            and contained(project['objDll']['path'], artifact_root / 'obj')
            and project['dll']['sha256'] == project['objDll']['sha256']
            and project['pdb']['sha256'] == project['objPdb']['sha256'], 'Fresh product obj/bin bytes differ.')
    sources = {}
    authored = set()
    generated = 0
    for item in project['sources']:
        same(item); same(item['frozen'])
        require((item['bytes'], item['sha256']) == (item['frozen']['bytes'], item['frozen']['sha256']),
                'Frozen compiler source differs.')
        identity = key(item['path'])
        require(identity not in sources, 'Duplicate compiler source.')
        sources[identity] = item
        if item['generated']:
            generated += 1
            require(contained(item['path'], artifact_root / 'obj'), 'Generated source escaped current graph.')
        else:
            path = Path(item['path']).resolve()
            require(contained(path, source_root), 'Authored compiler source escaped final snapshot.')
            relative = path.relative_to(source_root)
            current = ROOT / relative
            require(contained(current, ROOT / 'src' / name)
                    and (fact(current)['bytes'], fact(current)['sha256']) == (item['bytes'], item['sha256']),
                    'Current repository differs from final compiler source: ' + str(relative))
            authored.add(key(current))
    expected = {key(path) for path in (ROOT / 'src' / name).rglob('*.cs')
                if not {'bin', 'obj'}.intersection(path.relative_to(ROOT / 'src' / name).parts)}
    require(authored == expected and len(authored) == project['authoredSources'] and generated == 3
            and project['generatedSources'] == generated
            and len(sources) == project['compilerSourceCount'] == project['pdbDocumentCount'],
            'Current complete authored/generated source set differs: ' + name)
    csc_arguments(project, sources)
    pdb = portable(project['pdb']['path'])
    cv = codeview(project['dll']['path'])
    require(pdb['id'] == project['portablePdbIdHex']
            and cv == project['codeView']
            and cv['guidBytes'] == pdb['id'][:32]
            and cv['stamp'] == int.from_bytes(bytes.fromhex(pdb['id'][32:]), 'little')
            and cv['age'] == 1 and key(cv['path']) == key(project['objPdb']['path']),
            'Portable PDB and PE CodeView identity differ.')
    documents = {}
    for item in project['documents']:
        identity = key(item['actualCompilePath'])
        require(identity not in documents, 'Duplicate graph PDB document.')
        documents[identity] = item
    seen = set()
    for raw in pdb['documents']:
        path = document_path(raw['document'], source_root)
        identity = key(path)
        require(identity in sources and identity in documents and identity not in seen,
                'Raw portable PDB document does not match compiler inputs.')
        seen.add(identity)
        row = sources[identity]
        reported = documents[identity]
        require(raw['hashAlgorithm'] == PDB_SHA256 and raw['sha256'] == row['sha256']
                and {field: reported[field] for field in raw} == raw
                and reported['generated'] == row['generated']
                and reported['frozen'] == row['frozen'],
                'Raw PDB, Csc, frozen source or reported document hash differs.')
    require(seen == set(sources), 'PDB document set is incomplete.')
    for group, rows in project['dependencies'].items():
        for item in rows:
            same(item)
            if group in ('references', 'resolved-references') and Path(item['path']).stem in PRODUCTS:
                require(contained(item['path'], artifact_root), 'Historical product reference detected.')
    for item in project.get('captureFiles', []):
        same(item)
    return {field: project[field] for field in ('projectName', 'dll', 'pdb', 'authoredSources',
                                                'generatedSources', 'compilerSourceCount',
                                                'pdbDocumentCount', 'portablePdbIdHex')}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--graph', required=True, help='Actual final metadata/graph/result.json')
    parser.add_argument('--endpoint-receipt', required=True, help='Actual post-merge application-record.json')
    parser.add_argument('--focused-result', required=True, help='Actual 881-source focused result.json')
    parser.add_argument('--output', required=True, help='New one-shot review JSON path')
    args = parser.parse_args()
    graph_path = Path(args.graph).resolve()
    require(graph_path.as_posix().lower().endswith('/metadata/graph/result.json'), 'Canonical six graph path required.')
    scope = graph_path.parents[2]
    require(scope == Path('D:/riz-build-cache-next/line-ring-endpoint-integration-20261113/main-r1'),
            'Use a fresh D final graph scope.')
    graph_fact = fact(graph_path)
    graph = read(graph_path)
    totals = graph['totals']
    require(graph.get('kind') == 'delivery-final-six-raw-metadata-v1'
            and graph['sourceRoot'] == str(ROOT)
            and Path(graph['artifactRoot']).resolve() == scope / 'artifacts'
            and Path(graph['captureRoot']).resolve() == scope / 'csc-inputs'
            and graph['passed'] is True and graph['metadataOnly'] is True
            and graph['allCscFrozenPdbCompleteAndSha256Equal'] is True
            and graph['warnings'] == graph['errors'] == 0
            and graph['historicalProductReferences'] == 0
            and graph['casesRun'] == graph['nativeCalls'] == graph['nativeDllsLoaded'] == graph['assembliesExecuted'] == 0,
            'Final raw-metadata graph did not pass its own read-only gates.')
    require(totals['projects'] == 6 and 500 <= totals['authored'] <= 700
            and totals['generated'] == 18 and totals['pdbDocuments'] == totals['authored'] + 18
            and totals['freshProductReferenceEdges'] == 11 and totals['copyLocalPePdbFiles'] == 22,
            'Six-product counts are outside bounded current contract.')
    projects = graph['projects']
    require(len(projects) == 6 and {row['projectName'] for row in projects} == PRODUCTS,
            'Exactly the six runtime product projects required.')
    endpoint_fact, focused_fact = fact(args.endpoint_receipt), fact(args.focused_result)
    endpoint, focused = read(endpoint_fact['path']), read(focused_fact['path'])
    require(endpoint['appliedToMain'] is True and endpoint['newInputCount'] == 881
            and focused['passed'] is True and focused['compilerInputs'] == 881
            and focused['inputsUnchanged'] is True and focused['controlsUnchanged'] is True
            and focused['compiledBytesUnchanged'] is True
            and all(row['naturalExit'] and row['matchesExpected'] for row in focused['steps']),
            'Final endpoint application and 881-source focused result are required.')
    product_rows = [review_project(row, scope, graph) for row in projects]
    require(sum(row['authoredSources'] for row in product_rows) == totals['authored']
            and sum(row['generatedSources'] for row in product_rows) == totals['generated']
            and sum(row['pdbDocumentCount'] for row in product_rows) == totals['pdbDocuments'],
            'Per-product source and PDB totals differ.')
    require(len(graph['freshProductEdges']) == 11 and len(graph['copyLocalPePdbFiles']) == 22,
            'Fresh reference and copied PE/PDB closure differs.')
    for edge in graph['freshProductEdges']:
        require(edge['consumer'] in PRODUCTS and edge['producer'] in PRODUCTS, 'Unknown fresh product edge.')
        for field in ('reference', 'newProducer', 'refint'):
            same(edge[field])
            require(contained(edge[field]['path'], scope / 'artifacts'), 'Fresh product edge escaped graph.')
        require(edge['reference']['sha256'] == edge['refint']['sha256'], 'Producer reference/refint bytes differ.')
    for item in graph['copyLocalPePdbFiles']:
        require(item['consumer'] in PRODUCTS and item['producer'] in PRODUCTS, 'Unknown product copy.')
        same(item['copied']); same(item['newProducer'])
        require(item['copied']['sha256'] == item['newProducer']['sha256'], 'Copied PE/PDB differs.')
    require(graph['actualBuildProcess'] == focused_fact, 'Graph must bind final focused controller receipt.')
    steps = {row['name']: row for row in focused['steps']}
    require('six-release' in steps and steps['six-release']['naturalExit'] is True
            and steps['six-release']['exitCode'] == 0 and steps['six-release']['matchesExpected'] is True
            and focused['productsAfterSix'] == focused['productsBefore'] == focused['productsAfter'],
            'Captured six build was not the exact final product bytes.')
    facts = {}
    walk_facts(graph, facts)
    for row in facts.values():
        same(row)
    result = {'kind': 'delivery-final-six-review-v1', 'passed': True,
              'reviewedUtc': datetime.datetime.now(datetime.timezone.utc).isoformat(),
              'sixFreshComponentsAccepted': True, 'finalRepositorySourceClosure': True,
              'compilerArgumentsAndPdbVerified': True,
              'endpointReceipt': endpoint_fact, 'focusedResult': focused_fact,
              'graphMetadata': graph_fact, 'totals': totals, 'projects': product_rows,
              'fullLegacy300RunOnFinal881Bytes': False,
              'fileFactsRehashed': len(facts), 'runtimeGameVerified': False,
              'completeSpeedEnabled': False, 'completeSeekEnabled': False,
              'productionAdmission': False, 'reviewerScript': fact(__file__)}
    output = Path(args.output).resolve()
    require(not output.exists() and output.parent.is_dir(), 'Review output must be a new path in an existing scope.')
    with output.open('xb') as stream:
        stream.write((json.dumps(result, ensure_ascii=False, indent=2) + '\n').encode('utf-8'))
        stream.flush(); os.fsync(stream.fileno())
    print(json.dumps({'passed': True, 'review': fact(output), 'totals': totals}))


if __name__ == '__main__':
    main()
