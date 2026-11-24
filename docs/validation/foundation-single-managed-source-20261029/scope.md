# Single 托管接线范围

2026-10-29 root 在用户继续完整 B01–B19、代码优先、游戏实测稍后的任务中分派本包。auth 为该明确委派；network_profile 为 offline。

in_scope：新 Runtime/Hooks `NativeTweenSingleEvent*` bridge/types/parent/root-batch 文件；新 `NativeRuntime.SingleExecutingList.cs`；Adapter 的 TypedEvents/TypedEffects 和新 SingleDispatch/TypedDispatch/tracker partial；独立 `tests/RizToolbox.SingleEvent.Tests` 及验证目录。root 后续明确授权只将 `NativeLocalTweenBackend.cs` 两个直接 typed binding activity 读取改为合并 Single lane 的 `ReadTypedEventActivity`。

out_of_scope：root 拥有的 NativeFirstInstall、NativeOperationPublication、NativeListenerPublication、正式 native 布局、pack 和旧 Hooks fixture 的修改；游戏启动/停止/安装/GUI；提交推送；性能研究。没有新增代理。

本包只读取已保存 V7 合同、现有 native header/producer 和既有固定构建证据；没有重新分析或执行 GameAssembly。生产 publication/真实 mapped CLR adoption 的验证由 root 独立完成，本包不借其计划声明自己的正向结果。没有新增 caller Configure/Ready 或 effects-completion 入口。
