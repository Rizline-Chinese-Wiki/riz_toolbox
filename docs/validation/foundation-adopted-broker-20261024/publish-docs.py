"""Publish only this batch's documentation after the root builder freezes proof.

This script does not build, install, launch a process or write a build input.
Every prerequisite is checked before the first documentation write. The root
builder runs it after a real full-build terminal exit and archive verification.
All generated Markdown uses UTF-8 without BOM and LF.
"""
import hashlib
import json
import re
import zipfile
from pathlib import Path, PurePosixPath


batch = Path(__file__).resolve().parent
repo = batch.parents[2]
previous_batch = batch.parent / "foundation-clr-preinit-20261024"
assert batch.name == "foundation-adopted-broker-20261024"


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
changes = read("changes-from-clr-preinit.json")
callbacks = read("adopted-broker-evidence.json")
staging = read("staging-check.json")
integrity = read("source-integrity-check.json")
installed = read("previous-installation-check.json")
originals = read("original-game-check.json")

# A targeted result, a running build or an older batch must fail before writes.
assert run["exitCode"] == 0
assert run.get("installed", False) is False
assert not run.get("gameExecuted", False) and not run.get("gameCodeExecuted", False)
assert run.get("rootSoleBuilder", True) is True
assert summary["passed"] == summary["total"] == 1515
assert summary["projects"] == len(summary["suites"]) == 25
assert sum(row["passed"] for row in summary["suites"]) == 1515
assert all(row["passed"] == row["total"] for row in summary["suites"])
assert sha(batch / "build.log") == summary["logSha256"] == run["logSha256"]
for expected in [
    "Native runtime checks: 160/160 passed.",
    "171/171 exact-hook checks passed.",
    "71/71 visual checks passed.",
    "UI/input checks: 79/79 passed.",
    "573/573 audio/content checks passed.",
]:
    assert sum(row["summary"] == expected for row in summary["suites"]) == 1

# Exact before/after inputs, not just the same count or selected source hashes.
assert inputs_before["files"] == manifest["files"]
assert len(manifest["files"]) == 484
assert inputs_integrity["checked"] == 484
assert inputs_integrity["preBuildManifestMatchesArchivedManifest"] is True
assert inputs_integrity["currentChanged"] == []
assert sha(batch / "source-manifest.json") == source["manifestSha256"]
archive_check(source, manifest, repo, 484)
archive_check(package, package_manifest, repo / "artifacts/package", 22)
assert package["installed"] is False
assert integrity["checked"] == 484 and integrity["changed"] == []
assert staging["allMatched"] is True and staging["installed"] is False
assert len(staging["files"]) == 22
assert all(row["expected"] == row["actual"] for row in staging["files"])
assert changes["previous"] == previous_batch.name and changes["current"] == batch.name
assert changes["previousInputs"] == 482 and changes["currentInputs"] == 484
assert changes["bothFrozenArchivesVerified"] is True and changes["removed"] == []
assert 482 + len(changes["added"]) - len(changes["removed"]) == 484
assert len(changes["added"]) == 2
assert installed["batch"] == "foundation-local-protection-host-20261022"
assert installed["allMatched"] is True and len(installed["files"]) == 18
assert installed["archivedSourceInputsChecked"] == 326
assert all(row["expected"] == row["actual"] for row in installed["files"])
assert originals["allUnchanged"] is True and len(originals["files"]) == 3
assert all(row["expected"] == row["actual"] for row in originals["files"])

# Assert the copied evidence is the actual full-build child result.
assert summary["adoptedBrokerCallbacks"] == callbacks
assert callbacks["kind"] == "native-adopted-broker-callback-summary"
assert callbacks["passed"] is True and callbacks["failures"] == 0
assert callbacks["checks"] == 379 and callbacks["independentProcesses"] == 3
assert callbacks["nativeToClrCallbackVerified"] and callbacks["metadataIdentityControlled"]
assert not any(callbacks[key] for key in ["firstInstallReceiptUsed", "gameCodeExecuted", "gameRuntimeVerified", "installed"])
modes = {row["mode"]: row for row in callbacks["modes"]}
assert set(modes) == {"happy", "fault", "late-attach"} and len(callbacks["modes"]) == 3
expected_counts = {
    "happy": (123, 6, 6, 6, 4, 4, 4, 4, 8),
    "fault": (181, 6, 6, 6, 12, 4, 8, 12, 12),
    "late-attach": (75, 2, 1, 2, 2, 2, 2, 2, 2),
}
count_fields = ["checks", "originalNativeCalls", "observedNativeInvocations", "realNativeMidCalls",
                "captures", "mids", "afters", "contextDisposals", "probeEnters"]
