# 主代理代码与产物审查

2026-10-23。本轮三名子代理因账号额度报错中断。主代理检查其留下的返回 owner 和共享 scene backend，补受控入口的生产 backend 拒绝规则，完成 13 组返回回归与 8 组 Boolean 观察回归。本文件是主代理自查，不是独立子代理或外部审计。

- 返回目标取实际 completed launch owner 的 origin；生产 source 以 ReferenceEquals 绑定同 registry/session，controlled receipt 不授生产 native 调用权限。原 owner 的一次 claimant 在释放观察根后仍保留，重复请求在 backend.Capture 前拒绝。
- 第一份强 owner 在原生根/metadata/call 之前保留，异常不无条件 free 或退配额；Tick 的未知失败也保留责任。正常清理只释放 scene observer 根，独立 free 未确认不会重试。
- 返回完成同时要求确切 operation、目标 loaded、manager lock/slot 清除和原 Level 不再 loaded；原场景句柄读取前后夹着 manager/active/operation 新读值，错误 handle/name/外来操作和不稳定观察均拒绝。此证明仅限场景，并未授所有消费者结束。
- Boolean 参数签名只接受一字节非 byref System.Boolean，普通公共 Bind 不接受此 ABI，内部 retained 路径拒绝 MethodInfo/class alias；外来非空 MethodInfo 只原样转发，不先访问 receiver。逻辑 detach 后 original 仍一次，After 不再发送，已捕获 context 仍在真实在途区内释放。
- cdecl calli 包括原始字节 0、1、0x80、0xff，以及高位 receiver/MethodInfo。该原函数和 engine 为受控 managed fixture，未证明新入口真实 Dobby 或 native original SEH/C++ unwind；文档没有扩张证据范围。
- 与 1266 的冻结源码逐项对照为 4 新增/7 修改；共享 scene backend 的已有 `_transTime`、`_waitPredicator` 名称未改，未把先前研究口头提到的“字段修复”记成实际变更。
- 完整 25 套 1287/1287 exit 0；387 声明源和 18 暂存文件分别按 manifest 冻结并核对。1021 安装及旧 326 源归档仍匹配，3 个原游戏文件未变；未启动/停止/安装/操作游戏。

## 剩余风险

没有新的真实游戏返回验收。Loader/模块未组合完整启动、结果、返回命令链，菜单锁/相机连续所有权、全 native Judge 在途、活动 controller/音频/旧回调结束和 run 资源释放仍缺。观察根释放与 scene completed 都不能替代这些条件；RequestPlay 继续关闭。完整目标仍未达成，不新增 B 项勾选。
