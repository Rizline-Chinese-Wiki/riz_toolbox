"""Retain this completed isolated increment on the Desktop without applying its product diff."""
from pathlib import Path
import datetime
import hashlib
import json
import shutil
import zipfile

R = Path(__file__).resolve().parent
MAIN = Path('C:/Users/71957/Desktop/riz_toolbox')
O = MAIN / 'docs/validation/game-camera-scale-x-20261103/offline-r1'
now = lambda: datetime.datetime.now(datetime.timezone.utc).isoformat()
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
fact = lambda p: dict(path=str(p), sha256=sha(p), bytes=p.stat().st_size)
read = lambda p: json.loads(p.read_text(encoding='utf-8-sig'))


def save(p, value):
    p.write_text(json.dumps(value, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')


def verify(row):
    p = Path(row['path'])
    assert p.is_file() and sha(p) == row['sha256'], str(p)
    assert 'bytes' not in row or p.stat().st_size == row['bytes']


def walk_facts(value):
    if isinstance(value, dict):
        if isinstance(value.get('path'), str) and Path(value['path']).is_absolute() and isinstance(value.get('sha256'), str):
            yield value
        for child in value.values():
            yield from walk_facts(child)
    elif isinstance(value, list):
        for child in value:
            yield from walk_facts(child)


def main():
    assert not O.exists(), 'Retained scopes are immutable.'
    functional = read(R / 'functional-r1/test-result.json')
    foundation = read(R / 'foundation-r1/test-result.json')
    symbols = read(R / 'symbols-r1/test-result.json')
    static = read(R / 'scale-static-proof-r1/test-result.json')
    assert functional['passed'] and functional['checks'] == 55
    assert foundation['passed'] and foundation['managedChecks'] == 2620 and len(foundation['projects']) == 28
    assert foundation['sdkChecks'] == 14 and foundation['releaseAssemblyCount'] == 6 and foundation['solutionErrors'] == 0
    assert symbols['passed'] and symbols['pePdbPairs'] == 11 and symbols['documentCount'] == 990
    assert static['passed'] and static['completeMethodCount'] == 4 and static['gameExecuted'] is False
    source_rows = read(R / 'functional-r1/source-manifest.json')['files']
    assert len(source_rows) == 915
    for row in source_rows:
        for root in (R / 'source', R / 'functional-r1/source', R / 'foundation-r1/source'):
            assert sha(root / row['path']) == row['sha256']
    guards = read(R / 'functional-r1/guarded-baselines.json')
    for guard in guards:
        for row in guard['files']:
            assert sha(Path(guard['root']) / row['path']) == row['sha256']
    assert sha(MAIN / 'README.md') == 'cfbac4691d36edc5daba513aa62d5856ae61e29bf553d0dc2979832750847364'
    original_facts = []
    for relative in ['functional-r1/test-result.json','functional-r1/build-products.json',
                     'foundation-r1/test-result.json','foundation-r1/solution-products.json',
                     'foundation-r1/release-products.json','foundation-r1/fixed-inputs.json','symbols-r1/test-result.json',
                     'scale-static-proof-r1/hash-manifest.json','scale-static-proof-r1/test-result.json']:
        original_facts.extend(walk_facts(read(R / relative)))
    for row in original_facts:
        verify(row)
    O.mkdir(parents=True)
    originals = set()
    for name in ['IMPLEMENTATION-CONTRACT.md','baseline-manifest.json','Run-CameraScaleR1.py',
                 'Run-FoundationR1.py','Verify-ManagedSymbolsR1.ps1','Retain-CameraScaleR1.py']:
        originals.add(R / name)
    for folder in ['basis','delivery-docs','scale-static-proof-r1','symbols-r1']:
        originals.update(p for p in (R / folder).rglob('*') if p.is_file())
    for folder in ['functional-r1','foundation-r1']:
        for p in (R / folder).rglob('*'):
            if not p.is_file():
                continue
            parts = p.relative_to(R / folder).parts
            if parts[0] in {'source','sdk-source','environment','release-managed'}:
                continue
            originals.add(p)
    for row in original_facts:
        p = Path(row['path'])
        if p.is_relative_to(R):
            originals.add(p)
    sdk_root = R / 'foundation-r1/sdk-source/artifacts'
    originals.update(p for p in sdk_root.rglob('*') if p.is_file())
    for image in symbols['images']:
        for row in [image['assembly'], image['pdb']]:
            originals.add(Path(row['path']))
    retained = []
    for p in sorted(originals):
        target = O / 'retained' / p.relative_to(R)
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(p, target)
        assert sha(target) == sha(p)
        retained.append(dict(original=fact(p), retained=fact(target)))
    source_zip = O / 'candidate-source-915.zip'
    with zipfile.ZipFile(source_zip, 'x', compression=zipfile.ZIP_DEFLATED) as archive:
        for row in source_rows:
            archive.write(R / 'source' / row['path'], row['path'])
    with zipfile.ZipFile(source_zip) as archive:
        assert len(archive.namelist()) == 915
        for row in source_rows:
            assert hashlib.sha256(archive.read(row['path'])).hexdigest() == row['sha256']
    save(O / 'retained-artifacts.json', dict(passed=True, files=retained, count=len(retained)))
    save(O / 'verified-original-facts.json', dict(passed=True, count=len(original_facts), files=original_facts))
    scope = '''# 本次离线范围

- owner: riz_toolbox 项目用户
- authorization: 用户要求自主推进底层功能、离线构建测试并更新证据与checkpoint；本轮“继续”沿用。
- allowed: 隔离候选源码修改、固定游戏文件的只读静态分析、自有fake/exports正向、整树与SDK离线验证、桌面证据归档。
- forbidden: 真实游戏安装、启动、方法或DllMain执行、游戏UI、账号操作；无commit/push/reset。
- candidate: D:/riz-build-cache-next/game-camera-scale-x-r1-20261103
- baseline: U913／484managed／48native；新915／485managed／48native。
- acceptance: 本包只签local-scale-x的离线增量；mainApplied=false、runtimeCameraAcceptance=false、fullBCompleted=false。
- integration gate: 等U与T真实闭合、U先合主，再只合V的9路径差分；保留U已经测试的Lifecycle Dispose修补。
'''
    (O / 'scope.md').write_text(scope, encoding='utf-8')
    report = '''# rig localScale.x 离线增量

当前局相机rig新增独立的横向缩放标量，位置和缩放可以由两个插件同时控制并各自恢复。新候选专项55/55、整树28套2620/2620、SDK独立双插件14/14及六组件Release均实际通过，solution为0错误／56既有警告，11对PE/PDB与990份源码文档逐项一致。主目录产品保持T894／482，新增能力仍为隔离候选；真实Update/render次序、持续可见效果和玩法影响留待实测。

## 范围与来源

范围以 [scope.md](scope.md) 为准。基线是U最终913输入，本包冻结915输入，含485托管产品输入、48原生输入和767个C#文件。差分仅9路径：SDK角色常量，service完整descriptor白名单及属性自身channel/policy，backend双属性独立图和固定方法，新的scale accessor，Module注释，以及四个相机测试路径。

## Evidence → Finding → Path

| 证据 | 原始记录 | 结论 |
|---|---|---|
| E-001 | [固定方法](retained/scale-static-proof-r1/test-result.json)，4方法848字节及RF／完整反汇编 | SetScale逐轴乘B分量并写localScale，Update调用SetScale；只能称单轴缩放，不能声称uniform zoom |
| E-002 | [专项](retained/functional-r1/test-result.json)及camera完整日志 | 两属性可同时覆盖、分别退役；新鲜Y/Z保留，外部基线／优先权／异常回滚／旧票拒绝／全根清理均通过 |
| E-003 | [新整树](retained/foundation-r1/managed-matrix-result.json) | 当前915输入实际跑28套2620项，不用U旧2599或历史相机33代签 |
| E-004 | [构建链](retained/foundation-r1/test-result.json) | fresh solution、独立SDK双作者14项、六组件Release均actual0；功能没有依赖真实游戏执行 |
| E-005 | [PE/PDB](retained/symbols-r1/test-result.json) | 11对CodeView身份与990份实际源码SHA256一致 |

F-001（validated，confidence=high，category=other）：`RigLocalScaleX`／`local-scale-x`为无量纲单轴能力，无游戏数值范围证据，沿用experimental和玩法影响说明。依据E-001、E-002。

F-002（validated，confidence=high，category=design）：位置与缩放各自保留完整当前局图根、identity、physical identity、别名及仲裁通道，单属性退役不会释放另一属性根；backend完整descriptor快照限制候选授予范围。依据E-002、E-003、E-005。

P-001（callflow，E-001→E-002→E-003）：SDK role/query → 当前局唯一target → 共享MutationCoordinator优先权 → 完整图当前性校验 → 新鲜Vector3 getter → 再次图校验 → 只替换X的exact setter → readback；错误或新一代对象拒绝旧票，作用域结束恢复该属性当前基线。

## 复核与待办

[retained-artifacts.json](retained-artifacts.json)记录归档原件与桌面副本的逐项hash；[candidate-source-915.zip](candidate-source-915.zip)保留全部冻结输入。[scope-index.json](scope-index.json)给出准确范围，不能把其passed解释成整体底层或真实游戏通过。

在本报告所在目录，可用 `dotnet retained/functional-r1/source/tests/RizToolbox.GameCamera.Tests/bin/Release/net8.0/RizToolbox.GameCamera.Tests.dll` 重跑55项自有exports检查；完整构建参数和环境在两个归档Run脚本及各launch.json中，复跑须另建目录保护原件。固定游戏文件按853d完整SHA校验，只读取文件，不加载游戏。

下一步等U末SDK及独立最终审计、T剩余六Runtime场景实际闭合，U精确合主后再合V；不可拿V旧基线ZIP覆盖U已验证的Lifecycle修补。完整generation、未知lower、production、Seek、effect及B01–B19仍未完成。
'''
    (O / '2026-11-03_game-camera-scale-x-report.md').write_text(report, encoding='utf-8')
    index = dict(kind='retained-isolated-camera-scale-x-offline-r1', passed=True, createdAtUtc=now(),
                 sourceFiles=915, managedInputs=485, nativeInputs=48, cSharpInputs=767, changedPaths=9,
                 cameraChecks=55, managedSuites=28, managedChecks=2620, solutionErrors=0,
                 solutionWarnings=foundation['solutionWarnings'], sdkChecks=14, independentAuthors=2,
                 releaseAssemblies=6, pePdbPairs=11, sourceDocuments=990,
                 retainedOriginalCount=len(retained), verifiedOriginalFactCount=len(original_facts),
                 originalEvidenceIntegrityVerified=True, sourceArchive=fact(source_zip),
                 retainedArtifacts=fact(O / 'retained-artifacts.json'), verifiedFacts=fact(O / 'verified-original-facts.json'),
                 scope=fact(O / 'scope.md'), report=fact(O / '2026-11-03_game-camera-scale-x-report.md'),
                 baselineGuardsVerified=True, mainApplied=False, resourceCandidateApplied=False,
                 runtimeCameraAcceptance=False, gameExecuted=False, installed=False, productionAdmission=False,
                 formalStockStopSdkComplete=False, resourceSdkCurrentCaseComplete=False, fullBCompleted=False)
    save(O / 'scope-index.json', index)
    files = [fact(p) for p in sorted(O.rglob('*')) if p.is_file()]
    save(O / 'package-hash-manifest.json', dict(kind='camera-scale-x-offline-package-integrity', passed=True, files=files))
    print(json.dumps(dict(passed=True, retainedFiles=len(retained), packageFiles=len(files), output=str(O), mainApplied=False)))


if __name__ == '__main__':
    main()
