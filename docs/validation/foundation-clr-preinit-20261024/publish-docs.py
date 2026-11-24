"""Publish this CLR-adoption batch only after the root builder froze evidence.

This script edits documentation only. It neither builds nor installs anything,
and every write explicitly uses LF. Run it from any directory after all required
JSON, log and archive evidence has been produced by the root builder.
"""
import hashlib
import json
import re
from pathlib import Path


batch = Path(__file__).resolve().parent
repo = batch.parents[2]


def read(name):
    return json.loads((batch / name).read_text(encoding="utf-8-sig"))


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def write(path, body):
    body = body.replace("\r\n", "\n").replace("\r", "\n")
    path.write_text(body.strip() + "\n", encoding="utf-8", newline="\n")


summary = read("build-summary.json")
source = read("source-archive-check.json")
package = read("package-archive-check.json")
changes = read("changes-from-1515.json")
run = read("build-run.json")
managed = read("managed-adoption-evidence.json")
unknown = read("atomic-unknown-evidence.json")
init_call = read("init-call-evidence.json")

# Refuse to publish a plan, a targeted pass or an earlier batch as this full.
assert summary["passed"] == summary["total"] == 1515
assert summary["projects"] == len(summary["suites"]) == 25
assert sum(row["passed"] for row in summary["suites"]) == 1515
assert all(row["passed"] == row["total"] for row in summary["suites"])
assert run["exitCode"] == 0
assert run.get("installed", False) is False
assert source["entries"] > 474 and source["allEntryHashesMatched"]
assert package["entries"] == 22 and package["allEntryHashesMatched"]
assert package["installed"] is False
assert changes["previousInputs"] == 474
assert changes["currentInputs"] == source["entries"]
assert changes["bothFrozenArchivesVerified"]
assert not changes["removed"]
assert source["entries"] == 474 + len(changes["added"]) - len(changes["removed"])
assert sha(batch / "build.log") == summary["logSha256"]
if "logSha256" in run:
    assert run["logSha256"] == summary["logSha256"]
assert sha(batch / source["archive"]) == source["archiveSha256"]
assert sha(batch / "source-manifest.json") == source["manifestSha256"]
assert sha(batch / package["archive"]) == package["archiveSha256"]
assert read("staging-check.json")["allMatched"]
assert read("previous-installation-check.json")["allMatched"]
assert read("original-game-check.json")["allUnchanged"]

assert managed["passed"] and managed["failures"] == 0
assert managed["independentProcesses"] == 2
assert not managed["gameRuntimeVerified"] and not managed["firstInstallSafetyVerified"]
modes = {row["mode"]: row for row in managed["modes"]}
assert set(modes) == {"happy", "reentry"}
for row in modes.values():
    assert row["passed"] and row["failures"] == 0 and row["checks"] > 0
    assert all(row[key] for key in ["noProducerRejected", "realNativeProducerReady", "productionRequireReadyVerified", "metadataIdentityControlled", "mappedImageOnly", "diskHashesUnchanged"])
    assert not any(row[key] for key in ["actualIl2CppMetadataResolved", "gameCodeExecuted", "gameInitExecuted", "nativeToClrCallbackVerified", "actualLedgerCaptureVerified", "firstInstallSafetyVerified", "gameRuntimeVerified"])
    assert row["transportInvocations"] == row["managedObserverCallbacks"] == 0
    assert row["writesStarted"] == 3 and row["voidSlot"] == 31
assert all(modes["happy"][key] for key in ["productionRuntimeAdoptVerified", "productionBrokerAdoptVerified", "duplicateRuntimeRejected", "wrongTargetRejected", "conflictingMetadataRejected", "doubleSinkSharedEntry", "logicalDetachPreservedEntry"])
assert modes["reentry"]["originalReentryRejected"]
assert not modes["reentry"]["productionRuntimeAdoptVerified"]
assert not modes["reentry"]["productionBrokerAdoptVerified"]