for name, row in modes.items():
    assert tuple(row[key] for key in count_fields) == expected_counts[name]
    assert row["passed"] is True and row["failures"] == 0
    assert all(row[key] for key in ["nativeToClrCallbackVerified", "sharedProductionAdoptionCoreVerified",
        "productionGameFrontRejected", "metadataIdentityControlled", "fixtureOwnedOriginal",
        "sharedPhysicalTokenVerified", "bridgeKnown", "diskHashesUnchanged"])
    assert not any(row[key] for key in ["firstInstallReceiptUsed", "actualIl2CppMetadataResolved",
        "gameRuntimeVerified", "gameCodeExecuted", "firstInstallSafetyVerified", "actualLedgerCaptureVerified"])
    assert row["probeExits"] == row["probeEnters"]
    assert row["gateActive"] == row["gateParked"] == row["gateUnknown"] == 0
    assert row["helperSlot"] == 30 and row["epoch"] > 0
    assert row["bridgeFactoryCalls"] == 1 and row["brokerInstallCalls"] == 0
    assert row["brokerKnown"] is (name != "fault")
    assert row["managedObserverFaultVerified"] is (name == "fault")
    assert row["nativeOriginalExceptionsVerified"] is (name == "fault")
    assert row["oldInvocationDidNotInheritObserver"] is (name == "late-attach")
    assert all(file["beforeSha256"] == file["afterSha256"] for file in row["files"])
assert sum(row["checks"] for row in modes.values()) == 379
assert {row["mode"] for row in callbacks["processes"]} == set(modes)
assert len(callbacks["processes"]) == 3
native_results = batch / "native-build/adopted-broker-managed"
assert json.loads((native_results / "fixture-result.json").read_text(encoding="utf-8-sig")) == callbacks
for process in callbacks["processes"]:
    assert process["exitCode"] == 0
    assert sha(native_results / (process["mode"] + ".log")) == process["logSha256"]
child_inputs = json.loads((native_results / "source-file-check.json").read_text(encoding="utf-8-sig"))
assert child_inputs["allMatched"] is True and child_inputs["installed"] is False
assert child_inputs["gameCodeExecuted"] is False and len(child_inputs["files"]) == 5
assert all(row["beforeSha256"] == row["afterSha256"] for row in child_inputs["files"])

# Preserve the red targeted compile result; it was a two-line fixture fix.
red = batch / "targeted-first-compile-failure.log"
assert red.is_file()
red_text = text(red)
assert "CS0246" in red_text and "UnsupportedSignatureException" in red_text
assert "CS1061" in red_text and "WrongThread" in red_text
assert run["firstTargeted"]["exitCode"] == 1
assert run["firstTargeted"]["log"] == red.name
assert sha(red) == run["firstTargeted"]["logSha256"]
assert run["targeted"]["exitCode"] == 0
assert sha(batch / run["targeted"]["log"]) == run["targeted"]["logSha256"]
test_path = repo / "tests/RizToolbox.Runtime.Tests/NativeAdoptedBrokerRegression.cs"
assert "using RizToolbox.Runtime;" in text(test_path)
assert ".SuppressedWrongThread == 1" in text(test_path)

