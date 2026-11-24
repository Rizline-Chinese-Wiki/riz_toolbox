"""Publish this native-listener batch from the root builder's final proof.

Documentation only. It never builds, installs or starts a child/game process.
Every validation and all seven document transformations precede the first write.
Do not run until final fixture schema, full terminal status and archives exist.
"""
import hashlib
import json
import re
import zipfile
from pathlib import Path, PurePosixPath


batch = Path(__file__).resolve().parent
repo = batch.parents[2]
previous_name = "foundation-adopted-broker-20261024"
assert batch.name == "foundation-listener-native-20261024"


def read(name):
    return json.loads((batch / name).read_text(encoding="utf-8-sig"))


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def text(path):
    return path.read_text(encoding="utf-8-sig").replace("\r\n", "\n").replace("\r", "\n")


def archive_check(proof, manifest, current_root, count):
    entries = manifest["files"]
    assert len(entries) == count == proof["entries"]
    assert len({entry["path"] for entry in entries}) == count
    archive_path = batch / proof["archive"]
    assert archive_path.resolve().parent == batch
    assert sha(archive_path) == proof["archiveSha256"]
    assert proof["allEntryHashesMatched"] is True
    with zipfile.ZipFile(archive_path) as archive:
        assert len(archive.namelist()) == count and archive.testzip() is None
        assert set(archive.namelist()) == {entry["path"] for entry in entries}
        for entry in entries:
            relative = PurePosixPath(entry["path"])
            assert not relative.is_absolute() and ".." not in relative.parts
            current = (current_root / entry["path"]).resolve()
            assert current.is_relative_to(current_root.resolve()) and current.is_file()
            assert sha(current) == entry["sha256"]
            assert hashlib.sha256(archive.read(entry["path"])).hexdigest() == entry["sha256"]


summary = read("build-summary.json")
run = read("build-run.json")
source = read("source-archive-check.json")
package = read("package-archive-check.json")
manifest = read("source-manifest.json")
package_manifest = read("package-manifest.json")
inputs_before = read("build-inputs-before.json")
inputs_integrity = read("build-input-integrity-check.json")
integrity = read("source-integrity-check.json")
changes = read("changes-from-adopted-broker.json")
staging = read("staging-check.json")
installed = read("previous-installation-check.json")
originals = read("original-game-check.json")
listener = read("listener-native-evidence.json")

# Full terminal proof, not a targeted success or a running process.
assert run["exitCode"] == 0
assert run.get("installed", False) is False
assert not run.get("gameExecuted", False) and not run.get("gameCodeExecuted", False)
assert run.get("rootSoleBuilder", True) is True
assert summary["passed"] == summary["total"] == 1515
assert summary["projects"] == len(summary["suites"]) == 25
assert sum(row["passed"] for row in summary["suites"]) == 1515
assert all(row["passed"] == row["total"] for row in summary["suites"])
assert sha(batch / "build.log") == summary["logSha256"] == run["logSha256"]
for expected in ["Native runtime checks: 160/160 passed.", "171/171 exact-hook checks passed.",
                 "71/71 visual checks passed.", "UI/input checks: 79/79 passed.",
                 "573/573 audio/content checks passed."]:
    assert sum(row["summary"] == expected for row in summary["suites"]) == 1

