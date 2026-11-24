# 自有早期加载 fixture 审查

2026-10-25。**最终完整构建 session 99557 已实际 exit 0；本次有限审查未发现阻断自有宿主交付的缺陷。** 25 套 1617/1617 回归与额外 39 个子进程、7316 条父断言分列，不能当作完整底层覆盖率。新包未安装，游戏生产安装和 loader 全依赖安全仍未获证明。完整 B01–B19 仍有效，仅 B03 整项、B18a 子项已验收。

## 审查范围与方法

本次对 [Proxy](../../../native/RizToolbox.EarlyLoaderFixture/Proxy.cpp)、[Recorder](../../../native/RizToolbox.EarlyLoaderFixture/Recorder.cpp)、[父进程](../../../scripts/Test-EarlyLoaderFixture.ps1)和[独立验证器](verify-early-loader.py)作有限交叉审查，同时读取实际 PE、共享映射、进程终态与 debugger 原件。没有重新构建、执行 fixture、启动游戏、安装或操作 UI。

审查者是 Importer、Host、OwnedTargets.asm 和 ImporterApi.h 的作者：这些文件的复核属于作者自检，不称独立第三方审计；Proxy/Recorder/父验证器属于其他代理实现的跨组件复核。本审查不能证明全部线程历史、OS API 内部和未执行的 Dobby 分支安全。

## 已核实的机制

最终完整构建中的[正式宿主汇总](early-loader-evidence.json)为 38 个普通子进程和 1 个额外 debugger 子进程，39/39、7316 条独立父断言通过；已和[归档原件](native-build/early-loader-cases/fixture-result.json)及最终 `early-loader-aligned-final-20261025` cache 实际比较一致。原件共 324 次原调用、168 对观察、24 次原生异常捕获。普通 normal/worker 子进程分别有 86/92 次原调用，各 56 对观察；额外 debugger 的 86 次原调用计入上述总数，不能再重复相加。所有计数只描述自有宿主。

| 最终证据 | 本审查实际只读复核 |
| --- | --- |
| [实际终态](build-run.json)、[wrapper](build.log.run.json)、[汇总](build-summary.json)、[日志](build.log) | session 99557、exit 0；25/1617；94 条去重原始编译诊断；四份 log hash 一致，为 `30a1c36770e52318c3bd0da8e9e8de2e6c16342e16b14b7799ddf3f0c351fc65` |
| [源码 ZIP 核对](source-archive-check.json)、[源码 manifest](source-manifest.json)、[输入前后](build-input-integrity-check.json) | 实读 ZIP 576 条，文件名集合完全一致、逐项 hash 匹配、testzip 无损坏；576 声明输入前后无漂移。ZIP SHA256 `848cd15849c38694dc3e445f9bd26a0e9560cb4cd80cc1a242a5ae4fb8b40e38` |
| [包 ZIP 核对](package-archive-check.json)、[包 manifest](package-manifest.json)、[暂存](staging-check.json) | 实读 ZIP 22 条，文件名集合完全一致、逐项 hash 匹配、testzip 无损坏；暂存 22 项一致。ZIP SHA256 `c448ff03cd6e2f1f34a3d7209403845ce858aea291de40b835a107560c39d086` |
| [旧安装](previous-installation-check.json)、[原游戏](original-game-check.json) | 证明文件记录旧安装 18 项、其 326 源 ZIP 与原游戏 3 文件匹配；本审查未重新操作游戏目录或安装 |
| [39 子进程原件](native-build/early-loader-cases/fixture-result.json) | 39/7316；普通 normal/worker 86/92 原调用，全部原件共 324 原调用、168 对观察、24 次捕获；正式汇总和两处 raw 复制/最终 cache 一致 |

