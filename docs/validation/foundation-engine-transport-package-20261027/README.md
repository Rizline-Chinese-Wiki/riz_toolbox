# V5 完整候选、安装恢复与 SDK 分发

2026-10-27。完整候选已真实构建并静态复核，最新合成目录安装及实际入口矩阵 16/16 通过；此前 11 项矩阵保留为独立历史记录。冻结源码的 24 套回归 1790 项和独立 SDK 分发 10 项通过。**本报告不证明游戏已安装、运行或开放生产首次写入许可。**

## 产物与范围

当前[完整候选 r2](D:/riz-build-cache-next/engine-transport-production-package-r2-20261027/transport-candidate-package-manifest.json) 为 24 项 payload 与 12 项 evidence，manifest SHA256 `2e4d3c682a80050a1521829a01e7bb978cd2b22a51ad3491ccd73f9eb9d2d02d`。包含六个托管组件、根目录 Bootstrap/Kernel、82 导出 Doorstop、固定构建适配资料、默认不启用的示例以及实际对应源和许可；不拷贝游戏文件到候选。

[原生候选 r2](D:/riz-build-cache-next/engine-transport-production-native-r2-20261027/production-native-build.json) 用 `RIZ_ENGINE_TRANSPORT_PRODUCTION=1` 实际严格构建，未包含 owned-image 路径。独立 PE 验证器核对实际五映像、13 完整函数、27 源 RF、4 个 V5 出口、35 个 Kernel 出口、82 个代理出口，以及最终解析后的 44 条普通 ILT/IAT/EAT 依赖；生成事实中未知的 self RVA 在最终 PE 中重新解析，不能把初始生成图摘要当成最终图。

[六托管组件 release 构建 r2](D:/riz-build-cache-next/engine-transport-production-managed-r2-20261027/managed-build-manifest.json) 使用实际 core-r5 冻结源码与五份最终 PE 指纹。构建成功，1 条生成文件 nullable 警告、0 错误；没有声称零警告。生成的指纹不能打开生产准入。

打包器先验证实际原生/托管构建、原始 base23 的每份 hash 和归属，之后产出新的 V5 schema，并再次在搬移后的包内解析 PE、核对完整 payload/evidence 树。旧 V4 Common、打包和安装入口保留；新 V5 入口复用既有持久 journal 事务。

## 实际合成目录矩阵

### 历史 11 项矩阵

[安装父回执](D:/riz-build-cache-next/engine-transport-package-install-r2-20261027/test-result.json) SHA256 `da4a4476ef2af284106006a928483dc3bf0412a1475bfb0151051c5e955fc15b`，11 个真实场景均通过：

1. 全 24 项首次安装和幂等重装。
2. 原始 base23 中嵌套 Kernel 迁移到根目录 V5。
3. 真正旧 V4 候选降级，再升级回 V5。
4. 外来 loader 文件拒绝覆盖。
5. 已安装且被修改的归属文件拒绝覆盖。
6. 候选源 hash 不符拒绝。
7. 路径逃逸及 game junction 拒绝。
8. 候选 reparse point 和 manifest 越界路径拒绝。
9. 最终提交故障恢复完整先前版本。
10. 实际终止安装 worker，缺候选时仍先恢复 journal，重复恢复幂等。
11. 卸载保留被修改及不属于安装器的文件。

被终止的 worker 实际 PID 为 29400，与拦截标记中的 PID 相同；父进程确认它已经终止，退出码 -1。不是用异常或布尔标志代替进程中断。所有变更均位于 `fixtures/*/fake-game`，真实游戏两份 PE 只在复制和前后 hash 时持只读句柄，fake GameAssembly 为合成文本。没有运行任一游戏 EXE 或 native payload，也没有调用依赖准备/联网安装流程。

上述 install-r2 矩阵使用此前 **package-r1**。package-r2 增加已验证的 listener 判据收紧；其后独立 [install-r3](D:/riz-build-cache-next/engine-transport-package-install-r3-20261027/test-result.json) 也实际通过原有 11/11 项，SHA256 `87bf374d32ce39f4da5cec292b9fcf5134cfcd579e04458df7c2c41758b82eab`，`candidateManifestSha256` 为 package-r2 的 `2e4d3c682a80050a1521829a01e7bb978cd2b22a51ad3491ccd73f9eb9d2d02d`。这两份 11 项回执均未改写成新版 16 项结果。

最初矩阵请求引用 `C:/Users/71957/riz-build-cache` junction，路径规则实际拒绝；随后使用它已核对的真实目标 `D:/riz-build-cache-relocated-20261026` 中的原件，保留路径拒绝行为，不放宽 reparse point 策略。

### 最新 16 项实际入口矩阵

[install-r4 父回执](D:/riz-build-cache-next/engine-transport-package-install-r4-20261027/test-result.json) 实际 `passed=true`、`count=16`、`passedCount=16`、`inputHashesUnchanged=true`，SHA256 `338db9072a452a158683b7d31c7406045934381969cf8ab0b1be946665d75829`。它重新执行原有 11 项，并加入正式安装入口的 5 项检查；对象明确为 **package-r2 / managed-r2 / core-r5**，候选 manifest SHA 与上文一致。后来的 core-r6 性能改动未进入这个包，不能把其性能结果算作本包的验收。