# Actual final count comes from both frozen manifests, never from an estimate.
source_count = len(manifest["files"])
assert source_count == source["entries"] == 491
assert inputs_before["files"] == manifest["files"]
assert inputs_integrity["checked"] == source_count
assert inputs_integrity["preBuildManifestMatchesArchivedManifest"] is True
assert inputs_integrity["currentChanged"] == []
assert integrity["checked"] == source_count and integrity["changed"] == []
assert sha(batch / "source-manifest.json") == source["manifestSha256"]
archive_check(source, manifest, repo, source_count)
archive_check(package, package_manifest, repo / "artifacts/package", 22)
assert package["installed"] is False
assert staging["allMatched"] is True and staging["installed"] is False
assert len(staging["files"]) == 22
assert all(row["expected"] == row["actual"] for row in staging["files"])
assert changes["previous"] == previous_name and changes["current"] == batch.name
assert changes["previousInputs"] == 484 and changes["currentInputs"] == source_count
assert changes["bothFrozenArchivesVerified"] is True and changes["removed"] == []
assert 484 + len(changes["added"]) - len(changes["removed"]) == source_count
assert len(changes["added"]) == 7 and len(changes["changed"]) == 3
assert installed["batch"] == "foundation-local-protection-host-20261022"
assert installed["allMatched"] is True and len(installed["files"]) == 18
assert installed["archivedSourceInputsChecked"] == 326
assert all(row["expected"] == row["actual"] for row in installed["files"])
assert originals["allUnchanged"] is True and len(originals["files"]) == 3
assert all(row["expected"] == row["actual"] for row in originals["files"])

# The actual listener child is native only. Validate its exact observed schema.
assert summary["listenerNative"] == listener
assert listener["kind"] == "native-listener-six-region-fixture"
assert listener["passed"] is True and listener["failures"] == 0 and listener["checks"] > 0
assert not any(listener[key] for key in ["gameRuntimeVerified", "gameCodeExecuted",
                                       "firstInstallSafetyVerified", "allFutureListenersClosed"])
assert listener["postBoundaryOffsets"] == [0, 5, 7, 9]
assert listener["patchedSourceWidths"] == [5, 5, 6]
assert listener["patchedPostWidths"] == [6, 9, 9]
for key in ["actualCalleeReturnChecks", "originalCalls", "replayChecks",
            "actualCallbackStackWalks", "postInstructionBoundaryUnwinds", "fixtureCodeWrites",
            "preFullStateChecks", "postFullStateChecks",
            "lateAttachOldCalls", "nullParentObserverOldCalls", "normalCases",
            "originalSehCases", "originalCppCases", "observerFaultCases",
            "observerFaults", "workerCases", "nestedCases"]:
    assert isinstance(listener[key], (int, float)) and listener[key] > 0, key
assert listener["actualCalleeReturnChecks"] == listener["originalCalls"]
observed_counts = dict(checks=8349, actualCalleeReturnChecks=74, originalCalls=74, replayChecks=62,
    actualCallbackStackWalks=124, postInstructionBoundaryUnwinds=224, preFullStateChecks=68,
    postFullStateChecks=56, fixtureCodeWrites=14, lateAttachOldCalls=2, nullParentObserverOldCalls=1,
    normalCases=14, originalSehCases=6, originalCppCases=6, observerFaultCases=4, observerFaults=4,
    workerCases=2, nestedCases=4)
assert all(listener[key] == value for key, value in observed_counts.items())
assert listener["actualCallbackStackWalks"] == listener["preFullStateChecks"] + listener["postFullStateChecks"]
assert listener["postInstructionBoundaryUnwinds"] == 4 * listener["postFullStateChecks"]
assert listener["originalCalls"] - listener["replayChecks"] == listener["originalSehCases"] + listener["originalCppCases"]
assert listener["preFullStateChecks"] == listener["originalCalls"] - 3 * listener["lateAttachOldCalls"]
assert listener["postFullStateChecks"] == listener["replayChecks"] - 3 * listener["lateAttachOldCalls"]
assert len(listener["regions"]) == 2
assert {row["slot"] for row in listener["regions"]} == {28, 29}
for row in listener["regions"]:
    assert [len(bytes.fromhex(value)) for value in row["originalPre"]] == [5, 5, 6]
    assert [len(bytes.fromhex(value)) for value in row["originalPost"]] == [6, 9, 9]
assert isinstance(listener["unimplemented"], list) and listener["unimplemented"]
assert len(listener["unimplemented"]) == 8
assert {"dynamic-vtable-mutation", "target-read-av", "full-exception-record", "prehelper-every-instruction-unwind",
        "foreign-parent-mask-and-overflow", "missing-post-corruption",
        "all-delegate-leaves", "mutation-stopcoroutine-retirement"} == set(listener["unimplemented"])
