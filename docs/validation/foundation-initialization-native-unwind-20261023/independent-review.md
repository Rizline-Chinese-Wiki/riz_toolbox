# 有限交叉审查与归档复核

本记录覆盖 I04 发布存储的只读交叉审查，以及冻结后源码/发布文件和测试证据的一致性核对。文档代理也参与了本批 native ABI fixture 实现，不能称为完全独立的源码作者审计。完整构建与独立 runner 由主任务执行；本文代理没有重跑主任务的 dotnet suites/runner，也未安装或操作游戏。它单独构建过 fixture 并执行 18-call probe、4 条直接 native original 异常捕获检查；这些只是 native-only 检查，不冒充 production broker 结果。

## 实际读取和重新计算的证据

1. 读取 [完整日志](build.log)及[摘要](build-summary.json)，重新计算日志 SHA，确认 25 套合计 1216/1216、Runtime131/Hooks99/Audio375，真实 Dobby production 初始化 ABI 用例为 PASS。日志没有 compiler warning/error 诊断行；这是本轮增量构建的事实，不是无缓存构建结论。
2. 实际打开 [371 项源码 ZIP](source-inputs.zip)和[18 项暂存 ZIP](staged-package.zip)，检查成员集合、CRC、每项 SHA，并与当前声明的 source/artifacts 逐项比对。未修改 manifest 或归档。ZIP SHA 分别为 `f3863e3dc70751f6bcfcb85aef8cda1534f0c2875826580ea16ab403c27d57f8` 与 `61c40626b73e4c97cc5956dd7ef54af79e1ba3d592d149319cb5434990d2577a`。
3. 实际重新读取游戏目录的 1021 安装 18 项与原游戏三文件，各自匹配本批 check 中的 expected/actual。旧 326 源 ZIP 的核对读取自主任务生成的 [previous-installation-check](previous-installation-check.json)，没有把新工作树当作旧安装源码。
4. 实读 [runner result](independent-native-abi/fixture-result.json)、[编译日志](independent-native-abi/build.log)、[执行 manifest](independent-native-abi/fixture-manifest.json)、[package-match](independent-native-abi/package-match.json)、[input integrity](independent-native-abi/evidence-integrity.json)。重新计算 7 个原执行路径及 7 份归档输入的 SHA；确认 Runtime/SDK/helper 三者匹配暂存 manifest，测试源匹配 source manifest，native fixture/helper 匹配本批归档。runner result 为 passed，编译 0 warning/error；主任务执行报告 exit0。
5. 读取 [native helper result](native-build/initialization-context/fixture-result.json)：1747 checks、0 failures，真实 Dobby、Prepare/Configure/Commit、配置竞争、Start/getter 原 SEH/C++、worker、callback containment；两 kind 均 entered=exited=32、inFlight=0。它与 1216 suite 总数分开。

## 源码交叉审查结论

| 审查点 | 当前可确认行为 | 仍未证明的部分 |
|---|---|---|
| 原生发布 | 两 kind 使用独立不可变配置；native Configure 在 Prepare 后、Commit 前；unknown/失败 entry 保留责任，拒绝 managed entry 原位升级 | 不承诺任意游戏线程 patch 原子性；完整游戏安装未测 |
| native original 异常 | typed helper 原调用无本地 catch，仅 native finally；Enter/Exit callback 被各自包含。production fixture 原故障发生在安全 managed observer 返回之后，由原生 caller 捕获原 code/typed payload，避免测试把异常穿过 CLR | Play/Progress/Timeline original 仍是 managed 路径；本批不覆盖其 native unwind |
| 预留 token 与固定槽 | Record 构造时先创建零 scalar token；完成后才 Volatile 发布。classifier 只读发布槽，不读 root/native。token 发布后永不改写，dictionary key hash 保持稳定；槽复用建新 Record/token | 不是全系统无分配；新测试未直接做 controller map 撤回/再排空/复用矩阵 |
| 退休顺序 | 当前源码先撤 progress/controller 发布，再检查真实 entry drain，之后 dispose 根，最后释放 record slot；未知保留 process owner | 新回归只直接覆盖 progress slot 复用及既有 progress withdrawal 场景，不能推成 controller map 新实测；active 完整消费者结束仍缺 |
| 权限边界 | Start/getter claimed scope 均可隔离 Mode+Tooltip，Play 仅 Mode；ordinary 子 scope 遮蔽旧权限，logical detach 不抹当前 claim | 不能推导所有 Steam 间接/异步入口已覆盖 |

执行过 `rg -n 'NativeLocalRunTerminationBoundaryState\.Create|new LocalRunTerminationHandler|NativeLocalRunLaunchReservation\.Reserve|LocalRunTerminationHandler\(' src`；当前结果只有 handler 构造声明，没有 Loader/模块生产调用点。另读取 `LocalCatalogService.Selection.RequestPlay`，仍返回 OperationUnsupported。这些是未接通用户 LOCAL 启动链的直接证据，不能把离线生产实现接线写成游戏功能已启用。

## 交付结论的限界

本轮有限核对没有发现上述发布改法新增的确定缺陷，且已读测试/归档与当前声明输入相符；它不证明代码没有其它问题。原生 ABI 用例验证的是自建 C++ originals 与生产 broker/helper 的输送。实际游戏 metadata/prologue、真实 activation/Awake/globals、commands/result view、Retry/Back、active 退休、连续游戏对照仍缺。当前安装继续为 1021，本批新包未安装；不新增 B01–B19 勾选，不把离线计数转换成底层完成率。