1. PE 保留普通静态依赖 `Host → Importer → Proxy → Recorder/helper/Dobby`，Importer 具有真实 TLS callback；四个非转发导出有固定 MASM 目标前缀。源码中 `.CRT$XIU` 的 C 初始化器、C++ 全局构造器和 DllMain 都实际调用四目标，Start 同步调用真实 Execute/Release。不是用预期顺序拼接模拟日志。
2. [normal 原件](native-build/early-loader-cases/mode-0/result.json)中 Ready 序号 34 早于 TLS 初始化进入 35，随后依次执行 TLS/C/C++/DllMain。[额外 debugger 原件](native-build/early-loader-cases/normal-debugger/result.json)实际观察 helper 的 TLS/entry 返回、Dobby entry 返回均早于 Proxy 安装；Ready 48 早于 Importer 实际 TLS callback 50，C/C++/DllMain 位于 Importer entry 的 75–145 之间。该补充使用临时内存断点，必须和无 debugger 主样例区分。
3. 四项 Prepare 全部完成后才 Configure，四项不可变 Configure 全部完成后才 Commit。每个 tuple 保留实际模块/hash/RVA、target/original/entry/epoch、原始及发布字节、保护和写序号；正常原调用转发、完整 Execute EAX、嵌套、加载后 Attach、同线程 LIFO 和原 SEH/C++ 展开有实际事件及断言。
4. [迟到 mode-2](native-build/early-loader-cases/mode-2/result.json)有额外的真实工作线程：worker 执行四目标后阻塞，主线程在 loader 窗口外等待确认它尚未结束，再尝试安装。原件保留 WorkerStarted 和额外调用，没有 WorkerFinished；准入以已有历史拒绝，写计数为零。不是只设置“已有 worker”标签。
5. 部分发布故障保持真实责任。[fault-5-2](native-build/early-loader-cases/fault-5-2/result.json) 在第三项 Commit 前保留两次已完成写入；[fault-6-2](native-build/early-loader-cases/fault-6-2/result.json) 在第三项 Commit 后保留三次，均为 Unknown，未放行 Importer、重试、卸载或假回滚。零写拒绝仍可能保留已 Prepare/Configure 的永久记录，DisabledNoWrite 不表示“从未配置过”。实际日志溢出也保留 Unknown。

## 拒绝场景的实际边界

| 场景 | 本批实际制造的条件 | 不能替代的验证 |
| --- | --- | --- |
| wrong-hash | 改变 expected hash，与未改动的真实文件比较 | 实际换入篡改模块及其全部加载行为 |
| wrong-architecture | AMD64 实图对 I386 expected | 在 32 位进程加载或执行 |
| wrong-prefix | 实际 `90` 对 expected `CC` | 实际修改目标前缀后恢复或并发篡改 |
| wrong-ABI | 当前 helper 返回 2，对 expected 3 | 实际旧 helper、缺导出 DLL 的加载 |
| missing-dependency | 查询假依赖名称，固定 Dobby 仍静态加载 | Windows 因缺少普通静态依赖而拒绝进程 |
| dependency-not-ready | selector 进入拒绝分支 | 让真实 helper/CRT 保持未初始化后调用 |
| 24 处故障 | 每项成功 Prepare/Configure/Commit 的前后边界注入 | Dobby 内部 OOM、非零返回、保护失败或机器码写到一半中断 |

reentry、duplicate、prior-call、late-worker 和 recorder overflow 则有对应的实际重入、重复调用、目标调用、工作线程或定长日志写入事件。上述区分必须保留，不能把所有 selector 都写成真实系统故障矩阵。

## 失败史与最终验收门

- 专项 session 52575 已实际结束并用于上述有限预检，但不是最终完整构建。
- 首次完整构建 session 46741 实际 exit 1，不能列作通过。冻结后 `EarlyLoaderShared.h` 与 `Proxy.cpp` 两项源码漂移，新增 OverflowProbe event 17；旧验证器在 mode-12 严格报告 `Unknown event id 17`。其日志、输入与失败原件保存在 [before-overflow-event-align](before-overflow-event-align/build-run.json)，没有改写为绿色结果。
- 后续只补入 event 17 的明确事件字典，再重新冻结 576 项输入并另开完整构建 99557；未知事件拒绝、完整行/序号/原件/终态校验不放宽。本审查已核对 99557 的实际 terminal、576 源归档、22 包归档和最终 39 个子进程原件。最终结论来自这次新构建，没有沿用 52575 或 46741 作为最终通过凭据。

## 尚未获得的结论

[固定 Dobby 有限静态审查](dobby-loader-audit.md)发现 CRT 堆、lazy singleton、日志/文件流/锁、迟到 LoadLibrary 候选和未解间接调用。正常样例没有触发错误日志，不能证明这些分支在 loader 阶段安全。debugger 看到 helper/Dobby attach 返回，也不能替代全部依赖或失败闭包。

本批尚未证明真实系统 WinHTTP 转发、游戏四入口生产首次安装、V4 凭据、coordinator 的真实对象/GC、scheduler/async/Start 内联清理、所有来源/消费者封口和完整资源退休。`allTransitiveLoaderDependenciesSafeVerified=false`、`gameFirstInstallVerified=false`、`EngineCancelled=false`；Pending 保留，RequestPlay 关闭。下一段是另行实现并验证固定四入口的最小发布器，而非将现有 Dobby producer 直接移入游戏 DllMain。
