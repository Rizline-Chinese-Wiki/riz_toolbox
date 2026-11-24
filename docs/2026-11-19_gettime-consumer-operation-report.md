# GetTime consumer 的实际运行检查接线（2026-11-19）

正式 Clock consumer 现在必须持有最初附加的同一个 GetTime hook lease，读取前后都核对它的实际原生通道。采样完成后再次检查当前 run、source、route、receipt 和唯一 Clock 快照，才返回读数。这样，固定 consumer 的安装证明与当前操作检查有了明确入口，getter 重入切源后也不会发布旧样本。

`GameplayClockCodeProfile.VerifyOperation(expectedHook)` 检查 registered self、owner thread 和原附加 lease 的引用身份；相同模块/MethodInfo、复制 lease 或同 owner 再签发的另一个 lease 不能借用。`GameplayClockNativeProfile` 转发前后检查模块，`NativePlaybackGetTimeCallProfile` 同样保留独立注册身份和线程约束。安装、附加与显式诊断继续核对完整映像、原跳板和固定 consumer；Standalone 运行检查也保留完整审计。正式 group 的运行检查使用已有窄通道，不缓存成功、不跨操作保留验证 scope。

固定 null MethodInfo 入口仍只有 `ProgressControl.Update` 的 CALL RVA `0x401218`、返回 RVA `0x40121d`。`PreparePausedClock` 在完整固定入口检查后核对实际 group lease；`VerifyOperationSource` 和真正原生 reader 的 `ReadClock` 在实际采样前后调用 consumer 检查。`ReadClock` 最后检查 receipt 的原生 fences、原 run/source/route 和 Clock 快照，随后不再调用 getter。

最终执行为 [build-and-checks-r4/results.json](../work/r06-gettime-consumer-operation-20261119/build-and-checks-r4/results.json)，汇总为 [validation-r4.json](../work/r06-gettime-consumer-operation-20261119/validation-r4.json)。两个 Release **增量**构建和九个检查进程共 **11 个子进程均自然退出 0**；本次构建日志均为 0 errors、0 warnings，不表示此前完整编译的 55 条测试警告已被消除。1790 项当前输入、10 个受测 DLL 和六份原生材料的 path/bytes/SHA256 执行前后相同；用户 README 保持原 SHA256。

| 当前执行范围 | 检查数 | 结论 |
| --- | ---: | --- |
| Gameplay 托管回归 | 238 | 当前源码通过 |
| 映射固定映像的 consumer profile | 20 | 真正 profile、独立 hook 和 native publication；拒绝 copy/worker/另一个 lease/相邻 null 返回；恢复字节不复活 |
| Standalone hook 正常释放及 tail 破坏 | 24 | 完整 original/consumer 审计与退出后拒绝 |
| 五个 owned 原生操作场景 | 357 | 当前真实自有 original、provider/SDK 与退出责任通过 |
| **合计** | **639** | 仅统计最终 r4，本批范围通过 |

新增 owned 场景 `operation-gettime-readback-source` 在实际 GetTime reader 的第二次 DSP getter 中调用真正 `set_MusicPlayer`，发生在媒体采样之后。原 GetTime 恰好调用一次、自然返回原始 Single `3`；source fence 增加一次、旧 Clock 撤回、旧 provider/SDK 拒绝，没有 pitch/time 写入。getter 内根保持，最后 owner release 根数为 0。该变化是正常逻辑撤权，原生 Known 保持、faults 不增加；有原生故障或 publication in-flight 的另一场景仍保留 Unknown 和七根责任，未削弱它。

本批失败全部保留：r1 用固定映像测试夹具尝试正式 source group 时缺精确 UnityPlayer/foundation，产品拒绝未放宽，改成只签 standalone profile；r2 错把正常切源要求为物理 Unknown；r3 新断言使用不存在的 snapshot 字段，独立 provider 编译失败，修为实际 `NativeFlags`。第一次封存比较原生描述对象时，借用材料多带 `kind/borrowsHistoricalResult` 字段，汇总进程退出 1；源和 DLL 没变，改按 path/bytes/SHA256 三个身份字段核对后封存退出 0，没有重跑已过检查。

`source-before/tests/RizToolbox.Gameplay.Tests/Program.cs` 是加入 dispatch **之后**的副本，不能称为改前原件；验证汇总对此标为 `after-dispatch snapshot`。其原哈希仍来自最初 implementation 输入，其他六份副本与真实改前哈希一致。当前四产品文件、三份已有测试改动及两份新测试共九项差异已记录。

**限制与下一步**：映射游戏映像没有执行 DllMain、GetTime 游戏 original 或固定游戏 consumer；owned group 没有正式 22-role Fresh/foundation/backing 准入。它们分别验证 profile 和真实自有 reader，不能拼成正式 Fresh 正向结论。当前 `NativePlaybackFreshSourceCapture.VerifyCurrent` 已接 `BeginSourceOperation`，以及同票 Clock/adoption/watch 功能已经存在；下一优先补其实际入口和 worker watcher/SuppressObserver 的离线可执行切片，不重新实现已有 scope，不把 11-role owned 夹具改标成正式组，也不放宽正式门禁。

本轮未安装、启动或操作游戏、UI 或账号，未生成新发行包，封存 R08 包保持旧身份、不能验本批新源码。R06 整体、R07 成本、R08 实机场景、真实 Speed/Seek、B01–B19 与 production 均未完成。三位新代理已统一 `gpt-6.1-sol / max`，但任务传递仍只返回询问任务，没有有效审查交付；本批由 root 实施、复核和串行执行，不将空答复计入验证。