assert unknown["passed"] and unknown["failures"] == 0 and unknown["checks"] > 0
assert all(unknown[key] for key in ["unknownAtomicPrecedesEveryRead", "detailWriterPausedDuringReads", "productionFirstInstallSourceExecuted", "fixtureCompileTimeSeam", "nativeTransportsFromRealHelper", "mappedImageOnly"])
assert unknown["readsWhileWriterPaused"] == unknown["observedUnknownSnapshots"] == 100
assert unknown["readySnapshotPhase"] == 3 and unknown["readySnapshotUnknown"] == 0
assert unknown["writesStarted"] == unknown["currentWritesStarted"] == 3
assert unknown["reentryStatus"] == 2 and unknown["currentPhase"] == 4 and unknown["currentUnknown"] == 1
assert unknown["transportInvocations"] == 0
assert not any(unknown[key] for key in ["gameCodeExecuted", "gameInitExecuted", "firstInstallSafetyVerified", "gameRuntimeVerified"])

assert init_call["passed"] and init_call["failures"] == 0 and init_call["checks"] > 0
assert init_call["independentProcesses"] == len(init_call["cases"]) == 16
assert init_call["normalCases"] == 12 and init_call["realFailFastCases"] == 4
assert init_call["productionSourceExecuted"] and init_call["werNoUi"]
assert not any(init_call[key] for key in ["actualProducerExecuted", "coreClrExecuted", "gameCodeExecuted", "coldStartupVerified"])
assert all(row["passed"] for row in init_call["cases"])
assert all(row["realExitCode"] == 3221227010 for row in init_call["cases"] if "realExitCode" in row)
seh = next(row for row in init_call["cases"] if row["mode"] == "ready-original-seh")
assert seh["raiseExceptionSourceFlags"] == 0
assert seh["sehFirstChanceCount"] == 1
assert seh["sehFirstChanceFlags"] == seh["sehCaughtFlags"] == 128
assert seh["sehFirstChanceCode"] == seh["exceptionCode"]
assert seh["sehFirstChanceParameters"] == seh["sehCaughtParameters"] == 3
assert seh["sehFirstChancePayload"] == seh["sehCaughtPayload"]

# Check the actual task completion marks before touching any index.
checklist_path = repo / ".docs/foundation-expansion/CHECKLIST.md"
checklist_before = checklist_path.read_text(encoding="utf-8-sig")
assert set(re.findall(r"^- \[x\] (B\d+[a-z]?)\b", checklist_before, re.M)) == {"B03", "B18a"}

source_count = source["entries"]
added, changed, removed = (len(changes[key]) for key in ["added", "changed", "removed"])
diagnostic_count = len(summary["compilerDiagnosticLines"])
session = run.get("runnerSession", run.get("sessionId", run.get("runnerSessionId", "见runner记录")))
result = "1515/1515、25套"
managed_checks = int(managed["checks"])
unknown_checks = int(unknown["checks"])
init_checks = int(init_call["checks"])
limits = "真实Doorstop冷启动顺序、生产native→CLR观察回调、实际IL2CPP元数据/listener/GC尚未验收；连续mutation/provider转交与取消、未来来源、全部消费者资源退休及完整Retry/Return/Loader仍缺。RequestPlay关闭，Pending保留，InlineExecutionUnobserved=true、DispatchPathVerified=false。"
freeze = f"{source_count}项源码/22项暂存包已逐项冻结，较producer1515批新增{added}/修改{changed}/删除{removed}。source ZIP `{source['archiveSha256']}`；package ZIP `{package['archiveSha256']}`；完整日志SHA `{summary['logSha256']}`。"
diagnostics = f"本次完整构建保留{diagnostic_count}条去重编译诊断，原文见汇总；不宣称零warning或干净环境B19复现。"
seam = "Unknown确定交错使用真实FirstInstall.cpp编入独立EXE，编译期宏只在该fixture暂停atomic写后/详情锁前；生产helper编译不定义宏，无运行时测试mode。没有测试export只是辅助检查，仍须结合生产编译参数与源码清单。"
command = "scripts/Build.ps1 -NativeOutputDirectory D:/RizToolbox-build-cache/clr-preinit-final-20261024 -ValidationGameAssemblyPath D:/SteamLibrary/steamapps/common/Rizline/GameAssembly.dll"

