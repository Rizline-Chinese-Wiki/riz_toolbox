# 范围与授权

本批继续用户授权的 PC MOD 兼容底层，遵守“先继续代码，实测稍后”。`auth` 为当前会话中的代码实现、构建、离线验证和文档授权；`network_profile` 为本地文件及离线 Windows x64 fixture，没有访问游戏网络、操作 UI、安装新包或启停游戏。

`in_scope`：把 LevelControl.Start 和 NormalMode.GetInsideLevelParams 的生产 detour 改为 Prepare → Configure → Commit 发布的独立 typed native transport；令原调用及其 SEH/C++ 异常展开留在 native 栈上，managed Enter/Exit 只承担作用域、回调和责任记账；为既有 run record 提前预留两枚身份令牌和固定发布槽；验证原生异常、发布失败、身份稳定及部分采用；冻结源码、暂存包、实际执行输入和有限审查记录。

固定目标画像沿用 GameAssembly SHA-256 `853d6530d694079cf1cd596b3362c5bcb647da211541a008d7463999bc5b1b79`、Unity 2022.3.62f2、Windows x64。入口依据是 [初始化静态证据](../local-initialization-order-20261023/README.md) 和 [I01–I08 计划](../local-initialization-order-20261023/next-initialization-isolation.md)。本批 fixture 使用自己编译的 C++ originals，不把它当作游戏 prologue、metadata、IL2CPP 对象图或真实 Unity 调度证据。

权限保持源码事实：已认领 Start/getter scope 可隔离 Mode 与 Tooltip，独立 Play 仅隔离 Mode；ordinary 子作用域遮蔽旧权限。Mode 继续复用既有 Progress entry。新 native-original unwind 证明只覆盖 Start/getter；Play、Progress.Update 和 Timeline original 仍由既有 managed transport 调用，不由这批结果签发同等异常保证。

`out_of_scope`：真实 Loader/模块 LOCAL 启动调用点、launch/activation 协调器、实际 Awake/settings/six-globals 发布及条件恢复、commands、result view、Retry/Back、已激活局完整消费者结束与退休、游戏内连续会话、全部 Save/Steam 间接或异步路径。`LocalCatalogService.Selection.RequestPlay` 仍明确返回 OperationUnsupported。

完整构建为 1216/1216、25 套；两个历史 Judge helper fixture 的 31261/61358、新 initialization helper 的 1747 个断言以及 native-only ABI probe 的 18 次调用分别记账，不加进 suite 总数。独立 runner 使用本批同 SHA 的 Runtime/SDK/helper 与冻结测试源再次执行。新包仅暂存，实际安装仍 [1021 四门批](../foundation-local-protection-host-20261022/README.md)。本批不新增 B01–B19 整项或子项勾选，不将完整目标缩小为初始化一段。
