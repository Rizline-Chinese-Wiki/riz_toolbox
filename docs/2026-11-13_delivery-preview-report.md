# RizToolbox 2026-11-13 开发预览交付

已产出 [SDK / Runtime 预览包](../artifacts/delivery-20261113/RizToolbox-0.3.0-sdk-runtime-20261113.zip)，约 47.2 MiB。SDK 合同版本 1.2.0，Runtime 版本 0.3.0。新 ZIP 已独立解包验证，模板和原五个示例均能离线编译。此包完成本次离线预览交付，完整 SDK、B01–B19 和完整 Speed/Seek 仍未完成；本次没有安装或启动游戏。

## 使用范围

解压后先读 `QUICKSTART.txt`，公共服务说明见包内 `sdk/SDK.md`。现有示例覆盖场景订阅、面板、扩展点、颜色资源覆盖、曲目目录和成绩读取。示例数量不代表公共 SDK 的全部接口；服务可用性须按 SDK 的能力查询和 `GetService` 结果判断。本次验证的是编译和包内字节一致性，游戏内效果仍待实测。

在解压目录执行：

```powershell
dotnet restore examples/RizModProject/RizModProject.csproj --configfile NuGet.Config
dotnet build examples/RizModProject/RizModProject.csproj -c Release --no-restore
dotnet restore examples/RizToolbox.Examples/RizToolbox.Examples.csproj --configfile NuGet.Config
dotnet build examples/RizToolbox.Examples/RizToolbox.Examples.csproj -c Release --no-restore
```

离线 feed 包含 SDK 引用包及 .NET 6.0.36 的三个引用包。需要 .NET SDK 8；示例编译不向插件输出目录复制宿主 SDK 实现。安装脚本随包提供，本次未执行。

## 本次实现和验证

主树新增了 `NativeSeekLineEndpointLocalTarget`，把明确的 Canvas 槽位与原始端点数据投影为局部坐标：保留逐 Single 计算、跨 Canvas 的专用终点、物理 Transform 别名检查，并拒绝重复 Canvas index。相同 Canvas 的 EndLine 只保留原引用，不猜测相邻物理端点。该类目前是内部纯模型，没有接到现有 native 调用者，不提供完整世界坐标目标或写入事务。

| 项目 | 实际结果 |
| --- | --- |
| 最终源码图 | 881 项 solution 编译输入，另有 Build.ps1 控制 |
| 六个运行组件 | Release 构建成功，0 警告、0 错误 |
| 解决方案 | 构建成功，56 条测试字段警告、0 错误 |
| 最终增量专项 | 端点 6、数值 13、Ring X 7、原 Canvas 来源 3，共 29 项通过 |
| 非法选择器 | 2 项均以退出码 2 输出精确 usage |
| 编译字节 | 首次六组件构建、solution 后、专项后 DLL/PDB 完全一致 |
| Csc / 调试符号 | 619 项手写源码 + 18 项生成源码 = 637 个 PDB 文档；11 条新引用、22 个复制项闭合 |
| 原生复用 | 65 个托管接口接触点、126 项原生输入保持原字节，复用 5 项原生载荷 |
| 新 ZIP | 64 个成员逐项核验，23 条 runtime 安装路径，SDK 保持 reference-only |
| 独立解包 | 模板、五示例项目的 restore/build 全部退出 0；两组 build 均 0 警告、0 错误 |

上一轮 879 输入图的完整离线批次已接受，包含旧组 300/300、渲染接口 51/51、音频/内容 1001/1001。它是独立的前序证据；本次 881 最终字节只做上述增量验证，没有重新运行旧 300 项，也不声称默认 361 项单进程通过。

## 证据、结论与路径

范围：用户指定的 `C:/Users/71957/Desktop/riz_toolbox` 离线 SDK 开发和预览交付；不操作游戏、账号、代理或永久环境。报告采用通用工程结构，flavor 为 null。