# Prepare every output before touching any entry document.
outputs = {}
source_count = source["entries"]
added, changed, removed = (len(changes[key]) for key in ["added", "changed", "removed"])
diagnostic_count = len(summary["compilerDiagnosticLines"])
session = run.get("runnerSession", run.get("sessionId", run.get("runnerSessionId")))
assert session is not None
result = "1515/1515、25套"
command = "pwsh -File scripts/Build.ps1 -NativeOutputDirectory D:/RizToolbox-build-cache/adopted-broker-final-20261024 -ValidationGameAssemblyPath D:/SteamLibrary/steamapps/common/Rizline/GameAssembly.dll"
limits = "真实固定游戏receipt的native→CLR回调、Doorstop冷启动顺序、实际IL2CPP metadata/listener/GC、未来来源与连续mutation/provider转交/取消、全部消费者资源退休及完整Retry/Return/Loader仍缺。RequestPlay关闭，Pending保留，InlineExecutionUnobserved=true、DispatchPathVerified=false。"
freeze = f"484源/22暂存包逐项冻结，较CLR启动采用批482源新增{added}/修改{changed}/删除{removed}。source ZIP SHA-256：{source['archiveSha256']}；package ZIP：{package['archiveSha256']}；完整日志：{summary['logSha256']}。"
diagnostics = f"完整构建保留{diagnostic_count}条去重编译诊断，原文见build-summary；不宣称零warning或干净环境B19复现。"
boundary = "共用生产配置通道采用与dispatch core已执行真实callbacks，原函数是自有fixture。它没有生产FirstInstall receipt，没有运行GameAssembly，不解析受控高位MethodInfo/Class指针，也没有真实ledger capture。不能称为固定游戏receipt已触发回调。"
mid_boundary = "Info56不含expectedMidReturnPc：adoption可读回slot/target/original/entry/epoch；mid来自发布者tuple、共享字典严格比对，实际回调再核ReturnPc。生产post-Verify失败撤本次binding/probe且置Unknown，只完成源码复核，未进行该生产分支的动态故障注入。"
failure = "首次定向session32771因测试缺using RizToolbox.Runtime、使用不存在的WrongThread字段而编译失败；root仅修为正确namespace与SuppressedWrongThread后，第二次定向session35938通过。红日志保留；这是测试编译修正，不是production运行时缺陷。"

outputs[batch / "README.md"] = f"""# 共用采用与原生回调联合验证

2026-10-24。**{result}完整通过，484源/22包冻结未安装；另列真实回调379项/3独立进程。** Runtime160、Hooks171、Visual71、UI/input79、AudioContent573。root唯一builder，最终session {session}真实终态exit0；379是额外fixture断言，未并入25套1515，不代表覆盖率。

{boundary}

## 实际通过的范围

| 模式 | checks / 原native / broker观察 | 真实结果 |
|---|---|---|
| happy | 123 / 6 / 6 | 两broker共一Entry与一次Attach；双sink同physical token、各自context，实际capture/Mid/After/Dispose；worker与foreign metadata仅scalar probe/fault；逻辑解绑、全部解绑、重绑后GC回调 |
| fault | 181 / 6 / 6 | managed After故障不改原返回；原SEH/C++的kind/code/payload不变，异常不发After；每种异常后同线程正常调用恢复context/probe；broker Unknown保留，native bridge Known idle |
| late-attach | 75 / 2 / 1 | 旧调用实际Enter后、Mid前暂停；Attach后经过真实native Mid/Exit，但没有新broker订阅/context/probe交付；随后新调用才交付双sink |

三个进程各bridge factory=1、broker Install/Prepare/Commit=0；slot30/epoch严格对应实际native通道，最终gate active/parked/unknown皆0、probe配平、输入文件hash不变。late-attach的零交付计数属于broker订阅/context/probe，没有单独计raw CLR delegate入口，不能声称其入口次数为0。测试只以weak reference查询bridge，GC不能由测试强根掩盖生产所有权；这仍不是游戏全run/GC退休证明。

生产front保留RequireReady、精确UnityTarget和同一Gate内前后VerifyReady；内部configured tuple不授予生产Ready，不向SDK公开。测试先要求生产front拒绝无receipt的fixture，再走与生产共用的采用core和dispatch；不另写替代broker。{mid_boundary}

{failure} {diagnostics}

{limits} 用户“实测稍后”保持，游戏仍旧1021安装18项/其326源归档，原游戏三hash不变。仅B03整项/B18a子项勾选，完整B01–B19 active；没有新安装、游戏/UI操作、游戏进程终止或commit/push。

## 冻结与复现

{freeze} build-inputs-before与source-manifest的484条路径及hash完全相同；源码/包ZIP、日志和runner冻结匹配。

仓库根PowerShell7：

~~~powershell
{command}
~~~

实际映像验证需显式ValidationGameAssemblyPath。生产publisher只在隔离映像fixture验证；DONT_RESOLVE不执行游戏代码，不用于生产启动。完整新缓存构建并非全新机器/干净依赖复现。

[完整日志](build.log)、[runner终态](build-run.json)、[汇总](build-summary.json)、[回调JSON](adopted-broker-evidence.json)、[三个进程与hash](native-build/adopted-broker-managed/fixture-result.json)、[构建前输入](build-inputs-before.json)、[源码冻结](source-archive-check.json)、[包冻结](package-archive-check.json)、[差异](changes-from-clr-preinit.json)、[首编译失败](targeted-first-compile-failure.log)、[独立复核](review.md)。

[范围](scope.md)、[下一步](next-integration.md)、[listener设计](native-listener-design.md)、[正式报告](../../2026-10-24_底层-采用回调联合-report.md)。
"""

