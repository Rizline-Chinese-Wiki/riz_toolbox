# Rizline 玩法时间与有值控制事务

本包交付当前 SDK 的有限数值/单位/RunId/ControllerGeneration 请求、控制准备到恢复读回的服务状态机，以及固定构建的只读游戏坐标↔进度秒适配。fresh 隔离源码实际通过玩法 **60/60**、局审计 **17/17**、SDK **10/10**；三个 Release 构建均零警告、零错误。受控后端实际修改音频/游戏/调度速率或定位相关状态，再逐参与者读回，覆盖六个部分提交故障和独立恢复验证。固定 PE/元数据另通过 10 函数、5 类、34 条指令/常量与 5 份生产 profile 正文哈希核验。

**本包没有实际启动、停止、安装或操作游戏。原生 SetSpeed/Seek 仍未开放，真实时钟校准、结果及提交影响没有验收；完整 N2/B11 和 B01–B19 均未完成。** 新代码未改正式 native 布局、FirstInstall 或打包，也没有混入另一条 Visual 工作。`flavor = null`，这是普通固定二进制语义与工程验证，不含攻击、IOC 或 ATT&CK 结论。

## 范围与重现

授权、样本和工具边界见 [scope](scope.md)，实际过程与失败原件见 [timeline](timeline.md)。依赖为 root 已捕获的 N1 r2 `source-snapshot.zip`，SHA `de6301e06475931a6604a3a4758fb7c49543d6718cc07484bd0e121ecc91bb11`；只覆盖本代理拥有的 Gameplay SDK/Adapter/测试/说明。隔离源共 603 文件，最终归档 SHA 为 `90c2993f1a68da4f25b5d17ef47618a8f2d67d0a2d23d837606bcd6d2939cadb`。

下面命令从仓库根运行；输出目录/文件必须尚不存在，脚本不会覆盖历史原件。需要本机既有固定样本、dump、Python pefile/Capstone、.NET SDK 以及指定 N1 依赖归档。

```powershell
C:/Python314/python.exe docs/validation/foundation-gameplay-values-20261029/run-checks.py --output D:/riz-build-cache-next/foundation-gameplay-values-replay-20261029
C:/Python314/python.exe docs/validation/foundation-gameplay-values-20261029/inspect-clock-profile.py --output D:/riz-build-cache-next/foundation-gameplay-values-static-replay-20261029/clock-proof.json
```

最终实际输出为 `D:/riz-build-cache-next/foundation-gameplay-values-isolated-r3-20261029/` 和 `D:/riz-build-cache-next/foundation-gameplay-values-static-r2-20261029/`。仓库内保留同字节的 [test-result](test-result.json)、[clock-proof](clock-proof.json)、[source-before](source-before.json)、[source-after](source-after.json) 及日志。完整冻结项和哈希以 [evidence-index](evidence-index.json) 为准。

## Evidence

| Evidence | source_ref / content_hash | repro_command 与观察 |
|---|---|---|
| E-imports | [clock-proof.json](clock-proof.json)，SHA `f438375ddee7beb334b477dce78b3d0e4d699c2713de023897db2e45b5975c4f` | `inspect-clock-profile.py --output <fresh file>`；PE 实际导入 KERNEL32 148、USER32 1、ADVAPI32 4、ole32 7、OLEAUT32 11、SHELL32 2、WS2_32 37、IPHLPAPI 3、dbghelp 2、baselib 13 |
| E-exports | 同 E-imports | 同命令；实际 386 导出。样本 SHA `853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79`，dump SHA `092cdb96914ccb7af3f31d9d2e703e00dd6ddff63fd559f11ac6b42e6635c1cb` |
| E-clock | clock-proof 的 metadata、instructionAndConstantChecks、functions | 同命令；Progress.Update 生产/暂停分支、BPM 双向分段、ChartDelay/1000、offset 哨兵、MusicPlayer 的 DSP/AudioSource 回退与原点写入、Note/Line speed 消费 |
| E-profile | clock-proof 的 runtimeProfileBodies/sourceSha256 | 同命令；独立 PE 解码与生产中五份方法正文 SHA 完全相等；实际元数据精确入口和内存正文的运行时检查已编译，未在游戏调用 |
| E-tests | [test-result.json](test-result.json)，SHA `c03a95bda40d6059fdd2f1c471d693139a97390e65040a4841eccbac6b2c9f06`；Gameplay/RunModification/Sdk 日志 | `run-checks.py --output <fresh directory>`；三个实际 build/test exit 0，60/60、17/17、10/10 |
| E-freeze | source-before/source-after，均 SHA `45a3f3170159aef0de10d9390d4992d7b8985fbd86df18b39c567343ba26bff4` | 同命令；603 输入构建前后相等，受权工作区 overlay 也逐项匹配；归档保存 N1 依赖及本包输入，没有采用 live Visual/native 修改 |