native_output = batch / "native-build/listener-context"
assert json.loads((native_output / "fixture-result.json").read_text(encoding="utf-8-sig")) == listener
native_manifest = json.loads((native_output / "fixture-manifest.json").read_text(encoding="utf-8-sig"))
assert native_manifest["kind"] == "native-listener-context-runner"
assert native_manifest["exitCode"] == 0 and native_manifest["allMatched"] is True
assert not any(native_manifest[key] for key in ["gameRuntimeVerified", "gameCodeExecuted", "installed"])
assert len(native_manifest["files"]) == 3
assert all(row["beforeSha256"] == row["afterSha256"] for row in native_manifest["files"])
exports = re.findall(r"^\s+\d+\s+[0-9A-Fa-f]+\s+[0-9A-Fa-f]+\s+([A-Za-z_][A-Za-z0-9_@?$]*)(?:\s|$)",
                     text(batch / "native-build/exports.txt"), re.M)
assert len(exports) == len(set(exports)) == 41
assert {f"RizListenerContext_{name}" for name in ["GetPreEntry", "GetPostEntry", "Configure", "Attach", "GetInfo"]}.issubset(exports)

# Legacy CLR callback regressions remain separate from the NEW listener lane.
old_callbacks = summary["adoptedBrokerCallbacks"]
assert old_callbacks["passed"] is True and old_callbacks["failures"] == 0
assert old_callbacks["checks"] == 379 and old_callbacks["independentProcesses"] == 3
assert old_callbacks["nativeToClrCallbackVerified"] is True
assert old_callbacks["firstInstallReceiptUsed"] is False
compile_failures = [
    ("targeted-first-asm-failure.log", "A2041"),
    ("targeted-second-asm-failure.log", "A2041"),
    ("targeted-third-asm-failure.log", "A2006"),
    ("targeted-cpp-alignment-failure.log", "C4324"),
    ("targeted-cpp-compile-failure.log", "C2664"),
]
for name, marker in compile_failures:
    assert (batch / name).is_file() and marker in text(batch / name)
assert len(run["failedTargetedRuns"]) == 5
assert {row["log"] for row in run["failedTargetedRuns"]} == {name for name, _ in compile_failures}
for row in run["failedTargetedRuns"]:
    assert row["exitCode"] == 1 and sha(batch / row["log"]) == row["logSha256"]
assert run["targeted"]["exitCode"] == 0 and run["targeted"]["checks"] == 8349
assert sha(batch / run["targeted"]["log"]) == run["targeted"]["logSha256"]