outputs[batch / "next-integration.md"] = f"""# 下一阶段：真实listener与固定游戏启动

前提：本批{result}、484源/22包冻结未安装，额外379项自有fixture回调通过。{boundary} 本文是计划，用户“实测稍后”保持，不能作为已运行证据。

## 1. 实施三处listener的六区运输

按[native-listener-design](native-listener-design.md)，两direct CALL保source+5，virtual六字节用90 E8保source+6；pre完整保存后尾跳真实callee，post E9进入动态stub、真实CALL观察helper并完整replay原continuation。post没有原callee的新CALL栈顶，必须以原frame chained unwind与真实RtlVirtualUnwind逐边界实证；禁止普通C++再CALL伪装透明。

在自有原生函数先核真实_ReturnAddress、所有原参数/flags、原调用与replay一次、normal/SEH/C++、递归/worker、旧调用跨Attach、溢出遮蔽、子代token和父finally清理。V2 observer一次冻结及V2 receipt明确六区责任；旧Ready144三写不能授权新六区。完成后再以固定映像隔离fixture核bytes/页保护/function table，不执行游戏。

## 2. 固定游戏receipt到真实回调与冷启动

生产RequireReady/UnityTarget/Gate复核继续保留；{mid_boundary} 先补可审的实际metadata/父调用/receipt联合证据，再在获准游戏实测时验证Doorstop→producer三写Ready→原init一次与真实int/原异常→CoreCLR采用→首合法tick/UnityEvent的同会话顺序。本批自有callback及上一批双idle sink采用均不能代替这条固定游戏执行链。

## 3. listener/future及全部消费者退休

将真实executing-list的三条调用关联当前parent/child与同代ledger，闭合动态leaf/provider、异步转交、连续mutation与取消；未证明来源保持opaque/Pending。全部未来来源结束后，组合audio/图roots/globals/menu/result/settings逐owner证据，才执行VerifyConsumersEnded→Close→bridge/bundle/reservation/handler/coordinator退休，最后同revision重做Retry/Return/Loader。

每种normal/失败/取消/provider撤回与多轮重开核原调用一次、Unknown不释放、条件式基线恢复、资源计数不单调增长。完整B01–B19标准不缩小，RequestPlay关闭，仅B03/B18a勾选。旧1021/18/326与原游戏文件保持原证据边界。
"""

evidence_rows = []
for evidence_id, name, reproduce in [
    ("E-build", "build.log", command),
    ("E-run", "build-run.json", "读取root真实终态；publisher不启动构建"),
    ("E-callback", "adopted-broker-evidence.json", "同完整命令，三个独立CLR child"),
    ("E-inputs", "build-inputs-before.json", "与source-manifest逐路径/hash精确比对"),
    ("E-source", "source-archive-check.json", "逐项核source-inputs.zip与当前484源"),
    ("E-package", "package-archive-check.json", "逐项核staged-package.zip与当前22暂存文件"),
    ("E-red", "targeted-first-compile-failure.log", "只读保留红日志，不重写首次失败"),
    ("E-review", "review.md", "只读生产front/core/fixture及Info56的有限复核"),
]:
    evidence_rows.append(f"| {evidence_id} | [{name}](validation/{batch.name}/{name}) | {reproduce} | {sha(batch / name)} |")