write(batch / "README.md", f"""# CLR实际采用、Unknown快照与初始化调用合同

2026-10-24。**{result}完整通过，{source_count}源/22包冻结，未安装。** 本批补上真实producer到生产CLR adopter的隔离进程接入，确定修复Unknown发布滞后，并执行生产Doorstop C初始化函数。测试总数与上一producer批同为1515，标题和证据目录区分这两个批次；不以总数代表覆盖率。

## 实际通过的范围

| 验证 | 已运行的具体路径 | 证明边界 |
|---|---|---|
| 实际CLR采用 | {managed_checks}检查、happy/reentry两个独立dotnet进程；真实producer→RequireReady→RuntimeAdopt→broker双sink共享entry；单方/全部解绑后采用、重复/错误target/冲突metadata拒绝 | 两sink均idle，transport调用与managed callbacks全为0；metadata身份受控，不解析真实IL2CPP |
| atomic Unknown | {unknown_checks}检查；writer实际设unknown=1后暂停，reader在其更新详情前连续100次得到Unknown4/1；三写与原身份不变 | 确定验证发布滞后因果，不承诺返回之后的未来状态；不执行游戏函数 |
| 生产init C函数 | {init_checks}检查、16独立进程：12正常/原异常/bootstrap异常，4真实failfast；原指针/int/一次调用及0返回不启CLR | ready与original/bootstrap受控；执行同生产C源，未实际运行producer/CoreCLR或游戏冷启动 |

实际CLR用DONT_RESOLVE_DLL_REFERENCES映射固定GameAssembly，只执行helper生产publisher和生产managed adopter。RequireReady与RuntimeAdopt、双sink broker采用是真的，**没有游戏native→CLR回调**；MethodInfo/Class是受控、不解引用的身份值。没有二次Dobby安装，采用前后代码字节不变。另一独立模式真实重入producer后要求Unknown且拒绝生产adopter。

{seam}

首次完整构建因SEH测试把ExceptionFlags硬编码为0而失败。被动first-chance诊断和外层捕获实际均为128，source RaiseException flags仍为0；现在比较实际first-chance与捕获的code/flags/参数/payload。**只修正测试假设，未修改生产异常路径。** [首轮失败](build-first-seh-assertion-failure.log)与[诊断](init-seh-diagnostic.log)保留，最终full session {session} exit0。

{limits} 实测稍后；游戏仍旧1021包。

## 构建、冻结与复现

{freeze} {diagnostics} 旧1021安装18项、其326项归档与原游戏三文件hash核对不变；本轮没有安装、游戏/UI操作、游戏进程终止、原文件修改或commit/push。

仓库根PowerShell7：`{command}`。实际映像联合验证需要显式ValidationGameAssemblyPath，不能将省略该参数的构建称为本批验证。缓存工具链依赖与干净环境复现另验。

[完整日志](build.log)、[汇总](build-summary.json)、[runner终态](build-run.json)、[实际CLR采用](managed-adoption-evidence.json)、[atomic Unknown](atomic-unknown-evidence.json)、[init C函数](init-call-evidence.json)、[源码归档](source-archive-check.json)、[包归档](package-archive-check.json)、[与producer1515差异](changes-from-1515.json)、[有限复核](review.md)。

仅B03整项/B18a子项勾选，[完整B01–B19](../../../.docs/foundation-expansion/SPEC.md)继续active。[授权](scope.md)、[下一步](next-integration.md)、[正式报告](../../2026-10-24_底层-CLR启动采用-report.md)。
""")

