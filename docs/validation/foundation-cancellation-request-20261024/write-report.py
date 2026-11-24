"""Read frozen local evidence and generate the ordinary Chinese report.

This script never builds, starts a fixture/game, installs or changes source.
Final generation deliberately requires the full terminal and archive proofs.
"""
import hashlib
import json
import zipfile
from datetime import datetime, timedelta, timezone
from pathlib import Path


batch = Path(__file__).resolve().parent
repo = batch.parents[2]
destination = repo / "docs/2026-10-24_底层-取消请求与退休屏障-report.md"


def read(name):
    return json.loads((batch / name).read_text(encoding="utf-8-sig"))


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def link(name, label=None):
    return f"[{label or name}](validation/{batch.name}/{name})"


def choose(*names):
    for name in names:
        if (batch / name).is_file():
            return name, read(name)
    raise FileNotFoundError(f"Missing final evidence: {names}")


def local_time(value):
    return datetime.fromisoformat(value.replace("Z", "+00:00")).astimezone(
        timezone(timedelta(hours=8))).strftime("%Y-%m-%d %H:%M:%S +08:00")


def verify_archive(proof, manifest, current):
    entries = manifest["files"]
    assert proof["entries"] == len(entries) and proof["allEntryHashesMatched"]
    assert sha(batch / proof["archive"]) == proof["archiveSha256"]
    with zipfile.ZipFile(batch / proof["archive"]) as archive:
        assert set(archive.namelist()) == {row["path"] for row in entries}
        for row in entries:
            assert hashlib.sha256(archive.read(row["path"])).hexdigest() == row["sha256"]
            if current:
                assert sha(repo / row["path"]) == row["sha256"], row["path"]