outputs[repo / "docs/2026-10-24_底层-采用回调联合-report.md"] = f"""# 共用生产采用核心与自有原生回调联合报告

2026-10-24，flavor=null，兼容框架工程验证。**{result}完整构建通过，484源/22包冻结未安装；额外native→CLR回调379项/3进程。** {boundary} {limits}

按[scope](validation/{batch.name}/scope.md)，三个代理分别实现采用core与托管联合、原生pre-Mid门、独立只读复核；root唯一builder。{failure}

## Evidence → Finding → Path

source_type为file/log，observed_at为各JSON时间及root终态确认时刻；下表content_hash是链接文件本身，不混用ZIP hash。复现命令从仓库根执行；本publisher只读冻结证据和写文档。

| E-id | source_ref / artifact_path | repro_command或离线限制 | content_hash SHA-256 |
|---|---|---|---|
{chr(10).join(evidence_rows)}

| F-id | category / severity | evidence_ids | status / confidence / location |
|---|---|---|---|
| F-shared | design / n/a_re | E-callback,E-source,E-review | validated/high，生产同configured-slot采用/dispatch core；双sink同静态Entry、全identity字典同Gate，actual bridge factory1、broker安装0 |
| F-callback | other / n/a_re | E-callback,E-build,E-source | validated/high，自有原函数实际Enter/Mid/Exit到CLR；独立context共physical token且拒绝peer，worker/foreign仅scalar；正常解绑与重绑后GC回调 |
| F-snapshot | design / n/a_re | E-callback,E-review,E-source | validated/high，native Enter后/Mid前真实门暂停，旧call跨Attach后无新broker订阅/context/probe交付；原/Mid2但broker1；不单计raw CLR delegate入口 |
| F-fault | other / n/a_re | E-callback,E-review,E-source | validated/high，After故障不改原返回；原SEH/C++ kind/code/payload保留；随后同线程正常call恢复配对，broker Unknown不被洗白 |
| F-front | design / n/a_re | E-source,E-review | source-reviewed/high，RequireReady/UnityTarget/Gate内前后VerifyReady未弱化；postVerify失败撤logical binding/probe仅静态复核，未生产故障注入 |
| F-mid-limit | design / n/a_re | E-review,E-source | accepted boundary/high，Info56不读回expectedMidReturnPc；producer tuple与共享字典、实际callback ReturnPc核对分层，不能扩大ABI保证 |
| F-red | other / n/a_re | E-red,E-build,E-source | validated/high，仅测试namespace和SDK字段名两行编译修正；首次失败保留，不是production bug |
| F-limits | design / n/a_re | E-callback,E-run,E-review | accepted boundary/high，无fixed-game receipt callbacks/真实metadata/ledger/game code/coldboot；全底层尚未验收 |

P-callback，path_type=callflow：自有MASM目标与真实caller标签→fixture侧一次Dobby Prepare→native Bootstrap Configure与真正Mid E8→Commit→共享生产configured adoption core一次Attach→双sink capture→真实native原函数/Mid→After/Dispose与physical probe Exit→native/gate计数核对。E-callback/E-source，F-shared/F-callback。kind4/slot30与固定游戏producer slot31分开；受控MethodInfo/Class/self仅作为标量身份。

P-old-call，path_type=callflow：未Attach的实际native Enter→pre-Mid门暂停，核parent在途且Mid/body未进→双sink采用→GC→finally释放→旧call真实native Mid/Exit且无新broker订阅/context/probe交付→新call才正常交付。E-callback/E-review，F-snapshot。Enter固定descriptor是源码合同，本轮零交付不是raw CLR delegate入口次数0的测量；原始Mid入口即使错误进入，也可能被core因无physicalTop而忽略。外层child60s cap、native门10s与managed finally resume有界；正常用例没有证明超时分支实际执行。

P-unwind，path_type=callflow：managed After受控故障被包含→原返回保持且释放两context→broker stickyUnknown→原native SEH/C++从自有body真实抛出→父native finally与managed Exit清理→异常kind/code/payload外层原样→每种异常后正常call恢复配对。E-callback/E-review，F-fault。异常状态不因后继正常调用或新sink重回Known，native channel仍保有永久责任。

## 实际结果与冻结

happy123、fault181、late-attach75，总379，三个child各exit0。happy原native6/broker6、capture/Mid/After/Dispose各4及probe8/8；fault原native6、capture/Dispose12、After8、probe12/12且brokerUnknown/nativeKnown；late原native2/broker1，旧call无新broker订阅/context/probe交付，新call向两个sink分别交付一次，capture/Mid/After/Dispose合计各2、probe2/2。三者gateActive/Parked/Unknown皆0、factory1/install0、输入hash不变。

{freeze} build-inputs-before与source-manifest完全一致。最终full session {session} exit0，Runtime160/Hooks171/Visual71/UIinput79/AudioContent573。{diagnostics} 保留首次定向失败，完整full没有被定向结果替代；新缓存完整构建不是干净环境B19全矩阵。

当前新包未装，旧1021安装18项/其326源归档与原游戏三hash匹配。{mid_boundary} 三listener六区只形成[具体设计](validation/{batch.name}/native-listener-design.md)，未实现/发布。真实receipt回调、冷启动、实际IL2CPP/listener/GC、未来来源和全部消费者退休仍缺，实测稍后；仅B03/B18a已勾，RequestPlay关闭。

[批次索引](validation/{batch.name}/README.md)、[下一步](validation/{batch.name}/next-integration.md)、[独立复核](validation/{batch.name}/review.md)。
"""

