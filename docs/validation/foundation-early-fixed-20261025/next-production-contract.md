# 接下来实现游戏首次接入与 CLR 只读采用

本批已经把固定四入口 Publisher 接入真实自有初始化链。[73 个原件](README.md)关闭的是“两个独立成功的组件尚未接通”这一缺口；生产游戏合同仍须独立实现。此处继续完整 B01–B19，不把受控模块标记改名后当作游戏授权。

静态前提已另批落地：[WinHTTP 契约](../foundation-winhttp-contract-20261025/research/game-winhttp-contract.md)从四份实际 PE 固定了 `WinHttpOpen`、`WinHttpGetProxyForUrl`、`WinHttpGetIEProxyConfigForCurrentUser`、`WinHttpCloseHandle` 四个按名导入，代理与系统导出序号不同。它提供实现名单，不授真实解析/转发或首发时机证明；四槽也不覆盖其他模块的全部 WinHTTP 使用。

下一个编码单元先收敛 producer 的可控依赖：独立构建 `RizToolbox.EngineNodeKernel.dll`，直接复用 canonical `native/RizToolbox.JudgeContext/EngineNodeContext.cpp`。将四个入口表改为 typed constexpr 指针表，GetEntry/IsEntry 边界才转 `void*`；保持8个 ABI2 export、32永久槽及实际完整 EAX/SEH合同。新 builder 用 `/NOENTRY /NODEFAULTLIB` 硬核无 CRT/TLS/动态初始化依赖，复用现有 native/managed fixture 验证；不复制引擎逻辑，不覆盖旧59-export JudgeContext。现有 FixedPublisher::Bind 精确要求旧模块身份，生产 bootstrap 和新集成链应独立绑定新 Kernel，不能把两个模块的槽/epoch混用。旧 COFF 已观察到静态折叠只是可行性参考，最终新 DLL 仍须实际编译、核 PE 并执行验证。

1. **实现独立生产 bootstrap。** 先从当前固定游戏 EXE、UnityPlayer 的普通静态导入与真实系统 WinHTTP 导出确定最早可用入口、转发命名和依赖放置。把实际游戏映像 hash、PE/x64、目标 RVA、完整指令前缀与原函数桩一起固定；当前 `RizFixedPublisher` 的自有模块名、marker 和导出白名单不能直接泛化为任意模块配置。游戏中已有 Doorstop 的 pre-il2cpp_init 回调不证明 UnityPlayer 首次相关执行尚未发生，不能用它默认签发凭据。
2. **只在可信首发阶段发布。** 生产 producer 单次 Prepare 全目标、不可变 Configure、Bind、Commit，保留真实 write/protection/cache 事实和所有永久槽责任。任何无法证明首次执行前时机、依赖就绪或未发生竞争的情况拒绝发布；运行后补 patch 不能补造“冷启成功”。故障保留 Unknown，不重试半写目标。必要外部初始化必须有实际依赖/时序证据。
3. **定义独立 V4 事实凭据及采用路径。** 凭据携带精确双映像身份、producer 来源/代次、四目标原始与发布字节、helper ABI2/epoch/slot 和不可伪造的进程内归属；CLR 只读核对已发布事实并 Attach。旧 V3、fixture receipt、外部 JSON 和改哈希的自报信息均不能授予生产 V4。先实现真实 producer 再开放 consumer，不能先用测试构造器填“成功”。
4. **接生产对象与责任。** 把真实 coordinator、root、managed receiver、native owner、Enumerator/Coroutine 与 engine node 代次接通，并做 GC/失效证明。补 scheduler 登记、消费、排空、异步 future 来源、Start 内联清理及每条 writer/delegate 消费者；最后才可生成取消完成和资源退休凭据。当前 Release0、Detach、请求原函数返回或无活动快照均不能单独完成取消。
5. **完成发布与真实流程验收。** 加入统一构建和包清单，未知版本拒绝，回滚与旧安装归属仍可核对。在用户恢复实测后，验证冷启动、暂停恢复、正常/失败结算、重试、返回菜单再开局与 Loader 重建；同时完成相关 B 项的双插件共存、撤回、对象重建和性能证据。

实现优先级是 bootstrap/系统转发与独立 producer → V4 只读采用 → coordinator/GC 与调度责任 → 完整流程。优先复用现有职责相符的模块，不把测试 fixture 或通用地址写入 API 装进生产游戏。完整目标和各项验收继续以 [SPEC](../../../.docs/foundation-expansion/SPEC.md)、[CHECKLIST](../../../.docs/foundation-expansion/CHECKLIST.md) 为准；用户仍要求先继续代码、游戏实测稍后。
