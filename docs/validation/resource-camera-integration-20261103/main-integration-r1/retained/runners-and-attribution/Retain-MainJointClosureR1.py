"""Retain actual post-merge main evidence and binaries on the Desktop project."""
from pathlib import Path
import datetime
import hashlib
import json
import shutil

P = Path(__file__).resolve().parent
MAIN = Path('C:/Users/71957/Desktop/riz_toolbox')
T = Path('D:/riz-build-cache-next/playback-stock-stop-occurrence-r1-20261102')
U = Path('D:/riz-build-cache-next/playback-progress-resource-continuity-r1-20261102')
O = MAIN / 'docs/validation/resource-camera-integration-20261103/main-integration-r1'
read = lambda p: json.loads(p.read_text(encoding='utf-8-sig'))
sha = lambda p: hashlib.sha256(p.read_bytes()).hexdigest()
fact = lambda p: dict(path=str(p), sha256=sha(p), bytes=p.stat().st_size)


def save(p, value):
    p.write_text(json.dumps(value, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')


def main():
    assert not O.exists(), 'Do not overwrite final source/evidence epochs.'
    closure = P / 'main-integration-closure-r1'
    accepted = read(closure / 'test-result.json')
    assert accepted['passed'] and accepted['currentIncrementOfflineValidationComplete'] and accepted['mainApplied']
    for row in read(closure / 'hash-manifest.json')['files']:
        assert sha(Path(row['path'])) == row['sha256']
    for row in read(closure / 'current-main-source-manifest.json')['files']:
        assert sha(MAIN / row['path']) == row['sha256']
    assert sha(MAIN / 'README.md') == 'cfbac4691d36edc5daba513aa62d5856ae61e29bf553d0dc2979832750847364'
    O.mkdir(parents=True)
    files = {}
    for folder in ['main-integration-closure-r1','main-resource-application-r1','main-camera-application-r1',
                   'main-joint-validation-r1','main-joint-validation-continuation-r1','symbols-main-joint-r2',
                   'main-joint-symbol-execution-r2','admission-check-r1']:
        for p in (P / folder).rglob('*'):
            if not p.is_file():
                continue
            rel = p.relative_to(P / folder)
            if rel.parts[0] in {'source','sdk-source','environment'}:
                continue
            files[p] = 'retained/' + p.relative_to(P).as_posix()
    for p in P.iterdir():
        if p.is_file() and p.suffix in {'.py','.ps1','.patch','.json'}:
            files[p] = 'retained/runners-and-attribution/' + p.name
    for p in [P / 'main-before-894.zip',P / 'resource-with-sdk-disposal-913.zip']:
        files[p] = 'retained/source-epochs/' + p.name
    for folder, prefix in [(T / 'integration-closure-after-shutdown-r1','retained/closed-T'),
                           (T / 'formal-sdk-combined-after-shutdown-r1','retained/combined-T'),
                           (U / 'final-sdk-disposal-integration-r1','retained/closed-U')]:
        for p in folder.rglob('*'):
            if p.is_file():
                files[p] = prefix + '/' + p.relative_to(folder).as_posix()
    for p in (P / 'main-joint-validation-continuation-r1/sdk-source/artifacts').rglob('*'):
        if p.is_file():
            files[p] = 'retained/current-sdk-artifacts/' + p.relative_to(P / 'main-joint-validation-continuation-r1/sdk-source/artifacts').as_posix()
    # The source ZIP carries ordinary sources once. Keep all actual solution
    # binaries so test applications, their copied dependencies and PDBs remain inspectable.
    for row in read(P / 'main-joint-validation-r1/solution-products.json'):
        p = Path(row['path'])
        assert sha(p) == row['sha256']
        files[p] = 'retained/' + p.relative_to(P).as_posix()
    for row in read(P / 'main-resource-application-r1/fresh-main-build-products.json')['files']:
        p = Path(row['freshMain']['path'])
        files[p] = 'retained/' + p.relative_to(P).as_posix()
    for p in [MAIN / 'docs/Game-camera.md', MAIN / '.docs/CURRENT-CHECKPOINT.md']:
        files[p] = 'retained/document-state-at-retention/' + p.name
    copied = []
    for original, relative in sorted(files.items()):
        target = O / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(original,target)
        assert sha(target) == sha(original)
        copied.append(dict(original=fact(original),retained=fact(target)))
    save(O / 'retained-artifacts.json',dict(passed=True,count=len(copied),files=copied))
    (O / 'scope.md').write_text('''# 本次主目录整合范围

用户授权：继续离线实现PC兼容底层、构建测试、更新证据及checkpoint。本次只合已验证的资源连续性与rig横向缩放，保留已测试的Lifecycle Dispose修补。

精确范围：主894→U53路径／主913→V9路径／主915，共61路径（21新增40修改）。验证源为实际合入后的主目录快照。禁止真实游戏安装、启动、GameAssembly方法或DllMain执行、游戏UI及账号；无commit／push／reset。

本case只签当前增量离线整合；完整generation、未知lower、production、Seek、effect、全部B01–B19及真实游戏仍未完成。
''',encoding='utf-8')
    (O / '2026-11-03_resource-camera-main-report.md').write_text('''# 资源连续性与相机横向缩放：主目录离线整合

资源连续性与相机rig横向缩放已精确合入主目录，当前915输入／485托管产品／48原生输入。新主目录实际通过针对性76项、整树28套2620项、独立SDK双插件14项、solution0错误／56既有警告、六组件Release和11对PE/PDB／991份源码文档核对。T18SDK／669与U12功能／275均已闭合，原失败及旧源码时期完整保留；当前增量离线验证完成，整个底层仍未完成。

## 授权与边界

见 [scope.md](scope.md)。没有安装或启动真实游戏，不执行游戏方法或DllMain，不操作游戏UI或账号。相机仍为experimental／可能影响玩法；真实更新与渲染次序、可见效果及恢复未证明。原生Speed/Seek与production没有开启。

## Evidence → Finding → Path

| 证据 | 原始记录 | 结论 |
|---|---|---|
| E-001 | [T完整汇总](retained/combined-T/test-result.json)，原6＋新12来源及[22范围closure](retained/closed-T/test-result.json) | 18个SDK场景、669项actual0，关机中断与旧失败保留 |
| E-002 | [U最终功能](retained/closed-U/test-result.json) | 原11／237＋新SDK38＝12／275全0，独审通过，Dispose仅测试修补回带 |
| E-003 | [U合主](retained/main-resource-application-r1/test-result.json)与[V合主](retained/main-camera-application-r1/test-result.json) | 53＋9精确路径，旧值hash先核，主915保持Dispose，fresh针对性76全0 |
| E-004 | [新主28套](retained/main-joint-validation-r1/managed-matrix-result.json)和[两来源构建链](retained/runners-and-attribution/main-joint-build-chain-r1.json) | 新主2620、SDK14、六Release实际通过；原driver在记录SDK路径处FileNotFound，总体false原件未改，只补未执行的两阶段 |
| E-005 | [11对PE/PDB](retained/symbols-main-joint-r2/test-result.json) | 991个实际SHA256文档与当前编译源一致；专项Runtime包含一个profile，因此不是隔离V的990 |
| E-006 | [最终整合](retained/main-integration-closure-r1/test-result.json)和[历史输入迁移](retained/main-integration-closure-r1/historical-input-transitions.json) | 核8863事实，40条主目录旧字节由894改前ZIP保留，另1条U测试旧字节单独保留；不把旧live路径当成当前源码 |

F-001（validated／high／design，E-001→E-003）：资源引用变动只使未来捕获票失效，当前音源与已有Clock仍可继续控制；相机位置和缩放各自仲裁、恢复，缩放每次保留最新Y/Z。当前源来自已实际验证的913＋9路径。

F-002（validated／high／other，E-004→E-006）：主目录新源码经过实际构建和测试，源码与产物可追溯。首次后续打包因脚本残留缓存路径而中断，原29个成功子命令不重跑，新SDK／Release两命令另scope全0；从未将原aggregate false改成true。

P-001（callflow，E-001→E-002→E-003→E-004→E-005→E-006）：收齐T与U真实终态 → 核8266前置事实 → U53合主／fresh76 → V9合主 → fresh主28／2620 → SDK双插件14／六Release → 11PE-PDB991 → 核915主源及历史迁移。

## 复核与下一步

[retained-artifacts.json](retained-artifacts.json)绑定原件和桌面副本，完整915源码ZIP与894／913历史ZIP已保留。实际命令在各launch／process-result及归档脚本中；保护原件，复跑使用新目录。默认不把本增量离线完成解释为真实游戏、production或全部B01–B19通过。

下一从checkpoint和NEXT-FUNCTIONAL-GAPS继续完整generation／未知lower writer及formal provider、Play外层实际回调与非空来源／native Add队列、Seek和其它明确未完成能力；先功能、后性能，真实游戏继续后置。
''',encoding='utf-8')
    index=dict(kind='desktop-current-resource-camera-main-integration-offline-r1',passed=True,createdAtUtc=datetime.datetime.now(datetime.timezone.utc).isoformat(),
         mainApplied=True,mainSourceFiles=915,managedProductInputs=485,nativeInputs=48,
         tSdkProcesses=18,tSdkChecks=669,uFunctionalModes=12,uFunctionalChecks=275,
         freshFocusedChecks=76,freshManagedSuites=28,freshManagedChecks=2620,freshSdkChecks=14,
         freshSolutionErrors=0,freshSolutionWarnings=56,freshReleaseAssemblies=6,freshPePdbPairs=11,freshSourceDocuments=991,
         retainedOriginals=len(copied),retainedArtifacts=fact(O / 'retained-artifacts.json'),
         currentIncrementOfflineValidationComplete=True,artifactIntegrityPassed=True,
         originalFailedAggregatePreserved=True,gameExecuted=False,installed=False,productionAdmission=False,
         nativeSpeedSeekEnabled=False,fullGenerationCompleted=False,unknownLowerCompleted=False,fullBCompleted=False)
    save(O / 'scope-index.json',index)
    manifest=[fact(p) for p in sorted(O.rglob('*')) if p.is_file()]
    save(O / 'package-hash-manifest.json',dict(passed=True,files=manifest))
    print(json.dumps(dict(passed=True,retainedOriginals=len(copied),packageFiles=len(manifest),output=str(O),mainApplied=True,fullBCompleted=False)))


if __name__ == '__main__':
    main()