new_begin, new_end = "<!-- ADOPTED-BROKER-LATEST:BEGIN -->", "<!-- ADOPTED-BROKER-LATEST:END -->"
old_begin, old_end = "<!-- CLR-PREINIT-LATEST:BEGIN -->", "<!-- CLR-PREINIT-LATEST:END -->"


def latest_block(relative):
    return f"""{new_begin}
- **最新采用回调联合批：{result}、484源/22包冻结未安装。** [本轮证据]({relative})：同生产采用/dispatch core在自有native fixture真实回调379项/3进程，factory1、broker安装0；不是固定游戏receipt callbacks，metadata受控。
- **实证与边界**：happy123/fault181/late75；双sink/token、旧call跨Attach无新broker交付、原SEH/C++后恢复、stickyUnknown和重绑后GC仍可回调已验。未单计raw CLR入口；Info56不读回mid，生产postVerify失败仅源码复核；Runtime160/Hooks171/Visual71/UIinput79/AudioContent573。最终session {session} exit0；{diagnostics}
- **余项**：{limits} 实测稍后，游戏仍1021安装18项/其326源归档；完整B01–B19 active，仅B03整项/B18a子项勾选。
{new_end}"""


def replace_latest(body, header, relative):
    assert body.count(header) == 1
    block = latest_block(relative)
    if new_begin in body:
        assert body.count(new_begin) == body.count(new_end) == 1
        return re.sub(re.escape(new_begin) + r".*?" + re.escape(new_end), lambda _: block, body, count=1, flags=re.S)
    assert body.count(old_begin) == body.count(old_end) == 1
    previous = re.search(re.escape(old_begin) + r"(.*?)" + re.escape(old_end), body, re.S).group(1).strip()
    previous = previous.replace("最新CLR启动采用批", "历史CLR启动采用批")
    replacement = block + "\n\n### 历史1515（CLR启动采用批）\n\n" + previous
    return re.sub(re.escape(old_begin) + r".*?" + re.escape(old_end), lambda _: replacement, body, count=1, flags=re.S)


def alter_row(body, pattern, transform):
    body, count = re.subn(pattern, lambda match: transform(match.group(0)), body, flags=re.M)
    assert count == 1, pattern
    return body


def counts_row(line, task):
    if task == "B02":
        return re.sub(r"(?:最新|当前离线)?Runtime\d+", "当前离线Runtime160", line, count=1)
    if task == "B07":
        return re.sub(r"(?:最新源码|最新|当前离线)?Hooks\d+", "当前离线Hooks171", line, count=1)
    if task == "B13":
        return re.sub(r"(?:当前离线)?Visual\d+", "当前离线Visual71", line, count=1)
    if task == "B14":
        return re.sub(r"(?:当前离线)?UI/input\d+", "当前离线UI/input79", line, count=1)
    if task == "B15":
        return re.sub(r"(?:最新|当前离线)?AudioContent\d+", "当前离线AudioContent573", line, count=1)
    assert task == "B12"
    if "当前离线Visual71/Runtime160" not in line:
        line = line.replace("Visual68及Runtime57", "当前离线Visual71/Runtime160；历史Visual68及Runtime57", 1)
    return line