| 项 | 实际场景 | 结果 |
|---|---|---|
| 1 | 完整 24 项首次安装及幂等重装 | 通过 |
| 2 | 真实 base23 嵌套 Kernel 迁移到根目录 V5 | 通过 |
| 3 | 真实旧 V4 候选降级后再升级 V5 | 通过 |
| 4 | 外来 loader 文件拒绝覆盖 | 通过 |
| 5 | 已安装且修改的归属文件拒绝覆盖 | 通过 |
| 6 | 候选源 hash 不符拒绝 | 通过 |
| 7 | 路径逃逸及 game junction 拒绝 | 通过 |
| 8 | 候选 reparse point 和 manifest 越界路径拒绝 | 通过 |
| 9 | 最终提交故障回滚完整先前版本 | 通过 |
| 10 | 实际终止 worker；只读模式拒绝未完成 journal；显式恢复先于缺候选检查且幂等 | 通过 |
| 11 | 卸载保留修改及不属于安装器的文件 | 通过 |
| 12 | 安装计划独立拒绝错误 GameAssembly 指纹 | 通过 |
| 13 | 实际 V5 `-ValidateOnly` 入口：首次和已有 V5 安装均保持文件树与归属不变 | 通过 |
| 14 | 实际 V5 只读入口拒绝错误 GameAssembly，且不改文件 | 通过 |
| 15 | 实际 V5 只读入口拒绝外来 MelonLoader 目录，且不改文件 | 通过 |
| 16 | 实际旧安装入口：默认及 V4 路径拒绝隐式覆盖 V5；显式降级开关才进入后续边界 | 通过 |

本轮被实际终止的 worker PID 为 **67060**，与 marker PID 相同，父回执确认 `stopped=true`、`terminalConfirmed=true`、退出码 **-1**；矩阵本身正常完成。该记录与历史 PID 29400 分开。只读未完成 journal 检查核对了整个文件树和 journal hash，拒绝时没有恢复、清除或改写事务；后续显式事务恢复仍使用既有持久 journal 与备份。

### 安装入口修复与证据边界

`GameAssembly.dll` 现在通过普通绝对只读路径校验，安装计划另行核对独立固定 SHA256 `853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79`。原游戏文件没有加入 loader 可写白名单。固定名字的外来文件/目录探测使用只读路径检查，修复此前误用 payload 路径白名单而阻断正常入口的问题；安装与移除计划仍分别核对归属及当前 hash。

正式 `-ValidateOnly` 核对真实游戏三文件、候选 24 项、最终五 PE/44 边以及写入/迁移计划后返回。它不准备依赖、不联网、不写 payload 或安装归属，也不恢复未完成事务；可能取得安装锁。实际[只读入口回执](D:/riz-build-cache-next/engine-transport-package-install-r4-20261027/fixtures/entry-validate/validate-only-result.json) SHA256 `240a743145940e2ea34e138a7fd3aff146aac5841543068a92b117671cf071be`，明确记录 `journalRecoveryPerformed=false`、`dependenciesPrepared=false`、`payloadWritten=false`、`nativeExecuted=false` 与生产准入关闭。

旧 `Install.ps1` 在 journal 恢复之后、依赖准备之前，识别实际归属且 hash 一致的 V5 `.doorstop_version`。默认包和旧 V4 候选都需要显式 `-AllowCandidateDowngrade` 才能继续替换；这个开关不改变任何 hash、事务或生产准入条件。第 16 项实际调用与源码 hash 相同的安装入口副本，明确开关的正向检查止于隔离的依赖 stub，未执行真实依赖准备；真正旧包降级事务由第 3 项另行验证。

全部写入只发生在结果目录的合成 `fixtures/*/fake-game` 及测试副本中。与历史 11 项不同，本轮 `Rizline.exe`、`UnityPlayer.dll`、`GameAssembly.dll` 三份真实原文件都只在复制与前后 hash 时持只读句柄；错误 GameAssembly 用例仅修改合成副本。没有执行任何复制的游戏 EXE 或 native payload，没有安装到真实游戏目录，也没有把候选 metadata 中的 `false` 当成生产许可。

## 完整托管与分发

[24 套回归](D:/riz-build-cache-next/foundation-managed-full-r2-20261027/test-result.json) SHA256 `5b5f484051e446c3b5f835b5716d8dfc1852f701d46f8473c0cb5072fe32e48a`：1790 项全部通过，523 项编译来源冻结。覆盖 Core、Adapter、Hosting、Game、Runtime、SDK、Scope、Diagnostics、PackageLifecycle、Module、Mutation、Data、Storage、Services、GameResources、Hooks、Gameplay、Settings、Visual、Performance、AudioContent、UiInput、RunModification、Judgements。首轮 full-r1 在 UiInput 构建因漏拷两个链接的 examples 文件失败；r2 冻结完整真实 examples 后通过，失败原件未覆盖。

[独立 SDK 分发](D:/riz-build-cache-next/foundation-sdk-distribution-r1-20261027/test-result.json) SHA256 `f80395c7eaa1fcc1310f296bd037be9dd5369fb34d7c44ac8e20bb959ff62c97`：实际打引用 NuGet 包，在独立模板 hive 中生成两个插件、分别 restore/build，再通过正式加载器验证隔离、共享场景事件、停用及能力撤回/恢复，10/10 通过。两组合计为 25 套 1800 项，不与自有冷启动或游戏流程计数混加。

## 尚未验收

实际游戏的首次执行窗口、默认无插件/双插件性能基线、完整正常结束/失败/暂停/重试/返回菜单、任意 listener 副作用及所有未来来源仍需相应证据。原生代码和注册表保持进程级保留不等于本局根未释放；完整游戏退出链的各独立 owner 必须逐项真实完成后才移除该局 owner。当前实际证据范围见 [active 退休报告](../foundation-active-retirement-20261027/README.md)与[当前进度](../../../.docs/PROGRESS.md)。
