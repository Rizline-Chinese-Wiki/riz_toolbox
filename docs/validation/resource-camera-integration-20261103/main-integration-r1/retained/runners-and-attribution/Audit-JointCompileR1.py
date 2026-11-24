"""Read-only attribution of the staged compile; it grants no gameplay/merge admission."""
from pathlib import Path
import datetime
import hashlib
import json

P = Path(__file__).resolve().parent
V = Path('D:/riz-build-cache-next/game-camera-scale-x-r1-20261103')
LIFECYCLE = 'tests/RizToolbox.PlaybackStartInitialization.Tests/ProgressClipResourceLifecycleChecks.cs'
read = lambda p: json.loads(p.read_text(encoding='utf-8-sig'))
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
fact = lambda p: dict(path=str(p), sha256=sha(p), bytes=p.stat().st_size)


def main():
    output = P / 'joint-compile-r1/source-and-product-attribution.json'
    assert not output.exists(), 'Preserve the original attribution.'
    compiled = read(P / 'joint-compile-r1/test-result.json')
    assert compiled['passed'] is True and compiled['actualBuildExitCode'] == 0 and compiled['errors'] == 0
    joint = {r['path']: r for r in read(P / 'joint-candidate-manifest.json')['files']}
    prior = {r['path']: r for r in read(V / 'functional-r1/source-manifest.json')['files']}
    assert set(joint) == set(prior) and len(joint) == 915
    difference = [p for p in joint if joint[p] != prior[p]]
    assert difference == [LIFECYCLE]
    products = [p for p in joint if p.startswith('src/')]
    native = [p for p in joint if p.startswith('native/')]
    assert len(products) == 485 and len(native) == 48
    assert all(joint[p] == prior[p] for p in products + native)
    for rel, row in joint.items():
        assert sha(P / 'candidate-915' / rel) == row['sha256']
    build_products = read(P / 'joint-compile-r1/products.json')['files']
    for row in build_products:
        assert sha(Path(row['path'])) == row['sha256']
    old_products = {str(Path(row['path']).relative_to(V / 'foundation-r1/source')).replace('\\','/'): row
                    for row in read(V / 'foundation-r1/solution-products.json')}
    compared = []
    for name in ['Sdk','Core','Runtime','Adapter','Hosting','Loader']:
        for suffix in ['.dll','.pdb']:
            rel = f'src/RizToolbox.{name}/bin/Release/net6.0/RizToolbox.{name}{suffix}'
            old = old_products[rel]
            current = fact(P / 'candidate-915' / rel)
            assert sha(Path(old['path'])) == old['sha256']
            assert current['sha256'] == old['sha256'] and current['bytes'] == old['bytes'], rel
            compared.append(dict(relative=rel, earlierValidatedV=old, freshJoint=current, byteIdentical=True))
    result = dict(kind='fresh-joint-compile-source-and-twelve-product-attribution', passed=True,
                  observedAtUtc=datetime.datetime.now(datetime.timezone.utc).isoformat(),
                  sourceInputs=915, sourceDifferenceFromV=[LIFECYCLE],
                  managedProductInputsUnchanged=485, nativeInputsUnchanged=48,
                  lifecycleDisposePreserved=True, productPePdbPairs=6, byteIdenticalFiles=12, compared=compared,
                  freshCompile=fact(P / 'joint-compile-r1/test-result.json'), allActualBuildProductsVerified=len(build_products),
                  compilerProfile=compiled['generatedProfile'],
                  newFunctionalTestsExecuted=False, jointBehaviorValidationComplete=False,
                  integrationAdmissionVerified=False, mainApplicationReady=False, mainApplied=False,
                  gameExecuted=False, installed=False, fullBCompleted=False)
    output.write_text(json.dumps(result, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(dict(passed=True, byteIdenticalFiles=12, mainApplied=False, jointBehaviorValidationComplete=False)))


if __name__ == '__main__':
    main()