write(batch / "next-integration.md", f"""# 下一阶段：真实回调与全run闭合

前提：本批{result}、{source_count}源/22包已冻结未安装。actualCLR已完成RequireReady/RuntimeAdopt与双idle sinks采用；零游戏和native callback的边界保持。用户“实测稍后”继续有效，本计划不能作为通过证据。

## 1. 将采用接到真实回调证据

复用真实Dobby/MASM/helper联合fixture，把生产adopter与broker实际native→CLR Enter/Mid/Exit、两个sink、逻辑解绑/异常和ledger同代identity串起来；metadata/图若仍受控须继续标明，不能替代真实IL2CPP与GC。核旧调用跨Attach不借新callback、原函数一次、Unknown保留责任、probe snapshot配对以及全部解绑不伪造physical drain。

## 2. 固定游戏冷启动合同

获准实测时才安装同一冻结包，记录Doorstop→真实producer三写Ready→original init一次/int结果或原异常→CoreCLR→RequireReady/Attach→首合法tick/UnityEvent的同会话顺序。当前受控ready的16进程C函数验证不证明这个实际顺序；DONT_RESOLVE映射也不是生产启动模式。init0、不支持映像、重入/Unknown与正常启动的透明行为须分别验收。

## 3. listener/future与全部消费者退休

继续实际executing-list的三条listener callsite，闭合动态leaf/provider、异步转交、连续mutation与取消。未证明的来源保持opaque/Pending；只在全部未来来源结束后组合audio/图roots/globals/menu/result/settings等逐owner证据，执行VerifyConsumersEnded→Close→bridge/bundle/reservation/handler/coordinator退休，再验证同revision的Retry/Return/Loader。

正常/失败/取消/provider撤回与多轮重开都需检查原调用一次、Unknown不释放、条件式基线恢复以及资源计数不单调增长。完整标准未变，RequestPlay仍关闭，仅B03/B18a可勾。热路径已确定不会每次重做SHA或遍历pdata；固定VirtualQuery/锁开销需独立测量，不能凭估计优化，也不能称隔离映像计时为游戏性能基线。
""")

report = repo / "docs/2026-10-24_底层-CLR启动采用-report.md"
evidence_rows = []
for evidence_id, name, reproduce in [
    ("E-build", "build.log", command),
    ("E-managed", "managed-adoption-evidence.json", "同完整命令，Test-FirstInstallManaged的happy/reentry独立进程"),
    ("E-unknown", "atomic-unknown-evidence.json", "同完整命令，Test-FirstInstallUnknownNative编译真实producer源"),
    ("E-init", "init-call-evidence.json", "同完整命令，Test-DoorstopInitNative的16独立进程"),
    ("E-failed", "build-first-seh-assertion-failure.log", "只读取保留红日志；最终构建不覆盖它"),
    ("E-diagnostic", "init-seh-diagnostic.log", "只读取保留SEH诊断日志"),
    ("E-source", "source-archive-check.json", "读取冻结记录并逐项核source-inputs.zip"),
    ("E-package", "package-archive-check.json", "读取冻结记录并逐项核staged-package.zip"),
]:
    assert (batch / name).is_file()
    evidence_rows.append(f"| {evidence_id} | [{name}](validation/{batch.name}/{name}) | {reproduce} | `{sha(batch / name)}` |")