outputs = {}
checks = int(listener["checks"])
added, changed, removed = (len(changes[key]) for key in ["added", "changed", "removed"])
diagnostics_count = len(summary["compilerDiagnosticLines"])
session = run.get("runnerSession", run.get("sessionId", run.get("runnerSessionId")))
assert session is not None
result = "1515/1515、25套"
command = "pwsh -File scripts/Build.ps1 -NativeOutputDirectory D:/RizToolbox-build-cache/listener-native-final-20261024 -ValidationGameAssemblyPath D:/SteamLibrary/steamapps/common/Rizline/GameAssembly.dll"
limits = "本批listener为native only，不是CLR listener/managed ledger接入；没有固定游戏六区发布，不升级FirstInstall V1 Ready144。invokable dispatch不等于每个delegate leaf；真实游戏receipt callbacks、冷启动、IL2CPP/GC、连续mutation/provider转交/取消、全部future来源与消费者退休、完整Retry/Return/Loader仍缺。"
state = "RequestPlay关闭，Pending保留，InlineExecutionUnobserved=true、DispatchPathVerified=false；完整B01–B19 active，仅B03整项/B18a子项勾选，用户“实测稍后”保持。"
freeze = f"{source_count}源/22暂存包逐项冻结，较采用回调联合批新增{added}/修改{changed}/删除{removed}。source ZIP SHA-256：{source['archiveSha256']}；manifest：{source['manifestSha256']}；package ZIP：{package['archiveSha256']}；完整日志：{summary['logSha256']}。"
diagnostics = f"完整构建保留{diagnostics_count}条去重编译诊断，不宣称零warning或干净环境B19全矩阵复现。"
failure_notes = "五份定向编译红日志保留：fixture MASM长名称/局部label与代码LABEL适配，以及fixture C++显式对齐padding/Windows RtlVirtualUnwind const参数修正；生产Listener h/cpp/ASM与Bootstrap四源没有因此修改。最终原生定向session68082 exit0后才进行full，不能把编译失败写为原生运行通过或production故障。"
abi = "新增Config80/Observer32/Frame176/Info176及五exports，helper总41。独立listener descriptor在Bootstrap V1 descriptor之后、任何managed Enter之前固定；V1 observer可null，ParentManagedToken0合法，两个发布点不是聚合V2。"
fault_contract = "virtual一次目标读取位于observer保护之外，读取异常的地址是helper真实指令，不能伪称原CALL RIP。TargetReadFaulted的Abnormal After(token0)可能没有Before，未来managed只能按flags做native cleanup，不能从0推已capture。该分支没有本批动态验收，不冒充原callee SEH/C++用例。"
unimplemented = "、".join(listener["unimplemented"])
native_counts = (f"实际callee返回地址核{int(listener['actualCalleeReturnChecks'])}次、original{int(listener['originalCalls'])}次、"
    f"replay{int(listener['replayChecks'])}次；callback内真实stack walk{int(listener['actualCallbackStackWalks'])}次，"
    f"post四指令边界展开{int(listener['postInstructionBoundaryUnwinds'])}次；"
    f"pre对真实callee完整状态核{int(listener['preFullStateChecks'])}次、post对实际loop叶快照核{int(listener['postFullStateChecks'])}次；"
    f"normal{int(listener['normalCases'])}/原SEH{int(listener['originalSehCases'])}/原C++{int(listener['originalCppCases'])}/"
    f"observer故障case{int(listener['observerFaultCases'])}（实际fault{int(listener['observerFaults'])}）/"
    f"worker{int(listener['workerCases'])}/nested{int(listener['nestedCases'])}。")

