# 生产启动组件接线：I 批阶段验证

更新于 2026-10-26。r5 最终候选、候选包 r2、最终包的合成安装回归 r4，以及使用最终候选的四个自有 cold 进程均已有实际结果。本批候选尚未安装到真实游戏，生产首次写入许可保持源码常量 `false`，不能把这些阶段验证视为游戏底层已经全覆盖。游戏实测继续遵循用户“先继续代码，实测稍后”的安排。

| 交付 | 当前实际证据 | 尚欠 |
|---|---|---|
| Doorstop→Bootstrap 普通依赖 | r5 磁盘核验 2755 检查；17 条选定磁盘边、完整 EXE/Unity 导入及 82 项代理名称/序号；cold r2 实际执行最终代理入口 | 真实游戏运行时 IAT、启动顺序和首次执行窗口 |
| 原生生产身份 | 5 模块身份及函数体/展开合同；V4 保持 4096 字节、保留区为零；自有非游戏 Host 实际得到无写入 Disabled | 固定游戏生产准入仍未证明 |
| C# 只读采用与协调器 | 最终生成指纹编入候选；11 项编译指纹/gate 检查及 7 项 V4、6 项协调器模型通过；托管构建 0 警告/错误 | 真实游戏中的采用和全部生命周期 |
| 消费者接线 | 3 类 tracker 接入统一观察，保留 2 个 receiver/native owner 身份 | 双 receiver 死亡、取消完成、迟到调用与最终资源退休 |
| 独立候选包与事务 | 最终包 24 payload、10 evidence；安装 r4 13 场景、98 断言，含真实终止 PID 13012 后独立 PID 36652 恢复 | 真实游戏安装与运行验收 |
| 最终组件 cold 加载 | 2 正常进程各 204 条离线检查；缺 Bootstrap/Kernel 各一个真实加载器拒绝进程；四进程均符合预期 | 自有 Host 不具备游戏 Unity 链，不能证明固定游戏 17 条运行时边 |

## 查阅实际证据

| 原件 | 实际结论 | SHA256 |
|---|---|---|
| [r5 最终构建声明](C:/Users/71957/riz-build-cache/engine-production-r5-20261026/production-build-manifest.json) | native/managed 统一候选已完成；admission、game、installed 均 false | `dc84ad01002e5eccf699aba58aeff8bf7b065447ee748fd4f8e63a569f1a6fdf` |
| [cold r2 汇总](C:/Users/71957/riz-build-cache/engine-production-cold-cases-r2-20261026/fixture-result.json) | 4/4 个实际进程符合预期，全部持有输入前后哈希不变 | `b0269fe6658d45dc51baf28871a307b8f9741afbbb0cfc503c3f9a3e65c59173` |
| [最终包安装 r4](C:/Users/71957/riz-build-cache/engine-production-install-cases-r4-20261026/results.json) | 13 场景、98 断言；合成目录，不加载 native/game | `18e67f231869f55a2755f3fa8f0ba4b07dc78c9aabed17e40aa494a5fce1a0ae` |

cold 正常 PID 为 38856、20688，均退出 0；TLS、DllMain、main 三时点直接复制准确 12448 字节私有 MAP 记录，均观察 `state=5/failure=5`、`infoReads=3`、`verifyAttempts=0`，配置、绑定、提交、源写及保护职责为零。没有调用 Bootstrap ABI 污染观察计数。早期 `Close(NULL)` 的转发结果与同阶段、独立调用的固定系统原函数相等；本机实际双方均返回 FALSE/LastError 5023，验证逻辑不写死这个错误码。main 只做 NO_PROXY Open 和 Close，没有 HTTP 请求。

缺 Bootstrap PID 43176、缺 Kernel PID 44308 均退出 `-1073741515`（`0xC0000135`），没有 main 原件。此结果只证明普通依赖缺失时加载器拒绝启动，不宣称这些进程已执行 Bootstrap。正常进程明确保留 `fixedGameSeventeenEdgesProved=false` 和 `productionAdmissionProved=false`。

## 冻结输入与后续工作

r5 的 [project-sources.zip](C:/Users/71957/riz-build-cache/engine-production-r5-20261026/project-sources.zip) 固定 665 份项目源和 2 份生成文件，SHA256 为 `e5a5da23a600d5661fe0fa7ff1ef4e4f1d571b5a2f3ff11b6231a6ebd111a381`。cold v2 是之后的独立 fixture 源增量，以 [cold r2 构建声明](C:/Users/71957/riz-build-cache/engine-production-cold-r2-20261026/production-cold-build-manifest.json) 中的 9 行源码哈希为准；[cold-fixture-sources.zip](C:/Users/71957/riz-build-cache/engine-production-cold-r2-20261026/cold-fixture-sources.zip) SHA256 为 `d522f58dd4040d86badf202e3a60099d9f4251a2c71ec018bbc3c25721d07df6`，叠加核心归档内的旧 cold v1，没有重编或回写 r5 生产组件。J 批 receiver 生命周期新构建已结束，完整 Audio/content 660/660 和 Runtime owner 10/10 受控检查通过（含 23 项新增）；证据见 [J 批索引](C:/Users/71957/riz-build-cache/engine-receiver-lifetime-r1-20261026/evidence-index.json)。这些后续源码与结果不加入 I 批候选或通过数量。

早期生产 r1 链接失败、r2/r3 严格编译问题、安装 r1/r2 失败以及 cold r1 的错误码假设失败均保留原件。cold r1 的真实子进程虽退出 0，离线验收失败，不能计为通过或被 r2 覆盖。上一批 34 个自有进程及归档属于[冻结的 H 批](../foundation-engine-publication-20261025/README.md)，不与本批相加。

参见[范围](scope.md)、[时间线](timeline.md)、[正式报告](../../2026-10-26_底层-生产启动组件接线-report.md)、[项目进度](../../../.docs/PROGRESS.md)与[B01–B19 验收表](../../../.docs/foundation-expansion/CHECKLIST.md)。`RequestPlay`、取消完成和全部资源退休仍未验收，完整目标继续进行。