def main():
    summary, run = read("build-summary.json"), read("build-run.json")
    assert run["exitCode"] == run.get("terminalExitCodeObserved", run.get("terminalExitCode")) == 0
    assert run["rootSoleBuilder"] and not run["installed"] and not run["gameExecuted"]
    assert summary["projects"] == len(summary["suites"]) == 25
    assert summary["passed"] == summary["total"]
    assert all(row["passed"] == row["total"] for row in summary["suites"])
    assert sha(batch / "build.log") == summary["logSha256"] == run["logSha256"]
    log_text = (batch / "build.log").read_text(encoding="utf-8-sig")
    assert "619/619 audio/content checks passed." in log_text
    case_groups = [
        ("tests/RizToolbox.AudioContent.Tests/NativeCoroutineCancellationLedgerTests.cs", 8),
        ("tests/RizToolbox.AudioContent.Tests/NativeCoroutineCancellationLaunchTests.cs", 6),
        ("tests/RizToolbox.AudioContent.Tests/NativeCoroutineCancellationRetirementTests.cs", 8),
    ]
    import re
    for path, count in case_groups:
        names = re.findall(r'^\s*\("([^"\r\n]+)",\s*[A-Za-z][A-Za-z0-9_]*\)', (repo / path).read_text(encoding="utf-8"), re.M)
        assert len(names) == count, path
        assert all(f"PASS {name}" in log_text for name in names), path
    if "targetedLogSha256" in run:
        assert sha(batch / "targeted.log") == run["targetedLogSha256"]

    joint_name, joint = choose("operation-ledger-evidence.json", "native-build/operation-ledger/fixture-result.json")
    dobby_name, dobby = choose("operation-dobby-evidence.json", "native-build/operation-dobby/fixture-result.json")
    assert joint["kind"] == "native-operation-ledger-summary"
    assert joint["passed"] and joint["failures"] == 0 and joint["independentProcesses"] == 5
    modes = {row["mode"]: row for row in joint["modes"]}
    assert set(modes) == {"normal", "original-seh", "original-cpp", "before-fault", "after-fault"}
    assert joint["checks"] == sum(row["checks"] for row in modes.values())
    raw_joint = "native-build/operation-ledger/fixture-result.json"
    assert read(raw_joint) == joint
    for row in modes.values():
        assert row["passed"] and row["failures"] == 0 and row["modeVerified"]
        assert row["nativeToClrCallbackVerified"] and row["productionBridgeAndBrokerVerified"]
        assert row["actualMutationLedgerVerified"] and row["actualCancellationRequestLedgerVerified"]
        assert row["actualOriginalCalls"] == row["observedMutations"] == 3
        assert row["completionCount"] == row["disposalCount"] == 3
        assert row["cancellationRequests"] == row["cancellationCompletions"] == row["cancellationDisposals"] == 2
        assert row["nativeInFlight"] == row["ledgerNativeInFlight"] == 0
        assert row["initialLedgerActivityControlled"] and row["metadataGraphRootsControlled"]
        assert row["identityProviderControlled"] and row["diskHashesUnchanged"]
        assert all(row[key] is False for key in ["actualTrackerProviderUsed", "productionStopBindingVerified",
            "actualIl2CppMetadataResolved", "productionObjectBackendExecuted", "firstInstallReceiptUsed",
            "firstInstallSafetyVerified", "gameCodeExecuted", "gameRuntimeVerified",
            "exceptionFlagsTransparencyVerified", "dobbyPublicationVerified", "allMutationWritersClosed",
            "allFutureListenersClosed", "engineCancellationExecuted", "installed"])
        assert read(f"native-build/operation-ledger/{row['mode']}-result.json") == row
        assert row["confirmedReturnedMutations"] == (3 if row["mode"] == "normal" else 0)
        for result in row["results"]:
            assert result["OriginalCalls"] == result["ledgerObserved"] == result["completions"] == result["disposals"] == 1
            assert result["Completed"] and result["nativeFailures"] == 0
            assert result["nativeInFlight"] == result["ledgerNativeInFlight"] == 0
            assert result["actualActivityReads"] > 0 and result["initialControlledIdleReads"] > 0
            assert result["enginePrepare"] == result["engineCommit"] == 1 and result["engineInstall"] == 0
            if result["kind"] < 2:
                cancellation = result["cancellation"]
                assert cancellation["ObservedRequests"] == cancellation["completions"] == cancellation["disposals"] == 1
                assert cancellation["Outstanding"] == 0 and cancellation["Pending"] and not cancellation["EngineCancelled"]
                assert cancellation["actualSequence"] == result["Sequence"]
            else:
                assert result["cancellation"] is None
    for process in joint["processes"]:
        assert process["exitCode"] == 0
        for stream in ["stdout", "stderr"]:
            path = batch / f"native-build/operation-ledger/{process['mode']}.{stream}.log"
            assert sha(path) == process[f"{stream}Sha256"]

    assert dobby["kind"] == "native-operation-dobby-fixture" and dobby["passed"] and dobby["failures"] == 0
    assert dobby["checks"] == 1165
    assert dobby["originalCalls"] == dobby["entered"] == dobby["exited"] == dobby["uniqueSequenceChecks"] == 69
    assert dobby["unpublishedOriginalCalls"] == 1 and dobby["totalActualOriginalCalls"] == 70
    assert (dobby["originalExceptionCases"], dobby["observerExceptionCases"], dobby["nativeWorkerCalls"],
        dobby["nestedCalls"], dobby["logicalDetachCases"], dobby["legitimateZeroTokens"]) == (12, 24, 12, 6, 3, 3)
    assert (dobby["preparedTargets"], dobby["committedTargets"], dobby["commitRejections"],
        dobby["configureRejectedTargets"], dobby["retainedUnpublishedPreparedTargets"]) == (4, 3, 3, 1, 1)
    assert dobby["realDobbyPreparedCommit"] and dobby["exactSourceCallerVerified"] and dobby["pinnedDobbyVerified"]
    assert all(dobby[key] is False for key in ["gameCodeExecuted", "gameRuntimeVerified", "installed",
        "firstInstallSafetyVerified", "concurrentFirstPublicationVerified", "partialPublicationFailureInjected",
        "exceptionFlagsTransparencyVerified", "nativeAdmissionGateVerified"])
    assert read("native-build/operation-dobby/fixture-result.json") == dobby
    dobby_manifest = read("native-build/operation-dobby/fixture-manifest.json")
    assert dobby_manifest["exitCode"] == 0 and dobby_manifest["allMatched"]
    for row in dobby_manifest["files"]:
        assert row["beforeSha256"] == row["afterSha256"] == sha(Path(row["path"]))
    joint_files = read("native-build/operation-ledger/source-file-check.json")
    assert joint_files["allMatched"] and not joint_files["installed"] and not joint_files["gameCodeExecuted"]
    for row in joint_files["files"]:
        assert row["beforeSha256"] == row["afterSha256"] == sha(Path(row["path"]))
    stdout = (batch / "native-build/operation-dobby/fixture-stdout.log").read_text(encoding="utf-8-sig")
    lines = [line for line in stdout.splitlines() if line.startswith("{")]
    assert len(lines) == 1 and json.loads(lines[0]) == dobby

    source, package = read("source-archive-check.json"), read("package-archive-check.json")
    manifest, package_manifest = read("source-manifest.json"), read("package-manifest.json")
    verify_archive(source, manifest, True)
    verify_archive(package, package_manifest, False)
    assert sha(batch / "source-manifest.json") == source["manifestSha256"]
    assert package["entries"] == 22 and not package["installed"]
    integrity = read("build-input-integrity-check.json")
    assert integrity["currentChanged"] == [] and integrity["preBuildManifestMatchesArchivedManifest"]
    assert read("source-integrity-check.json")["changed"] == []
    assert read("staging-check.json")["allMatched"] and read("original-game-check.json")["allUnchanged"]
    assert read("previous-installation-check.json")["allMatched"]
    assert (batch / "scope.md").is_file(), "The batch scope must be finalized before this report."

    previous = json.loads((batch.parent / "foundation-listener-mutation-20261024/source-manifest.json").read_text(encoding="utf-8-sig"))
    old = {row["path"]: row["sha256"] for row in previous["files"]}
    hashes = {row["path"]: row["sha256"] for row in manifest["files"]}
    added, removed = set(hashes) - set(old), set(old) - set(hashes)
    changed = {name for name in old.keys() & hashes.keys() if old[name] != hashes[name]}
    declared_change = read("changes-from-listener-mutation.json")
    assert set(declared_change["added"]) == added and set(declared_change["changed"]) == changed
    assert set(declared_change["removed"]) == removed
    assert summary["total"] == 1575 and source["entries"] == 534
    assert len(added) == 12 and len(changed) == 11 and not removed
    assert joint["checks"] == 544
    suites = "\n".join(f"| {row['summary']} | {row['passed']}/{row['total']} |" for row in summary["suites"])
    mode_purposes = {
        "normal": "实际 mutation 正常前后图 + 两种取消请求；两账本均配对",
        "original-seh": "三种原调用分别 SEH；仅 scalar 异常完成，不读 After 图/provider",
        "original-cpp": "三种原调用分别 C++；原生 typed catch，保留异常事实与清理",
        "before-fault": "mutation Before backend 取得受控根后故障；实际返回≠确认图返回",
        "after-fault": "mutation After 图读取故障；取消账本作为独立 sink 继续清理",
    }
    mode_rows = "\n".join(f"| `{name}` | {row['checks']} | {row['actualOriginalCalls']} | {row['confirmedReturnedMutations']} | {mode_purposes[name]} |" for name, row in modes.items())
    source_paths = [
        "src/RizToolbox.Adapter/AudioContent/NativeCoroutineCancellationLedger.cs",
        "src/RizToolbox.Adapter/AudioContent/NativeCoroutineCancellationObserver.cs",
        "src/RizToolbox.Runtime/NativeRuntime.CoroutineStopHooks.cs",
        "src/RizToolbox.Runtime/Hooks/CoroutineStopProfile.cs",
        "src/RizToolbox.Adapter/AudioContent/LocalRunLaunchCommitState.cs",
        "src/RizToolbox.Adapter/AudioContent/NativeLocalRunLaunchCoordinator.cs",
        "src/RizToolbox.Adapter/AudioContent/NativeLocalInitializationCoroutineTracker.cs",
        "src/RizToolbox.Adapter/AudioContent/NativeLocalCutoutCoroutineTracker.cs",
        "src/RizToolbox.Adapter/AudioContent/NativeLocalTerminalCoroutineTracker.cs",
        "src/RizToolbox.Adapter/AudioContent/NativeLocalInitializationCoroutineBackend.cs",
        "src/RizToolbox.Adapter/AudioContent/NativeLocalCutoutCoroutineBackend.cs",
        "src/RizToolbox.Adapter/AudioContent/NativeLocalTerminalCoroutineBackend.cs",
        "native/RizToolbox.JudgeContext/fixture/OperationDobbyFixture.cpp",
        "tests/RizToolbox.Runtime.Tests/NativeOperationLedgerRegression.cs",
        "tests/RizToolbox.AudioContent.Tests/NativeCoroutineCancellationLedgerTests.cs",
        "tests/RizToolbox.AudioContent.Tests/NativeCoroutineCancellationLaunchTests.cs",
        "tests/RizToolbox.AudioContent.Tests/NativeCoroutineCancellationRetirementTests.cs",
        "scripts/Test-OperationLedger.ps1",
    ]
    source_rows = "\n".join(f"| [{Path(path).name}](../{path}) | `{hashes[path]}` |" for path in source_paths)
    runtime_inputs = {Path(row["path"]).name: row["afterSha256"] for row in dobby_manifest["files"] + joint_files["files"]}
    runtime_input_rows = "\n".join(f"| `{name}` | `{digest}` |" for name, digest in runtime_inputs.items())
    full_command = "pwsh -File scripts/Build.ps1 -NativeOutputDirectory D:/RizToolbox-build-cache/cancellation-request-repro-20261024 -ValidationGameAssemblyPath D:/SteamLibrary/steamapps/common/Rizline/GameAssembly.dll"
    ledger_command = "pwsh -File scripts/Test-OperationLedger.ps1 -ManagedAssemblyPath tests/RizToolbox.Runtime.Tests/bin/Release/net8.0/RizToolbox.Runtime.Tests.dll -FixturePath D:/RizToolbox-build-cache/cancellation-request-repro-20261024/operation-fixture/RizToolbox.OperationManagedFixture.dll -HelperDirectory D:/RizToolbox-build-cache/cancellation-request-repro-20261024 -OutputDirectory D:/RizToolbox-build-cache/cancellation-request-repro-20261024/operation-ledger-repeat"
    dobby_command = "pwsh -File scripts/Test-OperationDobby.ps1 -HelperDirectory D:/RizToolbox-build-cache/cancellation-request-repro-20261024 -FixturePath D:/RizToolbox-build-cache/cancellation-request-repro-20261024/operation-dobby-fixture/OperationDobby.Fixture.exe -DobbyPath .tools/bepinex-789/BepInEx/core/dobby.dll -OutputDirectory D:/RizToolbox-build-cache/cancellation-request-repro-20261024/operation-dobby-repeat"
    specs = [
        ("E-001", "完整构建真实终态及日志汇总", "build-run.json", "log", full_command,
         f"sessionId={run['sessionId']}; exitCode=0; projects=25; passed={summary['passed']}; installed=false; gameExecuted=false"),
        ("E-002", "自有目标的真实 Dobby Prepare/Commit", dobby_name, "log", dobby_command,
         "checks=1165; observed originals=69; unpublished original=1; concurrentFirstPublicationVerified=false"),
        ("E-003", "真实 native→CLR 同时进入 mutation/取消请求账本", joint_name, "log", ledger_command,
         f"independentProcesses=5; checks={joint['checks']}; actualOriginalCalls=15; observedMutations=15; identityProviderControlled=true"),
        ("E-004", "六 Stop ABI 的磁盘只读研究", "stop-abi.json", "file", "阅读本批 research.md 内记录的只读 PE/Capstone 复现命令",
         "six fixed-RF declarations; no gamecode; dump/script naming is not live metadata"),
        ("E-005", "冻结的生产 gate、请求账本与回归源码", "source-inputs.zip", "file",
         f"C:/Python314/python.exe docs/validation/{batch.name}/write-report.py",
         f"{source['entries']} declared inputs; archive/manifest/current hashes all match"),
        ("E-006", "暂存包及未安装边界", "package-archive-check.json", "file", full_command,
         "entries=22; allEntryHashesMatched=true; installed=false"),
        ("E-007", "原游戏与旧安装独立校验", "original-game-check.json", "file",
         f"Get-Content docs/validation/{batch.name}/original-game-check.json; Get-Content docs/validation/{batch.name}/previous-installation-check.json",
         "original game allUnchanged=true; previous installation allMatched=true"),
    ]
    evidence = "\n".join(f"""### {eid}：{title}

- observed_at: `{run['completedAtUtc']}`（完整构建终态；各原件另含自身时间）。
- source_type: `{kind}`；source_ref: {link(name)}。
- content_hash: `{sha(batch / name)}`。
- artifact_path: `docs/validation/{batch.name}/{name}`。
- repro_command: `{command}`。
- raw_excerpt: `{excerpt}`。
- linked_workitem: `n/a`（本批工程闭环）；supersedes: `none`。
""" for eid, title, name, kind, command, excerpt in specs)
    diagnostics = len(summary.get("compilerDiagnosticLines", []))
    body = f"""# 底层取消请求与退休屏障报告

日期：2026-10-24。普通工程报告，flavor=null，overlay=null。本文由本机原始证据生成；作者参与了联合回归实现，不把作者自查称为外部独立审计。

## 概述

本批把取消请求作为独立、单调的保留责任接到 launch Ready/Commit 与三类协程的退休路径。取消请求发生后，即使原调用正常返回、native 在途为零、请求上下文已清理，也继续保留 Pending，不能据此认定引擎已停止或允许释放根。完整构建 session {run['sessionId']} 已真实 exit0，25 套 {summary['passed']}/{summary['total']}；自有目标实际 Dobby 发布通过 {dobby['checks']} 项检查，5 个独立 native→CLR 进程通过 {joint['checks']:g} 项检查并进入实际 mutation 与取消请求账本。

本批没有执行游戏函数或实际 Unity Stop API，没有采用固定游戏 Ready 完成这条调用链。联合回归的元数据图、根、取消身份 provider 和账本初始化 idle 活动均受控。固定游戏首次发布并发安全、IL2CPP 元数据/GC、引擎取消完成、全部 future 来源和完整资源退休仍未验收。

## 范围、授权与依赖

本批范围见 {link('scope.md')}，总范围见 [docs/scope.md](scope.md)。用户“先继续代码，实测稍后”继续生效；只进行本地实现、离线构建及自有 DLL/EXE 的隔离子进程验证。本报告生成只读日志、JSON、源码和归档，写入本文；没有重新执行复现命令。新包未安装，旧安装与原游戏文件分别有校验。

依赖是 Windows x64、本机 MSVC 2019 14.29.30133、.NET 8 测试目标及既有 .NET 6 生产目标、仓库固定 BepInEx bundle 和 Dobby。Dobby SHA-256 为 `8015de7d867245a1095d13947a63763878e4cf5fd3d3089b63cc39200b055ded`；显式 DLL 路径和运行前后输入哈希由各 runner 记录。源码包不包含游戏映像、全部缓存依赖或工具链，不证明换机零配置复现。报告自身只需本机 `C:/Python314/python.exe` 标准库。

## 实现与验证结果

### 取消请求、身份与真实返回

`NativeCoroutineCancellationLedger` 先登记 owned receiver 的请求、当前父子关系和 context 责任，再调用只读 scalar provider。枚举器只匹配当前 owner/receiver/slot/generation 的枚举器；handle 还要求实际 Start 返回且确认过 handle。StopAll 保存调用前的人口，调用后新增 Starts、代次复用或人口变化会变 Unknown。StopNamed 只记录字符串指针并保持 MatchUnproven，不读取字符串内容。foreign receiver 不调用 provider；worker 故障只读不可变 receiver 表并发布 scalar Unknown。

请求的 NativeReturned、Abnormal、context 完成和 Dispose 分别记录。异常或非精确正常完成不读取 After provider，Unknown 后也不继续 provider 读取；清理仍保留并配对。正常请求可达到 Outstanding=0、CanConfirmRequestIdle=true，但 HasObservedRequests/Pending 继续为真，EngineCancelled 恒为 false。8 个 production scalar ledger 受控回归覆盖精确引用、StopAll 人口、嵌套、worker/foreign、provider 故障、named/null/异常、错误完成和容量溢出；它们没有执行实际引擎 Stop。

六个固定 Stop 声明含一种 Void0、五种 Reference1；两个 public wrapper 将内部 icall 路径内联，不能只观察内部方法。{link('research.md')} 与 {link('stop-abi.json')} 保留六个完整 RF 的机器码、缓存和尾跳证据。固定磁盘 GameAssembly SHA-256 为 `853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79`；dump.cs/script.json 仅辅助命名，尚未证明活体 MethodInfo、隐藏参数实际值或全部调用方。

### Pending 在 provider 前发布，阻止 Ready 与继续退休

生产 observer 在确认 receiver 属于当前 owner 后、进入任何身份 provider 之前设置 launch 的独立 atomic `CoroutineCancellationPending`。这可以是已知的保留状态，不要求伪造 Unknown、EarlyTerminal 或物理在途。实际 shared launch pump 在 activation、getter 完成及实际图验证后重核；state 自身的 Prepare/Commit 和持续 usable Ready 也拒绝 Pending，发布后有复核。6 个受控 launch 回归覆盖 pump 之前、activation 内、getter 内、图活动核验内、Prepare→Commit 之间和 Ready 发布之后。

初始化、Cutout、终止协程 tracker 与 production backend 的退休专用路径在 source/activity/enum 读取、逐 binding detach、逐 root free 前后重新检查 Pending。普通观察和 ReadActivity 继续报告实际数值，不把 Pending 伪造成活动或 Unknown。8 个退休回归运行实际 tracker，使用受控 authority/backend/free；在 detach 或首个独立 free 内出现 Pending 时不继续下一个 root，保留剩余责任。已完成的第一个 free 不会回滚，也不宣称其仍被保留。没有动态执行实际 IL2CPP GC handle free，检查点之间的任意多线程交错尚未穷举。

### 自有目标的 Dobby 发布

实际自有 EXE 对三种 typed target 完成 Prepare→原生 immutable Configure→Commit，调用真实 trampoline 并核独立 caller 的精确 source ReturnPC。完整运行 {dobby['checks']} 项检查通过：69 次观察内 original、另一个 Prepare 但未发布的目标直通一次，总实际 original=70；Entered/Exited/unique sequence 均69。覆盖原异常12、observer 异常24、native worker12、nested6、逻辑解绑3、合法零 token3。Prepare4、Commit3、重复 Commit 拒绝3、配置拒绝1，并保留一个未发布 prepared target。

先前独立预验证也是1165检查/69+1调用；它使用当时 helper，不能替代本批完整构建。上述结果取自本批最终原件。它没有验证生产目标首写的并发执行、任意写一半故障或 native admission gate；JSON 中 concurrentFirstPublicationVerified、partialPublicationFailureInjected、nativeAdmissionGateVerified 均为 false。不能因真实 Dobby fixture 通过就开放固定游戏六 Stop 的首次安装。

### 真实 native→CLR 同时进入两个生产账本

5 个独立子进程使用自有 `OperationManagedFixture` 的实际 original、method、Self、参数、caller 和 native sequence，经真实 `NativeOperationContextBridge` 与生产共用 broker 进入实际账本；回调 frame 没有重写。每进程三 kind 映射 RemoveAllListeners、AddListener、RemoveListener，实际 original 各一次。kind0/1 的第二 sink 把同一真实 frame 分别交给 StopAll、StopEnumerator 的实际取消请求账本；kind2 不硬套取消 ABI。双 sink 共享同一物理 entry。

| 模式 | 独立检查 | 实际 original | mutation 确认图返回 | 验证内容 |
|---|---:|---:|---:|---|
{mode_rows}

合计实际 original={joint['actualOriginalCalls']:g}、mutation={joint['observedMutations']:g}、取消请求={sum(row['cancellationRequests'] for row in modes.values())}；两个账本的完成与 Dispose 均配对，最终实际 native/broker 在途为0。normal 的三条 mutation 有实际算法的前后图；mutation Before/After backend 故障模式均保留根记录和 Unknown，并区分 NativeReturned 与 ConfirmedReturned。原 SEH/C++ 在 native fixture 内捕获，完整参数按对应断言核对；未让异常跨 P/Invoke。异常 Flags 只记录观察值，exceptionFlagsTransparencyVerified=false。

这里的初始 CaptureBackend owner 设置要求 idle；回调已经在途，因此初始化活动明确使用受控0，随后切换真实 binding 活动，并在实际 entered 回调核对 native inFlight>0。JSON 标记 initialLedgerActivityControlled=true。图/类/根集合及取消 provider 受控，地址是 actualSelf 派生的 scalar，不读取游戏对象。identityProviderControlled=true、actualTrackerProviderUsed=false、productionObjectBackendExecuted=false、actualIl2CppMetadataResolved=false。此 CLI 的 Prepare/Commit 是返回实际 original 的受控 engine，不做 Dobby；因此不能把本节与上一节拼成“生产 Stop 安全首次发布→真实游戏对象取消”的端到端证明。

## Evidence

每条哈希直接从本机原件计算。E-003 的5个模式与各 raw JSON 完全一致，逐 child 的 exit0 与 stdout/stderr 哈希复核；E-002 同时核 stdout 中原始单行 JSON、runner exit0 和输入 hash 一致。E-005 逐项核 ZIP、manifest 与当前源码；未修改任何构建输入。

{evidence}
完整日志：{link('build.log')}，SHA-256 `{sha(batch / 'build.log')}`；汇总：{link('build-summary.json')}。日志中的 Package 行不单独作为成功依据；这里使用 root 观察的实际进程终态。记录 {diagnostics} 条去重原始编译诊断行，不等于 {diagnostics} 个独立问题，不宣称零 warning。

## Findings

### F-001：真实原生回调已同时驱动 mutation 与取消请求账本

- severity: `n/a_re`；category: `design`；status: `validated`；confidence: `high`。
- evidence_ids: `[E-001, E-003, E-005]`。
- location: `NativeOperationContextBridge`、`ExactHookBroker.NativeOperation`、`NativeOperationLedgerRegression` 与两个 production ledger。
- impact: 15个实际 typed original 的观察、正常/异常 scalar 完成及 context 清理有联合证据；受控图、根、provider、初始 idle 不授予实际引擎取消或退休。
- repro_steps: 执行下节完整构建或 ledger runner，核5个 child、actualOriginalCalls=15、取消请求10、逐 kind final inFlight=0 和所有边界 false。
- remediation: 后续接实际 tracker provider、生产对象 backend 与已证明安全的首次发布；n/a（当前是有限工程能力验证）。
- optional_attack: 空。

### F-002：取消 Pending 独立撤销可用 Ready 并阻止后续退休步骤

- severity: `n/a_re`；category: `design`；status: `validated`；confidence: `high`。
- evidence_ids: `[E-001, E-005]`。
- location: `CaptureRequest`、`LocalRunLaunchCommitState`、shared launch pump、三类 tracker/backend 的退休 gate。
- impact: provider 前即发布保留责任；受控同步注入和 worker scalar case 不依赖 Unknown、伪活动或取消完成。首个 free 后出现取消只保留剩余根，不能逆转已发生的 free。
- repro_steps: 在完整 AudioContent suite 中核8个 request、6个 launch、8个 retirement case；逐 case 定义见附录源码。
- remediation: n/a（当前 gate 已实现）；继续验证实际对象、首写时序及不在现有检查点上的并发交错。
- optional_attack: 空。

### F-003：Dobby 真发布已覆盖自有目标，生产并发首写仍未证明

- severity: `n/a_re`；category: `design`；status: `validated`（仅自有目标范围）；confidence: `high`。
- evidence_ids: `[E-002, E-004, E-005]`。
- location: `OperationDobbyFixture`、六 Stop profile/resolver。
- impact: typed trampoline/source caller/immutable observer 与清理实际运行；磁盘 ABI 与受控 publication 不能授权在运行中的 GameAssembly 修改指令。
- repro_steps: 执行 Dobby runner，核1165、69+1、三次 Commit 及 concurrentFirstPublicationVerified=false 等未验字段。
- remediation: 建立固定游戏 pre-init 原生发布或等价可靠 admission 证据，完成冷启动和首次调用验证后再讨论启用生产六 Stop。
- optional_attack: 空。

## Path

### P-001：真实操作回调到两本保留账本

path_type: `callflow`；start: 自有 typed target；goal: 原函数执行一次且两本责任账本配对。

1. 自有 original/method 与真实 work→采用实际 native channel→共享 broker entry（E-003/E-005，F-001）。
2. unchanged Frame64→actualSelf 合法注册 owner→受控图/根 CaptureMutation；同 physical kind0/1→取消 Capture 请求（E-003/E-005，F-001）。
3. 原生 original 正常/SEH/C++一次→实际 flags→normal After 或 scalar abnormal cleanup→context Dispose（E-003，F-001）。
4. actual native/broker inFlight归零，保留 ledger Unknown/Pending 与根，不授予 engine cancellation（E-003/E-005，F-001）。

residual_risks: 初始 idle、graph/root/provider 受控；不是实际 Stop 目标或固定游戏 Ready，没有 IL2CPP GC 和全 writer/future closure。

### P-002：取消观察到阻止 Ready/继续释放

path_type: `callflow`；start: owned receiver 的取消请求；goal: 保留 launch 与协程资源责任。

1. receiver归属→atomic CoroutineCancellationPending→provider之前即保留（E-005，F-002）。
2. 请求 Before/After 只读当前 scalar；population/代次/异常→Unknown，清理仍继续（E-003/E-005，F-001/F-002）。
3. shared pump/state持续拒 Pending Ready，三类 tracker/backend 在读取、detach/free检查点拒退休（E-001/E-005，F-002）。
4. 请求 idle 或正常原返回仍 Pending，EngineCancelled=false，不进入成功清除或资源释放许可（E-003/E-005，F-002）。

residual_risks: 没有引擎 scheduler 撤销、all-future 封口、全部 writer 关闭或检查点间任意竞态证明。F-003 的生产首写边界也未消除。

## 复现、时间线与未验收项目

在仓库根目录运行，先满足本机依赖和固定文件路径：

```powershell
{full_command}
{dobby_command}
{ledger_command}
C:/Python314/python.exe docs/validation/{batch.name}/write-report.py
```

最后一条只核证据并重新生成本文，前三条才运行构建或自有子进程。所有 runner 有有限 timeout；不能在另一构建仍活跃时修改输入。本批先取得独立 Dobby 预验证，再取得定向 session {run['targetedSessionId']} 的实际 exit{run['targetedExitCodeObserved']}，最后完整 session {run['sessionId']} 实际 exit{run['terminalExitCodeObserved']}。定向日志为 {link('targeted.log')}，SHA-256 `{sha(batch / 'targeted.log')}`。完整终态时间 `{run['completedAtUtc']}`（本地 {local_time(run['completedAtUtc'])}）。前批构建或修复记录仍归其原批，不能把前批故障或通过计数移作本批结果。

尚未验收：固定游戏六 Stop 的安全首发与并发首写、真实 metadata 解析/隐藏 MethodInfo、IL2CPP GC/production roots、引擎取消完成、StopNamed 字符串语义、任意部分代码写失败恢复、完整异常 Record/Context 透明性、全部 direct/inlined/icall 来源、全部 mutation writers/future producers、真实整局冷启动及资源退休。RequestPlay 仍关闭；全覆盖架构/B01–B19不因本批局部通过而扩勾。

## 归档与源码附录

源码输入 {source['entries']} 项，上一批 {len(old)} 项；新增 {len(added)}、修改 {len(changed)}、删除 {len(removed)}。源码 ZIP `{source['archiveSha256']}`，manifest `{sha(batch / 'source-manifest.json')}`；22项暂存包 ZIP `{package['archiveSha256']}`。{link('source-archive-check.json')}、{link('package-archive-check.json')} 与当前输入一致性原件共同记录边界；不把源码归档等同全部依赖或成功换机复现。

实际自有子进程输入由 runner 记录为下表，现场文件哈希与运行前后记录再次核对；其完整绝对路径在对应 manifest 中。

| 运行输入 | SHA-256 |
|---|---|
{runtime_input_rows}

| 源码 | SHA-256 |
|---|---|
{source_rows}

## 完整 suite 摘要

独立 native/CLR fixture checks 不并入下表25套的 `{summary['total']}`，也不把不同层的计数相加称为一个覆盖率。

| 原始摘要 | 通过/总数 |
|---|---:|
{suites}
"""
    destination.write_text(body, encoding="utf-8")
    print(json.dumps({"report": str(destination), "sha256": sha(destination),
        "sourceInputs": source["entries"], "packageEntries": package["entries"],
        "suiteChecks": summary["total"], "ledgerChecks": joint["checks"],
        "dobbyChecks": dobby["checks"]}, ensure_ascii=False))


if __name__ == "__main__":
    main()