outputs[batch / "README.md"] = f"""# 三处 invokable 的原生前后观察

2026-10-24。**{result}完整通过，{source_count}源/22包冻结未安装；原生listener另列{checks}项。** root唯一builder，最终session {session}真实终态exit0。Runtime160/Hooks171/Visual71/UIinput79/AudioContent573；额外native断言不并入25套1515，也不代表覆盖率。

本批实现真实pre CALL→fullsave→tail原callee，与post continuation E9→零栈动态stub→真实CALL观察helper→完整replay。测试只改自有函数，三处pre宽度5/5/6、post宽度6/9/9；两direct与virtual的真实硬件返回地址分别保source+5/+5/+6。原函数没有被C++包装器再次CALL。

## 实际执行与边界

{native_counts} fixture代码写入{int(listener['fixtureCodeWrites'])}处；lateAttach旧call{int(listener['lateAttachOldCalls'])}次，其中null V1 parent observer旧call{int(listener['nullParentObserverOldCalls'])}次。实际计数与原始bytes见[JSON](listener-native-evidence.json)；不是游戏patch写数。

动态post stub登记零额外栈CHAININFO，post真实快照在0/5/7/9四个指令边界核展开；callback内实际捕获context另走helper→stub→原parent→caller。末E9与局部边界必须结合本机结果看待，不以RtlAddFunctionTable成功代替展开证明。pre真实callee与post实际loop叶快照核GPR、XMM低128、RFLAGS/控制字，post明确计入原inc/mov产生的预期变化；无AVX upper lanes/完整x87数据栈保存承诺，不覆盖prehelper每指令展开。

{abi} lane Configure只配置，不发布游戏指令。pre以callee-owned home[0]=[entryRsp+8]作最终target scratch，保原硬件return槽；这不是不可变canary。child只有当前父top/epoch/self/site/sourceRsp/event/list/index匹配才正常退出；原异常由父finally先清child，再执行旧V1 parent Exit，观察故障置stickyUnknown但不改原函数行为。

{fault_contract} 明确未实现/未验分支：{unimplemented}。Post错list/index拒绝与底层Unknown管理传播的源码修正已复核，不冒充新增故障注入通过。

{failure_notes}

{limits} 上批自有native→CLR broker回调379项/3进程已在本full回归，但没有变成本批listener的CLR证明。{state}

## 冻结与复现

{freeze} build-inputs-before与post manifest完全相同，current源/包逐项核对。{diagnostics} 游戏仍1021安装18项/其326源归档，原游戏三hash不变；没有本批安装、游戏/UI操作、游戏进程终止或commit/push。

仓库根PowerShell7：

~~~powershell
{command}
~~~

实际映像验证须显式ValidationGameAssemblyPath；DONT_RESOLVE只用于旧隔离映像publisher检查，不执行游戏代码，不是本批六区游戏发布或生产启动模式。

[完整日志](build.log)、[runner终态](build-run.json)、[汇总](build-summary.json)、[listener原生证据](listener-native-evidence.json)、[fixture输入核对](native-build/listener-context/fixture-manifest.json)、[源码冻结](source-archive-check.json)、[包冻结](package-archive-check.json)、[差异](changes-from-adopted-broker.json)、[复核](review.md)、[ABI文档](../../../native/RizToolbox.JudgeContext/ListenerContext.md)。

[授权范围](scope.md)、[下一步](next-integration.md)、[正式报告](../../2026-10-24_底层-监听调用原生层-report.md)。
"""

outputs[batch / "next-integration.md"] = f"""# 下一阶段：发布身份与实际CLR listener

前提：本批{result}、{source_count}源/22包冻结未安装，自有原生listener {checks}项。{limits} 本文件为计划，用户“实测稍后”有效，不能当作验收证据。

## 1. 收口原生fixture剩余分支

当前明确未实现/未验：{unimplemented}。另外还需取得错post list/index与独立lane父依赖Unknown时序的确定注入证据。按真实机器路径增加故障用例，不把人工构造frame包装成真实callee执行；overflow与foreign必须遮蔽祖先，missingPost只能Abnormal并保Unknown，不能补造normal。现有pre真实callee与post外侧叶快照的GPR/XMM/control核对继续回归，不扩大AVX/x87 data保证。

{fault_contract} 两份独立descriptor不是聚合V2；若下一版需要整份callback原子快照，应版本化ABI并给明确迁移/旧调用隔离，而不是热补已Attach表。

## 2. 版本化生产publisher与receipt

固定profile先核三处source ReturnPC、6/9/9完整continuation、原primary unwind/frameRegister，再准备近端stub、完整CHAININFO和永久root。全部table注册、真实lookup与边界展开证明必须在首写前完成；有写尝试后Unknown不得释放。新receipt明确六区、各stub/target/epoch/页保护/原bytes/写数和publisher身份；旧V1 Ready144三写不得授权六区。

先在只映射固定GameAssembly的隔离进程核真实bytes与页权限，绝不调用游戏函数。自有fixture代码写入不是游戏生产发布，也不能以known代码猜测任意RVA安全。

## 3. managed adopter、ledger与全run闭环

真实CLR listener采用应核新receipt与原生full identity，共享完整slot/epoch/site/PC/target；保持生产RequireReady前门，不公开SDK bypass。Before/Returned/Abnormal按private parent/child sequence与受控token关联同代context；TargetReadFaulted没有Before时只nativecleanup，token0不能证明capture。机器帧只读copy且不可跨callback持有。

继续实际IL2CPP invokable/provider/对象根、动态listener leaf与异步转交、连续mutation和取消；invokable dispatch不等于全部delegate leaves。来源未闭合保持opaque/Pending。全部future及消费者结束后才组合VerifyConsumersEnded→Close→native/图/audio/globals/menu/result/settings资源退休，最后完整Retry/Return/Loader及真实冷启动/game acceptance。{state}
"""