write(report, f"""# CLR启动采用与初始化调用合同验证

2026-10-24，flavor=null，兼容框架工程验证。**{result}、{source_count}源/22包冻结，未安装。** 本轮实际执行生产RequireReady、RuntimeAdopt与双idle sink broker采用；修复atomic Unknown详情滞后并验证100次确定交错；同生产C初始化函数在16个受控进程保留调用/返回/原异常。{limits}

按[scope](validation/{batch.name}/scope.md)，三代理协作，root唯一builder。GameAssembly只映射、不运行游戏；真实冷启动和完整B01–B19尚未验收。

## Evidence → Finding → Path

source_type为file/log，observed_at为各JSON记录时间及root确认终态时刻。下表content_hash对应链接文件本身；源码/包ZIP另见后面的archiveSha256，避免把归档hash误标成JSON文件hash。

| E-id | source_ref | repro_command或离线限制（仓库根） | content_hash SHA-256 |
|---|---|---|---|
{chr(10).join(evidence_rows)}

| F-id | category / severity | evidence_ids | status / confidence / location |
|---|---|---|---|
| F-managed | other / n/a_re | E-managed,E-build,E-source | validated/high，生产adopter在两个真实CLR子进程接实际publisher；双idle sinks共享entry、解绑/拒绝；零native callback与受控metadata |
| F-unknown | design / n/a_re | E-unknown,E-build,E-source | validated/high，writer已atomic失效且详情锁尚未更新，100读全Unknown；identity/三写不变，独立EXE compile-time seam |
| F-init | other / n/a_re | E-init,E-build,E-source | validated/high，生产C原init调用函数16进程；original一次、真实int/异常、0返回禁bootstrap、实际failfast；ready受控 |
| F-seh-test | design / n/a_re | E-failed,E-diagnostic,E-init | validated/high，仅修测试Flags0假设；first-chance={seh['sehFirstChanceFlags']}、outer={seh['sehCaughtFlags']}，实际code/flags/参数/payload保持；生产异常路径不变 |
| F-limit | design / n/a_re | E-managed,E-unknown,E-init | accepted boundary/high，均未运行实际game init/CoreCLR冷启动/IL2CPP metadata/listener/GC；不能扩大为底层完整验收 |

P-adopt，path_type=callflow：隔离CLR进程DONT_RESOLVE映射固定GameAssembly→真实producer发布三处并Ready→生产RequireReady核实际身份→runtime一次Attach→broker采用现slot31并共享双sink→逻辑解绑/再绑定与错误身份拒绝；E-managed/F-managed。该路径native原函数、观察回调和ledger捕获计数都是0，不是游戏事件执行证明。

P-unknown，path_type=callflow：同生产FirstInstall.cpp编入独立EXE→实际固定映像三写Ready→worker真实重入→atomic unknown=1后在详情锁前暂停→主线程等待后进行100次读→最终snapshot同时核atomic返回4/1→释放writer→重入2/三写不变；E-unknown/F-unknown。{seam}

P-init，path_type=callflow：受控ready/disabled→共享生产C一次性CAS→真实native original(domain指针)一次→normal int0跳过bootstrap、normal nonzero且ready才bootstrap→int原样返回；原SEH/C++不被包装函数捕获。递归/重调/invalid-ready在第二次original前真实failfast。E-init/F-init。此处不执行实际producer、CoreCLR或游戏。

## 失败记录、冻结与余项

首full在ready-original-seh失败：测试假定ExceptionFlags恒为0，但被动first-chance及外层filter实际均128；source RaiseException参数0没有被当作OS返回flags。诊断后改为逐字段比较真实异常记录，仅测试修正，最终session {session} exit0。首失败和诊断均保留，不追认失败为通过，也不声称任意SEH状态都为128。

{freeze} {diagnostics} 旧1021安装18项、326项归档及原游戏三hash不变；本包未安装，实测稍后。对应源码与LGPL交付沿修改版Doorstop保留，干净环境复现另验。

仍须实际native→CLR回调/ledger组合、真实冷启动顺序、IL2CPP/listener/GC、未来来源/取消、所有消费者资源退休与完整Retry/Return/Loader。仅B03整项/B18a子项已验收，[下一阶段](validation/{batch.name}/next-integration.md)、[批次索引](validation/{batch.name}/README.md)。
""")

begin, end = "<!-- CLR-PREINIT-LATEST:BEGIN -->", "<!-- CLR-PREINIT-LATEST:END -->"