| Evidence | 证据文件 | 关键内容 |
| --- | --- | --- |
| E-001 | [879 图验收](validation/line-ring-target-primitives-20261113/validation-summary.json) | 前序完整离线批次，源图与新图分开 |
| E-002 | [881 图验收](validation/line-ring-endpoint-local-20261113/validation-summary.json) | 11 个自然终止步骤、29 项专项、2 项非法参数 |
| E-003 | [最终 Csc/PDB 图](validation/delivery-preview-20261113/graph/result.json) | 实际 C 工作区输入与同一次六组件编译产物对应 |
| E-004 | [原生接口复用审查](validation/delivery-preview-20261113/reviews/native-abi-review.json) | 65/126/5 项字节与接口检查 |
| E-005 | [新包独立验收](../artifacts/delivery-20261113/delivery-verification.json) | ZIP 全成员、runtime 清单、独立解包编译 |
| E-006 | [实际退出观察记录](validation/delivery-preview-20261113/controls/execution-receipt.json) | 记录原工具会话实际退出；保留元数据首次语言解析失败和一次会话重置事实 |

F-001（validated，E-002/E-003）：端点局部计算已进入当前主树和交付字节，6 个测试用例包含原候选的 19 个模型断言。它证明局部投影行为，不证明 native target/provider 或完整 Seek。

F-002（validated，E-003/E-004/E-005）：预览包使用最终六组件和已核对的原生载荷，SDK 引用包及公开接口示例能从新 ZIP 独立离线编译。这支持开发者开始编写模组，不等同于游戏内准入验证。

F-003（open，E-001/E-002/E-005）：完整 Speed/Seek 与 production admission 继续关闭；前序 879 图回归不认证新 881 字节的完整旧组执行。B01–B19 未整体完成。

P-001（callflow）：冻结四文件补丁 → 新 881 图六组件及 solution 构建（E-002）→ 29 项专项与精确退出检查（E-002）→ 真实 Csc/PDB 与原生接口核对（E-003/E-004）→ 打包 → 新 ZIP 独立解包与 QUICKSTART 编译（E-005/E-006）。对应 F-001/F-002；F-003 中的运行时与完整事务缺口仍保留。

## 下一离线工作

1. 优先复核并接入 `work/line-chart-occurrence-20261113/` 的物理 Line → chart segment occurrence 候选，补无 Note 的来源覆盖。该候选仅有独立旧引用图上的 34 条模型检查，尚未合主；合入后须在新源码图重新编译执行，不能沿用候选测试结果验收新字节。
2. 将端点 slot/parent 局部模型接到正式来源 provider，加入临用前来源核对；继续补目标 world/camera/scene、同 Canvas 物理绑定及 canonical Ring seed，禁止用 baseline pose 或默认零值代替来源证据。
3. 补 scratch alias/renderer、Hold 引擎退出及实际 writer，再与 Pause/Replay/Resume occurrence、DSP schedule 和完整时钟事务闭合。继续按 N2 实际值请求与事务合同推进，不重做已接受相机、N1、退休批次或冻结的 rate/float/Play 批次。

真实游戏验证继续后置。功能优先、性能后置，不添加旧版本兼容层。以 [.docs/CURRENT-CHECKPOINT.md](../.docs/CURRENT-CHECKPOINT.md) 顶部为续接入口。

## 校验

ZIP SHA-256：`ffe59188b7fcd28df5b0501b15172f52871d0bdceccb9525909d8a2ec53f350a`。

旧 2026-11-07 ZIP 保持原字节：`24c5ab5008027fc574992b1688ba399fc61b930925c213f001256a206b23ac0e`。新包校验文件位于 [SHA256SUMS.txt](../artifacts/delivery-20261113/SHA256SUMS.txt)，完整交付证据位于 [delivery-preview-20261113](validation/delivery-preview-20261113/validation-summary.json)。