status_path = repo / "docs/IMPLEMENTATION-STATUS.md"
checkpoint_path = repo / ".docs/CURRENT-CHECKPOINT.md"
checklist_path = repo / ".docs/foundation-expansion/CHECKLIST.md"
status = replace_latest(text(status_path), "## 已验证到哪里", f"validation/{batch.name}/README.md")
checkpoint = replace_latest(text(checkpoint_path), "## 当前现场", f"../docs/validation/{batch.name}/README.md")
checklist_before = text(checklist_path)
marks = re.findall(r"^- \[([ x])\] (B\d+[a-z]?)\b", checklist_before, re.M)
assert {task for mark, task in marks if mark == "x"} == {"B03", "B18a"}
checklist = replace_latest(checklist_before, "# Checklist: foundation-expansion", f"../../docs/validation/{batch.name}/README.md")
for task in ["B02", "B07", "B12", "B13", "B14", "B15"]:
    status = alter_row(status, rf"^\| {task} [^\n]+$", lambda line, task=task: counts_row(line, task))
    checklist = alter_row(checklist, rf"^- \[[ x]\] {task} [^\n]+$", lambda line, task=task: counts_row(line, task))


def status_distribution(line):
    cells = line.split("|")
    assert len(cells) == 5
    old = cells[2].strip()
    history = old.split("历史953批：", 1)[1] if "历史953批：" in old else old
    cells[2] = f" 当前1515/1515、25套，484源/22暂存包冻结未安装，构建前后源码完全一致；游戏仍1021安装18项/其326源归档与原游戏三hash匹配。{diagnostics} 历史953批：{history} "
    return "|".join(cells)


def checklist_distribution(line):
    prefix = "- [ ] B19 分发："
    assert line.startswith(prefix)
    old = line[len(prefix):]
    history = old.split("历史1266批记录：", 1)[1] if "历史1266批记录：" in old else old.replace("最新1266/1266", "1266/1266", 1).replace("最新包完整复现", "当时包完整复现", 1)
    return prefix + f"当前1515/1515、25套，484源输入/22暂存包ZIP逐项冻结未安装，构建前后manifest完全一致；游戏仍1021安装18项/其326源归档与原游戏三hash匹配。{diagnostics} 当前包完整复现/全新系统、真实断电/磁盘故障与全部能力矩阵仍缺。历史1266批记录：" + history


status = alter_row(status, r"^\| B19 [^\n]+$", status_distribution)
checklist = alter_row(checklist, r"^- \[ \] B19 [^\n]+$", checklist_distribution)
assert re.findall(r"^- \[([ x])\] (B\d+[a-z]?)\b", checklist, re.M) == marks
status_tasks = re.findall(r"^\| (?:\*\*)?(B\d{2}) ", status, re.M)
assert len(status_tasks) == 19 and set(status_tasks) == {f"B{index:02d}" for index in range(1, 20)}
outputs[status_path] = status
outputs[checkpoint_path] = checkpoint
outputs[checklist_path] = checklist
root_readme = repo / "README.md"
replacement = f"2026-10-24 更新：采用回调联合批{result}、484源/22包冻结未安装；同生产采用/dispatch core在自有native fixture真实callbacks379项/3进程，非固定游戏receipt callbacks，metadata受控。Runtime160/Hooks171/Visual71/UIinput79/AudioContent573；真实冷启动、listener/GC与全资源闭合仍缺，RequestPlay关闭，实测稍后，游戏仍1021。见[本轮证据](docs/validation/{batch.name}/README.md)、[实现状态](docs/IMPLEMENTATION-STATUS.md)与[续接点](.docs/CURRENT-CHECKPOINT.md)。"
readme, count = re.subn(r"2026-10-24 更新：[^\n]+", lambda _: replacement, text(root_readme), count=1)
assert count == 1
outputs[root_readme] = readme

# All validation and transformations succeeded. Only these seven docs may change.
allowed = {
    batch / "README.md", batch / "next-integration.md",
    repo / "docs/2026-10-24_底层-采用回调联合-report.md",
    root_readme, status_path, checkpoint_path, checklist_path,
}
assert set(outputs) == allowed and len(outputs) == 7
assert not {path.relative_to(repo).as_posix() for path in outputs}.intersection(entry["path"] for entry in manifest["files"])
for path, body in outputs.items():
    body = body.replace("\r\n", "\n").replace("\r", "\n").strip() + "\n"
    path.write_text(body, encoding="utf-8", newline="\n")

print(json.dumps(dict(published=True, batch=batch.name, fullExitCode=run["exitCode"], runnerSession=session,
    total=1515, projects=25, sourceInputs=484, packageEntries=22, callbackChecks=379, callbackProcesses=3,
    added=added, changed=changed, removed=removed, installed=False, gameCodeExecuted=False,
    diagnostics=diagnostic_count, files=[path.relative_to(repo).as_posix() for path in outputs]), ensure_ascii=False))