def index_update(path, header, relative):
    old = path.read_text(encoding="utf-8-sig")
    assert old.count(header) == 1
    block = f"""{begin}
- **最新CLR启动采用批：{result}，{source_count}源/22包冻结，未安装。** [本轮证据]({relative})：实际producer→生产CLR RequireReady/RuntimeAdopt与双idle sinks接入，零游戏/native callbacks，metadata受控。
- **实证**：CLR{managed_checks}检查/2进程；atomic Unknown{unknown_checks}检查、暂停writer期间100读；同生产C init {init_checks}检查/16进程。首SEH测试Flags0假设已按first-chance/捕获128一致修正，仅改测试；最终session {session} exit0。{diagnostics}
- **余项**：{limits} 实测稍后，游戏仍1021，完整B01–B19 active，仅B03整项/B18a子项勾选。
{end}"""
    if begin in old:
        assert old.count(begin) == old.count(end) == 1
        old = re.sub(re.escape(begin) + r".*?" + re.escape(end), lambda _: block, old, count=1, flags=re.S)
    else:
        assert "foundation-preinit-producer-20261024/README.md" in old
        prefix, tail = old.split(header, 1)
        lines = tail.splitlines()
        previous = next(index for index, line in enumerate(lines) if line.startswith("- **最新1515/1515") and "474源/22包" in line)
        lines[previous] = lines[previous].replace("**最新1515/1515", "**历史1515/1515", 1)
        old = prefix + header + "\n\n" + block + "\n\n### 历史1515（原生启动安装批）\n" + "\n".join(lines)
    write(path, old)


index_update(repo / "docs/IMPLEMENTATION-STATUS.md", "## 已验证到哪里", f"validation/{batch.name}/README.md")
index_update(repo / ".docs/CURRENT-CHECKPOINT.md", "## 当前现场", f"../docs/validation/{batch.name}/README.md")

latest = f"""{begin}
最新CLR启动采用批[{result}](../../docs/validation/{batch.name}/README.md)已冻结{source_count}源/22包，未安装。实际RequireReady/RuntimeAdopt与双idle sinks接入通过，native/game callbacks为0且metadata受控；atomic Unknown确定100读、生产C init受控ready16进程透明性通过。仅B03/B18a勾选，RequestPlay关闭，B01–B19 active，实测稍后；真实冷启动、listener/GC与全部退休仍缺。
{end}"""
if begin in checklist_before:
    assert checklist_before.count(begin) == checklist_before.count(end) == 1
    checklist_after = re.sub(re.escape(begin) + r".*?" + re.escape(end), lambda _: latest, checklist_before, count=1, flags=re.S)
else:
    marker = "# Checklist: foundation-expansion\n\n"
    assert marker in checklist_before
    checklist_after = checklist_before.replace("\n最新[1515/1515", "\n历史1515（原生启动安装批）[1515/1515", 1)
    checklist_after = checklist_after.replace(marker, marker + latest + "\n\n", 1)
assert re.findall(r"^- \[[ x]\] B[^\n]+", checklist_after, re.M) == re.findall(r"^- \[[ x]\] B[^\n]+", checklist_before, re.M)
write(checklist_path, checklist_after)

root_readme = repo / "README.md"
old = root_readme.read_text(encoding="utf-8-sig")
replacement = f"2026-10-24 更新：CLR启动采用批25套1515/1515、{source_count}源/22包冻结，未安装。真实producer→生产RequireReady/RuntimeAdopt及双idle sinks通过（零游戏/native callbacks、metadata受控）；atomic Unknown确定100读和生产C init受控16进程已验。真实冷启动、listener/GC与全资源闭合仍缺，实测稍后。见[本轮证据](docs/validation/{batch.name}/README.md)、[实现状态](docs/IMPLEMENTATION-STATUS.md)与[续接点](.docs/CURRENT-CHECKPOINT.md)。"
old, count = re.subn(r"2026-10-24 更新：[^\n]+", lambda _: replacement, old, count=1)
assert count == 1
write(root_readme, old)

print(json.dumps(dict(published=True, batch=batch.name, total=1515, sourceInputs=source_count,
    packageEntries=22, added=added, changed=changed, removed=removed, installed=False,
    managedChecks=managed_checks, atomicUnknownChecks=unknown_checks, initCallChecks=init_checks,
    diagnostics=diagnostic_count), ensure_ascii=False))