IDA 入口技能、先例和工具索引已读取；索引无可用 IDA/MCP，使用已有 Python 3.14、pefile 2024.8.26、Capstone 5.0.6 静态回退。没有新增工具或 GUI。E-imports/E-exports 先于新方法推导核验。

## Findings

| Finding | severity / evidence_ids | location、confidence、status 和准确边界 |
|---|---|---|
| F-units | n/a_re；E-clock、E-profile | `ProgressControl.Update@0x400FE0`、转换 `0x400C80/0x400400`。high、validated(static)：nowTime 为 BPM 分段坐标，nowRealTime/nowSyncedTime 为同一内部 double 的 float 秒值。`SecondsMappingVerified/RuntimeVerified=false`，静态已知单位不等于真实动态校准 |
| F-domains | n/a_re；E-clock、E-tests | `GameplayService.Read/Convert`。high、validated(controlled/static)：游戏 seconds 与 chart 坐标可绑定同局做有限值转换；offset、ChartDelay 分开，capture 起止包含转换耗时。DSP/host 仅样本区间，速率/延迟 null；切局不发布旧局映射，NaN/Inf 分来源不可用 |
| F-values | n/a_re；E-tests | `GameplayControlRequest`、`GameplayService.Execute`。high、validated(controlled)：正速度倍率或有单位定位请求通过独占、run/代次和状态前置，受控后端实际更新全部参与者并确认有限实际值/单位；不是新增 enum 拒绝测试 |
| F-restore | n/a_re；E-tests | `GameplayPreparedControl.Restore/ConfirmRestored`、受控 `ValuePreparation`。high、validated(controlled)：六个部分失败、错误/NaN 确认、stop/重入、外部写入与恢复异常都检查原状态。恢复后独立读回全部基线才签 Restored；无法确认则保留未知副作用审计、释放租约并撤回该 action |
| F-native-gap | n/a_re；E-clock、E-profile | `MUGMusicPlayer/ProgressControl` 元数据、`GameplayNativeBackend.Probe`。high、candidate boundary：没有完整同步速率或任意定位及音符/判定/hold/音频调度恢复事务证据，原生两项继续 OperationUnsupported。单字段 speed/AudioSource.time 不构成完整实现 |

## Path

P-clock，`path_type=callflow`：`MusicPlayer.GetTime - Progress._offset`（仅静态观察，当前读时钟不调用这个可能改写原点的 getter）→ `_nowTime`；非播放/暂停区间保持，levelOver 分支可按 DeltaTime 推进 → nowRealTime/nowSyncedTime 复制秒值 → `RealTimeToChartTime` 生成 nowTime；`LevelControl.Start` 反向调用 `ChartTimeToRealTime` 写 Note 的 realTime/holdEndRealTime。依据 E-clock，得到 F-units；SDK 读取同 rooted capture 的字段/原转换结果并界定 host capture 区间，得到 F-domains。

P-control，`path_type=callflow`：当前 scope 取得独占 lease → 有限值/单位/RunId/ControllerGeneration 校验 → 新 capture 的实际暂停/就绪前置 → read-only Prepare 保留所有音频/游戏参与者及基线 → 再核 ownership/取消/局/状态 → Commit 一次 → 实际值和单位 Confirm。失败则 Restore 后独立 ConfirmRestored，已恢复进入既有 journal 的 Restored；未知进入 InvocationFailedSideEffectsUnknown、撤回 action。Dispose 只释放资源，不自动撤销已确认命令。依据 E-tests，得到 F-values/F-restore；受控后端状态和 native 支持边界分别记录，未越过 F-native-gap。

## 验证及局限

60 项玩法检查包含既有 30 项及新增 30 项。新增检查覆盖实际速率、两个定位单位、双 BPM 段、暂停/恢复、无 RunId、重用对象新局、双插件竞争、完整准备失败、2 操作×3 部分提交失败、假/错单位/非有限确认、恢复后被外部改变、未知副作用撤回、stop/重入、时钟转换耗时区间、NaN/Infinity、读取/转换中切局及根延后释放。17 项共享 journal 回归与 10 项当前结构化 SDK 回归也实际运行。

这些是同冻结源码的受控服务与静态构建证明。原生只读适配尚未实际运行，源单位/offset/ChartDelay 的动态范围仍待同候选游戏流程验证；原生值控制仍需要完整参与者与恢复实现，不能仅补一个 setter 即开放。结果/提交 verified 标志始终 false。没有全量性能测试、正式原生新 ABI、游戏安装或发行候选变更。

timeline 的 diagnostic-r1、live diagnostic-r2 和 isolated-r1 失败均保留原件。r2 的 58 项为中间成功；最终 r3 的 60 项及 static-r2 才覆盖当前冻结源码，不能用 N1 的完整矩阵或历史游戏包替代。