evidence_rows = []
for evidence_id, name, reproduce in [
    ("E-full", "build.log", command),
    ("E-run", "build-run.json", "读取root真实终态，不启动构建"),
    ("E-native", "listener-native-evidence.json", "同完整命令，Test-ListenerContextNative自有EXE"),
    ("E-inputs", "build-input-integrity-check.json", "逐路径/hash比较before、post manifest与current"),
    ("E-source", "source-archive-check.json", "逐项核source-inputs.zip及当前声明输入"),
    ("E-package", "package-archive-check.json", "逐项核22项staged-package.zip"),
    ("E-review", "review.md", "有限静态复核与本机结果边界"),
    *[(f"E-red-{index}", name, "只读保留fixture编译红日志；不以最终成功覆写它") for index, (name, _) in enumerate(compile_failures, 1)],
]:
    evidence_rows.append(f"| {evidence_id} | [{name}](validation/{batch.name}/{name}) | {reproduce} | {sha(batch / name)} |")
outputs[repo / "docs/2026-10-24_底层-监听调用原生层-report.md"] = f"""# 三处监听调用的原生运输层报告

2026-10-24，flavor=null，兼容框架工程验证。**{result}完整构建通过，{source_count}源/22包冻结未安装；另列native listener {checks}项。** {limits} {state}

按[scope](validation/{batch.name}/scope.md)，三个子代理分别实现native lane、自有fixture和独立只读审查，root唯一builder。自有函数首次执行前发布；没有游戏六区publisher或新SDK listener API。{abi}

## Evidence → Finding → Path

source_type为file/log，observed_at为各JSON与root runner终态时间；content_hash对应链接文件本身，不混用ZIP SHA。复现从仓库根，文档publisher不运行构建。

| E-id | source_ref / artifact_path | repro_command或离线限制 | content_hash SHA-256 |
|---|---|---|---|
{chr(10).join(evidence_rows)}

| F-id | category / severity | evidence_ids | status / confidence / location |
|---|---|---|---|
| F-native | other / n/a_re | E-native,E-full,E-source | validated/high，自有两direct/一virtual真实pre tail、source+5/+5/+6返回与post完整6/9/9 replay；pre真实callee/post实际叶快照核GPR/XMM低128/control，post含replay预期变化 |
| F-unwind | design / n/a_re | E-native,E-review,E-source | validated/high，本机post四边界0/5/7/9及callback真实stack walk；零栈secondary链到实际parent，不将table注册当充分证据 |
| F-lifetime | design / n/a_re | E-native,E-review,E-source | validated/high，独立lane父Enter快照、旧call跨Attach、同/异槽nested与worker、原SEH/C++及观察故障；不扩大到未验overflow/foreign/missingPost |
| F-strict | design / n/a_re | E-review,E-source | source-reviewed/high，post实际list/index配对与管理读刷新Void/Gate/Mid Unknown已修；没有对应受控坏帧/确定竞争注入证据 |
| F-fault-contract | design / n/a_re | E-review,E-source | accepted boundary/high，TargetReadFaulted的After可没有Before，真实helper read异常地址变化；不宣称此分支动态通过 |
| F-limit | design / n/a_re | E-native,E-run,E-review | accepted boundary/high，新通道native only、无游戏六区publisher/CLRlistener/ledger；invokable不等于delegate leaf，全底层未完成 |

P-pre，path_type=callflow：自有source真实CALL→fullsave MidFrame→当前Bootstrap top独立listener snapshot→private child/sequence→固定direct target或一次virtual读取→Before观察→目标写callee home[0]→完整restore与合法FF25 epilog→独立leaf tail→callee真实硬件return到原source+5/+5/+6。E-native/E-source，F-native。home0允许scratch；无AVX upper/完整x87 data保证。

P-post，path_type=callflow：callee RET→源continuation E9→零额外栈动态CHAININFO stub→真实CALL post helper→按actual transportPC/parent/sequence/site/sourceRsp/event/list/index配对→Returned观察→restore/RET到stub→完整inc/mov/jmp loop一次。E-native/E-review，F-native/F-unwind。SourceReturnPc与TransportReturnPc分开；post helper只观察，不再次调用callee。

P-abnormal，path_type=callflow：原callee SEH/C++绕过post→既有native parent finally→先清本代listener children/masks、发Abnormal→原V1 parent Exit→原异常继续外层；观察callback故障包含且stickyUnknown、不修改原函数。E-native/E-source，F-lifetime。{fault_contract}

## 实际结果与冻结

{native_counts} lateAttach旧call{int(listener['lateAttachOldCalls'])}、null V1 parent旧call{int(listener['nullParentObserverOldCalls'])}，自有code writes{int(listener['fixtureCodeWrites'])}。额外旧broker CLR回调379/3仍单独回归，不能写为新listener CLR已通。机器状态只覆盖fixture的pre真实callee与post实际叶快照断言（含inc/mov预期变化），不扩大AVX/x87 data或prehelper每指令展开。

{failure_notes}

{freeze} 完整session {session} exit0；before/post/current声明输入精确一致，22包逐项核对。{diagnostics} 旧1021安装18项/其326源归档与原游戏三hash匹配，新包未装。

未实现/未验：{unimplemented}。新版本receipt、固定游戏六区publisher、实际CLRlistener/ledger、真实metadata/GC与动态delegate leaf、mutation/cancel/future关闭及全消费者退休另验。RequestPlay关闭，用户实测稍后，仅B03/B18a已勾。[下一步](validation/{batch.name}/next-integration.md)、[批次索引](validation/{batch.name}/README.md)。
"""

