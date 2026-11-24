# 本批范围

2026-10-23。继续用户“先继续代码，实测稍后”的任务；同时复核用户提供的 InFalsusTools 架构参考。整项目标仍是 B01–B19，本批不新增验收勾选。

## 实现

- Runtime 增加内部精确 `void(Boolean)` 只读观察。验证真实元数据参数为非 byref 的一字节 System.Boolean，保留原始字节、receiver 和完整 MethodInfo；只在原函数正常返回后通知，逻辑解绑后继续原调用并释放已留上下文，在途覆盖清理退出。
- Adapter 增加真实返回场景 owner。目标只能来自同局实际已完成首次转场的来源；生产 admission 关联精确 registry/session，要求调用方已 BeginReturn，一次授权先于任何原生工作消耗。独立强责任 owner 在捕获根/调用前登记。
- 返回观察必须看到自己的原生 AsyncOperation、新目标场景、锁和 async 槽归零，并按原 Level 句柄独立确认其不再 loaded。前后再次读 manager、场景和 operation，变化或未知失败保留责任且不重试。
- 初次转场与返回复用同一个精确元数据 backend；新增按指定场景句柄读取和从已捕获来源选择目标。受控测试入口拒绝生产 backend，不以伪造 receipt 授权原生调用。

## 明确限界

本批的 Boolean Hook 尚未绑定菜单 GameObject.SetActive；菜单相机/粒子、TouchBlock/BackLock 所有权和回调余额账本仍待实现。cdecl calli 检查使用受控 engine/managed original，不是此新入口的真实 Dobby/game 验证，未证明 native original SEH/C++ 跨 CLR 展开。

返回 owner 目前有可调用生产工厂，没有 Loader/模块/命令调用点。它只签发场景完成记录并释放自己的观察根，不会调用 Registry.Close，不证明 controller、Judge、UI、旧延迟回调或音频等全部消费者结束，也不授权 globals/bundle/controller 释放。RequestPlay 保持关闭；Retry 新局事务、active 退休与 coordinator 释放仍缺。

不启动或停止游戏，不安装新包，不操作前台，不访问 QQ。三名子代理因账号额度中断，主代理接手完成代码、受控测试和审查；没有新独立子代理复核结论。
