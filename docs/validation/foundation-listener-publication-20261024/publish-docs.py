"""Publish documentation only after full terminal and archive evidence agree."""
import hashlib
import json
import re
import zipfile
from pathlib import Path

batch = Path(__file__).resolve().parent
repo = batch.parents[2]


def read(name):
    return json.loads((batch / name).read_text(encoding="utf-8-sig"))


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


summary, run = read("build-summary.json"), read("build-run.json")
source, package = read("source-archive-check.json"), read("package-archive-check.json")
manifest, package_manifest = read("source-manifest.json"), read("package-manifest.json")
assert run["exitCode"] == 0 and run["sessionId"] > 0 and not run["installed"] and not run["gameExecuted"]
assert summary["projects"] == 25 and summary["passed"] == summary["total"] == 1515
assert summary["logSha256"] == run["logSha256"] == sha(batch / "build.log")
assert read("build-inputs-before.json")["files"] == manifest["files"]
integrity = read("build-input-integrity-check.json")
assert integrity["preBuildManifestMatchesArchivedManifest"] and integrity["currentChanged"] == []
count = len(manifest["files"])
assert count == source["entries"] == 493 and len(package_manifest["files"]) == package["entries"] == 22
for proof, entries, current in [(source, manifest["files"], repo), (package, package_manifest["files"], repo / "artifacts/package")]:
    archive_path = batch / proof["archive"]
    assert sha(archive_path) == proof["archiveSha256"] and proof["allEntryHashesMatched"]
    with zipfile.ZipFile(archive_path) as archive:
        assert set(archive.namelist()) == {row["path"] for row in entries} and archive.testzip() is None
        for row in entries:
            assert sha(current / row["path"]) == hashlib.sha256(archive.read(row["path"])).hexdigest() == row["sha256"]
assert read("staging-check.json")["allMatched"] and not read("staging-check.json")["installed"]
assert read("previous-installation-check.json")["allMatched"] and read("original-game-check.json")["allUnchanged"]
mapped, unknown, managed = read("mapped-publication-evidence.json"), read("atomic-unknown-evidence.json"), read("managed-adoption-evidence.json")
assert mapped["passed"] and mapped["allProcessChecks"] == 1943 and mapped["independentProcesses"] == 5
assert unknown["passed"] and unknown["checks"] == 713 and unknown["v2ObservedUnknownSnapshots"] == 100
assert managed["passed"] and managed["checks"] == 72 and managed["independentProcesses"] == 2
assert len(mapped["tamperModes"]) == 4 and all(row["tamperDetected"] and row["noTenthProducerWrite"] for row in mapped["tamperModes"])
assert all(row["productionListenerReceiptV2Verified"] and row["listenerReadonlyAdoptionVerified"] and not row["listenerCallbacksVerified"] for row in managed["modes"])
diagnostics = len(summary["compilerDiagnosticLines"])
changes = read("changes-from-listener-native.json")
assert changes["previousInputs"] == 491 and changes["currentInputs"] == 493 and changes["removed"] == []
delta = f'新增{len(changes["added"])} / 修改{len(changes["changed"])} / 删除0'
boundary = "真实游戏冷启动、listener→CLR 回调与 ledger、IL2CPP/GC、动态 delegate leaves、连续 mutation/provider 转交与取消、全消费者退休和完整 Retry/Return/Loader 仍未验收。RequestPlay 关闭，Pending 保留，InlineExecutionUnobserved=true、DispatchPathVerified=false。完整 B01–B19 继续，仅 B03 整项与 B18a 子项已勾；游戏实测按用户安排暂缓。"


def latest(prefix):
    return ("<!-- LISTENER-PUBLICATION-LATEST:BEGIN -->\n"
        f'- **最新：生产监听发布与只读采用已通过离线验证。** [证据]({prefix}docs/validation/{batch.name}/README.md)：25 套 1515/1515，493 源/22 包冻结，未安装。固定映像九处发布、V2 416 字节记录与 C# RequireReady 已接通，helper 共42 exports。\n'
        f'- **本批实证**：映像5进程1943检查，含 source/post/relay/chain 四种篡改拒绝；atomic Unknown713检查、V1/V2各100次确定竞态读取；CLR只读采用72检查/2进程。全部不执行游戏代码，listener未附加且调用0；上批自有native listener8349检查继续回归。full session {run["sessionId"]} exit0，保留{diagnostics}条去重编译诊断。\n'
        f'- **余项**：{boundary} 当前游戏仍1021安装18项/其326源归档，原游戏三文件未改。\n'
        "<!-- LISTENER-PUBLICATION-LATEST:END -->")