new_begin, new_end = "<!-- LISTENER-NATIVE-LATEST:BEGIN -->", "<!-- LISTENER-NATIVE-LATEST:END -->"
old_begin, old_end = "<!-- ADOPTED-BROKER-LATEST:BEGIN -->", "<!-- ADOPTED-BROKER-LATEST:END -->"


def block(relative):
    return f"""{new_begin}
- **最新listener原生层批：{result}、{source_count}源/22包冻结未安装。** [本轮证据]({relative})：自有native pre-tail/post-continuation {checks}项，helper共41exports（新增5）；原硬件返回与动态chain/replay按实际fixture验证，非游戏六区发布。
- **边界**：新listener native only，非CLRlistener/ledger；独立lane非聚合V2，TargetReadFaulted After可无Before，V1Ready144不授权六区；invokable不等于每个delegate leaf。未验分支保留。Runtime160/Hooks171/Visual71/UIinput79/AudioContent573；session {session} exit0，{diagnostics}
- **余项**：{limits} {state} 游戏仍1021安装18项/其326源归档。
{new_end}"""


def replace_latest(body, header, relative):
    assert body.count(header) == 1
    if new_begin in body:
        assert body.count(new_begin) == body.count(new_end) == 1
        return re.sub(re.escape(new_begin) + r".*?" + re.escape(new_end), lambda _: block(relative), body, count=1, flags=re.S)
    assert body.count(old_begin) == body.count(old_end) == 1
    old = re.search(re.escape(old_begin) + r"(.*?)" + re.escape(old_end), body, re.S).group(1).strip()
    old = old.replace("最新采用回调联合批", "历史采用回调联合批")
    replacement = block(relative) + "\n\n### 历史1515（采用回调联合批）\n\n" + old
    return re.sub(re.escape(old_begin) + r".*?" + re.escape(old_end), lambda _: replacement, body, count=1, flags=re.S)


