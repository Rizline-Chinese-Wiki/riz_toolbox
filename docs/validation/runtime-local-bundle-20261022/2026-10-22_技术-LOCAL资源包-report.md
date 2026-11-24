# LOCAL 原生资源包实现与验证报告

2026-10-22，`flavor = null`。本轮交付每局独立资源图及异常责任交接，完整 999 项回归通过，已安装包在真实游戏获得两包独立退休证据。完整基础架构仍未完成。资源包尚未进入 LOCAL controller，公开播放入口保持关闭。

## 范围

[授权与范围](scope.md)包括本地源码、已授权游戏安装及原创测试资源。本轮没有新增二进制逆向或更改原游戏文件。既有精确签名与构建指纹继续适用，三原文件 hash 及 18 安装文件核对见本批构建目录。

## Evidence

| ID | source_ref / hash | 可复核观察与命令 |
|---|---|---|
| E-001 | [独立源码审查](../foundation-local-bundle-20261022/independent-review.md)及其[输入 hash](../foundation-local-bundle-20261022/review-source-manifest.json) | 输出在临时清理前同步交接；CPU fork 精确 pin revision；五 direct root、上传 slot、延迟销毁与 claim 校验均可定位源码。复核 `dotnet run --project tests/RizToolbox.Runtime.Tests -c Release` 与 AudioContent 对应项目 |
| E-002 | [完整构建](../foundation-local-bundle-20261022/build.log)，SHA256 `a0c779daafa32445144daf3d1dbe801584204788b9594f717b5395f93f39eb42` | 999/999、25套；Runtime115、AudioContent212、Hooks61。原生fixtures是本机 helper/Dobby 验证，不能代替游戏接线证据 |
| E-003 | [runtime-proof.json](runtime-proof.json)中的 files 各自记录原始 events/session/package 的 SHA256 | 879 连续事件，0Error/7Warning，seq728–840：两图独立、撤回/真实GC读回、两次后帧销毁与CPU2→1→0。复核命令见[README](README.md)；需要完整停止会话和对应安装包 |
| E-004 | [进程停止](process-stop-check.json)、[恢复检查](probe-restoration-check.json)、[安装核对](../foundation-local-bundle-20261022/installation-check.json) | marker 恢复 Absent、三原创fixture原样冻结后清理；18安装hash匹配。退出操作未观察到，原因未知，不能写成正常退出 |

## Findings

| ID / status | 结论 | evidence_ids / location / confidence |
|---|---|---|
| F-001 / validated | 两份包保留相同精确CPU来源，各自创建metadata、资源、Chart、JudgeData、params。跨来源撤回/GC和第一包销毁后第二包仍可读，最终各自退休 | E-001,E-003；NativeLocalPlayBundleFactory/LocalNativeBundleProbe；high，仅限本次小型原创fixture |
| F-002 / validated | 新显式publication路径在临时清理故障时不丢 caller 输出；未知临时free保记录和额度且不重试 | E-001,E-002；NativePublicationCall/NativeRuntime.SingleArray/NativePublicationRegression；high，原生故障注入为受控exports，非所有游戏异常矩阵 |
| F-003 / validated | 每包 direct root 和来源观测根/CPU fork/template 均达到探针规定的退休状态 | E-001,E-003；TryCompleteDestroy/seq840；high，不等于native heap/GPU归零或长期GC验证 |
| F-004 / incomplete | LOCAL启动闭环仍缺首次结果receiver归属、生产前置门、初始化/激活及真实消费者结束验证器 | E-001,E-003；CanLaunch=false与LocalCatalogService.RequestPlay；high，不能勾选完整B15 |

## P-001：构建、交接与退休

`path_type=callflow`。精确 CPU request fork（E-001）→复制真实普通局 JudgeData、创建独立完整图（E-001/E-003，F-001）→输出先登记固定slot再临时清理（E-001/E-002，F-002）→撤回provider/catalog/content和caller后执行原生GC（E-003）→第一包Destroy后帧确认释放，第二包仍存活（E-003，F-001）→第二包后帧确认释放、CPU prepared0、source根0（E-003，F-003）→停止后冻结与marker/fixture恢复（E-004）。

## 时间线和下一步

- 11:23:18 UTC：本会话开始；等待普通局的完整就绪来源。
- frame13996：两套图构造成功，各5 direct roots；四复制窗口0.14/0.18/0.07/0.2。
- frame13997–14177：撤回/GC与两次分帧退休完成。
- 收尾：独立核对进程已停止，完整879事件冻结；没有观察最终退出动作。marker/fixture恢复、安装/源输入/原文件再核对通过。

下一步由宿主建立实际Start/Retry/Back前置保护和首次结果页归属，确保provider/模块撤回不提前放开原版路径，再接精确参数与原版设置初始化、受控激活、Awake正向一次和六globals归属/条件恢复、真实native消费者结束证明。不能用日志0Error、存档hash或CPU测试数替代该闭环。完整B01–B19仍active，仅B03整项及B18a子项已验收。