updates = {}
for relative, prefix in [(".docs/CURRENT-CHECKPOINT.md", "../"), ("docs/IMPLEMENTATION-STATUS.md", "../"), (".docs/foundation-expansion/CHECKLIST.md", "../../")]:
    path = repo / relative
    body = path.read_text(encoding="utf-8-sig")
    already_published = "<!-- LISTENER-PUBLICATION-LATEST:BEGIN -->" in body
    pattern = (r"<!-- LISTENER-PUBLICATION-LATEST:BEGIN -->.*?<!-- LISTENER-PUBLICATION-LATEST:END -->" if already_published else
               r"<!-- LISTENER-NATIVE-LATEST:BEGIN -->.*?<!-- LISTENER-NATIVE-LATEST:END -->")
    match = re.search(pattern, body, flags=re.S)
    assert match, relative
    historical = "" if already_published else "\n\n" + match.group().replace("<!-- LISTENER-NATIVE-LATEST:BEGIN -->", "### 历史1515（监听原生层）\n").replace("<!-- LISTENER-NATIVE-LATEST:END -->", "").replace("最新listener原生层批", "历史listener原生层批")
    body = body[:match.start()] + latest(prefix) + historical + body[match.end():]
    if relative.endswith("CHECKLIST.md"):
        body, replacements = re.subn(r"- \[ \] B19 分发：.*", f'- [ ] B19 分发：当前1515/1515、25套，493源/22包冻结未安装；新V2生产listener发布与只读采用已有隔离映像证据。保留{diagnostics}条去重编译诊断，游戏仍1021。真实冷启动、干净环境、升级和全部能力矩阵仍缺。见[本批证据](../../docs/validation/{batch.name}/README.md)。', body, count=1)
        assert replacements == 1
    updates[path] = body
readme_path = repo / "README.md"
body = readme_path.read_text(encoding="utf-8-sig")
body, replacements = re.subn(r"2026-10-24 更新：.*", f'2026-10-24 更新：生产监听六区已接预初始化发布和 C# 只读采用，25套1515/1515离线回归通过，493源/22包冻结未安装。映像九处发布与四种篡改拒绝、两版记录并发异常、CLR采用已验；真实游戏监听回调、整局资源清理与冷启动仍待完成。RequestPlay关闭，实测稍后。见[本批证据](docs/validation/{batch.name}/README.md)与[实现状态](docs/IMPLEMENTATION-STATUS.md)。', body, count=1)
assert replacements == 1
updates[readme_path] = body

readme = f'''# 生产监听发布与只读采用

2026-10-24。25套 **1515/1515** 完整通过，493源/22包冻结未安装；root唯一构建，session {run["sessionId"]} exit0。Runtime160/Hooks171/Audio573/Visual71/UIinput79。本批是在固定映像中的真实发布及C#采用，不执行游戏代码；真实游戏验收仍缺。

预初始化 producer 现在先准备永久4KB RX块与3个动态函数记录，验证12处lookup和12个指令边界展开，再按 mid→三组pre/post→Unity父入口→runtime-invoke 发布九次。任一写尝试后不确定永久Unknown，不回滚或释放。V1 144字节布局保留并如实报告9次；新GetInfoV2 416字节记录六区、lane epoch、targets、stubs、表、页权限与首写前证明。C# RequireReady只接受实际V2及原生lane176，并再次核实际源bytes、relay/stub、表和页权限。

映像正常383检查；source/post/relay/chain各独立390检查，合计1943/5进程。每种篡改均导致V1/V2 Unknown，重入不产生第10次producer写；fixture自身篡改写单列为1。两版记录在Unknown atomic已置位且writer暂停期间各100次读取都未误报Ready，合计713检查。非法V2 header/reserved九种拒绝，调用方伪造identity被真实快照覆盖。CLR72检查/2进程实际RequireReady、runtime/双idle broker采用通过，listener仍未附加、调用0，不把idle当listener回调证据。

上批原生listener8349项、其他native/CLR fixture继续通过。完整构建保留{diagnostics}条去重编译诊断；不宣称零warning或B19干净环境全矩阵复现。{boundary}

源码相较491批{delta}。源码ZIP SHA-256 `{source["archiveSha256"]}`；源码manifest `{source["manifestSha256"]}`；包ZIP `{package["archiveSha256"]}`；完整日志 `{summary["logSha256"]}`。before/post/current输入一致；旧1021安装18项、其326源归档与原游戏三文件独立核对不变。

仓库根PowerShell7复现：

```powershell
pwsh -File scripts/Build.ps1 -NativeOutputDirectory D:/RizToolbox-build-cache/listener-publication-final-20261024 -ValidationGameAssemblyPath D:/SteamLibrary/steamapps/common/Rizline/GameAssembly.dll
```

只映射固定GameAssembly的测试使用DONT_RESOLVE；无DllMain、游戏导入、游戏函数或IL2CPP init执行。真实callee硬件返回地址证据来自[上批自有fixture](../foundation-listener-native-20261024/README.md)，本批仅核实际source+5/+5/+6字节与真实OS展开，不合并为游戏调用证明。

[完整日志](build.log)、[终态](build-run.json)、[汇总](build-summary.json)、[映像证据](mapped-publication-evidence.json)、[原子Unknown](atomic-unknown-evidence.json)、[CLR采用](managed-adoption-evidence.json)、[源码冻结](source-archive-check.json)、[包冻结](package-archive-check.json)、[差异](changes-from-listener-native.json)、[有限复核](review.md)、[授权范围](scope.md)、[下一步](next-integration.md)。
'''
updates[batch / "README.md"] = readme
for path, body in updates.items():
    path.write_text(body, encoding="utf-8", newline="\n")
print(json.dumps(dict(documents=len(updates), passed=1515, sourceInputs=count, packageFiles=22, compilerDiagnosticLines=diagnostics)))