status_path = repo / "docs/IMPLEMENTATION-STATUS.md"
checkpoint_path = repo / ".docs/CURRENT-CHECKPOINT.md"
checklist_path = repo / ".docs/foundation-expansion/CHECKLIST.md"
status = replace_latest(text(status_path), "## 已验证到哪里", f"validation/{batch.name}/README.md")
checkpoint = replace_latest(text(checkpoint_path), "## 当前现场", f"../docs/validation/{batch.name}/README.md")
before = text(checklist_path)
marks = re.findall(r"^- \[([ x])\] (B\d+[a-z]?)\b", before, re.M)
assert {task for mark, task in marks if mark == "x"} == {"B03", "B18a"}
checklist = replace_latest(before, "# Checklist: foundation-expansion", f"../../docs/validation/{batch.name}/README.md")


def status_distribution(match):
    cells = match.group(0).split("|")
    assert len(cells) == 5
    history = cells[2].strip().split("历史953批：", 1)[-1]
    cells[2] = f" 当前1515/1515、25套，{source_count}源/22暂存包冻结未安装；本批额外native listener{checks}项，41exports。游戏仍1021安装18项/其326源归档与原游戏三hash匹配。{diagnostics} 历史953批：{history} "
    return "|".join(cells)


status, count = re.subn(r"^\| B19 [^\n]+$", status_distribution, status, flags=re.M)
assert count == 1


def checklist_distribution(match):
    old = match.group(0).split("历史1266批记录：", 1)
    assert len(old) == 2
    return f"- [ ] B19 分发：当前1515/1515、25套，{source_count}源/22暂存包逐项冻结未安装；额外native listener{checks}项，41exports，before/post/current一致。游戏仍1021安装18项/其326源归档与原游戏三hash匹配。{diagnostics} 干净环境与全部能力矩阵仍缺。历史1266批记录：" + old[1]


checklist, count = re.subn(r"^- \[ \] B19 [^\n]+$", checklist_distribution, checklist, flags=re.M)
assert count == 1
assert re.findall(r"^- \[([ x])\] (B\d+[a-z]?)\b", checklist, re.M) == marks
outputs[status_path], outputs[checkpoint_path], outputs[checklist_path] = status, checkpoint, checklist
root_readme = repo / "README.md"
replacement = f"2026-10-24 更新：listener原生层批{result}、{source_count}源/22包冻结未安装；自有pre-tail/post动态chain/replay另列{checks}项，helper共41exports（新增5）。native only，非游戏六区发布或CLRlistener/ledger；V1Ready144不授权六区，invokable非全部delegate leaf。真实冷启动、listener/GC与全资源闭合仍缺，RequestPlay关闭，实测稍后，游戏仍1021。见[本轮证据](docs/validation/{batch.name}/README.md)、[实现状态](docs/IMPLEMENTATION-STATUS.md)与[续接点](.docs/CURRENT-CHECKPOINT.md)。"
root_text, count = re.subn(r"2026-10-24 更新：[^\n]+", lambda _: replacement, text(root_readme), count=1)
assert count == 1
outputs[root_readme] = root_text

allowed = {batch / "README.md", batch / "next-integration.md",
           repo / "docs/2026-10-24_底层-监听调用原生层-report.md",
           root_readme, status_path, checkpoint_path, checklist_path}
assert set(outputs) == allowed and len(outputs) == 7
assert not {path.relative_to(repo).as_posix() for path in outputs}.intersection(entry["path"] for entry in manifest["files"])
for path, body in outputs.items():
    path.write_text(body.replace("\r\n", "\n").replace("\r", "\n").strip() + "\n",
                    encoding="utf-8", newline="\n")
print(json.dumps(dict(published=True, batch=batch.name, fullExitCode=run["exitCode"], runnerSession=session,
    total=1515, projects=25, sourceInputs=source_count, packageEntries=22, listenerChecks=checks,
    added=added, changed=changed, removed=removed, installed=False, gameCodeExecuted=False,
    diagnostics=diagnostics_count, files=[path.relative_to(repo).as_posix() for path in outputs]), ensure_ascii=False))
